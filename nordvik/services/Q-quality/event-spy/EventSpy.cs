using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// event-spy: mira cualquier evento publicado sin suscribirte. Lo mejor para tests:
/// POST /marks te da "ahora" (la última secuencia) y GET /events/wait?after=... espera hasta que aparezca el evento que buscas.
/// /duplicates encuentra eventos repetidos (mismo topic, key y payload): la trampa T4.
/// </summary>
public sealed class EventSpy : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "event-spy", "Q-quality", 7602, 3, "Calidad y test",
        "Espía de eventos para tests: buscar, marcar un punto de partida, esperar a que llegue un evento (long-poll) y detectar duplicados.",
        DependsOn: new[] { "event-bus" });

    private static string Query(string? topic, string? key, string? correlationId, long after, int limit)
    {
        var q = new List<string> { "after=" + after, "limit=" + limit };
        if (topic is not null) q.Add("topics=" + Uri.EscapeDataString(topic));
        if (key is not null) q.Add("key=" + Uri.EscapeDataString(key));
        if (correlationId is not null) q.Add("correlationId=" + Uri.EscapeDataString(correlationId));
        return "/api/v1/events?" + string.Join("&", q);
    }

    private static async Task<List<JsonElement>> Read(HttpContext http, string? topic, string? key, string? corr, long after, int max)
    {
        var all = new List<JsonElement>();
        while (all.Count < max)
        {
            var r = await Downstream.Get(http, "event-bus", Query(topic, key, corr, after, 500), forwardAuth: false);
            if (!r.Ok) break;
            var items = r.Json.GetProperty("items").EnumerateArray().ToList();
            all.AddRange(items);
            if (!r.Json.GetProperty("hasMore").GetBoolean() || items.Count == 0) break;
            after = r.Json.GetProperty("lastSeq").GetInt64();
        }
        return all.Take(max).ToList();
    }

    private static bool PayloadHas(JsonElement ev, string? field, string? value)
    {
        if (field is null) return true;
        var p = ev.GetProperty("payload");
        return p.ValueKind == JsonValueKind.Object && p.TryGetProperty(field, out var v) && (value is null || (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) == value);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/marks", async (HttpContext http) =>
        {
            long last = 0;
            var r = await Downstream.Get(http, "event-bus", "/api/v1/events?after=0&limit=1", forwardAuth: false);
            // La última secuencia: avanzamos por páginas hasta el final
            var after = 0L;
            while (r.Ok)
            {
                r = await Downstream.Get(http, "event-bus", $"/api/v1/events?after={after}&limit=500", forwardAuth: false);
                if (!r.Ok) break;
                last = r.Json.GetProperty("lastSeq").GetInt64();
                if (!r.Json.GetProperty("hasMore").GetBoolean()) break;
                after = last;
            }
            return Results.Ok(new { seq = last, at = DateTimeOffset.UtcNow.ToString("O"), usage = $"GET /api/v1/events/wait?after={last}&topic=..." });
        }).WithTags("Espía").WithSummary("Marca el punto de partida: la última secuencia publicada ahora mismo");

        app.MapGet("/api/v1/events", async (HttpContext http, string? topic, string? key, string? correlationId, long? after, int? limit, string? payloadField, string? payloadValue) =>
        {
            var items = (await Read(http, topic, key, correlationId, after ?? 0, Math.Clamp(limit ?? 100, 1, 2000))).Where(e => PayloadHas(e, payloadField, payloadValue)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Espía").WithSummary("Buscar eventos (topic admite a,b y order.*; filtro opcional por un campo del payload)");

        app.MapGet("/api/v1/events/wait", async (HttpContext http, string? topic, string? key, string? correlationId, long? after, int? timeoutMs, int? count, string? payloadField, string? payloadValue) =>
        {
            if (topic is null && key is null && correlationId is null)
                return Problems.BadRequest(http, "FILTER_REQUIRED", "Di qué esperas: topic, key o correlationId.", "Ejemplo: /api/v1/events/wait?topic=deposit.received&key=dep_...&timeoutMs=5000");
            var wanted = Math.Clamp(count ?? 1, 1, 100);
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs ?? 5000, 100, 30000));
            List<JsonElement> found;
            do
            {
                found = (await Read(http, topic, key, correlationId, after ?? 0, 2000)).Where(e => PayloadHas(e, payloadField, payloadValue)).ToList();
                if (found.Count >= wanted) return Results.Ok(new { found = true, count = found.Count, first = found[0], items = found });
                await Task.Delay(150);
            } while (DateTimeOffset.UtcNow < deadline);
            return Problems.NotFound(http, "EVENT_NOT_SEEN", $"No llegaron {wanted} evento(s) con ese filtro a tiempo (vistos: {found.Count}).",
                "¿El productor está encendido? ¿outbox-relay está en marcha? Mira la cadena con correlation-tracer.");
        }).WithTags("Espía").WithSummary("Esperar (long-poll) a que llegue un evento: 200 en cuanto aparece, 404 EVENT_NOT_SEEN al agotar timeoutMs");

        app.MapGet("/api/v1/events/duplicates", async (HttpContext http, string? topic, long? after) =>
        {
            var items = await Read(http, topic ?? "*", null, null, after ?? 0, 2000);
            var groups = items.GroupBy(e => (e.GetProperty("topic").GetString(), e.GetProperty("key").GetString(), Canonical.Of(e.GetProperty("payload"))))
                .Where(g => g.Count() > 1)
                .Select(g => new { topic = g.Key.Item1, key = g.Key.Item2, times = g.Count(), eventIds = g.Select(e => e.GetProperty("eventId").GetString()).ToList() })
                .ToList();
            return Results.Ok(new { duplicates = groups, count = groups.Count, examined = items.Count });
        }).WithTags("Espía").WithSummary("Eventos repetidos (mismo topic, key y payload con distinto eventId)");

        app.MapGet("/api/v1/topics", async (HttpContext http) =>
        {
            var r = await Downstream.Get(http, "event-bus", "/api/v1/topics", forwardAuth: false);
            return Results.Content(r.Raw, "application/json", System.Text.Encoding.UTF8, r.Status);
        }).WithTags("Espía").WithSummary("Topics vistos en el bus");
    }
}
