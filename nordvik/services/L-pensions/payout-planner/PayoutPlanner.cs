using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PayoutPlanRequest(string? Type, int? TermYears, int? GuaranteeYears, string? StartPeriod);

/// <summary>
/// payout-planner: el plan de cobro de una pensión. Comprueba la elegibilidad (55 años y plan ACTIVE), valora el plan, calcula la renta con
/// annuity-calculator (sexo según el número nacional) y la deja en DRAFT. Estados: DRAFT → ACTIVE (el plan de pensión pasa a PAYOUT) ⇄ SUSPENDED → FINISHED;
/// DRAFT → CANCELLED. Fuera de orden: 409 INVALID_STATE_TRANSITION. Un plan de pensión tiene como mucho un plan de cobro vivo.
/// </summary>
public sealed class PayoutPlanner : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "payout-planner", "L-pensions", 7110, 3, "Pensiones",
        "Planes de cobro: elegibilidad, capital valorado, renta temporal o vitalicia (annuity-calculator) y ciclo DRAFT → ACTIVE ⇄ SUSPENDED → FINISHED.",
        DependsOn: new[] { "pension-plan-service", "annuity-calculator", "valuation-service", "customer-registry", "permission-service", "clock-service" },
        Emits: new[] { "payout-plan.created", "payout-plan.status-changed" });

    public static readonly Dictionary<string, string[]> Next = new()
    {
        ["DRAFT"] = new[] { "ACTIVE", "CANCELLED" }, ["ACTIVE"] = new[] { "SUSPENDED", "FINISHED" }, ["SUSPENDED"] = new[] { "ACTIVE", "FINISHED" },
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS payout_plans (id TEXT PRIMARY KEY, pension_plan_id TEXT, customer_id TEXT, type TEXT, term_years INTEGER, guarantee_years INTEGER, capital TEXT,
                        monthly TEXT, payments INTEGER, start_period TEXT, status TEXT, quote TEXT, created_at TEXT, updated_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Pp, string Cust, string Type, long? Term, long Guar, string Cap, string Monthly, long Payments, string Start, string Status, string At, string Up)>(
            "SELECT id, pension_plan_id, customer_id, type, term_years, guarantee_years, capital, monthly, payments, start_period, status, created_at, updated_at FROM payout_plans WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, pensionPlanId = r.Pp, customerId = r.Cust, type = r.Type, termYears = r.Term, guaranteeYears = r.Guar, capital = new { amount = r.Cap, currency = "SEK" },
            monthlyGross = new { amount = r.Monthly, currency = "SEK" }, expectedPayments = r.Payments, startPeriod = r.Start, status = r.Status, createdAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        IResult? Move(HttpContext http, string id, string to, string by)
        {
            using var db = ctx.OpenDb();
            var cur = db.ExecuteScalar<string?>("SELECT status FROM payout_plans WHERE id = @id", new { id });
            if (cur is null) return Problems.NotFound(http, "PAYOUT_PLAN_NOT_FOUND", $"No existe el plan de cobro {id}.", null);
            if (cur == to) return null;
            if (!(Next.GetValueOrDefault(cur)?.Contains(to) ?? false)) return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Un plan de cobro {cur} no puede pasar a {to}.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE payout_plans SET status = @to, updated_at = @at WHERE id = @id", new { id, to, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "payout-plan.status-changed", id, new { payoutPlanId = id, from = cur, to, by }, tx);
            tx.Commit();
            return null;
        }

        async Task<IResult?> Owner(HttpContext http, Principal p, string id, string action)
        {
            string? c;
            using (var db = ctx.OpenDb()) c = db.ExecuteScalar<string?>("SELECT customer_id FROM payout_plans WHERE id = @id", new { id });
            if (c is null) return Problems.NotFound(http, "PAYOUT_PLAN_NOT_FOUND", $"No existe el plan de cobro {id}.", null);
            return await CustomerGuard.Check(http, p, action, c);
        }

        app.MapPost("/api/v1/pension-plans/{planId}/payout-plans", async (HttpContext http, string planId, PayoutPlanRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{planId}", forwardAuth: false);
            if (!plan.Ok) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {planId}.", null);
            var customerId = plan.Str("customerId")!;
            if (await CustomerGuard.Check(http, p, "portfolio:update", customerId) is { } g) return g;
            var type = (b.Type ?? "TERM").ToUpperInvariant();
            var start = b.StartPeriod ?? clock.LocalNow.AddMonths(1).ToString("yyyy-MM");
            if (!System.Text.RegularExpressions.Regex.IsMatch(start, @"^\d{4}-(0[1-9]|1[0-2])$") || string.CompareOrdinal(start, clock.LocalNow.ToString("yyyy-MM")) < 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "startPeriod es AAAA-MM y no puede estar en el pasado.", null);
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM payout_plans WHERE pension_plan_id = @planId AND status IN ('DRAFT','ACTIVE','SUSPENDED')", new { planId }) is { } live)
                    return Problems.Conflict(http, "PAYOUT_PLAN_EXISTS", $"El plan ya tiene un plan de cobro vivo: {live}.", "Cancela el borrador o termina el plan anterior.");
            var el = await Downstream.Get(http, "pension-plan-service", $"/api/v1/pension-plans/{planId}/eligibility");
            if (!el.Json.GetProperty("eligible").GetBoolean())
                return Problems.Unprocessable(http, "NOT_ELIGIBLE", string.Join(" ", el.Json.GetProperty("reasons").EnumerateArray().Select(x => x.GetString())), "Edad mínima 55 y plan ACTIVE.");
            var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{plan.Str("portfolioId")}/valuation", forwardAuth: false);
            var capital = val.Ok && Money.TryParse(val.Str("totalValueSek"), out var c) ? c : 0;
            if (capital <= 0) return Problems.Unprocessable(http, "NOTHING_TO_PAY", "El plan no tiene valor invertido todavía.", "Las aportaciones se invierten al ejecutarse las órdenes (18:05 del día hábil).");
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var gender = NationalId.Check(cust.Str("nationalId")).Gender ?? "M";
            var age = (int)cust.Json.GetProperty("age").GetInt64();
            var q = await Downstream.Send(http, "annuity-calculator", HttpMethod.Post, "/api/v1/annuities/quote",
                new { capital = Money.Format(capital, 2), age, gender, type, termYears = b.TermYears, guaranteeYears = b.GuaranteeYears }, forwardAuth: false);
            if (!q.Ok) return Results.Content(q.Raw, "application/problem+json", System.Text.Encoding.UTF8, q.Status);
            var id = "po_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO payout_plans VALUES (@id, @planId, @customerId, @type, @TermYears, @g, @cap, @m, @n, @start, 'DRAFT', @quote, @at, @at)",
                    new { id, planId, customerId, type, b.TermYears, g = b.GuaranteeYears ?? 0, cap = Money.Format(capital, 2), m = q.Json.GetProperty("monthlyPayment").GetProperty("amount").GetString(),
                          n = q.Json.GetProperty("expectedPayments").GetInt32(), start, quote = q.Raw, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "payout-plan.created", id, new { payoutPlanId = id, pensionPlanId = planId, customerId, type }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/payout-plans/{id}", Load(db, id));
        }).WithTags("Planes de cobro").WithSummary("Crear el plan de cobro (DRAFT) con la renta calculada");

        app.MapGet("/api/v1/payout-plans/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await Owner(http, p, id, "portfolio:read") is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Planes de cobro").WithSummary("Un plan de cobro");

        app.MapPost("/api/v1/payout-plans/{id}/activate", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await Owner(http, p, id, "portfolio:update") is { } g) return g;
            string pp, cur;
            using (var db0 = ctx.OpenDb()) (pp, cur) = db0.QuerySingle<(string, string)>("SELECT pension_plan_id, status FROM payout_plans WHERE id = @id", new { id });
            if (cur != "DRAFT") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Solo se activa un plan DRAFT (está {cur}).", "Para reanudar uno SUSPENDED: /resume.");
            var s = await Downstream.Send(http, "pension-plan-service", HttpMethod.Post, $"/api/v1/internal/pension-plans/{pp}/status/PAYOUT", new { reason = "Inicio del cobro " + id }, forwardAuth: false);
            if (!s.Ok) return Results.Content(s.Raw, "application/problem+json", System.Text.Encoding.UTF8, s.Status);
            if (Move(http, id, "ACTIVE", p.Sub) is { } bad) return bad;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Planes de cobro").WithSummary("Activar (el plan de pensión pasa a PAYOUT)");

        foreach (var (verb, to) in new[] { ("cancel", "CANCELLED"), ("suspend", "SUSPENDED"), ("resume", "ACTIVE"), ("finish", "FINISHED") })
        {
            app.MapPost($"/api/v1/payout-plans/{{id}}/{verb}", async (HttpContext http, string id) =>
            {
                var roles = verb == "cancel" ? null : new[] { "Operations", "Admin" };
                if (Auth.Require(http, clock, out var p, roles: roles) is { } denied) return denied;
                if (await Owner(http, p, id, "portfolio:update") is { } g) return g;
                if (verb == "resume")
                {
                    using var db0 = ctx.OpenDb();
                    if (db0.ExecuteScalar<string>("SELECT status FROM payout_plans WHERE id = @id", new { id }) != "SUSPENDED")
                        return Problems.Conflict(http, "INVALID_STATE_TRANSITION", "Solo se reanuda un plan SUSPENDED.", null);
                    var pp = db0.ExecuteScalar<string>("SELECT pension_plan_id FROM payout_plans WHERE id = @id", new { id });
                    var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{pp}", forwardAuth: false);
                    if (plan.Str("status") == "FROZEN") return Problems.Conflict(http, "PLAN_FROZEN", "El plan de pensión está congelado (reclamación por fallecimiento).", null);
                }
                if (Move(http, id, to, p.Sub) is { } bad) return bad;
                using var db = ctx.OpenDb();
                return Results.Ok(Load(db, id));
            }).WithTags("Planes de cobro").WithSummary(verb switch
            {
                "cancel" => "Cancelar un borrador (DRAFT → CANCELLED)", "suspend" => "Suspender (Operaciones)", "resume" => "Reanudar (Operaciones; no si el plan está congelado)", _ => "Terminar (Operaciones)",
            });
        }

        app.MapGet("/api/v1/internal/payout-plans", (string? status, string? pensionPlanId, string? customerId) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM payout_plans WHERE (@status IS NULL OR status = @status) AND (@pensionPlanId IS NULL OR pension_plan_id = @pensionPlanId) AND (@customerId IS NULL OR customer_id = @customerId) ORDER BY created_at",
                new { status, pensionPlanId, customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Interno").WithSummary("(interno) Buscar planes de cobro");

        app.MapPost("/api/v1/internal/payout-plans/{id}/status/{status}", (HttpContext http, string id, string status) =>
        {
            if (Move(http, id, status.ToUpperInvariant(), "system") is { } bad) return bad;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Interno").WithSummary("(interno) Cambiar el estado (payout-execution, death-claims)");
    }
}
