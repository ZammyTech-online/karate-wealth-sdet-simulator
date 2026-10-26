using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record HopRequest(string? Token, string? Service, string? Action, string? Purpose, string? Actor);
public sealed record Hop(long Seq, string Token, string Service, string Action, string? Purpose, string? Actor, string At, string? EventId, string? CorrelationId);

/// <summary>
/// lineage-service: el viaje de cada dato personal. Escucha los eventos de pii-vault y data-passport y apunta cada salto
/// (quién lo tokenizó, quién lo leyó, para qué, con qué correlationId). Responde a la pregunta de un auditor:
/// "¿por dónde ha pasado el IBAN de este cliente?".
/// </summary>
public sealed class LineageService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "lineage-service", "F-privacy", 6504, 3, "Privacidad y protección de datos",
        "Linaje de datos personales: cada tokenización, lectura y pasaporte queda como un salto con servicio, propósito, actor y correlationId.",
        DependsOn: new[] { "event-bus", "clock-service" }, Consumes: new[] { "pii.tokenized", "pii.detokenized", "passport.issued", "passport.read", "passport.revoked" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS hops (seq INTEGER PRIMARY KEY AUTOINCREMENT, token TEXT, service TEXT, action TEXT, purpose TEXT, actor TEXT, at TEXT, event_id TEXT UNIQUE, correlation_id TEXT);
                     CREATE INDEX IF NOT EXISTS ix_hops_token ON hops(token)");
    }

    private static string? S(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        EventConsumer.Start(app, ctx, Info.Consumes!, ev =>
        {
            var p = ev.Payload;
            var token = S(p, "token");
            if (token is null) return Task.CompletedTask;
            var (action, purpose, actor) = ev.Topic switch
            {
                "pii.tokenized" => ("TOKENIZED", null, null),
                "pii.detokenized" => ("READ_IN_CLEAR", S(p, "purpose"), S(p, "by")),
                "passport.issued" => ("PASSPORT_ISSUED", S(p, "purpose"), S(p, "audience")),
                "passport.read" => ("PASSPORT_READ", S(p, "purpose"), S(p, "audience")),
                _ => ("PASSPORT_REVOKED", null, null),
            };
            using var db = ctx.OpenDb();
            db.Execute("INSERT OR IGNORE INTO hops (token, service, action, purpose, actor, at, event_id, correlation_id) VALUES (@token, @svc, @action, @purpose, @actor, @at, @eid, @corr)",
                new { token, svc = ev.Producer, action, purpose, actor, at = ev.OccurredAt, eid = ev.EventId, corr = ev.CorrelationId });
            return Task.CompletedTask;
        });

        app.MapGet("/api/v1/lineage/{token}", (HttpContext http, string token) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Compliance", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var hops = db.Query<Hop>("SELECT seq, token, service, action, purpose, actor, at, event_id AS EventId, correlation_id AS CorrelationId FROM hops WHERE token = @token ORDER BY at, seq", new { token }).ToList();
            if (hops.Count == 0) return Problems.NotFound(http, "LINEAGE_NOT_FOUND", $"No hay saltos para {token}.", "El linaje llega por eventos: si acabas de crear el token, espera un momento (retry until).");
            var readers = hops.Where(h => h.Action is "READ_IN_CLEAR" or "PASSPORT_READ").Select(h => h.Actor).Distinct().ToList();
            return Results.Ok(new { token, hops, count = hops.Count, clearReads = hops.Count(h => h.Action is "READ_IN_CLEAR"), readers });
        }).WithTags("Linaje").WithSummary("Todos los saltos de un dato, en orden");

        app.MapGet("/api/v1/lineage", (HttpContext http, string? correlationId, string? actor, string? action) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Compliance", "Admin" }) is { } denied) return denied;
            if (correlationId is null && actor is null && action is null)
                return Problems.BadRequest(http, "FILTER_REQUIRED", "Filtra por correlationId, actor o action.", null);
            using var db = ctx.OpenDb();
            var hops = db.Query<Hop>(@"SELECT seq, token, service, action, purpose, actor, at, event_id AS EventId, correlation_id AS CorrelationId FROM hops
                                       WHERE (@correlationId IS NULL OR correlation_id = @correlationId) AND (@actor IS NULL OR actor = @actor) AND (@action IS NULL OR action = @action) ORDER BY seq LIMIT 500",
                new { correlationId, actor, action }).ToList();
            return Results.Ok(new { hops, count = hops.Count });
        }).WithTags("Linaje").WithSummary("Buscar saltos por correlationId, actor o acción");

        app.MapPost("/api/v1/hops", (HttpContext http, HopRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Service", "Admin" }) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Token) || string.IsNullOrWhiteSpace(b.Service) || string.IsNullOrWhiteSpace(b.Action))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta token, service y action.", null);
            using var db = ctx.OpenDb();
            var corr = http.Items[NordvikMiddleware.CorrelationKey] as string;
            var seq = db.ExecuteScalar<long>("INSERT INTO hops (token, service, action, purpose, actor, at, event_id, correlation_id) VALUES (@Token, @Service, @Action, @Purpose, @actor, @at, NULL, @corr); SELECT last_insert_rowid();",
                new { b.Token, b.Service, b.Action, b.Purpose, actor = b.Actor ?? p.Sub, at = Nordic.Iso(clock.LocalNow), corr });
            return Results.Created($"/api/v1/lineage/{b.Token}", new { seq });
        }).WithTags("Linaje").WithSummary("Apuntar un salto a mano (servicios que no emiten eventos)");
    }
}
