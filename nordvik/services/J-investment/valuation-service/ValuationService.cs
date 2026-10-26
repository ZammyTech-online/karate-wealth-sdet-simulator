using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ValuationLine(string Isin, string FundName, string Units, string Nav, string NavDate, string FundCurrency, string FxRate, string ValueSek, string CostSek, string WeightPercent);
public sealed record Valuation(string PortfolioId, string CustomerId, string ValuationDate, string PriceDate, string Currency, string TotalValueSek, string TotalCostSek,
    string UnrealizedGainSek, string? UnrealizedGainPercent, IReadOnlyList<ValuationLine> Lines);

/// <summary>
/// valuation-service: valor de una cartera a una fecha = participaciones (position-keeping) × NAV del último día hábil publicado (market-data)
/// × tipo de cambio a SEK (fx-rates). Redondeo bancario a 2 decimales por línea; los pesos suman 100 ± 0.01.
/// </summary>
public sealed class ValuationService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "valuation-service", "J-investment", 6906, 2, "Inversiones",
        "Valoración de carteras a una fecha: participaciones × NAV × tipo de cambio, con coste, plusvalía latente y peso de cada fondo.",
        DependsOn: new[] { "position-keeping", "market-data", "fx-rates", "product-catalog", "portfolio-service", "permission-service", "clock-service" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public static async Task<Valuation?> Value(HttpContext? http, IClock clock, string portfolioId, DateOnly date)
    {
        var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
        if (pf.Status == 404) return null;
        var lastPublished = NavMath.LastPublishedDate(clock.LocalNow);
        var priceDate = NordicCalendar.PreviousOrSameTradingDay("XSTO", date);
        if (priceDate > lastPublished) priceDate = lastPublished;
        var pos = await Downstream.Get(http, "position-keeping", $"/api/v1/internal/portfolios/{portfolioId}/positions?asOf={Money.Date(date)}", forwardAuth: false);
        if (!pos.Ok) throw new DependencyUnavailableException("position-keeping", "position-keeping respondió " + pos.Status);
        var lines = new List<(string Isin, string Name, decimal Units, string Nav, string Ccy, decimal Fx, decimal Value, decimal Cost)>();
        foreach (var p in pos.Json.GetProperty("positions").EnumerateArray())
        {
            var units = D(p.GetProperty("units").GetString()!);
            if (units == 0) continue;
            var isin = p.GetProperty("isin").GetString()!;
            var nav = await Downstream.Get(http, "market-data", $"/api/v1/prices/{isin}?date={Money.Date(priceDate)}", forwardAuth: false);
            if (!nav.Ok) throw new DependencyUnavailableException("market-data", $"market-data no dio el NAV de {isin} ({nav.Status})");
            var fund = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{isin}", forwardAuth: false);
            var ccy = nav.Str("currency")!;
            var fx = 1m;
            if (ccy != "SEK")
            {
                var r = await Downstream.Get(http, "fx-rates", $"/api/v1/rates/{ccy}/SEK?date={Money.Date(priceDate)}", forwardAuth: false);
                fx = D(r.Str("rate")!);
            }
            var value = Math.Round(units * D(nav.Str("nav")!) * fx, 2, MidpointRounding.ToEven);
            lines.Add((isin, fund.Ok ? fund.Str("name")! : isin, units, nav.Str("nav")!, ccy, fx, value, D(p.GetProperty("costBasisSek").GetString()!)));
        }
        var total = lines.Sum(l => l.Value); var cost = lines.Sum(l => l.Cost);
        var result = lines.Select(l => new ValuationLine(l.Isin, l.Name, Money.Format(l.Units, 4), l.Nav, Money.Date(priceDate), l.Ccy, Money.Format(l.Fx, 6), Money.Format(l.Value, 2),
            Money.Format(l.Cost, 2), total == 0 ? "0.00" : Money.Format(l.Value / total * 100, 2))).ToList();
        return new Valuation(portfolioId, pf.Str("customerId")!, Money.Date(date), Money.Date(priceDate), "SEK", Money.Format(total, 2), Money.Format(cost, 2),
            Money.Format(total - cost, 2), cost == 0 ? null : Money.Format((total - cost) / cost * 100, 2), result);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/valuation", async (HttpContext http, string portfolioId, string? date) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            var d = clock.Today;
            if (date is not null && !Money.TryParseDate(date, out d)) return Problems.BadRequest(http, "INVALID_DATE", "date debe ser AAAA-MM-DD.", null);
            if (d > clock.Today) return Problems.Unprocessable(http, "FUTURE_DATE", "No se valora en el futuro.", "Usa hoy o una fecha pasada (reloj virtual).");
            if (d < NavMath.First) return Problems.NotFound(http, "DATE_OUT_OF_RANGE", "No hay precios antes de " + Money.Date(NavMath.First) + ".", null);
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var perm = await Permissions.Check(http, p, "portfolio:read", pf.Str("customerId"));
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");
            return Results.Ok(await Value(http, clock, portfolioId, d));
        }).WithTags("Valoración").WithSummary("Valor de una cartera a una fecha (por defecto hoy)").Produces<Valuation>();

        app.MapGet("/api/v1/internal/portfolios/{portfolioId}/valuation", async (HttpContext http, string portfolioId, string? date) =>
        {
            var d = clock.Today;
            if (date is not null && !Money.TryParseDate(date, out d)) return Problems.BadRequest(http, "INVALID_DATE", "date debe ser AAAA-MM-DD.", null);
            var v = await Value(http, clock, portfolioId, d);
            return v is null ? Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null) : Results.Ok(v);
        }).WithTags("Interno").WithSummary("Valoración para otros servicios (red interna)").Produces<Valuation>();
    }
}
