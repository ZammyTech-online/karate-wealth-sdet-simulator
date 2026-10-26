using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Benchmark(string Code, string Name, string Currency, string Description);
public sealed record Level(string Date, string Value);
public sealed record LevelSeries(string Code, string From, string To, int Count, IReadOnlyList<Level> Levels);
public sealed record BenchmarkReturn(string Code, string From, string To, string StartLevel, string EndLevel, string ReturnPercent);
public sealed record Comparison(string Isin, string Benchmark, string From, string To, string FundReturnPercent, string BenchmarkReturnPercent, string DifferencePercent, bool BeatBenchmark);

/// <summary>benchmark-service: índices de referencia ficticios y comparación de un fondo con su índice.</summary>
public sealed class BenchmarkService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "benchmark-service", "I-market", 6807, 1, "Inversiones",
        "Índices de referencia NVX con un valor por día hábil, rentabilidad entre fechas y comparación fondo frente a índice.",
        DependsOn: new[] { "market-data" });

    private static readonly Benchmark[] All =
    {
        new("NVX-SE", "Nordvik Sverige Index", "SEK", "Acciones suecas de gran capitalización"),
        new("NVX-GL", "Nordvik Global Index", "SEK", "Acciones globales en coronas"),
        new("NVX-BOND", "Nordvik Obligationsindex", "SEK", "Bonos suecos"),
        new("NVX-MM", "Nordvik Penningmarknadsindex", "SEK", "Mercado monetario sueco"),
    };
    private static readonly Dictionary<string, (decimal Start, double Drift, double Vol)> Params = new()
    {
        ["NVX-SE"] = (2300m, 0.00026, 0.010), ["NVX-GL"] = (1800m, 0.00028, 0.008), ["NVX-BOND"] = (450m, 0.00009, 0.0025), ["NVX-MM"] = (102m, 0.00005, 0.0002),
    };
    private static readonly ConcurrentDictionary<string, SortedDictionary<DateOnly, decimal>> Cache = new();

    private static SortedDictionary<DateOnly, decimal> Series(string code) => Cache.GetOrAdd(code, c =>
    {
        var (start, drift, vol) = Params[c];
        var rnd = new Random(c.Aggregate(7, (h, ch) => h * 31 + ch) & 0x7FFFFFFF);
        var s = new SortedDictionary<DateOnly, decimal>();
        var v = start;
        for (var d = new DateOnly(2025, 1, 2); d <= new DateOnly(2026, 12, 31); d = d.AddDays(1))
        {
            if (!NordicCalendar.IsTradingDay("XSTO", d)) continue;
            v = Math.Max(1m, v * (1 + (decimal)((rnd.NextDouble() * 2 - 1) * vol + drift)));
            s[d] = Math.Round(v, 2);
        }
        return s;
    });

    private static bool Range(HttpContext http, string? from, string? to, out DateOnly f, out DateOnly t, out IResult? p)
    {
        p = null; t = default;
        if (!Money.TryParseDate(from, out f) || !Money.TryParseDate(to, out t))
        { p = Problems.BadRequest(http, "INVALID_DATE", "from y to son obligatorios (AAAA-MM-DD).", "Ejemplo: ?from=2026-01-02&to=2026-06-30"); return false; }
        if (f >= t) { p = Problems.BadRequest(http, "INVALID_RANGE", "from debe ser anterior a to.", null); return false; }
        if (f < new DateOnly(2025, 1, 2) || t > new DateOnly(2026, 12, 31)) { p = Problems.NotFound(http, "DATE_OUT_OF_RANGE", "Hay datos de 2025-01-02 a 2026-12-31.", null); return false; }
        return true;
    }

    private static decimal At(SortedDictionary<DateOnly, decimal> s, DateOnly d) => s.Where(kv => kv.Key <= d).Select(kv => kv.Value).DefaultIfEmpty(s.First().Value).Last();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/benchmarks", () => Results.Ok(All)).WithTags("Índices").WithSummary("Índices disponibles").Produces<Benchmark[]>();

        app.MapGet("/api/v1/benchmarks/{code}/levels", (HttpContext http, string code, string? from, string? to) =>
        {
            if (!Params.ContainsKey(code)) return Unknown(http, code);
            if (!Range(http, from, to, out var f, out var t, out var p)) return p!;
            var levels = Series(code).Where(kv => kv.Key >= f && kv.Key <= t).Select(kv => new Level(Money.Date(kv.Key), Money.Format(kv.Value, 2))).ToList();
            return Results.Ok(new LevelSeries(code, Money.Date(f), Money.Date(t), levels.Count, levels));
        }).WithTags("Índices").WithSummary("Valores diarios de un índice").Produces<LevelSeries>();

        app.MapGet("/api/v1/benchmarks/{code}/return", (HttpContext http, string code, string? from, string? to) =>
        {
            if (!Params.ContainsKey(code)) return Unknown(http, code);
            if (!Range(http, from, to, out var f, out var t, out var p)) return p!;
            var s = Series(code);
            var a = At(s, f); var b = At(s, t);
            return Results.Ok(new BenchmarkReturn(code, Money.Date(f), Money.Date(t), Money.Format(a, 2), Money.Format(b, 2), Money.Format((b / a - 1) * 100, 2)));
        }).WithTags("Índices").WithSummary("Rentabilidad de un índice entre dos fechas").Produces<BenchmarkReturn>();

        app.MapGet("/api/v1/compare", async (HttpContext http, string? isin, string? benchmark, string? from, string? to) =>
        {
            if (isin is null || benchmark is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "isin y benchmark son obligatorios.", "Ejemplo: ?isin=SE0000000002&benchmark=NVX-SE&from=2026-01-02&to=2026-06-30");
            if (!Params.ContainsKey(benchmark)) return Unknown(http, benchmark);
            if (!Range(http, from, to, out var f, out var t, out var p)) return p!;
            var h = await Downstream.Get(http, "market-data", $"/api/v1/prices/{isin}/history?from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}");
            if (!h.Ok) return Results.Json(h.Body, statusCode: h.Status, contentType: "application/problem+json; charset=utf-8");
            var prices = h.Json.GetProperty("prices");
            if (prices.GetArrayLength() < 2) return Problems.Unprocessable(http, "NOT_ENOUGH_PRICES", "No hay suficientes precios publicados en ese rango.", "¿El rango termina en el futuro según el reloj virtual?");
            var first = decimal.Parse(prices[0].GetProperty("nav").GetString()!, CultureInfo.InvariantCulture);
            var last = decimal.Parse(prices[prices.GetArrayLength() - 1].GetProperty("nav").GetString()!, CultureInfo.InvariantCulture);
            var fr = (last / first - 1) * 100;
            var s = Series(benchmark);
            var br = (At(s, t) / At(s, f) - 1) * 100;
            return Results.Ok(new Comparison(isin, benchmark, Money.Date(f), Money.Date(t), Money.Format(fr, 2), Money.Format(br, 2), Money.Format(fr - br, 2), fr > br));
        }).WithTags("Índices").WithSummary("¿Ha batido el fondo a su índice?").Produces<Comparison>();
    }

    private static IResult Unknown(HttpContext http, string code) =>
        Problems.NotFound(http, "BENCHMARK_NOT_FOUND", $"No existe el índice {code}.", "Índices: NVX-SE, NVX-GL, NVX-BOND, NVX-MM.");
}
