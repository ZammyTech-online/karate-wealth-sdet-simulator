using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ExchangeSimRequest(string? MonthlySalary, string? ExchangeAmount, string? Agreement);
public sealed record ExchangeCreateRequest(string? PlanId, string? MonthlySalary, string? ExchangeAmount, string? Agreement, string? StartMonth, bool? AcknowledgeBelowThreshold);

/// <summary>
/// salary-exchange-service: cambiar sueldo bruto por aportación a la pensión. La empresa añade la bonificación del convenio.
/// Regla de oro sueca (simplificada): si el sueldo queda por debajo de 8,07 importes base al año, se pierde pensión pública:
/// el sistema avisa (SALARY_BELOW_THRESHOLD) y no deja contratar sin reconocerlo.
/// </summary>
public sealed class SalaryExchangeService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "salary-exchange-service", "L-pensions", 7105, 3, "Pensiones",
        "Intercambio salarial: simulación (bonificación del convenio, coste neto aproximado) y alta, con el aviso del umbral de 8,07 importes base.",
        DependsOn: new[] { "collective-agreement-rules", "pension-plan-service", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "salary-exchange.created" });

    public const decimal IncomeBaseAmount = 80_600m;
    public static decimal ThresholdMonthly => Math.Round(IncomeBaseAmount * 8.07m / 12m, 2);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS exchanges (id TEXT PRIMARY KEY, customer_id TEXT, plan_id TEXT, salary TEXT, exchange TEXT, agreement TEXT, bonus_pct TEXT, monthly_contribution TEXT, start_month TEXT, status TEXT, created_at TEXT)");
    }

    private static async Task<object?> Simulate(HttpContext http, decimal salary, decimal exchange, string agreement)
    {
        var a = await Downstream.Get(http, "collective-agreement-rules", $"/api/v1/agreements/{agreement}", forwardAuth: false);
        if (!a.Ok) return null;
        var bonusPct = a.Json.GetProperty("salaryExchangeBonusPct").GetDecimal();
        var bonus = Math.Round(exchange * bonusPct / 100, 2, MidpointRounding.ToEven);
        var after = salary - exchange;
        var below = after < ThresholdMonthly;
        return new
        {
            agreement, monthlySalary = Money.Format(salary, 2), exchange = Money.Format(exchange, 2), salaryAfter = Money.Format(after, 2),
            bonusPct, bonus = Money.Format(bonus, 2), monthlyPensionContribution = Money.Format(exchange + bonus, 2),
            netSalaryReductionApprox = Money.Format(Math.Round(exchange * 0.68m, 2), 2), assumedMarginalTaxPct = 32,
            thresholdMonthly = Money.Format(ThresholdMonthly, 2), belowThreshold = below,
            warning = below ? "Con este intercambio el sueldo queda por debajo de 8,07 importes base: perderías pensión pública." : null,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/salary-exchanges/simulate", async (HttpContext http, ExchangeSimRequest b) =>
        {
            if (!Money.TryParse(b.MonthlySalary, out var salary) || !Money.TryParse(b.ExchangeAmount, out var ex) || ex <= 0 || ex >= salary)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "monthlySalary y exchangeAmount (positivo y menor que el sueldo) como texto.", "Ejemplo: { \"monthlySalary\": \"65000.00\", \"exchangeAmount\": \"5000.00\", \"agreement\": \"ITP1\" }");
            return await Simulate(http, salary, ex, b.Agreement ?? "ITP1") is { } s ? Results.Ok(s) : Problems.NotFound(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {b.Agreement}.", null);
        }).WithTags("Intercambio salarial").WithSummary("Simular un intercambio (sin guardar nada)");

        app.MapPost("/api/v1/customers/{customerId}/salary-exchanges", async (HttpContext http, string customerId, ExchangeCreateRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            if (!Money.TryParse(b.MonthlySalary, out var salary) || !Money.TryParse(b.ExchangeAmount, out var ex) || ex <= 0 || ex >= salary || string.IsNullOrWhiteSpace(b.PlanId)
                || b.StartMonth is null || !System.Text.RegularExpressions.Regex.IsMatch(b.StartMonth, @"^\d{4}-(0[1-9]|1[0-2])$"))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "planId, monthlySalary, exchangeAmount y startMonth (AAAA-MM) son obligatorios.", null);
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{b.PlanId}", forwardAuth: false);
            if (!plan.Ok || plan.Str("customerId") != customerId) return Problems.Unprocessable(http, "PLAN_NOT_FOUND", "El plan no existe o no es de este cliente.", null);
            if (plan.Str("type") != "SALARY_EXCHANGE") return Problems.Unprocessable(http, "WRONG_PLAN_TYPE", "El intercambio salarial va a un plan de tipo SALARY_EXCHANGE.", null);
            var agreement = b.Agreement ?? plan.Str("agreement") ?? "ITP1";
            var sim = await Simulate(http, salary, ex, agreement);
            if (sim is null) return Problems.NotFound(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {agreement}.", null);
            var s = System.Text.Json.JsonSerializer.SerializeToElement(sim, Canonical.Web);
            if (s.GetProperty("belowThreshold").GetBoolean() && b.AcknowledgeBelowThreshold != true)
                return Problems.Unprocessable(http, "SALARY_BELOW_THRESHOLD", s.GetProperty("warning").GetString()!, "Si aun así lo quieres, repite con acknowledgeBelowThreshold: true.");
            var id = "sx_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO exchanges VALUES (@id, @customerId, @PlanId, @sal, @ex, @agreement, @bp, @mc, @StartMonth, 'ACTIVE', @at)",
                new { id, customerId, b.PlanId, sal = Money.Format(salary, 2), ex = Money.Format(ex, 2), agreement, bp = s.GetProperty("bonusPct").GetDecimal().ToString(Money.Inv),
                      mc = s.GetProperty("monthlyPensionContribution").GetString(), b.StartMonth, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "salary-exchange.created", id, new { exchangeId = id, customerId, planId = b.PlanId, monthlyContribution = s.GetProperty("monthlyPensionContribution").GetString() }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/salary-exchanges/{id}", new { id, customerId, planId = b.PlanId, startMonth = b.StartMonth, status = "ACTIVE", simulation = sim });
        }).WithTags("Intercambio salarial").WithSummary("Contratar el intercambio para un plan SALARY_EXCHANGE");

        app.MapGet("/api/v1/customers/{customerId}/salary-exchanges", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Id, string Plan, string Sal, string Ex, string Agr, string Mc, string Start, string Status)>(
                "SELECT id, plan_id, salary, exchange, agreement, monthly_contribution, start_month, status FROM exchanges WHERE customer_id = @customerId ORDER BY created_at", new { customerId })
                .Select(x => new { id = x.Id, planId = x.Plan, monthlySalary = x.Sal, exchange = x.Ex, agreement = x.Agr, monthlyPensionContribution = x.Mc, startMonth = x.Start, status = x.Status }).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Intercambio salarial").WithSummary("Intercambios del cliente");
    }
}
