using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record QuoteRequest(string? Capital, int? Age, string? Gender, string? Type, int? TermYears, decimal? TechnicalRatePct, int? GuaranteeYears);

/// <summary>
/// annuity-calculator: cálculos actuariales con una tabla de mortalidad SINTÉTICA (Gompertz-Makeham, no es una tabla oficial).
/// Pago temporal (TERM): capital repartido en n años con interés técnico. Vitalicio (LIFE): capital / suma de pagos esperados descontados
/// por supervivencia. Todo redondeado al céntimo con redondeo bancario. Determinista: el mismo cálculo da siempre lo mismo.
/// </summary>
public sealed class AnnuityCalculator : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "annuity-calculator", "L-pensions", 7112, 3, "Pensiones",
        "Rentas temporales y vitalicias con tabla de mortalidad sintética (Gompertz-Makeham) e interés técnico; esperanza de vida y factores, deterministas.");

    /// <summary>Probabilidad de morir en el año a la edad x (sintética).</summary>
    public static double Qx(int age, string gender)
    {
        var shift = gender == "F" ? -4 : 0; // las mujeres viven unos 4 años más en la tabla
        var mu = 0.0005 + 0.0001 * Math.Exp(0.1 * (age + shift - 20)); // calibrada: esperanza de vida a los 65 ≈ 21 (H) y 25 (M)
        return Math.Min(1.0, 1 - Math.Exp(-mu));
    }

    public static double LifeExpectancy(int age, string gender)
    {
        double alive = 1, years = 0;
        for (var x = age; x < 120 && alive > 1e-6; x++) { var q = Qx(x, gender); years += alive * (1 - q / 2); alive *= 1 - q; }
        return Math.Round(years, 2);
    }

    /// <summary>Factor de renta mensual anticipada: suma de v^(t/12) · p(supervivencia) para cada mes.</summary>
    public static double Factor(int age, string gender, string type, int termYears, double ratePct, int guaranteeYears)
    {
        var v = 1 / (1 + ratePct / 100);
        double factor = 0, alive = 1;
        var months = type == "TERM" ? termYears * 12 : (120 - age) * 12;
        for (var m = 0; m < months; m++)
        {
            var survival = type == "TERM" || m < guaranteeYears * 12 ? 1 : alive;
            factor += Math.Pow(v, m / 12.0) * survival;
            if (m % 12 == 11) alive *= 1 - Qx(age + m / 12, gender);
        }
        return factor;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/mortality", (HttpContext http, int? age, string? gender) =>
        {
            var g = (gender ?? "M").ToUpperInvariant();
            if (age is null or < 18 or > 110 || g is not ("M" or "F")) return Problems.BadRequest(http, "VALIDATION_FAILED", "age entre 18 y 110, gender M o F.", null);
            return Results.Ok(new { age, gender = g, qx = Math.Round(Qx(age.Value, g), 6), lifeExpectancyYears = LifeExpectancy(age.Value, g), table = "Gompertz-Makeham sintética Nordvik 2026 (no oficial)" });
        }).WithTags("Mortalidad").WithSummary("Probabilidad de fallecer en el año y esperanza de vida a una edad");

        app.MapPost("/api/v1/annuities/quote", (HttpContext http, QuoteRequest b) =>
        {
            var type = (b.Type ?? "TERM").ToUpperInvariant();
            var gender = (b.Gender ?? "M").ToUpperInvariant();
            var rate = (double)(b.TechnicalRatePct ?? 2.0m);
            var errors = new List<FieldError>();
            if (!Money.TryParse(b.Capital, out var capital) || capital <= 0) errors.Add(new FieldError("capital", "importe positivo como texto"));
            if (b.Age is null or < 55 or > 90) errors.Add(new FieldError("age", "entre 55 y 90 (antes de los 55 no se cobra una pensión privada)"));
            if (gender is not ("M" or "F")) errors.Add(new FieldError("gender", "M o F"));
            if (type is not ("TERM" or "LIFE")) errors.Add(new FieldError("type", "TERM o LIFE"));
            if (type == "TERM" && b.TermYears is null or < 5 or > 30) errors.Add(new FieldError("termYears", "entre 5 y 30 para TERM"));
            if (rate is < 0 or > 5) errors.Add(new FieldError("technicalRatePct", "entre 0 y 5"));
            if (b.GuaranteeYears is < 0 or > 20) errors.Add(new FieldError("guaranteeYears", "entre 0 y 20"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Cotización no válida.", "Ejemplo: { \"capital\": \"1200000.00\", \"age\": 65, \"gender\": \"F\", \"type\": \"TERM\", \"termYears\": 10 }", errors);
            var factor = Factor(b.Age!.Value, gender, type, b.TermYears ?? 0, rate, b.GuaranteeYears ?? 0);
            var monthly = Math.Round(capital / (decimal)factor, 2, MidpointRounding.ToEven);
            var payments = type == "TERM" ? b.TermYears!.Value * 12 : (int)Math.Round(LifeExpectancy(b.Age.Value, gender) * 12);
            return Results.Ok(new
            {
                type, capital = Money.Format(capital, 2), age = b.Age, gender, technicalRatePct = rate, termYears = b.TermYears, guaranteeYears = b.GuaranteeYears ?? 0,
                factor = Math.Round(factor, 6), monthlyPayment = new { amount = Money.Format(monthly, 2), currency = "SEK" }, expectedPayments = payments,
                expectedTotal = Money.Format(monthly * payments, 2), lifeExpectancyYears = LifeExpectancy(b.Age.Value, gender),
            });
        }).WithTags("Rentas").WithSummary("Pago mensual de una renta temporal o vitalicia");
    }
}
