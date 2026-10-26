using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record MeetingRequest(string? AdvisorId, string? CustomerId, string? Start, string? Channel, string? Topic);
public sealed record CancelMeetingRequest(string? Reason);

/// <summary>
/// meeting-scheduler: la agenda del asesor. Huecos de 60 minutos en su horario, solo en días hábiles de Estocolmo, nunca en el pasado
/// (reloj virtual) ni en sus ausencias. Un hueco se reserva una sola vez. Cancelar con menos de 24 h queda marcado como tardío.
/// Los recordatorios (24 h antes) salen al ejecutar /reminders/run, por notification-service.
/// </summary>
public sealed class MeetingScheduler : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "meeting-scheduler", "G-advice", 6604, 3, "Asesoramiento y comunicación",
        "Agenda del asesor: huecos de 60 min en su horario y días hábiles, reservas sin solapes, cancelación tardía (<24 h) y recordatorios 24 h antes por notification-service.",
        DependsOn: new[] { "advisor-directory", "permission-service", "customer-registry", "notification-service", "clock-service" },
        Emits: new[] { "meeting.booked", "meeting.cancelled" });

    public static readonly string[] Channels = { "OFFICE", "PHONE", "VIDEO" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS meetings (id TEXT PRIMARY KEY, advisor_id TEXT, customer_id TEXT, start_local TEXT, start_ticks INTEGER, channel TEXT, topic TEXT, status TEXT,
                        booked_by TEXT, booked_at TEXT, reminder_ticks INTEGER, reminder_sent_at TEXT, cancelled_at TEXT, cancel_reason TEXT, late_cancellation INTEGER)");
    }

    private static DateTimeOffset LocalToUtc(DateTime local) => new DateTimeOffset(local, Nordic.Stockholm.GetUtcOffset(local)).ToUniversalTime();

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Adv, string Cust, string Start, string Channel, string? Topic, string Status, string By, string At, long RemT, string? RemSent, string? Cancelled, string? Reason, long? Late)>(
            "SELECT id, advisor_id, customer_id, start_local, channel, topic, status, booked_by, booked_at, reminder_ticks, reminder_sent_at, cancelled_at, cancel_reason, late_cancellation FROM meetings WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, advisorId = r.Adv, customerId = r.Cust, start = r.Start, durationMinutes = 60, channel = r.Channel, topic = r.Topic, status = r.Status, bookedBy = r.By, bookedAt = r.At,
            reminderAt = Nordic.Iso(Nordic.ToStockholm(new DateTimeOffset(r.RemT, TimeSpan.Zero))), reminderSentAt = r.RemSent, cancelledAt = r.Cancelled, cancelReason = r.Reason, lateCancellation = r.Late == 1,
        };
    }

    private static async Task<JsonElement?> Advisor(HttpContext http, string id)
    {
        var a = await Downstream.Get(http, "advisor-directory", $"/api/v1/internal/advisors/{id}", forwardAuth: false);
        return a.Ok ? a.Json : null;
    }

    private static List<(string Start, bool Free)> Slots(Microsoft.Data.Sqlite.SqliteConnection db, JsonElement advisor, DateOnly date, DateTimeOffset now)
    {
        var res = new List<(string, bool)>();
        var ds = Money.Date(date);
        if (!NordicCalendar.IsTradingDay("XSTO", date)) return res;
        if (advisor.GetProperty("absences").EnumerateArray().Any(a => string.CompareOrdinal(a.GetProperty("from").GetString(), ds) <= 0 && string.CompareOrdinal(a.GetProperty("to").GetString(), ds) >= 0)) return res;
        var start = TimeOnly.Parse(advisor.GetProperty("workingHours").GetProperty("start").GetString()!, CultureInfo.InvariantCulture);
        var end = TimeOnly.Parse(advisor.GetProperty("workingHours").GetProperty("end").GetString()!, CultureInfo.InvariantCulture);
        var booked = db.Query<string>("SELECT start_local FROM meetings WHERE advisor_id = @a AND status = 'BOOKED' AND start_local LIKE @d", new { a = advisor.GetProperty("id").GetString(), d = ds + "%" }).ToHashSet();
        for (var t = start; t < end; t = t.AddHours(1))
        {
            var local = date.ToDateTime(t);
            if (LocalToUtc(local) <= now) continue;
            var key = local.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            res.Add((key, !booked.Contains(key)));
        }
        return res;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/advisors/{advisorId}/slots", async (HttpContext http, string advisorId, string? date) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            if (!Money.TryParseDate(date, out var d)) return Problems.BadRequest(http, "VALIDATION_FAILED", "date en AAAA-MM-DD.", null);
            var a = await Advisor(http, advisorId);
            if (a is null) return Problems.NotFound(http, "ADVISOR_NOT_FOUND", $"No existe el asesor {advisorId}.", null);
            using var db = ctx.OpenDb();
            var slots = Slots(db, a.Value, d, clock.UtcNow);
            var reason = slots.Count > 0 ? null : !NordicCalendar.IsTradingDay("XSTO", d) ? "día no hábil" : "sin huecos (ausencia, pasado o fuera de horario)";
            return Results.Ok(new { advisorId = a.Value.GetProperty("id").GetString(), date = Money.Date(d), slots = slots.Select(s => new { start = s.Start, durationMinutes = 60, free = s.Free }), freeCount = slots.Count(s => s.Free), reason });
        }).WithTags("Agenda").WithSummary("Huecos de un asesor un día (hora de Estocolmo)");

        app.MapPost("/api/v1/meetings", async (HttpContext http, MeetingRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            var customerId = b.CustomerId ?? p.CustomerId;
            var advisorId = b.AdvisorId ?? p.AdvisorId;
            var channel = (b.Channel ?? "VIDEO").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (customerId is null) errors.Add(new FieldError("customerId", "obligatorio"));
            if (advisorId is null) errors.Add(new FieldError("advisorId", "obligatorio"));
            if (!DateTime.TryParseExact(b.Start, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) errors.Add(new FieldError("start", "hora local de Estocolmo AAAA-MM-DDTHH:mm, en punto"));
            if (!Channels.Contains(channel)) errors.Add(new FieldError("channel", string.Join(", ", Channels)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Reserva no válida.", "Ejemplo: { \"advisorId\": \"a000...\", \"customerId\": \"c000...\", \"start\": \"2026-10-28T10:00\", \"channel\": \"VIDEO\" }", errors);
            if (p.Role == "Advisor" && advisorId != p.AdvisorId) return Problems.Of(http, 403, "NOT_YOUR_AGENDA", "Prohibido", "Un asesor solo reserva en su propia agenda.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", customerId!) is { } g) return g;
            var a = await Advisor(http, advisorId!);
            if (a is null) return Problems.NotFound(http, "ADVISOR_NOT_FOUND", $"No existe el asesor {advisorId}.", null);
            var assigned = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{advisorId}/customers", forwardAuth: false);
            if (!assigned.Json.EnumerateArray().Any(x => x.GetProperty("customerId").GetString() == customerId))
                return Problems.Unprocessable(http, "NOT_YOUR_ADVISOR", "Ese asesor no tiene asignado a este cliente.", "Mira quién es su asesor: GET advisor-directory /api/v1/customers/{id}/advisor");
            var date = DateOnly.FromDateTime(start);
            using var db = ctx.OpenDb();
            var key = start.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            var startUtc = LocalToUtc(start);
            if (startUtc <= clock.UtcNow) return Problems.Unprocessable(http, "MEETING_IN_THE_PAST", "Esa hora ya ha pasado (reloj virtual).", null);
            if (!NordicCalendar.IsTradingDay("XSTO", date)) return Problems.Unprocessable(http, "NOT_A_WORKING_DAY", $"El {Money.Date(date)} no es día hábil.", null);
            var slot = Slots(db, a.Value, date, clock.UtcNow).Where(s => s.Start == key).ToList();
            if (slot.Count == 0) return Problems.Unprocessable(http, "OUTSIDE_WORKING_HOURS", "Esa hora no está en el horario del asesor (o está ausente, o no es en punto).", "Pide los huecos: GET /api/v1/advisors/{id}/slots?date=...");
            if (!slot[0].Free) return Problems.Conflict(http, "SLOT_TAKEN", "Ese hueco ya está reservado.", null);
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM meetings WHERE customer_id = @customerId AND start_local = @key AND status = 'BOOKED'", new { customerId, key }) > 0)
                return Problems.Conflict(http, "CUSTOMER_BUSY", "El cliente ya tiene otra reunión a esa hora.", null);
            var id = "mtg_" + Guid.NewGuid().ToString("N")[..12];
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO meetings VALUES (@id, @advisorId, @customerId, @key, @t, @channel, @Topic, 'BOOKED', @by, @at, @rem, NULL, NULL, NULL, NULL)",
                new { id, advisorId, customerId, key, t = startUtc.UtcTicks, channel, b.Topic, by = p.Sub, at = Nordic.Iso(clock.LocalNow), rem = startUtc.AddHours(-24).UtcTicks }, tx);
            Outbox.Add(db, http, clock, "meeting.booked", id, new { meetingId = id, advisorId, customerId, start = key, channel }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/meetings/{id}", Load(db, id));
        }).WithTags("Agenda").WithSummary("Reservar un hueco (el cliente con su asesor, o el asesor en su agenda)");

        app.MapGet("/api/v1/meetings/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var cust = db.ExecuteScalar<string?>("SELECT customer_id FROM meetings WHERE id = @id", new { id });
            if (cust is null || (p.Role == "Customer" && p.CustomerId != cust)) return Problems.NotFound(http, "MEETING_NOT_FOUND", $"No existe la reunión {id}.", null);
            if (p.Role != "Customer" && await CustomerGuard.Check(http, p, "customer:read", cust) is { } g) return g;
            return Results.Ok(Load(db, id));
        }).WithTags("Agenda").WithSummary("Una reunión");

        app.MapGet("/api/v1/customers/{customerId}/meetings", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM meetings WHERE customer_id = @customerId ORDER BY start_ticks", new { customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Agenda").WithSummary("Reuniones de un cliente");

        app.MapPost("/api/v1/meetings/{id}/cancel", async (HttpContext http, string id, CancelMeetingRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var m = db.QuerySingleOrDefault<(string Id, string Adv, string Cust, long T, string Status)>("SELECT id, advisor_id, customer_id, start_ticks, status FROM meetings WHERE id = @id", new { id });
            if (m.Id is null || (p.Role == "Customer" && p.CustomerId != m.Cust)) return Problems.NotFound(http, "MEETING_NOT_FOUND", $"No existe la reunión {id}.", null);
            if (p.Role == "Advisor" && p.AdvisorId != m.Adv) return Problems.Of(http, 403, "NOT_YOUR_AGENDA", "Prohibido", "No es una reunión de tu agenda.", null);
            if (m.Status != "BOOKED") return Problems.Conflict(http, "MEETING_NOT_BOOKED", $"La reunión está {m.Status}.", null);
            if (m.T <= clock.UtcNow.UtcTicks) return Problems.Conflict(http, "MEETING_ALREADY_STARTED", "La reunión ya empezó o pasó.", null);
            var late = m.T - clock.UtcNow.UtcTicks < TimeSpan.FromHours(24).Ticks;
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE meetings SET status = 'CANCELLED', cancelled_at = @at, cancel_reason = @r, late_cancellation = @late WHERE id = @id",
                new { at = Nordic.Iso(clock.LocalNow), r = b.Reason, late = late ? 1 : 0, id }, tx);
            Outbox.Add(db, http, clock, "meeting.cancelled", id, new { meetingId = id, by = p.Role, late }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Agenda").WithSummary("Cancelar (el hueco vuelve a estar libre; con menos de 24 h queda como cancelación tardía)");

        app.MapPost("/api/v1/reminders/run", async (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin", "Service" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var now = clock.UtcNow.UtcTicks;
            var due = db.Query<(string Id, string Cust, string Start, string Channel)>("SELECT id, customer_id, start_local, channel FROM meetings WHERE status = 'BOOKED' AND reminder_sent_at IS NULL AND reminder_ticks <= @now AND start_ticks > @now",
                new { now }).ToList();
            var sent = new List<string>();
            foreach (var m in due)
            {
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{m.Cust}", forwardAuth: false);
                var n = await Downstream.Post(http, "notification-service", "/api/v1/notifications", new
                {
                    customerId = m.Cust, templateKey = "meeting.reminder", language = c.Str("language") ?? "sv", category = "TRANSACTIONAL", reference = m.Id,
                    vars = new { firstName = c.Str("firstName"), start = m.Start.Replace('T', ' '), channel = m.Channel },
                }, forwardAuth: false);
                if (n.Ok) { db.Execute("UPDATE meetings SET reminder_sent_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id = m.Id }); sent.Add(m.Id); }
            }
            return Results.Ok(new { sent, count = sent.Count });
        }).WithTags("Agenda").WithSummary("Enviar los recordatorios que tocan (24 h antes de cada reunión)");
    }
}
