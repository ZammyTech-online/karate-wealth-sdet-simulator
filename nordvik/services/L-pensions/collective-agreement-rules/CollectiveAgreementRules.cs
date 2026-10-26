using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Agreement(string Code, string Name, string Sector, int StartAge, decimal RateLowPct, decimal RateHighPct, decimal CapIncomeBaseAmounts, decimal SalaryExchangeBonusPct, int VestingYears);
public sealed record ContributionRequest(string? MonthlySalary, int? Age);

/// <summary>
/// collective-agreement-rules: las reglas de cada convenio colectivo (simplificadas y ficticias, inspiradas en los suecos):
/// desde qué edad aporta la empresa, qué porcentaje hasta 7,5 "importes base" anuales y cuál por encima, la bonificación del
/// intercambio salarial y los años para consolidar derechos.
/// </summary>
public sealed class CollectiveAgreementRules : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "collective-agreement-rules", "L-pensions", 7104, 3, "Pensiones",
        "Reglas de cada convenio: edad de inicio, porcentajes por tramo (hasta 7,5 importes base y por encima), bonificación del intercambio salarial y consolidación.");

    /// <summary>Importe base de ingresos anual del simulador (ficticio, no el oficial).</summary>
    public const decimal IncomeBaseAmount = 80_600m;

    public static readonly Agreement[] All =
    {
        new("ITP1", "ITP 1 (empleados del sector privado)", "PRIVATE_WHITE_COLLAR", 25, 4.5m, 30m, 7.5m, 6m, 0),
        new("ITP2", "ITP 2 (generación anterior)", "PRIVATE_WHITE_COLLAR", 25, 2m, 2m, 0m, 6m, 3),
        new("SAF-LO", "SAF-LO (obreros del sector privado)", "PRIVATE_BLUE_COLLAR", 24, 4.5m, 4.5m, 0m, 5.8m, 0),
        new("AKAP-KR", "AKAP-KR (municipios y regiones)", "MUNICIPAL", 21, 6m, 31.5m, 7.5m, 4m, 0),
        new("PA16", "PA 16 (Estado)", "STATE", 23, 4.5m, 30m, 7.5m, 5m, 0),
        new("NONE", "Sin convenio", "NONE", 99, 0m, 0m, 0m, 0m, 0),
    };

    public static (bool Eligible, decimal Low, decimal High, decimal Total, string? Reason) Contribution(Agreement a, decimal monthlySalary, int age)
    {
        if (a.Code == "NONE") return (false, 0, 0, 0, "sin convenio no hay aportación de empresa");
        if (age < a.StartAge) return (false, 0, 0, 0, $"la empresa aporta desde los {a.StartAge} años");
        if (age >= 66) return (false, 0, 0, 0, "a partir de 66 años no hay aportación");
        var capMonthly = a.CapIncomeBaseAmounts == 0 ? decimal.MaxValue : IncomeBaseAmount * a.CapIncomeBaseAmounts / 12m;
        var lowBase = Math.Min(monthlySalary, capMonthly);
        var highBase = Math.Max(0, monthlySalary - capMonthly);
        var low = Math.Round(lowBase * a.RateLowPct / 100, 2, MidpointRounding.ToEven);
        var high = Math.Round(highBase * a.RateHighPct / 100, 2, MidpointRounding.ToEven);
        return (true, low, high, low + high, null);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/agreements", () => Results.Ok(new { items = All, incomeBaseAmount = Money.Format(IncomeBaseAmount, 2), capMonthlyAt75 = Money.Format(IncomeBaseAmount * 7.5m / 12m, 2) }))
            .WithTags("Convenios").WithSummary("Todos los convenios y el importe base del año");

        app.MapGet("/api/v1/agreements/{code}", (HttpContext http, string code) =>
            All.FirstOrDefault(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } a ? Results.Ok(a)
                : Problems.NotFound(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {code}.", "Códigos: " + string.Join(", ", All.Select(x => x.Code))))
            .WithTags("Convenios").WithSummary("Un convenio");

        app.MapPost("/api/v1/agreements/{code}/contribution", (HttpContext http, string code, ContributionRequest b) =>
        {
            var a = All.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (a is null) return Problems.NotFound(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {code}.", null);
            if (!Money.TryParse(b.MonthlySalary, out var salary) || salary < 0 || b.Age is null or < 16 or > 75)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "monthlySalary (texto) y age (16-75) son obligatorios.", "Ejemplo: { \"monthlySalary\": \"62000.00\", \"age\": 40 }");
            var c = Contribution(a, salary, b.Age.Value);
            return Results.Ok(new
            {
                agreement = a.Code, monthlySalary = Money.Format(salary, 2), age = b.Age, eligible = c.Eligible, reason = c.Reason,
                employerMonthly = new { amount = Money.Format(c.Total, 2), currency = "SEK" },
                breakdown = new[]
                {
                    new { tranche = "hasta 7,5 importes base", ratePct = a.RateLowPct, amount = Money.Format(c.Low, 2) },
                    new { tranche = "por encima de 7,5 importes base", ratePct = a.RateHighPct, amount = Money.Format(c.High, 2) },
                },
            });
        }).WithTags("Convenios").WithSummary("Aportación mensual de la empresa según sueldo y edad");
    }
}
