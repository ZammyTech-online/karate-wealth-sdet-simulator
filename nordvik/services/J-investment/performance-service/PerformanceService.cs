using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PerformancePoint(string Date, string ValueSek, string FlowSek);
public sealed record Performance(string PortfolioId, string From, string To, int TradingDays, string StartValueSek, string EndValueSek, string NetFlowsSek, string GainSek,
    string? TwrPercent, string? MwrPercentAnnualized, IReadOnlyList<PerformancePoint>? Series, string Method);

/// <summary>
/// performance-service: rentabilidad de una cartera.
/// TWR (ponderada por tiempo): elimina el efecto de las aportaciones; encadena los rendimientos diarios r = (V_t − F_t) / V_(t−1) − 1.
/// MWR (ponderada por dinero, TIR anualizada): sí tiene en cuenta cuándo y cuánto dinero entró.
/// Flujos: compras = entrada (importe bruto), ventas = salida (importe neto), en su fecha de negociación.
/// </summary>
public sealed class PerformanceService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "performance-service", "J-investment", 6907, 2, "Inversiones",
        "Rentabilidad ponderada por tiempo (TWR) y por dinero (MWR/TIR anualizada) entre dos fechas, con la serie diaria de valores y flujos.",
        DependsOn: new[] { "position-keeping", "execution-service", "market-data", "fx-rates", "portfolio-service", "permission-service", "clock-service" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/performance", async (HttpContext http, string portfolioId, string? from, string? to, bool? series) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            if (!Money.TryParseDate(from, out var f)) return Problems.BadRequest(http, "INVALID_DATE", "from es obligatorio (AAAA-MM-DD).", "Ejemplo: ?from=2026-01-01&to=2026-06-30");
            var lastPublished = NavMath.LastPublishedDate(clock.LocalNow);
            var t = lastPublished;
            if (to is not null && !Money.TryParseDate(to, out t)) return Problems.BadRequest(http, "INVALID_DATE", "to debe ser AAAA-MM-DD.", null);
            if (t > lastPublished) t = lastPublished;
            if (f >= t) return Problems.BadRequest(http, "INVALID_RANGE", "from debe ser anterior a to (y a la última fecha con NAV publicado).", null);
            if (t.DayNumber - f.DayNumber > 366) return Problems.BadRequest(http, "RANGE_TOO_LARGE", "Como mucho 366 días.", null);
            if (f < NavMath.First) return Problems.NotFound(http, "DATE_OUT_OF_RANGE", "No hay precios antes de " + Money.Date(NavMath.First) + ".", null);
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var perm = await Permissions.Check(http, p, "portfolio:read", pf.Str("customerId"));
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");

            var moves = (await Downstream.Get(http, "position-keeping", $"/api/v1/internal/portfolios/{portfolioId}/movements", forwardAuth: false)).Json.EnumerateArray()
                .Select(m => (Isin: m.GetProperty("isin").GetString()!, Units: D(m.GetProperty("units").GetString()!), Date: DateOnly.Parse(m.GetProperty("date").GetString()!, CultureInfo.InvariantCulture))).ToList();
            var execs = (await Downstream.Get(http, "execution-service", $"/api/v1/executions?portfolioId={portfolioId}", forwardAuth: false)).Json.EnumerateArray()
                .Select(e => (Date: DateOnly.Parse(e.GetProperty("tradeDate").GetString()!, CultureInfo.InvariantCulture),
                    Flow: e.GetProperty("side").GetString() == "BUY" ? D(e.GetProperty("grossAmountSek").GetString()!) : -D(e.GetProperty("netAmountSek").GetString()!))).ToList();
            var days = new List<DateOnly>();
            for (var d = NordicCalendar.PreviousOrSameTradingDay("XSTO", f); d <= t; d = d.AddDays(1)) if (NordicCalendar.IsTradingDay("XSTO", d)) days.Add(d);
            var isins = moves.Select(m => m.Isin).Distinct().ToList();
            var navs = new Dictionary<string, Dictionary<DateOnly, decimal>>();
            var ccys = new Dictionary<string, string>();
            foreach (var isin in isins)
            {
                var h = await Downstream.Get(http, "market-data", $"/api/v1/prices/{isin}/history?from={Money.Date(days[0])}&to={Money.Date(t)}", forwardAuth: false);
                ccys[isin] = h.Str("currency")!;
                navs[isin] = h.Json.GetProperty("prices").EnumerateArray().ToDictionary(x => DateOnly.Parse(x.GetProperty("date").GetString()!, CultureInfo.InvariantCulture), x => D(x.GetProperty("nav").GetString()!));
            }
            var fxCache = new Dictionary<(string, DateOnly), decimal>();
            async Task<decimal> Fx(string ccy, DateOnly d)
            {
                if (ccy == "SEK") return 1m;
                if (fxCache.TryGetValue((ccy, d), out var v)) return v;
                var r = await Downstream.Get(http, "fx-rates", $"/api/v1/rates/{ccy}/SEK?date={Money.Date(d)}", forwardAuth: false);
                return fxCache[(ccy, d)] = D(r.Str("rate")!);
            }
            var points = new List<(DateOnly Date, decimal Value, decimal Flow)>();
            foreach (var d in days)
            {
                decimal value = 0;
                foreach (var isin in isins)
                {
                    var units = moves.Where(m => m.Isin == isin && m.Date <= d).Sum(m => m.Units);
                    if (units == 0 || !navs[isin].TryGetValue(d, out var nav)) continue;
                    value += Math.Round(units * nav * await Fx(ccys[isin], d), 2, MidpointRounding.ToEven);
                }
                var flow = d == days[0] ? 0 : execs.Where(e => e.Date == d).Sum(e => e.Flow);
                points.Add((d, value, flow));
            }
            // TWR: encadenar rendimientos diarios
            decimal? growth = 1m; var anyPeriod = false;
            for (var i = 1; i < points.Count; i++)
            {
                var prev = points[i - 1].Value;
                if (prev <= 0) continue;
                growth *= (points[i].Value - points[i].Flow) / prev;
                anyPeriod = true;
            }
            var twr = anyPeriod ? Money.Format((growth!.Value - 1) * 100, 2) : null;
            // MWR: TIR anualizada desde el punto de vista del inversor (aportar = negativo, valor final = positivo)
            var cfs = new List<(double Years, double Amount)>();
            var start = points[0];
            if (start.Value > 0) cfs.Add((0, -(double)start.Value));
            foreach (var pt in points.Skip(1).Where(x => x.Flow != 0)) cfs.Add(((pt.Date.DayNumber - start.Date.DayNumber) / 365.0, -(double)pt.Flow));
            cfs.Add(((points[^1].Date.DayNumber - start.Date.DayNumber) / 365.0, (double)points[^1].Value));
            string? mwr = null;
            if (cfs.Any(c => c.Amount < 0) && cfs.Any(c => c.Amount > 0))
            {
                double Npv(double r) => cfs.Sum(c => c.Amount / Math.Pow(1 + r, c.Years));
                double lo = -0.9999, hi = 100;
                if (Npv(lo) * Npv(hi) < 0)
                {
                    for (var k = 0; k < 200; k++) { var mid = (lo + hi) / 2; if (Npv(lo) * Npv(mid) <= 0) hi = mid; else lo = mid; }
                    mwr = Money.Format((decimal)((lo + hi) / 2 * 100), 2);
                }
            }
            var netFlows = points.Skip(1).Sum(x => x.Flow);
            return Results.Ok(new Performance(portfolioId, Money.Date(days[0]), Money.Date(t), points.Count, Money.Format(start.Value, 2), Money.Format(points[^1].Value, 2),
                Money.Format(netFlows, 2), Money.Format(points[^1].Value - start.Value - netFlows, 2), twr, mwr,
                series == true ? points.Select(x => new PerformancePoint(Money.Date(x.Date), Money.Format(x.Value, 2), Money.Format(x.Flow, 2))).ToList() : null,
                "TWR = Π((V_t − F_t) / V_(t−1)) − 1 · MWR = TIR anualizada (base 365)"));
        }).WithTags("Rentabilidad").WithSummary("TWR y MWR de una cartera entre dos fechas (series=true añade la serie diaria)").Produces<Performance>();
    }
}
