using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record KycDecisionRequest(string? Decision, string? Reason);
public sealed record KycStep(string Step, string Service, string Outcome, string? Ref, int Status);

/// <summary>
/// kyc-aml-service: el expediente "conoce a tu cliente". Un caso ejecuta en orden: cribado de sanciones → comprobación PEP → riesgo.
/// Riesgo LOW sin coincidencias se aprueba solo (revisión en 3 años); MEDIUM también (revisión en 1 año); HIGH o una posible
/// coincidencia van a revisión de Cumplimiento con cuatro ojos: quien abrió el caso no puede decidirlo. PROHIBITED se rechaza.
/// </summary>
public sealed class KycAmlService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "kyc-aml-service", "E-compliance", 6400, 3, "Cumplimiento normativo",
        "Casos KYC: cribado + PEP + riesgo en orden, aprobación automática según riesgo, revisión de Cumplimiento con cuatro ojos y fecha de próxima revisión.",
        DependsOn: new[] { "sanctions-adapter", "pep-service", "risk-scoring", "customer-registry", "clock-service" },
        Emits: new[] { "kyc.approved", "kyc.rejected", "kyc.review-required" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS cases (id TEXT PRIMARY KEY, customer_id TEXT, status TEXT, risk_level TEXT, risk_score INTEGER, steps TEXT, opened_by TEXT, opened_at TEXT,
                        decided_by TEXT, decided_at TEXT, decision_reason TEXT, next_review_due TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Status, string? Level, long? Score, string Steps, string By, string At, string? DecBy, string? DecAt, string? Reason, string? Next)>(
            "SELECT id, customer_id, status, risk_level, risk_score, steps, opened_by, opened_at, decided_by, decided_at, decision_reason, next_review_due FROM cases WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, status = r.Status, riskLevel = r.Level, riskScore = r.Score, steps = JsonDocument.Parse(r.Steps).RootElement,
            openedBy = r.By, openedAt = r.At, decidedBy = r.DecBy, decidedAt = r.DecAt, decisionReason = r.Reason, nextReviewDue = r.Next,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/customers/{customerId}/kyc-cases", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM cases WHERE customer_id = @customerId AND status IN ('OPEN','PENDING_REVIEW')", new { customerId }) is { } open)
                    return Problems.Conflict(http, "KYC_CASE_ALREADY_OPEN", $"El cliente ya tiene un caso en curso: {open}.", "Termínalo antes de abrir otro.");
            var steps = new List<KycStep>();
            var svc = await ServiceToken.Header(clock);
            var scr = await Downstream.Send(http, "sanctions-adapter", HttpMethod.Post, "/api/v1/screenings", new { customerId }, forwardAuth: false, headers: svc);
            steps.Add(new KycStep("SANCTIONS_SCREENING", "sanctions-adapter", scr.Ok ? scr.Str("status")! : "ERROR", scr.Ok ? scr.Str("id") : null, scr.Status));
            var pep = await Downstream.Send(http, "pep-service", HttpMethod.Post, $"/api/v1/customers/{customerId}/pep-checks", null, forwardAuth: false, headers: svc);
            steps.Add(new KycStep("PEP_CHECK", "pep-service", pep.Ok ? pep.Str("status")! : "ERROR", null, pep.Status));
            var risk = await Downstream.Send(http, "risk-scoring", HttpMethod.Post, $"/api/v1/customers/{customerId}/risk-assessments", null, forwardAuth: false, headers: svc);
            steps.Add(new KycStep("RISK_SCORING", "risk-scoring", risk.Ok ? risk.Str("level")! : "ERROR", risk.Ok ? risk.Str("id") : null, risk.Status));
            var level = risk.Ok ? risk.Str("level") : null;
            int? score = risk.Ok ? risk.Json.GetProperty("score").GetInt32() : null;
            var anyError = steps.Any(s => s.Outcome == "ERROR");
            var potential = scr.Ok && scr.Str("status") is "POTENTIAL_MATCH" or "INCOMPLETE";
            var (status, years, topic) = anyError ? ("OPEN", 0, (string?)null)
                : level == "PROHIBITED" ? ("REJECTED", 0, "kyc.rejected")
                : level == "HIGH" || potential ? ("PENDING_REVIEW", 0, "kyc.review-required")
                : level == "MEDIUM" ? ("APPROVED", 1, "kyc.approved")
                : ("APPROVED", 3, "kyc.approved");
            var id = "kyc_" + Guid.NewGuid().ToString("N")[..14];
            var now = Nordic.Iso(clock.LocalNow);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO cases VALUES (@id, @customerId, @status, @level, @score, @steps, @by, @now, @decBy, @decAt, @reason, @next)",
                new { id, customerId, status, level, score, steps = JsonSerializer.Serialize(steps, Canonical.Web), by = p.Sub, now,
                      decBy = status is "APPROVED" or "REJECTED" ? "kyc-aml-service (automático)" : null, decAt = status is "APPROVED" or "REJECTED" ? now : null,
                      reason = status == "REJECTED" ? "Coincidencia de sanciones confirmada" : status == "APPROVED" ? $"Riesgo {level}: aprobación automática" : null,
                      next = years > 0 ? Money.Date(clock.Today.AddYears(years)) : null }, tx);
            if (topic is not null) Outbox.Add(db, http, clock, topic, customerId, new { caseId = id, customerId, riskLevel = level }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/kyc-cases/{id}", Load(db, id));
        }).WithTags("KYC").WithSummary("Abrir un caso KYC: cribado → PEP → riesgo, y decisión automática si el riesgo lo permite");

        app.MapGet("/api/v1/kyc-cases/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Advisor", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } c ? Results.Ok(c) : Problems.NotFound(http, "KYC_CASE_NOT_FOUND", $"No existe el caso {id}.", null);
        }).WithTags("KYC").WithSummary("Un caso con sus pasos");

        app.MapGet("/api/v1/kyc-cases", (HttpContext http, string? status) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Compliance", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM cases WHERE (@status IS NULL OR status = @status) ORDER BY opened_at, id", new { status = status?.ToUpperInvariant() }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("KYC").WithSummary("Bandeja de casos de Cumplimiento");

        app.MapPost("/api/v1/kyc-cases/{id}/decision", (HttpContext http, string id, KycDecisionRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Compliance" }) is { } denied) return denied;
            var decision = b.Decision?.ToUpperInvariant();
            if (decision is not ("APPROVE" or "REJECT") || string.IsNullOrWhiteSpace(b.Reason))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "decision (APPROVE o REJECT) y reason son obligatorios.", null);
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<(string Id, string Cust, string Status, string By, string? Level)>("SELECT id, customer_id, status, opened_by, risk_level FROM cases WHERE id = @id", new { id });
            if (c.Id is null) return Problems.NotFound(http, "KYC_CASE_NOT_FOUND", $"No existe el caso {id}.", null);
            if (c.Status != "PENDING_REVIEW") return Problems.Conflict(http, "KYC_CASE_NOT_PENDING", $"El caso está {c.Status}: solo se decide en PENDING_REVIEW.", null);
            if (c.By == p.Sub) return Problems.Of(http, 403, "FOUR_EYES_VIOLATION", "Prohibido", "Quien abre un caso no puede decidirlo.", "Principio de cuatro ojos: lo decide otra persona de Cumplimiento.");
            var status = decision == "APPROVE" ? "APPROVED" : "REJECTED";
            var now = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE cases SET status = @status, decided_by = @by, decided_at = @now, decision_reason = @r, next_review_due = @next WHERE id = @id",
                new { status, by = p.Sub, now, r = b.Reason, next = status == "APPROVED" ? Money.Date(clock.Today.AddYears(1)) : null, id }, tx);
            Outbox.Add(db, http, clock, status == "APPROVED" ? "kyc.approved" : "kyc.rejected", c.Cust, new { caseId = id, customerId = c.Cust, riskLevel = c.Level, manual = true }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("KYC").WithSummary("Cumplimiento decide un caso en revisión (cuatro ojos: no puede ser quien lo abrió)");

        app.MapGet("/api/v1/customers/{customerId}/kyc", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Status, string? Level, string? DecAt, string? Next)>(
                "SELECT id, status, risk_level, decided_at, next_review_due FROM cases WHERE customer_id = @customerId ORDER BY rowid DESC LIMIT 1", new { customerId });
            if (r.Id is null) return Problems.NotFound(http, "KYC_NOT_FOUND", "Este cliente no tiene caso KYC.", null);
            var overdue = r.Next is not null && string.CompareOrdinal(r.Next, Money.Date(clock.Today)) < 0;
            return Results.Ok(new { customerId, caseId = r.Id, status = overdue ? "REVIEW_OVERDUE" : r.Status, riskLevel = p.Role == "Customer" ? null : r.Level, decidedAt = r.DecAt, nextReviewDue = r.Next });
        }).WithTags("KYC").WithSummary("Resumen KYC del cliente (el cliente no ve su nivel de riesgo)");
    }
}
