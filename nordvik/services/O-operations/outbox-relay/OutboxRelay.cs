using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SourceStatus(string Producer, long Cursor, long Relayed, string State, string? LastError, string? LastRelayAt);
public sealed record RelayStatus(bool Paused, int IntervalMs, long TotalRelayed, IReadOnlyList<SourceStatus> Sources);

/// <summary>
/// outbox-relay: cada 250 ms lee el outbox de cada servicio que emite eventos y lo publica en event-bus.
/// Si event-bus está caído, no avanza el cursor: los eventos esperan en el outbox del productor (nada se pierde).
/// Se puede pausar para practicar consistencia eventual.
/// </summary>
public sealed class OutboxRelay : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "outbox-relay", "O-operations", 7401, 2, "Plataforma de eventos",
        "Publica en event-bus los eventos guardados en el outbox de cada servicio. Pausable para practicar consistencia eventual.",
        DependsOn: new[] { "event-bus" });

    private Timer? _timer;
    private int _busy;
    private volatile bool _paused;
    private const int IntervalMs = 250;
    private readonly ConcurrentDictionary<string, (string State, string? Error, DateTimeOffset RetryAt)> _state = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS cursors (producer TEXT PRIMARY KEY, seq INTEGER, relayed INTEGER, last_relay_at TEXT)");
    }

    private static IEnumerable<string> Producers() =>
        ServiceDirectory.Infos.Where(i => i.Emits is { Length: > 0 }).Select(i => i.Name).OrderBy(n => n);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/relay/status", () => Results.Ok(Status(ctx))).WithTags("Relay").WithSummary("Por dónde va cada productor").Produces<RelayStatus>();
        app.MapPost("/api/v1/relay/pause", () => { _paused = true; return Results.Ok(Status(ctx)); }).WithTags("Relay").WithSummary("Pausar la publicación (los eventos se acumulan en los outbox)");
        app.MapPost("/api/v1/relay/resume", () => { _paused = false; return Results.Ok(Status(ctx)); }).WithTags("Relay").WithSummary("Reanudar la publicación");
        app.MapPost("/api/v1/relay/flush", async () => { await Relay(ctx, force: true); return Results.Ok(Status(ctx)); })
            .WithTags("Relay").WithSummary("Publicar ya todo lo pendiente (aunque esté en pausa)");
        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Relay(ctx, false), null, IntervalMs, IntervalMs));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private RelayStatus Status(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        var rows = db.Query<(string Producer, long Seq, long Relayed, string? At)>("SELECT producer, seq, relayed, last_relay_at FROM cursors").ToDictionary(r => r.Producer);
        var sources = Producers().Select(p =>
        {
            rows.TryGetValue(p, out var r);
            var st = _state.TryGetValue(p, out var s) ? s : ("PENDING", null, DateTimeOffset.MinValue);
            return new SourceStatus(p, r.Seq, r.Relayed, st.State, st.Error, r.At);
        }).ToList();
        return new RelayStatus(_paused, IntervalMs, sources.Sum(s => s.Relayed), sources);
    }

    private async Task Relay(ServiceContext ctx, bool force)
    {
        if (_paused && !force) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            if (!force) return;
            while (Interlocked.Exchange(ref _busy, 1) == 1) await Task.Delay(20);
        }
        try
        {
            foreach (var producer in Producers())
            {
                if (!force && _state.TryGetValue(producer, out var st) && st.RetryAt > DateTimeOffset.UtcNow) continue;
                try
                {
                    long cursor;
                    using (var db = ctx.OpenDb()) cursor = db.ExecuteScalar<long?>("SELECT seq FROM cursors WHERE producer = @producer", new { producer }) ?? 0;
                    var page = await Downstream.Send(null, producer, HttpMethod.Get, $"/api/v1/internal/outbox?after={cursor}&limit=200",
                        headers: new Dictionary<string, string> { [NordvikMiddleware.PollHeader] = "outbox-relay" }, timeout: TimeSpan.FromSeconds(2));
                    if (!page.Ok) { _state[producer] = ("ERROR", "outbox respondió " + page.Status, DateTimeOffset.UtcNow.AddSeconds(2)); continue; }
                    var items = page.Json.GetProperty("items");
                    if (items.GetArrayLength() == 0) { _state[producer] = ("UP", null, DateTimeOffset.MinValue); continue; }
                    var events = items.EnumerateArray().Select(e => new
                    {
                        eventId = e.GetProperty("eventId").GetString(), topic = e.GetProperty("topic").GetString(), key = e.GetProperty("key").GetString(),
                        producer, occurredAt = e.GetProperty("occurredAt").GetString(),
                        correlationId = e.GetProperty("correlationId").ValueKind == JsonValueKind.String ? e.GetProperty("correlationId").GetString() : null,
                        schemaVersion = e.GetProperty("schemaVersion").GetInt32(), payload = e.GetProperty("payload"),
                    }).ToList();
                    if (Chaos.TrapOn("T5")) events.Reverse(); // Trampa T5: el lote se publica al revés
                    var pub = await Downstream.Post(null, "event-bus", "/api/v1/events", events);
                    if (!pub.Ok) { _state[producer] = ("BUS_ERROR", "event-bus respondió " + pub.Status, DateTimeOffset.UtcNow.AddSeconds(1)); continue; }
                    var last = page.Json.GetProperty("lastSeq").GetInt64();
                    using (var db = ctx.OpenDb())
                        db.Execute(@"INSERT INTO cursors VALUES (@producer, @last, @n, @at)
                                     ON CONFLICT(producer) DO UPDATE SET seq = excluded.seq, relayed = relayed + excluded.relayed, last_relay_at = excluded.last_relay_at",
                            new { producer, last, n = events.Count, at = Nordic.Iso(Nordic.ToStockholm(DateTimeOffset.UtcNow)) });
                    _state[producer] = ("UP", null, DateTimeOffset.MinValue);
                }
                catch (DependencyUnavailableException ex)
                {
                    // Productor apagado (no está en el perfil) o event-bus caído: se reintenta más tarde.
                    _state[producer] = (ex.Service == "event-bus" ? "BUS_DOWN" : "DOWN", ex.Message, DateTimeOffset.UtcNow.AddSeconds(ex.Service == "event-bus" ? 1 : 5));
                }
            }
        }
        catch { /* nunca tumbar el servicio */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
