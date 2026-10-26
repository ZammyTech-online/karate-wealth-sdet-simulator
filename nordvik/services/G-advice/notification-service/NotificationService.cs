using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record NotificationRequest(string? CustomerId, string? To, string? Channel, string? TemplateKey, string? Language, Dictionary<string, JsonElement>? Vars,
    string? Priority, string? Category, string? Reference);
public sealed record Notification(string Id, string? CustomerId, string Channel, string To, string TemplateKey, string Language, int TemplateVersion, string Status,
    string? Reason, string Priority, string Category, string? Reference, string CreatedAt, string? ScheduledFor, string? SentAt);
public sealed record MailboxMessage(string Id, string NotificationId, string Channel, string To, string? Subject, string Body, string DeliveredAt, string ExpiresAt);

/// <summary>
/// notification-service: envía email, SMS y push a un buzón simulado (como MailHog). Respeta las preferencias del cliente:
/// canal e idioma, consentimiento de marketing (si no lo dio: SUPPRESSED) y horas de silencio (SMS/PUSH: SCHEDULED hasta que acaban,
/// salvo prioridad HIGH). Los mensajes del buzón se autodestruyen a las 24 h del reloj virtual.
/// </summary>
public sealed class NotificationService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "notification-service", "G-advice", 6600, 2, "Comunicación con clientes",
        "Notificaciones por email, SMS y push con preferencias, consentimiento, horas de silencio y buzón de pruebas efímero (24 h).",
        DependsOn: new[] { "message-template-service", "preferences-service", "customer-registry", "clock-service", "event-bus" },
        Emits: new[] { "notification.sent" },
        Consumes: new[] { "order.executed", "payment.settled", "payment.rejected", "deposit.received", "contract.sent" });

    private Timer? _timer;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS notifications (id TEXT PRIMARY KEY, customer_id TEXT, channel TEXT, recipient TEXT, template_key TEXT, language TEXT, template_version INTEGER,
                        status TEXT, reason TEXT, priority TEXT, category TEXT, reference TEXT, created_at TEXT, scheduled_ticks INTEGER, scheduled_for TEXT, sent_at TEXT, subject TEXT, body TEXT);
                     CREATE TABLE IF NOT EXISTS mailbox (id TEXT PRIMARY KEY, notification_id TEXT, channel TEXT, recipient TEXT, subject TEXT, body TEXT, delivered_at TEXT, expires_ticks INTEGER, expires_at TEXT, seq INTEGER)");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM notifications WHERE reference LIKE @p", new { p = prefix + "%" });
    }

    private static Notification? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string? Cust, string Channel, string To, string Key, string Lang, long Ver, string Status, string? Reason, string Prio, string Cat,
            string? Ref, string Created, string? Sched, string? Sent)>(
            "SELECT id, customer_id, channel, recipient, template_key, language, template_version, status, reason, priority, category, reference, created_at, scheduled_for, sent_at FROM notifications WHERE id = @id", new { id });
        return r.Id is null ? null : new Notification(r.Id, r.Cust, r.Channel, r.To, r.Key, r.Lang, (int)r.Ver, r.Status, r.Reason, r.Prio, r.Cat, r.Ref, r.Created, r.Sched, r.Sent);
    }

    /// <summary>¿Está la hora local dentro de las horas de silencio? Devuelve cuándo acaban.</summary>
    private static DateTimeOffset? QuietUntil(DateTimeOffset local, string from, string to)
    {
        var f = TimeSpan.Parse(from); var t = TimeSpan.Parse(to); var now = local.TimeOfDay;
        var inside = f <= t ? now >= f && now < t : now >= f || now < t;
        if (!inside) return null;
        var end = local.Date + t;
        if (end <= local.DateTime) end = end.AddDays(1);
        return new DateTimeOffset(end, Nordic.Stockholm.GetUtcOffset(end));
    }

    /// <summary>Crea y (si toca) entrega una notificación. Devuelve (status http, notificación o problema).</summary>
    private static async Task<(int Status, object Body)> Create(HttpContext? http, IClock clock, ServiceContext ctx, NotificationRequest b)
    {
        Dictionary<string, object?> Problem(int status, string code, string detail, string? hint = null, IEnumerable<FieldError>? errors = null) =>
            http is null ? new() { ["code"] = code, ["detail"] = detail } : Problems.BodyOf(http, status, code, status == 400 ? "Petición no válida" : "Regla de negocio incumplida", detail, hint, errors);
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(b.TemplateKey)) errors.Add(new FieldError("templateKey", "obligatorio"));
        if (b.CustomerId is null && string.IsNullOrWhiteSpace(b.To)) errors.Add(new FieldError("to", "obligatorio si no indicas customerId"));
        if (b.Channel is not (null or "EMAIL" or "SMS" or "PUSH")) errors.Add(new FieldError("channel", "EMAIL, SMS o PUSH"));
        if (b.Priority is not (null or "NORMAL" or "HIGH")) errors.Add(new FieldError("priority", "NORMAL o HIGH"));
        if (b.Category is not (null or "TRANSACTIONAL" or "MARKETING")) errors.Add(new FieldError("category", "TRANSACTIONAL o MARKETING"));
        if (errors.Count > 0) return (400, Problem(400, "VALIDATION_FAILED", "Notificación no válida.", "Ejemplo: { \"customerId\": \"...\", \"templateKey\": \"welcome\", \"vars\": { \"firstName\": \"Ana\", \"customerNumber\": \"CUS-900001\" } }", errors));

        string channel = b.Channel ?? "EMAIL", lang = b.Language ?? "sv", to = b.To ?? "";
        bool marketingConsent = true;
        (string From, string To) quiet = ("22:00", "08:00");
        if (b.CustomerId is not null)
        {
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            if (c.Status == 404) return (404, Problem(404, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}."));
            var p = await Downstream.Get(http, "preferences-service", $"/api/v1/internal/customers/{b.CustomerId}/preferences", forwardAuth: false);
            if (p.Ok)
            {
                channel = b.Channel ?? p.Str("channel") ?? "EMAIL";
                if (channel == "POST") channel = "EMAIL";
                lang = b.Language ?? p.Str("language") ?? "sv";
                marketingConsent = p.Json.GetProperty("marketingConsent").GetBoolean();
                var q = p.Json.GetProperty("quietHours");
                quiet = (q.GetProperty("from").GetString()!, q.GetProperty("to").GetString()!);
            }
            to = b.To ?? (channel == "EMAIL" ? c.Str("email") : c.Str("phone")) ?? "";
            if (to.Length == 0) return (422, Problem(422, "NO_CONTACT_FOR_CHANNEL", $"El cliente no tiene {(channel == "EMAIL" ? "email" : "teléfono")} para el canal {channel}."));
        }
        var vars = b.Vars ?? new();
        var render = await Downstream.Post(http, "message-template-service", $"/api/v1/templates/{b.TemplateKey}/render", new { language = lang, vars }, forwardAuth: false);
        if (render.Status == 404) return (404, Problem(404, "TEMPLATE_NOT_FOUND", $"No existe la plantilla {b.TemplateKey}."));
        if (render.Status == 422) return (422, Problem(422, "MISSING_VARIABLES", render.Str("detail") ?? "Faltan variables.", "Las variables las pide la plantilla: GET message-template-service /api/v1/templates/" + b.TemplateKey));
        if (!render.Ok) return (502, Problem(502, "DEPENDENCY_ERROR", "message-template-service respondió " + render.Status));

        var now = clock.UtcNow; var local = clock.LocalNow;
        var category = b.Category ?? (b.TemplateKey!.StartsWith("marketing.") ? "MARKETING" : "TRANSACTIONAL");
        var priority = b.Priority ?? "NORMAL";
        string status = "SENT"; string? reason = null; DateTimeOffset? scheduled = null;
        if (category == "MARKETING" && !marketingConsent) { status = "SUPPRESSED"; reason = "NO_MARKETING_CONSENT"; }
        else if (channel != "EMAIL" && priority != "HIGH" && b.CustomerId is not null && QuietUntil(local, quiet.From, quiet.To) is { } until)
        { status = "SCHEDULED"; reason = "QUIET_HOURS"; scheduled = until; }
        var id = "ntf_" + Guid.NewGuid().ToString("N")[..16];
        using var db = ctx.OpenDb();
        db.Execute(@"INSERT INTO notifications VALUES (@id, @cust, @channel, @to, @key, @lang, @ver, @status, @reason, @priority, @category, @ref, @created, @st, @sf, NULL, @subject, @body)",
            new { id, cust = b.CustomerId, channel, to, key = b.TemplateKey, lang = render.Str("language"), ver = render.Json.GetProperty("version").GetInt32(), status = status == "SENT" ? "PENDING" : status,
                  reason, priority, category, @ref = b.Reference, created = Nordic.Iso(local), st = scheduled?.UtcTicks, sf = scheduled is null ? null : Nordic.Iso(scheduled.Value),
                  subject = render.Str("subject"), body = render.Str("body") });
        if (status == "SENT") Deliver(db, http, clock, id);
        return (201, Load(db, id)!);
    }

    private static void Deliver(Microsoft.Data.Sqlite.SqliteConnection db, HttpContext? http, IClock clock, string id)
    {
        var n = db.QuerySingle<(string Channel, string To, string? Subject, string Body, string? Cust, string Key)>(
            "SELECT channel, recipient, subject, body, customer_id, template_key FROM notifications WHERE id = @id", new { id });
        var now = clock.UtcNow;
        var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM mailbox") ?? 0) + 1;
        db.Execute("INSERT INTO mailbox VALUES (@mid, @id, @Channel, @To, @Subject, @Body, @at, @et, @ea, @seq)",
            new { mid = "msg_" + Guid.NewGuid().ToString("N")[..16], id, n.Channel, n.To, n.Subject, n.Body, at = Nordic.Iso(Nordic.ToStockholm(now)),
                  et = now.AddHours(24).UtcTicks, ea = Nordic.Iso(Nordic.ToStockholm(now.AddHours(24))), seq });
        db.Execute("UPDATE notifications SET status = 'SENT', sent_at = @at WHERE id = @id", new { at = Nordic.Iso(Nordic.ToStockholm(now)), id });
        Outbox.Add(db, http, clock, "notification.sent", id, new { notificationId = id, customerId = n.Cust, channel = n.Channel, templateKey = n.Key });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/notifications", async (HttpContext http, NotificationRequest b) =>
        {
            var (status, body) = await Create(http, clock, ctx, b);
            if (status == 201 && body is Notification n) return Results.Created($"/api/v1/notifications/{n.Id}", n);
            return Results.Json(body, statusCode: status, contentType: "application/problem+json; charset=utf-8");
        }).WithTags("Notificaciones").WithSummary("Enviar una notificación (respeta preferencias, consentimiento y horas de silencio)").Produces<Notification>(201);

        app.MapGet("/api/v1/notifications/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var n = Load(db, id);
            return n is null ? Problems.NotFound(http, "NOTIFICATION_NOT_FOUND", $"No existe la notificación {id}.", null) : Results.Ok(n);
        }).WithTags("Notificaciones").WithSummary("Una notificación").Produces<Notification>();

        app.MapGet("/api/v1/customers/{customerId}/notifications", async (HttpContext http, string customerId, string? status) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "contact:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM notifications WHERE customer_id = @customerId AND (@status IS NULL OR status = @status) ORDER BY created_at DESC", new { customerId, status });
            return Results.Ok(ids.Select(i => Load(db, i)!).ToList());
        }).WithTags("Notificaciones").WithSummary("Notificaciones de un cliente").Produces<List<Notification>>();

        app.MapGet("/api/v1/mailbox", (HttpContext http, string? to, string? channel, int? limit) =>
        {
            if (string.IsNullOrWhiteSpace(to)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Indica to (email o teléfono).", "Ejemplo: /api/v1/mailbox?to=ana.lindqvist@correo.nordvik.test");
            using var db = ctx.OpenDb();
            db.Execute("DELETE FROM mailbox WHERE expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
            var items = db.Query<MailboxMessage>(@"SELECT id, notification_id AS NotificationId, channel, recipient AS ""To"", subject, body, delivered_at AS DeliveredAt, expires_at AS ExpiresAt
                                                   FROM mailbox WHERE recipient = @to AND (@channel IS NULL OR channel = @channel) ORDER BY seq DESC LIMIT @limit",
                new { to, channel, limit = Math.Clamp(limit ?? 20, 1, 200) }).ToList();
            return Results.Ok(new { to, count = items.Count, items });
        }).WithTags("Buzón de pruebas").WithSummary("Buzón simulado: lo que le ha llegado a un email o teléfono (lo más nuevo primero, se borra a las 24 h)");

        app.MapPost("/api/v1/notifications/dispatch", () => Results.Ok(new { delivered = DispatchScheduled(ctx, clock) }))
            .WithTags("Notificaciones").WithSummary("Entregar ya las programadas cuya hora llegó (también lo hace solo cada segundo)");

        EventConsumer.Start(app, ctx, new[] { "order.executed", "payment.settled", "payment.rejected", "deposit.received", "contract.sent" }, async ev =>
        {
            var p = ev.Payload;
            if (!p.TryGetProperty("customerId", out var cust) || cust.ValueKind != JsonValueKind.String) return;
            var key = ev.Topic switch { "contract.sent" => "contract.ready-to-sign", var t => t };
            var vars = p.TryGetProperty("notificationVars", out var v) && v.ValueKind == JsonValueKind.Object
                ? v.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()) : new Dictionary<string, JsonElement>();
            await Create(null, clock, ctx, new NotificationRequest(cust.GetString(), null, null, key, null, vars, ev.Topic == "payment.rejected" ? "HIGH" : null, "TRANSACTIONAL", ev.EventId));
        });

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => { try { DispatchScheduled(ctx, clock); } catch { } }, null, 1000, 1000));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private static int DispatchScheduled(ServiceContext ctx, IClock clock)
    {
        using var db = ctx.OpenDb();
        var due = db.Query<string>("SELECT id FROM notifications WHERE status = 'SCHEDULED' AND scheduled_ticks <= @t", new { t = clock.UtcNow.UtcTicks }).ToList();
        foreach (var id in due) Deliver(db, null, clock, id);
        return due.Count;
    }
}
