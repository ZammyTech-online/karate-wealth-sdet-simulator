using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PublishRequest(string? EventId, string? Topic, string? Key, string? Producer, string? OccurredAt, string? CorrelationId, int? SchemaVersion, JsonElement? Payload);
public sealed record PublishResult(int Accepted, int Duplicates, long LastSeq);
public sealed record TopicInfo(string Topic, long Events, string? LastEventAt);
public sealed record EventPage(IReadOnlyList<EventEnvelope> Items, long LastSeq, bool HasMore);
public sealed record SubscriptionRequest(string? Name, string? Topic, string? CallbackUrl, string? Secret);
public sealed record Subscription(string Id, string Name, string Topic, string CallbackUrl, bool Active, long Cursor, string CreatedAt);
public sealed record Delivery(long Id, string SubscriptionId, string EventId, long Seq, long Attempt, long? HttpStatus, string Outcome, string At, string? NextAttemptAt);
public sealed record DeadLetter(long Id, string SubscriptionId, string EventId, string Topic, long Attempts, string LastError, string At, bool Replayed);

/// <summary>
/// event-bus: bus de eventos sobre HTTP (ADR-005). Los servicios publican a través de outbox-relay; los consumidores
/// leen con GET /api/v1/events?topics=...&amp;after=N (como Kafka) o se suscriben con un webhook (reintentos 1-2-4-8 s y cola de muertos).
/// </summary>
public sealed class EventBus : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "event-bus", "O-operations", 7400, 2, "Plataforma de eventos",
        "Bus de eventos: temas, lectura por cursor, suscripciones por webhook con reintentos y cola de mensajes muertos. Sin RabbitMQ hasta el nivel 12.",
        DependsOn: new[] { "config-service" });

    private int[] _backoff = { 1, 2, 4, 8 };
    private int _maxAttempts = 5;
    private DateTimeOffset _configReadAt = DateTimeOffset.MinValue;
    private Timer? _timer;
    private int _busy;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS events (seq INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT UNIQUE, topic TEXT, key TEXT, producer TEXT,
                        occurred_at TEXT, correlation_id TEXT, schema_version INTEGER, payload TEXT, received_at TEXT);
                     CREATE INDEX IF NOT EXISTS ix_events_topic ON events(topic, seq);
                     CREATE TABLE IF NOT EXISTS subscriptions (id TEXT PRIMARY KEY, name TEXT, topic TEXT, callback_url TEXT, secret TEXT, active INTEGER, cursor INTEGER, created_at TEXT,
                        pending_seq INTEGER, attempts INTEGER, next_attempt_at TEXT);
                     CREATE TABLE IF NOT EXISTS deliveries (id INTEGER PRIMARY KEY AUTOINCREMENT, subscription_id TEXT, event_id TEXT, seq INTEGER, attempt INTEGER, http_status INTEGER, outcome TEXT, at TEXT, next_attempt_at TEXT);
                     CREATE TABLE IF NOT EXISTS dead_letters (id INTEGER PRIMARY KEY AUTOINCREMENT, subscription_id TEXT, event_id TEXT, topic TEXT, attempts INTEGER, last_error TEXT, at TEXT, replayed INTEGER)");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM subscriptions WHERE name LIKE @p", new { p = prefix + "%" });
    }

    private static EventEnvelope Map((long Seq, string EventId, string Topic, string Key, string Producer, string OccurredAt, string? CorrelationId, long SchemaVersion, string Payload) r) =>
        new(r.EventId, r.Seq, r.Topic, r.Key, r.Producer, r.OccurredAt, r.CorrelationId, (int)r.SchemaVersion, JsonDocument.Parse(r.Payload).RootElement);

    private const string Cols = "seq, event_id, topic, key, producer, occurred_at, correlation_id, schema_version, payload";

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/events", (HttpContext http, JsonElement body) =>
        {
            var list = body.ValueKind == JsonValueKind.Array ? body.EnumerateArray().ToList() : new List<JsonElement> { body };
            var errors = new List<FieldError>();
            var parsed = new List<PublishRequest>();
            for (var i = 0; i < list.Count; i++)
            {
                PublishRequest? r = null;
                try { r = list[i].Deserialize<PublishRequest>(Canonical.Web); } catch (JsonException) { }
                if (r is null) { errors.Add(new FieldError($"[{i}]", "no es un evento")); continue; }
                if (string.IsNullOrWhiteSpace(r.Topic) || !System.Text.RegularExpressions.Regex.IsMatch(r.Topic, @"^[a-z][a-z0-9-]*(\.[a-z0-9-]+)+$"))
                    errors.Add(new FieldError($"[{i}].topic", "obligatorio, en minúsculas con puntos: dominio.entidad.hecho (order.created)"));
                if (string.IsNullOrWhiteSpace(r.Key)) errors.Add(new FieldError($"[{i}].key", "obligatorio (id de la entidad)"));
                if (r.Payload is null || r.Payload.Value.ValueKind != JsonValueKind.Object) errors.Add(new FieldError($"[{i}].payload", "obligatorio, un objeto JSON"));
                parsed.Add(r);
            }
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Algún evento no es válido.",
                "Ejemplo: { \"topic\": \"order.created\", \"key\": \"<id>\", \"payload\": { ... } }. eventId es opcional y sirve para no duplicar.", errors);
            using var db = ctx.OpenDb();
            int accepted = 0, dup = 0;
            foreach (var r in parsed)
            {
                var id = string.IsNullOrWhiteSpace(r.EventId) ? "evt_" + Guid.NewGuid().ToString("N") : r.EventId;
                var n = db.Execute(@"INSERT OR IGNORE INTO events (event_id, topic, key, producer, occurred_at, correlation_id, schema_version, payload, received_at)
                                    VALUES (@id, @Topic, @Key, @producer, @at, @corr, @ver, @payload, @now)",
                    new
                    {
                        id, r.Topic, r.Key, producer = r.Producer ?? "manual", at = r.OccurredAt ?? Nordic.Iso(Nordic.ToStockholm(DateTimeOffset.UtcNow)),
                        corr = r.CorrelationId ?? http.Items[NordvikMiddleware.CorrelationKey] as string, ver = r.SchemaVersion ?? 1,
                        payload = r.Payload!.Value.GetRawText(), now = DateTimeOffset.UtcNow.ToString("O"),
                    });
                if (n == 1) accepted++; else dup++;
            }
            var last = db.ExecuteScalar<long?>("SELECT MAX(seq) FROM events") ?? 0;
            return Results.Json(new PublishResult(accepted, dup, last), statusCode: 202);
        }).WithTags("Eventos").WithSummary("Publicar uno o varios eventos (eventId repetido = se ignora)").Produces<PublishResult>(202);

        app.MapGet("/api/v1/events", (HttpContext http, string? topics, string? topic, string? key, string? correlationId, long? after, int? limit) =>
        {
            var wanted = (topics ?? topic ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var size = Math.Clamp(limit ?? 100, 1, 500);
            using var db = ctx.OpenDb();
            var all = wanted.Contains("*");
            var prefixes = wanted.Where(w => w.EndsWith(".*")).Select(w => w[..^1]).ToArray();
            var exact = wanted.Where(w => !w.EndsWith("*")).ToArray();
            var rows = db.Query<(long, string, string, string, string, string, string?, long, string)>(
                $"SELECT {Cols} FROM events WHERE seq > @after AND (@key IS NULL OR key = @key) AND (@corr IS NULL OR correlation_id = @corr) ORDER BY seq",
                new { after = after ?? 0, key, corr = correlationId })
                .Where(r => all || exact.Contains(r.Item3) || prefixes.Any(p => r.Item3.StartsWith(p)))
                .Take(size + 1).ToList();
            var items = rows.Take(size).Select(Map).ToList();
            return Results.Ok(new EventPage(items, items.Count > 0 ? items[^1].Seq : after ?? 0, rows.Count > size));
        }).WithTags("Eventos").WithSummary("Leer eventos desde un cursor (topics=a,b · order.* · *)").Produces<EventPage>();

        app.MapGet("/api/v1/events/{eventId}", (HttpContext http, string eventId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(long, string, string, string, string, string, string?, long, string)>($"SELECT {Cols} FROM events WHERE event_id = @eventId", new { eventId });
            return r.Item2 is null ? Problems.NotFound(http, "EVENT_NOT_FOUND", $"No existe el evento {eventId}.", null) : Results.Ok(Map(r));
        }).WithTags("Eventos").WithSummary("Un evento por id").Produces<EventEnvelope>();

        app.MapGet("/api/v1/topics", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<TopicInfo>("SELECT topic, COUNT(*) AS events, MAX(occurred_at) AS lastEventAt FROM events GROUP BY topic ORDER BY topic").ToList());
        }).WithTags("Eventos").WithSummary("Temas con eventos").Produces<List<TopicInfo>>();

        // ---------- Suscripciones por webhook ----------
        app.MapPost("/api/v1/subscriptions", (HttpContext http, SubscriptionRequest body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Name)) errors.Add(new FieldError("name", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.Topic)) errors.Add(new FieldError("topic", "obligatorio (admite order.* y *)"));
            if (!Uri.TryCreate(body.CallbackUrl, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) errors.Add(new FieldError("callbackUrl", "URL http(s) absoluta"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Suscripción no válida.",
                "Ejemplo: { \"name\": \"mi-mock\", \"topic\": \"order.*\", \"callbackUrl\": \"http://localhost:8099/hooks\" }. Un mock de Karate puede recibirlos.", errors);
            using var db = ctx.OpenDb();
            var id = "sub_" + Guid.NewGuid().ToString("N")[..12];
            var cursor = db.ExecuteScalar<long?>("SELECT MAX(seq) FROM events") ?? 0;
            db.Execute("INSERT INTO subscriptions VALUES (@id, @Name, @Topic, @CallbackUrl, @Secret, 1, @cursor, @now, NULL, 0, NULL)",
                new { id, body.Name, body.Topic, body.CallbackUrl, body.Secret, cursor, now = Nordic.Iso(Nordic.ToStockholm(DateTimeOffset.UtcNow)) });
            return Results.Created($"/api/v1/subscriptions/{id}", Load(ctx, id));
        }).WithTags("Suscripciones").WithSummary("Suscribir un webhook a un tema (recibe solo eventos nuevos)").Produces<Subscription>(201);

        app.MapGet("/api/v1/subscriptions", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM subscriptions ORDER BY created_at").Select(i => Load(ctx, i)).ToList());
        }).WithTags("Suscripciones").WithSummary("Suscripciones");

        app.MapDelete("/api/v1/subscriptions/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            return db.Execute("UPDATE subscriptions SET active = 0 WHERE id = @id", new { id }) == 0
                ? Problems.NotFound(http, "SUBSCRIPTION_NOT_FOUND", $"No existe la suscripción {id}.", null)
                : Results.Ok(Load(ctx, id));
        }).WithTags("Suscripciones").WithSummary("Desactivar una suscripción");

        app.MapGet("/api/v1/subscriptions/{id}/deliveries", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            if (Load(ctx, id) is null) return Problems.NotFound(http, "SUBSCRIPTION_NOT_FOUND", $"No existe la suscripción {id}.", null);
            return Results.Ok(db.Query<Delivery>("SELECT id, subscription_id AS SubscriptionId, event_id AS EventId, seq, attempt, http_status AS HttpStatus, outcome, at, next_attempt_at AS NextAttemptAt FROM deliveries WHERE subscription_id = @id ORDER BY id", new { id }).ToList());
        }).WithTags("Suscripciones").WithSummary("Intentos de entrega de una suscripción");

        app.MapGet("/api/v1/dead-letters", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<(long, string, string, string, long, string, string, long)>(DlSel + " ORDER BY id").Select(Dl).ToList());
        }).WithTags("Suscripciones").WithSummary("Cola de mensajes muertos (entregas que agotaron los reintentos)");

        app.MapPost("/api/v1/dead-letters/{id:long}/replay", async (HttpContext http, long id) =>
        {
            using var db = ctx.OpenDb();
            var row = db.QuerySingleOrDefault<(long, string, string, string, long, string, string, long)>(DlSel + " WHERE id = @id", new { id });
            var dl = row.Item2 is null ? null : Dl(row);
            if (dl is null) return Problems.NotFound(http, "DEAD_LETTER_NOT_FOUND", $"No existe el mensaje muerto {id}.", null);
            if (dl.Replayed) return Problems.Conflict(http, "ALREADY_REPLAYED", "Ese mensaje ya se reenvió.", null);
            var sub = Load(ctx, dl.SubscriptionId)!;
            var ev = db.QuerySingle<(long, string, string, string, string, string, string?, long, string)>($"SELECT {Cols} FROM events WHERE event_id = @e", new { e = dl.EventId });
            var (status, _) = await Deliver(sub, Map(ev), db);
            var ok = status is >= 200 and < 300;
            if (ok) db.Execute("UPDATE dead_letters SET replayed = 1 WHERE id = @id", new { id });
            return Results.Ok(new { id, delivered = ok, httpStatus = status });
        }).WithTags("Suscripciones").WithSummary("Reenviar un mensaje muerto");

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Dispatch(ctx), null, 300, 300));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private const string DlSel = "SELECT id, subscription_id, event_id, topic, attempts, last_error, at, replayed FROM dead_letters";
    private static DeadLetter Dl((long Id, string Sub, string Ev, string Topic, long Attempts, string Err, string At, long Replayed) r) =>
        new(r.Id, r.Sub, r.Ev, r.Topic, r.Attempts, r.Err, r.At, r.Replayed == 1);

    private static Subscription? Load(ServiceContext ctx, string id)
    {
        using var db = ctx.OpenDb();
        var r = db.QuerySingleOrDefault<(string Id, string Name, string Topic, string Url, long Active, long Cursor, string CreatedAt)>(
            "SELECT id, name, topic, callback_url, active, cursor, created_at FROM subscriptions WHERE id = @id", new { id });
        return r.Id is null ? null : new Subscription(r.Id, r.Name, r.Topic, r.Url, r.Active == 1, r.Cursor, r.CreatedAt);
    }

    private static bool Matches(string pattern, string topic) =>
        pattern == "*" || pattern == topic || (pattern.EndsWith(".*") && topic.StartsWith(pattern[..^1]));

    private static async Task<(int? Status, string? Error)> Deliver(Subscription sub, EventEnvelope ev, Microsoft.Data.Sqlite.SqliteConnection db)
    {
        var body = JsonSerializer.Serialize(ev, Canonical.Web);
        var secret = db.ExecuteScalar<string?>("SELECT secret FROM subscriptions WHERE id = @id", new { id = sub.Id });
        var headers = new Dictionary<string, string> { ["X-Event-Id"] = ev.EventId, ["X-Event-Topic"] = ev.Topic };
        if (!string.IsNullOrEmpty(secret)) headers["X-Nordvik-Signature"] = "sha256=" + Canonical.HmacHex(secret, body);
        try
        {
            var r = await Downstream.SendUrl(HttpMethod.Post, sub.CallbackUrl, body, headers, TimeSpan.FromSeconds(3), ev.CorrelationId);
            return (r.Status, r.Ok ? null : "HTTP " + r.Status);
        }
        catch (Exception ex) { return (null, ex.GetType().Name); }
    }

    private async Task Dispatch(ServiceContext ctx)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (DateTimeOffset.UtcNow - _configReadAt > TimeSpan.FromSeconds(2))
            {
                _configReadAt = DateTimeOffset.UtcNow;
                var csv = await Config.Get(null, "event-bus", "retryBackoffSeconds", "1,2,4,8");
                var parsed = csv.Split(',').Select(x => int.TryParse(x.Trim(), out var v) ? v : -1).Where(v => v >= 0).ToArray();
                if (parsed.Length > 0) _backoff = parsed;
                _maxAttempts = await Config.GetInt(null, "event-bus", "maxAttempts", 5);
            }
            using var db = ctx.OpenDb();
            var subs = db.Query<(string Id, string Topic, long Cursor, long? PendingSeq, long Attempts, string? NextAttemptAt)>(
                "SELECT id, topic, cursor, pending_seq, attempts, next_attempt_at FROM subscriptions WHERE active = 1").ToList();
            foreach (var s in subs)
            {
                if (s.NextAttemptAt is not null && DateTimeOffset.Parse(s.NextAttemptAt) > DateTimeOffset.UtcNow) continue;
                var sub = Load(ctx, s.Id)!;
                var raw = db.Query<(long, string, string, string, string, string, string?, long, string)>($"SELECT {Cols} FROM events WHERE seq > @c ORDER BY seq LIMIT 50", new { c = s.Cursor }).ToList();
                if (raw.Count == 0) continue;
                var lastRead = raw[^1].Item1;
                var rows = raw.Select(Map).Where(e => Matches(s.Topic, e.Topic)).ToList();
                var allDelivered = true;
                foreach (var ev in rows)
                {
                    var attempt = (int)(s.PendingSeq == ev.Seq ? s.Attempts + 1 : 1);
                    var (status, error) = await Deliver(sub, ev, db);
                    var now = DateTimeOffset.UtcNow;
                    if (error is null)
                    {
                        db.Execute("INSERT INTO deliveries (subscription_id, event_id, seq, attempt, http_status, outcome, at) VALUES (@id, @e, @seq, @attempt, @status, 'DELIVERED', @at)",
                            new { id = s.Id, e = ev.EventId, seq = ev.Seq, attempt, status, at = now.ToString("O") });
                        db.Execute("UPDATE subscriptions SET cursor = @seq, pending_seq = NULL, attempts = 0, next_attempt_at = NULL WHERE id = @id", new { seq = ev.Seq, id = s.Id });
                        continue;
                    }
                    if (attempt >= _maxAttempts)
                    {
                        db.Execute("INSERT INTO deliveries (subscription_id, event_id, seq, attempt, http_status, outcome, at) VALUES (@id, @e, @seq, @attempt, @status, 'DEAD_LETTER', @at)",
                            new { id = s.Id, e = ev.EventId, seq = ev.Seq, attempt, status, at = now.ToString("O") });
                        db.Execute("INSERT INTO dead_letters (subscription_id, event_id, topic, attempts, last_error, at, replayed) VALUES (@id, @e, @t, @attempt, @error, @at, 0)",
                            new { id = s.Id, e = ev.EventId, t = ev.Topic, attempt, error, at = now.ToString("O") });
                        db.Execute("UPDATE subscriptions SET cursor = @seq, pending_seq = NULL, attempts = 0, next_attempt_at = NULL WHERE id = @id", new { seq = ev.Seq, id = s.Id });
                        continue;
                    }
                    var next = now.AddSeconds(_backoff[Math.Min(attempt - 1, _backoff.Length - 1)]);
                    db.Execute("INSERT INTO deliveries (subscription_id, event_id, seq, attempt, http_status, outcome, at, next_attempt_at) VALUES (@id, @e, @seq, @attempt, @status, 'RETRY', @at, @next)",
                        new { id = s.Id, e = ev.EventId, seq = ev.Seq, attempt, status, at = now.ToString("O"), next = next.ToString("O") });
                    db.Execute("UPDATE subscriptions SET pending_seq = @seq, attempts = @attempt, next_attempt_at = @next WHERE id = @id", new { seq = ev.Seq, attempt, next = next.ToString("O"), id = s.Id });
                    allDelivered = false;
                    break;
                }
                if (allDelivered) db.Execute("UPDATE subscriptions SET cursor = @lastRead WHERE id = @id", new { lastRead, id = s.Id });
            }
        }
        catch { /* el despachador nunca debe tumbar el servicio */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
