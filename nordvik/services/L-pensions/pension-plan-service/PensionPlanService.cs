using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PlanAllocationLine(string? Isin, decimal Percent);
public sealed record PlanRequest(string? CustomerId, string? Type, string? EmployerOrgNumber, string? Agreement, List<PlanAllocationLine>? Allocation);
public sealed record PlanStatusRequest(string? Reason);

/// <summary>
/// pension-plan-service: los planes de pensión (PRIVATE, OCCUPATIONAL de empresa por convenio, SALARY_EXCHANGE). Cada plan tiene su
/// cartera PENSION en portfolio-service, creada aquí. Estados: ACTIVE → PAYOUT → CLOSED, FROZEN (por fallecimiento), TRANSFERRED_OUT.
/// Un plan no se cobra antes de los 55 años.
/// </summary>
public sealed class PensionPlanService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "pension-plan-service", "L-pensions", 7100, 3, "Pensiones",
        "Planes de pensión privados, de empresa (por convenio) y de intercambio salarial, con su cartera PENSION, elegibilidad de cobro (55 años) y congelación.",
        DependsOn: new[] { "portfolio-service", "customer-registry", "collective-agreement-rules", "valuation-service", "permission-service", "clock-service" },
        Emits: new[] { "pension-plan.created", "pension-plan.frozen", "pension-plan.status-changed" });

    public static readonly string[] Types = { "PRIVATE", "OCCUPATIONAL", "SALARY_EXCHANGE" };
    public const int MinPayoutAge = 55;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS plans (id TEXT PRIMARY KEY, customer_id TEXT, type TEXT, employer_org TEXT, agreement TEXT, portfolio_id TEXT, status TEXT, frozen_reason TEXT,
                        created_at TEXT, updated_at TEXT, version INTEGER)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Type, string? Org, string? Agr, string Pf, string Status, string? Frozen, string At, string Up, long V)>(
            "SELECT id, customer_id, type, employer_org, agreement, portfolio_id, status, frozen_reason, created_at, updated_at, version FROM plans WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, customerRef = "tok_cu_" + Canonical.Sha256Hex(r.Cust)[..6], type = r.Type, employerOrgNumber = r.Org, agreement = r.Agr, portfolioId = r.Pf,
            status = r.Status, frozenReason = r.Frozen, createdAt = r.At, updatedAt = r.Up, version = r.V,
        };
    }

    private static string? Owner(Microsoft.Data.Sqlite.SqliteConnection db, string id) => db.ExecuteScalar<string?>("SELECT customer_id FROM plans WHERE id = @id", new { id });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<IResult?> Guard(HttpContext http, Principal p, string id, string action)
        {
            using var db = ctx.OpenDb();
            var owner = Owner(db, id);
            if (owner is null) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {id}.", null);
            var d = await Permissions.Check(http, p, action, owner);
            return d.Allowed ? null : Permissions.Denied(http, p, d, "PLAN_NOT_FOUND", $"el plan {id}");
        }

        app.MapPost("/api/v1/pension-plans", async (HttpContext http, PlanRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor", "Operations", "Service" }) is { } denied) return denied;
            var customerId = b.CustomerId ?? p.CustomerId;
            var type = b.Type?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (customerId is null) errors.Add(new FieldError("customerId", "obligatorio"));
            if (type is null || !Types.Contains(type)) errors.Add(new FieldError("type", string.Join(", ", Types)));
            if (type is "OCCUPATIONAL" or "SALARY_EXCHANGE" && !EmploymentOrg(b.EmployerOrgNumber)) errors.Add(new FieldError("employerOrgNumber", "obligatorio en planes de empresa (NNNNNN-NNNN)"));
            if (type == "OCCUPATIONAL" && string.IsNullOrWhiteSpace(b.Agreement)) errors.Add(new FieldError("agreement", "obligatorio en OCCUPATIONAL"));
            if (b.Allocation is not { Count: > 0 }) errors.Add(new FieldError("allocation", "al menos un fondo"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Plan no válido.",
                "Ejemplo: { \"customerId\": \"c000...\", \"type\": \"OCCUPATIONAL\", \"employerOrgNumber\": \"556012-5790\", \"agreement\": \"ITP1\", \"allocation\": [ { \"isin\": \"SE0000000203\", \"percent\": 100 } ] }", errors);
            if (p.Role == "Customer" && type != "PRIVATE") return Problems.Of(http, 403, "EMPLOYER_PLAN_BY_EMPLOYER", "Prohibido", "Los planes de empresa los da de alta la empresa o su asesor.", null);
            if (await CustomerGuard.Check(http, p, "portfolio:create", customerId!) is { } g) return g;
            if (b.Agreement is not null)
            {
                var a = await Downstream.Get(http, "collective-agreement-rules", $"/api/v1/agreements/{b.Agreement}", forwardAuth: false);
                if (!a.Ok) return Problems.Unprocessable(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {b.Agreement}.", null);
            }
            using (var db0 = ctx.OpenDb())
                if (type != "PRIVATE" && db0.ExecuteScalar<long>("SELECT COUNT(*) FROM plans WHERE customer_id = @customerId AND type = @type AND employer_org = @o AND status <> 'CLOSED'", new { customerId, type, o = b.EmployerOrgNumber }) > 0)
                    return Problems.Conflict(http, "PLAN_ALREADY_EXISTS", "El cliente ya tiene un plan de ese tipo con esa empresa.", null);
            var id = "pp_" + Guid.NewGuid().ToString("N")[..14];
            var pf = await Downstream.Send(http, "portfolio-service", HttpMethod.Post, "/api/v1/portfolios",
                new { customerId, name = $"Pensión {type} {id[3..9]}", type = "PENSION", targetAllocation = b.Allocation!.Select(l => new { isin = l.Isin, percent = l.Percent }) },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            if (!pf.Ok) return Results.Content(pf.Raw, "application/problem+json", System.Text.Encoding.UTF8, pf.Status == 400 ? 422 : pf.Status);
            var now = Nordic.Iso(clock.LocalNow);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO plans VALUES (@id, @customerId, @type, @org, @agr, @pf, 'ACTIVE', NULL, @now, @now, 1)",
                new { id, customerId, type, org = b.EmployerOrgNumber, agr = b.Agreement?.ToUpperInvariant() ?? (type == "SALARY_EXCHANGE" ? "ITP1" : null), pf = pf.Str("id"), now }, tx);
            Outbox.Add(db, http, clock, "pension-plan.created", id, new { planId = id, customerId, type, employerOrgNumber = b.EmployerOrgNumber }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/pension-plans/{id}", Load(db, id));
        }).WithTags("Planes").WithSummary("Abrir un plan (crea su cartera PENSION con la asignación de fondos)");

        app.MapGet("/api/v1/pension-plans/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await Guard(http, p, id, "portfolio:read") is { } g) return g;
            using var db = ctx.OpenDb();
            var plan = Load(db, id)!;
            var pfId = db.ExecuteScalar<string>("SELECT portfolio_id FROM plans WHERE id = @id", new { id });
            var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{pfId}/valuation", forwardAuth: false);
            return Results.Ok(new { plan, value = val.Ok ? new { amount = val.Str("totalValueSek"), currency = "SEK", priceDate = val.Str("priceDate") } : null });
        }).WithTags("Planes").WithSummary("Un plan con su valor (cartera PENSION valorada)");

        app.MapGet("/api/v1/customers/{customerId}/pension-plans", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "portfolio:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM plans WHERE customer_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Planes").WithSummary("Planes de un cliente");

        app.MapGet("/api/v1/pension-plans/{id}/eligibility", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await Guard(http, p, id, "portfolio:read") is { } g) return g;
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(string Cust, string Status)>("SELECT customer_id, status FROM plans WHERE id = @id", new { id });
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{r.Cust}", forwardAuth: false);
            var age = (int)c.Json.GetProperty("age").GetInt64();
            var birth = DateOnly.Parse(c.Str("birthDate")!);
            var reasons = new List<string>();
            if (age < MinPayoutAge) reasons.Add($"Edad {age}: una pensión privada se cobra desde los {MinPayoutAge} años ({Money.Date(birth.AddYears(MinPayoutAge))}).");
            if (r.Status != "ACTIVE") reasons.Add($"El plan está {r.Status}.");
            return Results.Ok(new { planId = id, age, minimumAge = MinPayoutAge, eligible = reasons.Count == 0, earliestDate = Money.Date(birth.AddYears(MinPayoutAge)), reasons });
        }).WithTags("Planes").WithSummary("¿Puede empezar a cobrar? (edad mínima 55 y plan ACTIVE)");

        // ---- Red interna (contribution, vesting, payout, transfer, death-claims, employer files) ----
        app.MapGet("/api/v1/internal/pension-plans/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            return Load(db, id) is { } x ? Results.Ok(x) : Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {id}.", null);
        }).WithTags("Interno").WithSummary("(interno) Un plan");

        app.MapGet("/api/v1/internal/pension-plans", (string? customerId, string? employerOrgNumber, string? type) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM plans WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@employerOrgNumber IS NULL OR employer_org = @employerOrgNumber) AND (@type IS NULL OR type = @type) ORDER BY created_at",
                new { customerId, employerOrgNumber, type }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Interno").WithSummary("(interno) Buscar planes por cliente, empresa o tipo");

        app.MapPost("/api/v1/internal/pension-plans/{id}/status/{status}", (HttpContext http, string id, string status, PlanStatusRequest? b) =>
        {
            var allowed = new Dictionary<string, string[]>
            {
                ["ACTIVE"] = new[] { "PAYOUT", "FROZEN", "TRANSFERRED_OUT", "CLOSED" }, ["PAYOUT"] = new[] { "FROZEN", "CLOSED" }, ["FROZEN"] = new[] { "CLOSED" },
            };
            using var db = ctx.OpenDb();
            var cur = db.ExecuteScalar<string?>("SELECT status FROM plans WHERE id = @id", new { id });
            if (cur is null) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {id}.", null);
            status = status.ToUpperInvariant();
            if (cur == status) return Results.Ok(Load(db, id));
            if (!allowed.TryGetValue(cur, out var next) || !next.Contains(status))
                return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Un plan {cur} no puede pasar a {status}.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE plans SET status = @status, frozen_reason = CASE WHEN @status = 'FROZEN' THEN @r ELSE frozen_reason END, updated_at = @at, version = version + 1 WHERE id = @id",
                new { status, r = b?.Reason, at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, http, clock, status == "FROZEN" ? "pension-plan.frozen" : "pension-plan.status-changed", id, new { planId = id, from = cur, to = status, reason = b?.Reason }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Interno").WithSummary("(interno) Cambiar el estado de un plan (máquina de estados con 409 INVALID_STATE_TRANSITION)");
    }

    private static bool EmploymentOrg(string? org) => org is not null && System.Text.RegularExpressions.Regex.IsMatch(org, @"^\d{6}-\d{4}$");
}
