using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ComplaintRequest(string? CustomerId, string? Category, string? Channel, string? Description);
public sealed record ResolveComplaintRequest(string? Outcome, string? Explanation, string? Compensation);

/// <summary>
/// complaints-service: reclamaciones de clientes con plazos legales en días HÁBILES (calendario de Estocolmo): acuse en 2 días hábiles y
/// respuesta en 15. Cada reclamación abre un caso COMPLAINT en case-management. OPEN → ACKNOWLEDGED → RESOLVED (UPHELD, PARTIALLY_UPHELD,
/// REJECTED, con compensación opcional). /overdue lista las que han pasado un plazo según el reloj virtual.
/// </summary>
public sealed class ComplaintsService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "complaints-service", "E-compliance", 6407, 4, "Cumplimiento",
        "Reclamaciones de clientes con plazos legales en días hábiles (acuse en 2, respuesta en 15), caso en case-management, resolución y vencidas.",
        DependsOn: new[] { "case-management", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "complaint.received", "complaint.resolved" });

    public static readonly string[] Categories = { "FEES", "ADVICE", "SERVICE", "PENSION", "INSURANCE", "OTHER" };
    public static readonly string[] Outcomes = { "UPHELD", "PARTIALLY_UPHELD", "REJECTED" };
    public const int AckDays = 2, ResolveDays = 15;

    /// <summary>Fecha límite: N días hábiles después del día de entrada (el día de entrada no cuenta).</summary>
    public static DateOnly Due(DateOnly received, int businessDays) => NordicCalendar.AddTradingDays("XSTO", received, businessDays);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS complaints (id TEXT PRIMARY KEY, customer_id TEXT, category TEXT, channel TEXT, description TEXT, status TEXT, received_on TEXT, ack_due TEXT, resolve_due TEXT,
                        acknowledged_on TEXT, resolved_on TEXT, outcome TEXT, explanation TEXT, compensation TEXT, case_id TEXT, created_by TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Cat, string Ch, string Desc, string Status, string Rec, string AckDue, string ResDue, string? AckOn, string? ResOn, string? Outcome, string? Expl, string? Comp, string? Case)>(
            "SELECT id, customer_id, category, channel, description, status, received_on, ack_due, resolve_due, acknowledged_on, resolved_on, outcome, explanation, compensation, case_id FROM complaints WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var today = clock.Today;
        var ackLate = (r.AckOn is null ? today : DateOnly.Parse(r.AckOn)) > DateOnly.Parse(r.AckDue);
        var resLate = (r.ResOn is null ? today : DateOnly.Parse(r.ResOn)) > DateOnly.Parse(r.ResDue);
        return new
        {
            id = r.Id, customerId = r.Cust, category = r.Cat, channel = r.Ch, description = r.Desc, status = r.Status, receivedOn = r.Rec,
            acknowledgeBy = r.AckDue, resolveBy = r.ResDue, acknowledgedOn = r.AckOn, resolvedOn = r.ResOn, outcome = r.Outcome, explanation = r.Expl,
            compensation = r.Comp is null ? null : new { amount = r.Comp, currency = "SEK" }, caseId = r.Case,
            breaches = new { acknowledgement = ackLate, resolution = resLate },
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Compliance", "Admin" };

        app.MapPost("/api/v1/complaints", async (HttpContext http, ComplaintRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor", "Operations" }) is { } denied) return denied;
            var cat = (b.Category ?? "").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (b.CustomerId is null && p.CustomerId is null) errors.Add(new FieldError("customerId", "obligatorio"));
            if (!Categories.Contains(cat)) errors.Add(new FieldError("category", string.Join(", ", Categories)));
            if (string.IsNullOrWhiteSpace(b.Description) || b.Description.Length < 10) errors.Add(new FieldError("description", "al menos 10 caracteres"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Reclamación no válida.", null, errors);
            var customerId = b.CustomerId ?? p.CustomerId!;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var today = clock.Today;
            var id = "cmp_" + Guid.NewGuid().ToString("N")[..12];
            var cs = await Downstream.Send(http, "case-management", HttpMethod.Post, "/api/v1/cases",
                new { type = "COMPLAINT", customerId, priority = cat is "FEES" or "PENSION" ? "HIGH" : "MEDIUM", summary = $"Reclamación {cat}: {b.Description![..Math.Min(60, b.Description.Length)]}", sourceService = "complaints-service", sourceRef = id },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO complaints VALUES (@id, @customerId, @cat, @ch, @Description, 'OPEN', @rec, @ack, @res, NULL, NULL, NULL, NULL, NULL, @caseId, @by)",
                    new { id, customerId, cat, ch = (b.Channel ?? (p.Role == "Customer" ? "WEB" : "ADVISOR")).ToUpperInvariant(), b.Description, rec = Money.Date(today),
                          ack = Money.Date(Due(today, AckDays)), res = Money.Date(Due(today, ResolveDays)), caseId = cs.Ok ? cs.Str("id") : null, by = p.Sub }, tx);
                Outbox.Add(db, http, clock, "complaint.received", id, new { complaintId = id, customerId, category = cat }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/complaints/{id}", Load(db, id, clock));
        }).WithTags("Reclamaciones").WithSummary("Presentar una reclamación (calcula los plazos legales y abre un caso)");

        app.MapGet("/api/v1/complaints/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? owner;
            using (var db0 = ctx.OpenDb()) owner = db0.ExecuteScalar<string?>("SELECT customer_id FROM complaints WHERE id = @id", new { id });
            if (owner is null) return Problems.NotFound(http, "COMPLAINT_NOT_FOUND", $"No existe la reclamación {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", owner) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Reclamaciones").WithSummary("Una reclamación con sus plazos y si se han incumplido");

        app.MapPost("/api/v1/complaints/{id}/acknowledge", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var st = db.ExecuteScalar<string?>("SELECT status FROM complaints WHERE id = @id", new { id });
            if (st is null) return Problems.NotFound(http, "COMPLAINT_NOT_FOUND", $"No existe la reclamación {id}.", null);
            if (st != "OPEN") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Solo se acusa recibo de una reclamación OPEN (está {st}).", null);
            db.Execute("UPDATE complaints SET status = 'ACKNOWLEDGED', acknowledged_on = @d WHERE id = @id", new { id, d = Money.Date(clock.Today) });
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Reclamaciones").WithSummary("Acusar recibo (plazo: 2 días hábiles)");

        app.MapPost("/api/v1/complaints/{id}/resolve", (HttpContext http, string id, ResolveComplaintRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            var outcome = (b.Outcome ?? "").ToUpperInvariant();
            if (!Outcomes.Contains(outcome) || string.IsNullOrWhiteSpace(b.Explanation)) return Problems.BadRequest(http, "VALIDATION_FAILED", "outcome (" + string.Join(", ", Outcomes) + ") y explanation son obligatorios.", null);
            decimal comp = 0;
            if (b.Compensation is not null && (!Money.TryParse(b.Compensation, out comp) || comp < 0)) return Problems.BadRequest(http, "VALIDATION_FAILED", "compensation es un importe ≥ 0 como texto.", null);
            if (outcome == "REJECTED" && comp > 0) return Problems.Unprocessable(http, "COMPENSATION_ON_REJECTION", "Una reclamación rechazada no lleva compensación.", null);
            using var db = ctx.OpenDb();
            var st = db.ExecuteScalar<string?>("SELECT status FROM complaints WHERE id = @id", new { id });
            if (st is null) return Problems.NotFound(http, "COMPLAINT_NOT_FOUND", $"No existe la reclamación {id}.", null);
            if (st != "ACKNOWLEDGED") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", st == "OPEN" ? "Primero hay que acusar recibo." : $"La reclamación ya está {st}.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE complaints SET status = 'RESOLVED', resolved_on = @d, outcome = @outcome, explanation = @Explanation, compensation = @c WHERE id = @id",
                new { id, d = Money.Date(clock.Today), outcome, b.Explanation, c = b.Compensation is null ? null : Money.Format(comp, 2) }, tx);
            Outbox.Add(db, http, clock, "complaint.resolved", id, new { complaintId = id, outcome }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Reclamaciones").WithSummary("Resolver (plazo: 15 días hábiles)");

        app.MapGet("/api/v1/complaints/overdue", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var today = Money.Date(clock.Today);
            var items = db.Query<string>("SELECT id FROM complaints WHERE (status = 'OPEN' AND ack_due < @today) OR (status <> 'RESOLVED' AND resolve_due < @today) ORDER BY received_on", new { today })
                .Select(i => Load(db, i, clock)).ToList();
            return Results.Ok(new { asOf = today, items, count = items.Count });
        }).WithTags("Reclamaciones").WithSummary("Reclamaciones con algún plazo vencido");
    }
}
