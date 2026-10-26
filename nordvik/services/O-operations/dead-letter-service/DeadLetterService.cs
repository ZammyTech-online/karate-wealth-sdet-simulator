using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DeadLetterAction(string? Reason);

/// <summary>
/// dead-letter-service: un solo sitio para los eventos que nadie pudo procesar. Junta dos fuentes: los consumidores internos (un evento que
/// falla 5 veces se salta y queda en su tabla de fallos) y los webhooks de event-bus que agotaron sus reintentos. Desde aquí se reintentan
/// a mano o se descartan con un motivo; cada decisión queda registrada.
/// </summary>
public sealed class DeadLetterService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "dead-letter-service", "O-operations", 7406, 4, "Operaciones",
        "Eventos que nadie pudo procesar (consumidores internos y webhooks): lista unificada, reintento manual, descarte con motivo y registro de decisiones.",
        DependsOn: new[] { "event-bus", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS decisions (key TEXT, action TEXT, outcome TEXT, reason TEXT, by TEXT, at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Admin" };

        async Task<List<(string Key, string Source, string Service, string EventId, string Topic, long Attempts, string Error, bool Skipped)>> Collect()
        {
            var all = new List<(string, string, string, string, string, long, string, bool)>();
            foreach (var svc in EventConsumer.ConsumerNames.OrderBy(n => n))
            {
                try
                {
                    var r = await Downstream.SendUrl(HttpMethod.Get, ServiceDirectory.UrlOf(svc) + "/api/v1/internal/consumer", null, null, TimeSpan.FromSeconds(3));
                    if (!r.Ok) continue;
                    foreach (var f in r.Json.GetProperty("failures").EnumerateArray())
                    {
                        var ev = f.GetProperty("eventId").GetString()!;
                        var attempts = f.GetProperty("attempts").GetInt64();
                        all.Add(($"consumer:{svc}:{ev}", "CONSUMER", svc, ev, f.GetProperty("topic").GetString()!, attempts, f.GetProperty("lastError").GetString() ?? "", attempts >= 5));
                    }
                }
                catch (DependencyUnavailableException) { }
            }
            var dl = await Downstream.Get(null, "event-bus", "/api/v1/dead-letters", forwardAuth: false);
            if (dl.Ok)
                foreach (var d in dl.Json.EnumerateArray())
                {
                    if (d.GetProperty("replayed").GetBoolean()) continue;
                    var id = d.GetProperty("id").GetInt64();
                    all.Add(($"webhook:{id}", "WEBHOOK", "event-bus:" + d.GetProperty("subscriptionId").GetString(), d.GetProperty("eventId").GetString()!, d.GetProperty("topic").GetString()!,
                             d.GetProperty("attempts").GetInt64(), d.GetProperty("lastError").GetString() ?? "", true));
                }
            using var db = ctx.OpenDb();
            var discarded = db.Query<string>("SELECT key FROM decisions WHERE action = 'DISCARD'").ToHashSet();
            return all.Where(x => !discarded.Contains(x.Item1)).ToList();
        }

        app.MapGet("/api/v1/dead-letters", async (HttpContext http, string? source, string? topic) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            var items = (await Collect()).Where(x => (source == null || x.Source == source.ToUpperInvariant()) && (topic == null || x.Topic == topic))
                .Select(x => new { key = x.Key, source = x.Source, service = x.Service, eventId = x.EventId, topic = x.Topic, attempts = x.Attempts, lastError = x.Error, skipped = x.Skipped }).ToList();
            return Results.Ok(new { items, count = items.Count, consumersChecked = EventConsumer.ConsumerNames.Count });
        }).WithTags("Mensajes muertos").WithSummary("Todos los eventos fallidos (consumidores y webhooks)");

        app.MapPost("/api/v1/dead-letters/{key}/retry", async (HttpContext http, string key) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            var item = (await Collect()).FirstOrDefault(x => x.Key == key);
            if (item.Key is null) return Problems.NotFound(http, "DEAD_LETTER_NOT_FOUND", $"No hay ningún mensaje muerto {key}.", "Las claves salen en GET /api/v1/dead-letters.");
            DownstreamReply r = item.Source == "CONSUMER"
                ? await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf(item.Service) + $"/api/v1/internal/consumer/retry/{item.EventId}", null, null, TimeSpan.FromSeconds(10))
                : await Downstream.Send(http, "event-bus", HttpMethod.Post, $"/api/v1/dead-letters/{key.Split(':')[1]}/replay", null, forwardAuth: false);
            var ok = r.Ok && (item.Source == "CONSUMER" || r.Json.GetProperty("delivered").GetBoolean());
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO decisions VALUES (@key, 'RETRY', @o, NULL, @by, @at)", new { key, o = ok ? "OK" : "FAILED", by = p.Sub, at = Nordic.Iso(clock.LocalNow) });
            return ok ? Results.Ok(new { key, retried = true, outcome = "PROCESSED" })
                : Problems.Unprocessable(http, "RETRY_FAILED", "El reintento volvió a fallar: " + (r.Body is { } b && b.ValueKind == JsonValueKind.Object && b.TryGetProperty("detail", out var dt) ? dt.GetString() : r.Raw), "Corrige la causa o descártalo con motivo.");
        }).WithTags("Mensajes muertos").WithSummary("Reintentar un mensaje muerto");

        app.MapPost("/api/v1/dead-letters/{key}/discard", async (HttpContext http, string key, DeadLetterAction b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Reason)) return Problems.BadRequest(http, "VALIDATION_FAILED", "reason es obligatorio: descartar un evento es una decisión que se audita.", null);
            var item = (await Collect()).FirstOrDefault(x => x.Key == key);
            if (item.Key is null) return Problems.NotFound(http, "DEAD_LETTER_NOT_FOUND", $"No hay ningún mensaje muerto {key}.", null);
            if (item.Source == "CONSUMER") await Downstream.SendUrl(HttpMethod.Delete, ServiceDirectory.UrlOf(item.Service) + $"/api/v1/internal/consumer/failures/{item.EventId}", null, null, TimeSpan.FromSeconds(5));
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO decisions VALUES (@key, 'DISCARD', 'OK', @Reason, @by, @at)", new { key, b.Reason, by = p.Sub, at = Nordic.Iso(clock.LocalNow) });
            return Results.Ok(new { key, discarded = true, reason = b.Reason });
        }).WithTags("Mensajes muertos").WithSummary("Descartar con motivo");

        app.MapGet("/api/v1/decisions", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Key, string Action, string Outcome, string? Reason, string By, string At)>("SELECT key, action, outcome, reason, by, at FROM decisions ORDER BY at, rowid")
                .Select(x => new { key = x.Key, action = x.Action, outcome = x.Outcome, reason = x.Reason, by = x.By, at = x.At }).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Mensajes muertos").WithSummary("Registro de reintentos y descartes");
    }
}
