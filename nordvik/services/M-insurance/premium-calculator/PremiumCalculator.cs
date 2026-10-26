using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PremiumQuoteRequest(int? Age, string? Capital, string? Coverage, int? LoadingPct, int? TermYears);

/// <summary>
/// premium-calculator: prima mensual de un seguro de vida (LIFE) o invalidez (DISABILITY) ligado a la pensión. Riesgo anual medio del plazo
/// con la misma tabla sintética que annuity-calculator, recargo médico (loadingPct de la suscripción) y un 15 % de gastos. Determinista.
/// </summary>
public sealed class PremiumCalculator : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "premium-calculator", "M-insurance", 7202, 4, "Seguros",
        "Prima mensual de vida o invalidez según edad, capital, plazo y recargo médico; tabla sintética y 15 % de gastos, determinista.");

    public const decimal ExpenseLoading = 0.15m;

    public static double Qx(int age) => 1 - Math.Exp(-(0.0005 + 0.0001 * Math.Exp(0.1 * (age - 20))));

    public static decimal Monthly(int age, decimal capital, string coverage, int loadingPct, int termYears)
    {
        var avg = Enumerable.Range(age, termYears).Average(Qx);
        var rate = coverage == "DISABILITY" ? avg * 0.6 + 0.001 : avg;
        var annual = capital * (decimal)rate * (1 + loadingPct / 100m) * (1 + ExpenseLoading);
        return Math.Round(annual / 12, 2, MidpointRounding.ToEven);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/premiums/quote", (HttpContext http, PremiumQuoteRequest b) =>
        {
            var coverage = (b.Coverage ?? "LIFE").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (b.Age is null or < 18 or > 65) errors.Add(new FieldError("age", "entre 18 y 65"));
            if (!Money.TryParse(b.Capital, out var capital) || capital < 50_000 || capital > 5_000_000) errors.Add(new FieldError("capital", "entre 50000.00 y 5000000.00, como texto"));
            if (coverage is not ("LIFE" or "DISABILITY")) errors.Add(new FieldError("coverage", "LIFE o DISABILITY"));
            if (b.LoadingPct is not (null or 0 or 25 or 50 or 75)) errors.Add(new FieldError("loadingPct", "0, 25, 50 o 75 (lo da la suscripción médica)"));
            if (b.TermYears is null or < 1 or > 40) errors.Add(new FieldError("termYears", "entre 1 y 40"));
            else if (b.Age is not null && b.Age + b.TermYears > 70) errors.Add(new FieldError("termYears", "el seguro termina como muy tarde a los 70 años"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Cotización no válida.", "Ejemplo: { \"age\": 40, \"capital\": \"1000000.00\", \"coverage\": \"LIFE\", \"loadingPct\": 0, \"termYears\": 20 }", errors);
            var monthly = Monthly(b.Age!.Value, capital, coverage, b.LoadingPct ?? 0, b.TermYears!.Value);
            return Results.Ok(new
            {
                age = b.Age, capital = Money.Format(capital, 2), coverage, loadingPct = b.LoadingPct ?? 0, termYears = b.TermYears,
                monthlyPremium = new { amount = Money.Format(monthly, 2), currency = "SEK" }, annualPremium = Money.Format(monthly * 12, 2), expenseLoadingPct = 15,
            });
        }).WithTags("Primas").WithSummary("Prima mensual de vida o invalidez");
    }
}
