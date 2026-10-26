using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AuditRequest(string? Actor, string? Action, string? Resource, string? Outcome, JsonElement? Details);
public sealed record AuditRecord(long Seq, string Id, string At, string Actor, string Action, string Resource, string Outcome, string? CorrelationId,
    string Source, JsonElement Details, string PrevHash, string Hash);
public sealed record AuditPage(IReadOnlyList<AuditRecord> Items, int Total);
public sealed record ChainCheck(bool Valid, long Records, long? BrokenAtSeq, string? Reason, string? LastHash);

/// <summary>
/// audit-service: registro inmutable con cadena de hashes. Cada registro guarda el hash del anterior:
/// si alguien modifica uno, GET /api/v1/audit/verify señala exactamente dónde se rompe la cadena.
/// Además registra automáticamente cada evento del bus.
/// </summary>
public sealed class AuditService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "audit-service", "O-operations", 7402, 2, "Cumplimiento técnico",
        "Registro de auditoría inmutable (cadena SHA-256). Recibe registros directos y copia cada evento del bus. No admite PUT ni DELETE.",
        DependsOn: new[] { "event-bus", "clock-service" }, Consumes: new[] { "*" });

    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly object Gate = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS audit (seq INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT UNIQUE, at TEXT, actor TEXT, action TEXT, resource TEXT,
                     outcome TEXT, correlation_id TEXT, source TEXT, details TEXT, prev_hash TEXT, hash TEXT)");
    }

    /// <summary>Lo que se resume: todos los campos salvo el propio hash, en JSON canónico.</summary>
    public static string HashOf(string prevHash, string id, string at, string actor, string action, string resource, string outcome, string? corr, string source, string details) =>
        Canonical.Sha256Hex(prevHash + "|" + Canonical.Of(JsonSerializer.SerializeToElement(new
        {
            id, at, actor, action, resource, outcome, correlationId = corr, source, details = JsonDocument.Parse(details).RootElement,
        })));

    private static AuditRecord Append(ServiceContext ctx, string at, string actor, string action, string resource, string outcome, string? corr, string source, string details)
    {
        lock (Gate)
        {
            using var db = ctx.OpenDb();
            var prev = db.ExecuteScalar<string?>("SELECT hash FROM audit ORDER BY seq DESC LIMIT 1") ?? Genesis;
            var id = "aud_" + Guid.NewGuid().ToString("N")[..16];
            var hash = HashOf(prev, id, at, actor, action, resource, outcome, corr, source, details);
            db.Execute("INSERT INTO audit (id, at, actor, action, resource, outcome, correlation_id, source, details, prev_hash, hash) VALUES (@id, @at, @actor, @action, @resource, @outcome, @corr, @source, @details, @prev, @hash)",
                new { id, at, actor, action, resource, outcome, corr, source, details, prev, hash });
            return Load(db, "WHERE id = @id", new { id }).Single();
        }
    }

    private static List<AuditRecord> Load(Microsoft.Data.Sqlite.SqliteConnection db, string where, object args) =>
        db.Query<(long, string, string, string, string, string, string, string?, string, string, string, string)>(
            "SELECT seq, id, at, actor, action, resource, outcome, correlation_id, source, details, prev_hash, hash FROM audit " + where, args)
          .Select(r => new AuditRecord(r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7, r.Item8, r.Item9, JsonDocument.Parse(r.Item10).RootElement, r.Item11, r.Item12)).ToList();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/audit", (HttpContext http, IClock clock, AuditRequest body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Actor)) errors.Add(new FieldError("actor", "obligatorio (quién)"));
            if (string.IsNullOrWhiteSpace(body.Action)) errors.Add(new FieldError("action", "obligatorio (qué hizo, ej. customer.read)"));
            if (string.IsNullOrWhiteSpace(body.Resource)) errors.Add(new FieldError("resource", "obligatorio (sobre qué, ej. customer/<id>)"));
            if (body.Outcome is not (null or "SUCCESS" or "DENIED" or "FAILED")) errors.Add(new FieldError("outcome", "SUCCESS, DENIED o FAILED"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Registro de auditoría incompleto.", "Ejemplo: { \"actor\": \"asesor.erik\", \"action\": \"customer.read\", \"resource\": \"customer/<id>\" }", errors);
            var rec = Append(ctx, Nordic.Iso(clock.LocalNow), body.Actor!, body.Action!, body.Resource!, body.Outcome ?? "SUCCESS",
                http.Items[NordvikMiddleware.CorrelationKey] as string, "api", body.Details?.GetRawText() ?? "{}");
            return Results.Created($"/api/v1/audit/{rec.Id}", rec);
        }).WithTags("Auditoría").WithSummary("Añadir un registro (no se puede modificar después)").Produces<AuditRecord>(201);

        app.MapGet("/api/v1/audit", (string? actor, string? action, string? resource, string? correlationId, string? source, int? limit) =>
        {
            using var db = ctx.OpenDb();
            var items = Load(db, @"WHERE (@actor IS NULL OR actor = @actor) AND (@action IS NULL OR action = @action) AND (@resource IS NULL OR resource LIKE @resource || '%')
                                   AND (@corr IS NULL OR correlation_id = @corr) AND (@source IS NULL OR source = @source) ORDER BY seq DESC LIMIT @limit",
                new { actor, action, resource, corr = correlationId, source, limit = Math.Clamp(limit ?? 100, 1, 1000) });
            return Results.Ok(new AuditPage(items, items.Count));
        }).WithTags("Auditoría").WithSummary("Buscar registros (los más recientes primero)").Produces<AuditPage>();

        app.MapGet("/api/v1/audit/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var r = Load(db, "WHERE id = @id", new { id }).SingleOrDefault();
            return r is null ? Problems.NotFound(http, "AUDIT_RECORD_NOT_FOUND", $"No existe el registro {id}.", null) : Results.Ok(r);
        }).WithTags("Auditoría").WithSummary("Un registro").Produces<AuditRecord>();

        app.MapGet("/api/v1/audit/verify", () =>
        {
            using var db = ctx.OpenDb();
            var prev = Genesis;
            long n = 0;
            foreach (var r in db.Query<(long Seq, string Id, string At, string Actor, string Action, string Resource, string Outcome, string? Corr, string Source, string Details, string Prev, string Hash)>(
                         "SELECT seq, id, at, actor, action, resource, outcome, correlation_id, source, details, prev_hash, hash FROM audit ORDER BY seq"))
            {
                n++;
                if (r.Prev != prev) return Results.Ok(new ChainCheck(false, n, r.Seq, "prevHash no coincide con el hash del registro anterior", prev));
                var expected = HashOf(r.Prev, r.Id, r.At, r.Actor, r.Action, r.Resource, r.Outcome, r.Corr, r.Source, r.Details);
                if (expected != r.Hash) return Results.Ok(new ChainCheck(false, n, r.Seq, "el contenido del registro se ha modificado", prev));
                prev = r.Hash;
            }
            return Results.Ok(new ChainCheck(true, n, null, null, prev));
        }).WithTags("Auditoría").WithSummary("Verificar la cadena de hashes completa").Produces<ChainCheck>();

        foreach (var m in new[] { "PUT", "PATCH", "DELETE" })
            app.MapMethods("/api/v1/audit/{id}", new[] { m }, (HttpContext http, string id) =>
            {
                http.Response.Headers.Allow = "GET";
                return Problems.Of(http, 405, "AUDIT_IS_IMMUTABLE", "Método no permitido", "El registro de auditoría no se puede modificar ni borrar.",
                    "Es justo lo que tu test debe comprobar: un 405 aquí es la respuesta correcta.");
            }).WithTags("Auditoría").WithSummary("No permitido: el registro es inmutable");

        app.MapPost("/_test/audit/{seq:long}/tamper", (HttpContext http, long seq) =>
        {
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE audit SET outcome = CASE outcome WHEN 'SUCCESS' THEN 'DENIED' ELSE 'SUCCESS' END WHERE seq = @seq", new { seq });
            return n == 0 ? Problems.NotFound(http, "AUDIT_RECORD_NOT_FOUND", $"No existe el registro {seq}.", null)
                : Results.Ok(new { seq, tampered = true, note = "Ahora GET /api/v1/audit/verify debe decir valid=false y brokenAtSeq=" + seq });
        }).WithTags("Solo pruebas").WithSummary("Solo pruebas: altera un registro para ver cómo lo detecta verify");

        app.MapPost("/_test/audit/repair", () =>
        {
            // Recalcula la cadena entera (solo para dejar el entorno limpio tras practicar).
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                var prev = Genesis;
                foreach (var r in db.Query<(long Seq, string Id, string At, string Actor, string Action, string Resource, string Outcome, string? Corr, string Source, string Details)>(
                             "SELECT seq, id, at, actor, action, resource, outcome, correlation_id, source, details FROM audit ORDER BY seq").ToList())
                {
                    var h = HashOf(prev, r.Id, r.At, r.Actor, r.Action, r.Resource, r.Outcome, r.Corr, r.Source, r.Details);
                    db.Execute("UPDATE audit SET prev_hash = @prev, hash = @h WHERE seq = @Seq", new { prev, h, r.Seq });
                    prev = h;
                }
            }
            return Results.Ok(new { repaired = true });
        }).WithTags("Solo pruebas").WithSummary("Solo pruebas: rehace la cadena tras practicar con tamper");

        EventConsumer.Start(app, ctx, new[] { "*" }, ev =>
        {
            Append(ctx, ev.OccurredAt, ev.Producer, ev.Topic, ev.Topic.Split('.')[0] + "/" + ev.Key, "SUCCESS", ev.CorrelationId, "event:" + ev.EventId,
                JsonSerializer.Serialize(new { eventId = ev.EventId, schemaVersion = ev.SchemaVersion }));
            return Task.CompletedTask;
        });
    }
}
