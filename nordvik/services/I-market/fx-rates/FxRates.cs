using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RateTable(string Base, string RequestedDate, string RateDate, IReadOnlyDictionary<string, string> Rates);
public sealed record PairRate(string From, string To, string RequestedDate, string RateDate, string Rate);
public sealed record Conversion(string From, string To, string Amount, string Converted, string Rate, string RateDate);

/// <summary>
/// fx-rates: tipos de cambio diarios ficticios. Equipo de Tesorería, camelCase.
/// Los fines de semana y festivos de XSTO se usa el tipo del día hábil anterior (rateDate lo dice).
/// </summary>
public sealed class FxRates : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "fx-rates", "I-market", 6803, 0, "Tesorería",
        "Tipos de cambio diarios entre SEK, EUR, NOK, DKK, USD y GBP, y conversión de importes con redondeo bancario.",
        DependsOn: new[] { "clock-service" });

    /// <summary>Coronas suecas por unidad de cada divisa (valor de referencia).</summary>
    private static readonly Dictionary<string, decimal> SekPerUnit = new()
    {
        ["SEK"] = 1m, ["EUR"] = 11.45m, ["NOK"] = 1.02m, ["DKK"] = 1.535m, ["USD"] = 10.55m, ["GBP"] = 13.40m,
    };

    private static decimal SekPer(string ccy, DateOnly d)
    {
        if (ccy == "SEK") return 1m;
        var phase = (ccy.GetHashCode() & 0xFF) / 40.0;
        var wave = Math.Sin(d.DayNumber / 23.0 + phase) * 0.012 + Math.Sin(d.DayNumber / 5.0 + phase * 2) * 0.003;
        return Math.Round(SekPerUnit[ccy] * (1 + (decimal)wave), 6);
    }

    private static decimal Rate(string from, string to, DateOnly d) => Math.Round(SekPer(from, d) / SekPer(to, d), 6, MidpointRounding.ToEven);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1").WithTags("Tipos de cambio");

        g.MapGet("/currencies", () => Results.Ok(SekPerUnit.Keys.OrderBy(k => k).ToList()))
            .WithSummary("Divisas soportadas").Produces<List<string>>();

        g.MapGet("/rates", (HttpContext http, IClock clock, string? @base, string? date) =>
        {
            var b = (@base ?? "SEK").ToUpperInvariant();
            if (!SekPerUnit.ContainsKey(b)) return Unsupported(http, b);
            if (!ResolveDate(http, clock, date, out var requested, out var rateDate, out var problem)) return problem!;
            var rates = SekPerUnit.Keys.Where(k => k != b).OrderBy(k => k).ToDictionary(k => k, k => Money.Format(Rate(b, k, rateDate), 6));
            return Results.Ok(new RateTable(b, Money.Date(requested), Money.Date(rateDate), rates));
        }).WithSummary("Tabla de tipos de una divisa base: cuántas unidades de cada divisa vale 1 de la base").Produces<RateTable>();

        g.MapGet("/rates/{from}/{to}", (HttpContext http, IClock clock, string from, string to, string? date) =>
        {
            var f = from.ToUpperInvariant(); var t = to.ToUpperInvariant();
            if (!SekPerUnit.ContainsKey(f)) return Unsupported(http, f);
            if (!SekPerUnit.ContainsKey(t)) return Unsupported(http, t);
            if (!ResolveDate(http, clock, date, out var requested, out var rateDate, out var problem)) return problem!;
            return Results.Ok(new PairRate(f, t, Money.Date(requested), Money.Date(rateDate), Money.Format(Rate(f, t, rateDate), 6)));
        }).WithSummary("Tipo de cambio de un par").Produces<PairRate>();

        g.MapGet("/convert", (HttpContext http, IClock clock, string? from, string? to, string? amount, string? date) =>
        {
            if (from is null || to is null || amount is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Faltan parámetros: from, to y amount son obligatorios.",
                    "Ejemplo: /api/v1/convert?from=EUR&to=SEK&amount=100.00");
            var f = from.ToUpperInvariant(); var t = to.ToUpperInvariant();
            if (!SekPerUnit.ContainsKey(f)) return Unsupported(http, f);
            if (!SekPerUnit.ContainsKey(t)) return Unsupported(http, t);
            if (!Money.TryParse(amount, out var value) || value < 0)
                return Problems.BadRequest(http, "INVALID_AMOUNT", $"'{amount}' no es un importe válido.",
                    "Los importes son decimales positivos con punto, como texto: 100.00 (no 100,00).");
            if (!ResolveDate(http, clock, date, out _, out var rateDate, out var problem)) return problem!;
            var rate = Rate(f, t, rateDate);
            return Results.Ok(new Conversion(f, t, Money.Format(value, 2), Money.Format(value * rate, 2), Money.Format(rate, 6), Money.Date(rateDate)));
        }).WithSummary("Convertir un importe (redondeo bancario a 2 decimales)").Produces<Conversion>();
    }

    private static bool ResolveDate(HttpContext http, IClock clock, string? date, out DateOnly requested, out DateOnly rateDate, out IResult? problem)
    {
        problem = null; rateDate = default;
        if (date is null) requested = clock.Today;
        else if (!Money.TryParseDate(date, out requested))
        {
            problem = Problems.BadRequest(http, "INVALID_DATE", $"'{date}' no es una fecha válida.", "Usa AAAA-MM-DD.");
            return false;
        }
        if (requested.Year is < 2020 or > 2035)
        {
            problem = Problems.NotFound(http, "DATE_OUT_OF_RANGE", "Solo hay tipos entre 2020 y 2035.", "Elige una fecha en ese rango.");
            return false;
        }
        rateDate = NordicCalendar.PreviousOrSameTradingDay("XSTO", requested);
        return true;
    }

    private static IResult Unsupported(HttpContext http, string ccy) =>
        Problems.BadRequest(http, "UNSUPPORTED_CURRENCY", $"La divisa '{ccy}' no está soportada.",
            "Divisas válidas: SEK, EUR, NOK, DKK, USD, GBP (código ISO 4217 de 3 letras). Lista completa en GET /api/v1/currencies.");
}
