using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace Nordvik.Shared;

/// <summary>Un evento tal como viaja por event-bus.</summary>
public sealed record EventEnvelope(string EventId, long Seq, string Topic, string Key, string Producer, string OccurredAt,
    string? CorrelationId, int SchemaVersion, JsonElement Payload);

/// <summary>Un evento pendiente en el outbox de un servicio.</summary>
public sealed record OutboxEvent(long Seq, string EventId, string Topic, string Key, string Producer, string OccurredAt, string? CorrelationId, int SchemaVersion, JsonElement Payload);

/// <summary>
/// Patrón outbox (ADR-005): el servicio guarda el evento en SU base de datos, en la misma transacción que el cambio,
/// y outbox-relay lo lee de GET /api/v1/internal/outbox y lo publica en event-bus. Si event-bus está caído no se pierde nada.
/// </summary>
public static class Outbox
{
    public static void Ensure(SqliteConnection db) =>
        db.Execute(@"CREATE TABLE IF NOT EXISTS outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT UNIQUE, topic TEXT, key TEXT,
                     payload TEXT, occurred_at TEXT, correlation_id TEXT, schema_version INTEGER)");

    public static string Add(SqliteConnection db, HttpContext? http, IClock clock, string topic, string key, object payload,
        SqliteTransaction? tx = null, int schemaVersion = 1, string? correlationId = null)
    {
        var id = "evt_" + Guid.NewGuid().ToString("N");
        db.Execute("INSERT INTO outbox (event_id, topic, key, payload, occurred_at, correlation_id, schema_version) VALUES (@id, @topic, @key, @payload, @at, @corr, @schemaVersion)",
            new
            {
                id, topic, key,
                payload = payload as string ?? JsonSerializer.Serialize(payload, Canonical.Web),
                at = Nordic.Iso(clock.LocalNow),
                corr = correlationId ?? http?.Items[NordvikMiddleware.CorrelationKey] as string,
                schemaVersion,
            }, tx);
        return id;
    }

    /// <summary>Publica el endpoint interno que lee outbox-relay.</summary>
    public static void MapFeed(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/internal/outbox", (long? after, int? limit) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<(long Seq, string EventId, string Topic, string Key, string Payload, string OccurredAt, string? CorrelationId, long SchemaVersion)>(
                "SELECT seq, event_id, topic, key, payload, occurred_at, correlation_id, schema_version FROM outbox WHERE seq > @after ORDER BY seq LIMIT @limit",
                new { after = after ?? 0, limit = Math.Clamp(limit ?? 200, 1, 500) });
            var items = rows.Select(r => new OutboxEvent(r.Seq, r.EventId, r.Topic, r.Key, ctx.Info.Name, r.OccurredAt, r.CorrelationId, (int)r.SchemaVersion,
                JsonDocument.Parse(r.Payload).RootElement)).ToList();
            return Results.Ok(new { producer = ctx.Info.Name, items, lastSeq = items.Count > 0 ? items[^1].Seq : after ?? 0 });
        }).WithTags("Interno").WithSummary("Eventos del outbox (lo lee outbox-relay)");
    }
}

/// <summary>
/// Consumidor de eventos por sondeo (como un consumidor de Kafka): pregunta a event-bus cada poco por los temas que le interesan,
/// guarda por dónde va (cursor) y no procesa dos veces el mismo eventId.
/// </summary>
public sealed class EventConsumer
{
    private readonly ServiceContext _ctx;
    private readonly string _name;
    private readonly string[] _topics;
    private readonly Func<EventEnvelope, Task> _handle;
    private Timer? _timer;
    private int _busy;
    public long Processed { get; private set; }
    public string? LastError { get; private set; }
    private static readonly ConcurrentDictionary<string, EventConsumer> All = new();

    private EventConsumer(ServiceContext ctx, string name, string[] topics, Func<EventEnvelope, Task> handle)
    {
        _ctx = ctx; _name = name; _topics = topics; _handle = handle;
    }

    public static EventConsumer Start(WebApplication app, ServiceContext ctx, string[] topics, Func<EventEnvelope, Task> handle, int intervalMs = 250)
    {
        var c = new EventConsumer(ctx, ctx.Info.Name, topics, handle);
        using (var db = ctx.OpenDb())
            db.Execute(@"CREATE TABLE IF NOT EXISTS consumer_cursor (name TEXT PRIMARY KEY, seq INTEGER);
                         CREATE TABLE IF NOT EXISTS consumer_processed (event_id TEXT PRIMARY KEY, processed_at TEXT);
                         CREATE TABLE IF NOT EXISTS consumer_failures (event_id TEXT PRIMARY KEY, topic TEXT, attempts INTEGER, last_error TEXT)");
        app.Lifetime.ApplicationStarted.Register(() => c._timer = new Timer(_ => _ = c.Poll(), null, intervalMs, intervalMs));
        app.Lifetime.ApplicationStopping.Register(() => c._timer?.Dispose());
        All[ctx.Info.Name] = c;
        app.MapGet("/api/v1/internal/consumer", () =>
        {
            using var db = ctx.OpenDb();
            var seq = db.ExecuteScalar<long?>("SELECT seq FROM consumer_cursor WHERE name = @n", new { n = c._name }) ?? 0;
            var failures = db.Query<(string EventId, string Topic, long Attempts, string LastError)>("SELECT event_id, topic, attempts, last_error FROM consumer_failures")
                .Select(f => new { eventId = f.EventId, topic = f.Topic, attempts = f.Attempts, lastError = f.LastError }).ToList();
            return Results.Ok(new { consumer = c._name, topics = c._topics, cursor = seq, processed = c.Processed, lastError = c.LastError, failures });
        }).WithTags("Interno").WithSummary("Estado del consumidor de eventos de este servicio");
        return c;
    }

    /// <summary>Procesa ya lo pendiente (las pruebas lo usan para no esperar al temporizador).</summary>
    public async Task Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            long cursor;
            using (var db = _ctx.OpenDb()) cursor = db.ExecuteScalar<long?>("SELECT seq FROM consumer_cursor WHERE name = @n", new { n = _name }) ?? 0;
            DownstreamReply r;
            try
            {
                r = await Downstream.Send(null, "event-bus", HttpMethod.Get, $"/api/v1/events?topics={string.Join(',', _topics)}&after={cursor}&limit=100",
                    headers: new Dictionary<string, string> { [NordvikMiddleware.PollHeader] = _name }, timeout: TimeSpan.FromSeconds(3));
            }
            catch (DependencyUnavailableException) { return; }
            if (!r.Ok) { LastError = "event-bus respondió " + r.Status; return; }
            foreach (var item in r.Json.GetProperty("items").EnumerateArray())
            {
                var env = item.Deserialize<EventEnvelope>(Canonical.Web)!;
                using var db = _ctx.OpenDb();
                var done = db.ExecuteScalar<long>("SELECT COUNT(*) FROM consumer_processed WHERE event_id = @id", new { id = env.EventId }) > 0;
                if (!done)
                {
                    try
                    {
                        await _handle(env);
                        db.Execute("INSERT OR IGNORE INTO consumer_processed VALUES (@id, @at)", new { id = env.EventId, at = DateTimeOffset.UtcNow.ToString("O") });
                        Processed++;
                    }
                    catch (Exception ex)
                    {
                        LastError = $"{env.Topic} {env.EventId}: {ex.GetType().Name} {ex.Message}";
                        db.Execute(@"INSERT INTO consumer_failures VALUES (@id, @topic, 1, @err)
                                     ON CONFLICT(event_id) DO UPDATE SET attempts = attempts + 1, last_error = excluded.last_error",
                            new { id = env.EventId, topic = env.Topic, err = LastError });
                        var attempts = db.ExecuteScalar<long>("SELECT attempts FROM consumer_failures WHERE event_id = @id", new { id = env.EventId });
                        if (attempts < 5) return; // se reintenta en la siguiente vuelta sin avanzar el cursor
                    }
                }
                db.Execute("INSERT INTO consumer_cursor VALUES (@n, @s) ON CONFLICT(name) DO UPDATE SET seq = excluded.seq", new { n = _name, s = env.Seq });
            }
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
