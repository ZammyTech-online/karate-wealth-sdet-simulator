using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record InsurancePolicyRequest(string? CustomerId, string? PensionPlanId, string? Coverage, string? Capital, int? TermYears, string? HealthDeclarationId);

/// <summary>
/// insurance-policy: pólizas de vida (LIFE) e invalidez (DISABILITY) ligadas a un plan de pensión ACTIVE del mismo cliente. Exigen una
/// declaración de salud vigente y no DECLINED; la prima sale de premium-calculator con el recargo médico y la póliza se emite en la
/// aseguradora (E11). Una póliza activa por plan y cobertura. ACTIVE → CANCELLED | CLAIMED.
/// </summary>
public sealed class InsurancePolicy : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "insurance-policy", "M-insurance", 7200, 4, "Seguros",
        "Pólizas de vida e invalidez ligadas al plan de pensión: declaración de salud vigente, prima con recargo, emisión en la aseguradora y cancelación.",
        DependsOn: new[] { "pension-plan-service", "health-declaration", "premium-calculator", "insurer-adapter", "customer-registry", "permission-service", "clock-service" },
        Emits: new[] { "insurance-policy.issued", "insurance-policy.status-changed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS policies (id TEXT PRIMARY KEY, customer_id TEXT, plan_id TEXT, coverage TEXT, capital TEXT, term_years INTEGER, loading_pct INTEGER,
                        monthly_premium TEXT, insurer_policy TEXT, declaration_id TEXT, status TEXT, created_at TEXT, updated_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Plan, string Cov, string Cap, long Term, long Load, string Prem, string Ins, string Decl, string Status, string At, string Up)>(
            "SELECT id, customer_id, plan_id, coverage, capital, term_years, loading_pct, monthly_premium, insurer_policy, declaration_id, status, created_at, updated_at FROM policies WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, pensionPlanId = r.Plan, coverage = r.Cov, capital = new { amount = r.Cap, currency = "SEK" }, termYears = r.Term, loadingPct = r.Load,
            monthlyPremium = new { amount = r.Prem, currency = "SEK" }, insurerPolicyNumber = r.Ins, healthDeclarationId = r.Decl, status = r.Status, createdAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        IResult? Move(HttpContext http, string id, string to)
        {
            using var db = ctx.OpenDb();
            var cur = db.ExecuteScalar<string?>("SELECT status FROM policies WHERE id = @id", new { id });
            if (cur is null) return Problems.NotFound(http, "POLICY_NOT_FOUND", $"No existe la póliza {id}.", null);
            if (cur != "ACTIVE") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Una póliza {cur} no puede pasar a {to}.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE policies SET status = @to, updated_at = @at WHERE id = @id", new { id, to, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "insurance-policy.status-changed", id, new { policyId = id, from = cur, to }, tx);
            tx.Commit();
            return null;
        }

        app.MapPost("/api/v1/insurance-policies", async (HttpContext http, InsurancePolicyRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            var coverage = (b.Coverage ?? "").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (b.CustomerId is null) errors.Add(new FieldError("customerId", "obligatorio"));
            if (b.PensionPlanId is null) errors.Add(new FieldError("pensionPlanId", "obligatorio"));
            if (coverage is not ("LIFE" or "DISABILITY")) errors.Add(new FieldError("coverage", "LIFE o DISABILITY"));
            if (b.HealthDeclarationId is null) errors.Add(new FieldError("healthDeclarationId", "obligatorio: primero el cuestionario de salud"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Póliza no válida.", null, errors);
            if (await CustomerGuard.Check(http, p, "customer:update", b.CustomerId!) is { } g) return g;
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{b.PensionPlanId}", forwardAuth: false);
            if (!plan.Ok || plan.Str("customerId") != b.CustomerId) return Problems.Unprocessable(http, "PLAN_NOT_FOUND", "El plan no existe o no es de este cliente.", null);
            if (plan.Str("status") != "ACTIVE") return Problems.Conflict(http, "PLAN_NOT_ACTIVE", $"El plan está {plan.Str("status")}.", null);
            var decl = await Downstream.Get(http, "health-declaration", $"/api/v1/internal/health-declarations/{b.HealthDeclarationId}", forwardAuth: false);
            if (!decl.Ok || decl.Str("customerId") != b.CustomerId) return Problems.Unprocessable(http, "DECLARATION_NOT_FOUND", "La declaración de salud no existe o es de otra persona.", null);
            if (decl.Str("status") == "EXPIRED") return Problems.Unprocessable(http, "HEALTH_DECLARATION_EXPIRED", "La declaración de salud caducó (90 días): hay que rellenarla otra vez.", null);
            if (decl.Str("outcome") == "DECLINED") return Problems.Unprocessable(http, "APPLICANT_DECLINED", "La aseguradora no acepta el riesgo.", null);
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM policies WHERE plan_id = @PensionPlanId AND coverage = @coverage AND status = 'ACTIVE'", new { b.PensionPlanId, coverage }) is { } live)
                    return Problems.Conflict(http, "POLICY_ALREADY_EXISTS", $"El plan ya tiene una póliza {coverage} activa: {live}.", null);
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            var loading = decl.Json.GetProperty("loadingPct").GetInt32();
            var q = await Downstream.Send(http, "premium-calculator", HttpMethod.Post, "/api/v1/premiums/quote",
                new { age = cust.Json.GetProperty("age").GetInt32(), capital = b.Capital, coverage, loadingPct = loading, termYears = b.TermYears }, forwardAuth: false);
            if (!q.Ok) return Results.Content(q.Raw, "application/problem+json", System.Text.Encoding.UTF8, q.Status);
            var premium = q.Json.GetProperty("monthlyPremium").GetProperty("amount").GetString();
            var ins = await Downstream.Send(http, "insurer-adapter", HttpMethod.Post, "/api/v1/internal/policies",
                new { applicantRef = "tok_cu_" + Canonical.Sha256Hex(b.CustomerId!)[..6], coverage, capital = q.Str("capital"), monthlyPremium = premium, underwritingId = decl.Str("underwritingId") }, forwardAuth: false);
            if (!ins.Ok) return Results.Content(ins.Raw, "application/problem+json", System.Text.Encoding.UTF8, ins.Status);
            var id = "ip_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO policies VALUES (@id, @CustomerId, @PensionPlanId, @coverage, @cap, @TermYears, @loading, @premium, @ins, @HealthDeclarationId, 'ACTIVE', @at, @at)",
                    new { id, b.CustomerId, b.PensionPlanId, coverage, cap = q.Str("capital"), b.TermYears, loading, premium, ins = ins.Str("policyNumber"), b.HealthDeclarationId, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "insurance-policy.issued", id, new { policyId = id, customerId = b.CustomerId, coverage, capital = q.Str("capital") }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/insurance-policies/{id}", Load(db, id));
        }).WithTags("Pólizas").WithSummary("Contratar un seguro ligado al plan de pensión");

        app.MapGet("/api/v1/insurance-policies/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? owner;
            using (var db0 = ctx.OpenDb()) owner = db0.ExecuteScalar<string?>("SELECT customer_id FROM policies WHERE id = @id", new { id });
            if (owner is null) return Problems.NotFound(http, "POLICY_NOT_FOUND", $"No existe la póliza {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", owner) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Pólizas").WithSummary("Una póliza");

        app.MapGet("/api/v1/customers/{customerId}/insurance-policies", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM policies WHERE customer_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Pólizas").WithSummary("Pólizas de un cliente");

        app.MapPost("/api/v1/insurance-policies/{id}/cancel", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor", "Operations" }) is { } denied) return denied;
            string? owner;
            using (var db0 = ctx.OpenDb()) owner = db0.ExecuteScalar<string?>("SELECT customer_id FROM policies WHERE id = @id", new { id });
            if (owner is null) return Problems.NotFound(http, "POLICY_NOT_FOUND", $"No existe la póliza {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:update", owner) is { } g) return g;
            if (Move(http, id, "CANCELLED") is { } bad) return bad;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Pólizas").WithSummary("Cancelar (ACTIVE → CANCELLED)");

        app.MapGet("/api/v1/internal/insurance-policies", (string? customerId, string? status) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM policies WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@status IS NULL OR status = @status) ORDER BY created_at", new { customerId, status })
                .Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Interno").WithSummary("(interno) Buscar pólizas");

        app.MapGet("/api/v1/internal/insurance-policies/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            return Load(db, id) is { } x ? Results.Ok(x) : Problems.NotFound(http, "POLICY_NOT_FOUND", $"No existe la póliza {id}.", null);
        }).WithTags("Interno").WithSummary("(interno) Una póliza");

        app.MapPost("/api/v1/internal/insurance-policies/{id}/claimed", (HttpContext http, string id) =>
        {
            if (Move(http, id, "CLAIMED") is { } bad) return bad;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Interno").WithSummary("(interno) Marcar como siniestrada (insurance-claims)");
    }
}
