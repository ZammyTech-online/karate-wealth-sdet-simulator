using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record BreachRequest(string? DetectedAt, string? Description, string[]? DataCategories, string[]? AffectedCustomerIds, string? RiskToRights);
public sealed record AuthorityNoticeRequest(string? Reference);

/// <summary>
/// breach-service: brechas de datos personales (RGPD art. 33 y 34). Desde que se detecta hay 72 horas (reloj virtual) para avisar a la
/// autoridad; si el riesgo para las personas es HIGH, además hay que avisar a cada afectado (notification-service, plantilla breach.notice).
/// OPEN → AUTHORITY_NOTIFIED → (SUBJECTS_NOTIFIED) → CLOSED. Un aviso tarde queda marcado como late para siempre.
/// </summary>
public sealed class BreachService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "breach-service", "F-privacy", 6509, 4, "Privacidad",
        "Brechas de datos: plazo de 72 h para avisar a la autoridad, aviso a los afectados si el riesgo es alto, marcas de retraso y cierre.",
        DependsOn: new[] { "notification-service", "customer-registry", "clock-service" }, Emits: new[] { "breach.reported", "breach.authority-notified", "breach.closed" });

    public const int AuthorityHours = 72;
    public static readonly string[] Categories = { "IDENTITY", "CONTACT", "FINANCIAL", "HEALTH", "NATIONAL_ID", "CREDENTIALS" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS breaches (id TEXT PRIMARY KEY, detected_at TEXT, description TEXT, categories TEXT, affected TEXT, risk TEXT, status TEXT, authority_due TEXT,
                        authority_notified_at TEXT, authority_ref TEXT, subjects_notified INTEGER, closed_at TEXT, reported_by TEXT, created_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Det, string Desc, string Cat, string Aff, string Risk, string Status, string Due, string? Not, string? Ref, long? Subj, string? Closed, string By)>(
            "SELECT id, detected_at, description, categories, affected, risk, status, authority_due, authority_notified_at, authority_ref, subjects_notified, closed_at, reported_by FROM breaches WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var due = DateTimeOffset.Parse(r.Due);
        var late = r.Not is null ? clock.UtcNow > due : DateTimeOffset.Parse(r.Not) > due;
        var affected = JsonSerializer.Deserialize<string[]>(r.Aff)!;
        return new
        {
            id = r.Id, detectedAt = r.Det, description = r.Desc, dataCategories = JsonSerializer.Deserialize<string[]>(r.Cat), affectedCount = affected.Length, riskToRights = r.Risk,
            status = r.Status, notifyAuthorityBy = r.Due, authorityNotifiedAt = r.Not, authorityReference = r.Ref, authorityNoticeLate = late,
            hoursLeft = r.Not is null ? Math.Round((due - clock.UtcNow).TotalHours, 1) : (double?)null, subjectsNotified = r.Subj, subjectNoticeRequired = r.Risk == "HIGH",
            closedAt = r.Closed, reportedBy = r.By,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Compliance", "Admin" };

        app.MapPost("/api/v1/breaches", (HttpContext http, BreachRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            var risk = (b.RiskToRights ?? "").ToUpperInvariant();
            var cats = (b.DataCategories ?? Array.Empty<string>()).Select(c => c.ToUpperInvariant()).Distinct().ToArray();
            var errors = new List<FieldError>();
            if (!DateTimeOffset.TryParse(b.DetectedAt, out var detected)) errors.Add(new FieldError("detectedAt", "fecha y hora ISO 8601"));
            else if (detected > clock.UtcNow.AddMinutes(1)) errors.Add(new FieldError("detectedAt", "no puede ser futura"));
            if (string.IsNullOrWhiteSpace(b.Description)) errors.Add(new FieldError("description", "obligatorio"));
            if (cats.Length == 0 || cats.Any(c => !Categories.Contains(c))) errors.Add(new FieldError("dataCategories", string.Join(", ", Categories)));
            if (risk is not ("LOW" or "HIGH")) errors.Add(new FieldError("riskToRights", "LOW o HIGH"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Brecha no válida.", "Ejemplo: { \"detectedAt\": \"2026-10-20T09:00:00+02:00\", \"description\": \"...\", \"dataCategories\": [\"CONTACT\"], \"affectedCustomerIds\": [], \"riskToRights\": \"LOW\" }", errors);
            var id = "brc_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO breaches VALUES (@id, @det, @Description, @cats, @aff, @risk, 'OPEN', @due, NULL, NULL, NULL, NULL, @by, @at)",
                    new { id, det = detected.ToString("O"), b.Description, cats = JsonSerializer.Serialize(cats), aff = JsonSerializer.Serialize(b.AffectedCustomerIds ?? Array.Empty<string>()), risk,
                          due = detected.ToUniversalTime().AddHours(AuthorityHours).ToString("O"), by = p.Sub, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "breach.reported", id, new { breachId = id, risk, categories = cats }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/breaches/{id}", Load(db, id, clock));
        }).WithTags("Brechas").WithSummary("Registrar una brecha (empieza el plazo de 72 h)");

        app.MapGet("/api/v1/breaches/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id, clock) is { } x ? Results.Ok(x) : Problems.NotFound(http, "BREACH_NOT_FOUND", $"No existe la brecha {id}.", null);
        }).WithTags("Brechas").WithSummary("Una brecha con su plazo y horas restantes");

        app.MapPost("/api/v1/breaches/{id}/notify-authority", (HttpContext http, string id, AuthorityNoticeRequest? b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var st = db.ExecuteScalar<string?>("SELECT status FROM breaches WHERE id = @id", new { id });
            if (st is null) return Problems.NotFound(http, "BREACH_NOT_FOUND", $"No existe la brecha {id}.", null);
            if (st != "OPEN") return Problems.Conflict(http, "AUTHORITY_ALREADY_NOTIFIED", "La autoridad ya está avisada.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE breaches SET status = 'AUTHORITY_NOTIFIED', authority_notified_at = @at, authority_ref = @r WHERE id = @id",
                new { id, at = clock.UtcNow.ToString("O"), r = b?.Reference ?? "IMY-" + Random.Shared.Next(100000, 999999) }, tx);
            Outbox.Add(db, http, clock, "breach.authority-notified", id, new { breachId = id }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Brechas").WithSummary("Avisar a la autoridad de control (si pasan las 72 h queda late)");

        app.MapPost("/api/v1/breaches/{id}/notify-subjects", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            string? risk, status, aff, cats;
            using (var db0 = ctx.OpenDb()) (risk, status, aff, cats) = db0.QuerySingleOrDefault<(string?, string?, string?, string?)>("SELECT risk, status, affected, categories FROM breaches WHERE id = @id", new { id });
            if (status is null) return Problems.NotFound(http, "BREACH_NOT_FOUND", $"No existe la brecha {id}.", null);
            if (risk != "HIGH") return Problems.Unprocessable(http, "SUBJECT_NOTICE_NOT_REQUIRED", "Con riesgo LOW no se avisa a cada afectado (art. 34).", null);
            if (status == "OPEN") return Problems.Conflict(http, "AUTHORITY_FIRST", "Primero se avisa a la autoridad.", null);
            if (status != "AUTHORITY_NOTIFIED") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"La brecha está {status}.", null);
            var sent = 0; var failed = new List<string>();
            foreach (var c in JsonSerializer.Deserialize<string[]>(aff!)!)
            {
                var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{c}", forwardAuth: false);
                if (!cust.Ok) { failed.Add(c); continue; }
                var n = await Downstream.Send(http, "notification-service", HttpMethod.Post, "/api/v1/notifications",
                    new { customerId = c, channel = "EMAIL", templateKey = "breach.notice", priority = "HIGH", category = "TRANSACTIONAL", reference = id,
                          vars = new Dictionary<string, object> { ["firstName"] = cust.Str("firstName") ?? "", ["categories"] = string.Join(", ", JsonSerializer.Deserialize<string[]>(cats!)!) } }, forwardAuth: false);
                if (n.Ok) sent++; else failed.Add(c);
            }
            using var db = ctx.OpenDb();
            db.Execute("UPDATE breaches SET status = 'SUBJECTS_NOTIFIED', subjects_notified = @sent WHERE id = @id", new { id, sent });
            return Results.Ok(new { breach = Load(db, id, clock), sent, failed });
        }).WithTags("Brechas").WithSummary("Avisar a los afectados (solo riesgo HIGH, después de la autoridad)");

        app.MapPost("/api/v1/breaches/{id}/close", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string? Status, string? Risk)>("SELECT status, risk FROM breaches WHERE id = @id", new { id });
            if (r.Status is null) return Problems.NotFound(http, "BREACH_NOT_FOUND", $"No existe la brecha {id}.", null);
            if (r.Status == "OPEN") return Problems.Conflict(http, "AUTHORITY_NOT_NOTIFIED", "No se cierra una brecha sin avisar a la autoridad.", null);
            if (r.Status == "AUTHORITY_NOTIFIED" && r.Risk == "HIGH") return Problems.Conflict(http, "SUBJECTS_NOT_NOTIFIED", "Con riesgo HIGH hay que avisar antes a los afectados.", null);
            if (r.Status == "CLOSED") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", "Ya está cerrada.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE breaches SET status = 'CLOSED', closed_at = @at WHERE id = @id", new { id, at = clock.UtcNow.ToString("O") }, tx);
            Outbox.Add(db, http, clock, "breach.closed", id, new { breachId = id }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Brechas").WithSummary("Cerrar");

        app.MapGet("/api/v1/breaches/overdue", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Id, string Due)>("SELECT id, authority_due FROM breaches WHERE status = 'OPEN'").Where(x => DateTimeOffset.Parse(x.Due) < clock.UtcNow)
                .Select(x => Load(db, x.Id, clock)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Brechas").WithSummary("Brechas sin avisar a la autoridad y fuera de plazo");

        app.MapGet("/api/v1/internal/breaches", (string? month) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM breaches WHERE (@month IS NULL OR substr(created_at, 1, 7) = @month) ORDER BY created_at", new { month }).Select(i => Load(db, i, clock)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Interno").WithSummary("(interno) Brechas de un mes (regulatory-reporting)");
    }
}
