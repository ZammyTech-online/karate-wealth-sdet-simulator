using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// statement-service: el extracto de una cartera para un mes (AAAA-MM) o un trimestre (AAAA-Qn): valor al cierre del periodo anterior,
/// valor al cierre del periodo (o hoy si está en curso), órdenes ejecutadas en el periodo y la variación. En JSON y en PDF.
/// </summary>
public sealed class StatementService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "statement-service", "N-tax", 7303, 4, "Fiscalidad",
        "Extractos mensuales y trimestrales de una cartera: valor inicial y final, órdenes ejecutadas y variación, en JSON y PDF.",
        DependsOn: new[] { "portfolio-service", "valuation-service", "order-management", "permission-service", "clock-service" });

    /// <summary>Primer y último día de un periodo AAAA-MM o AAAA-Qn; null si el texto no vale.</summary>
    public static (DateOnly From, DateOnly To)? Period(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"^(\d{4})-(?:(0[1-9]|1[0-2])|Q([1-4]))$");
        if (!m.Success) return null;
        var y = int.Parse(m.Groups[1].Value);
        if (m.Groups[2].Success) { var mo = int.Parse(m.Groups[2].Value); var f = new DateOnly(y, mo, 1); return (f, f.AddMonths(1).AddDays(-1)); }
        var q = int.Parse(m.Groups[3].Value); var from = new DateOnly(y, (q - 1) * 3 + 1, 1);
        return (from, from.AddMonths(3).AddDays(-1));
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<(IResult? Fail, object? Statement)> Build(HttpContext http, string portfolioId, string period)
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return (denied, null);
            if (Period(period) is not { } range) return (Problems.BadRequest(http, "VALIDATION_FAILED", "period es AAAA-MM o AAAA-Qn.", "Ejemplos: 2026-10, 2026-Q4"), null);
            if (range.From > clock.Today) return (Problems.Unprocessable(http, "PERIOD_NOT_STARTED", $"El periodo {period} todavía no ha empezado.", null), null);
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return (Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null), null);
            var customerId = pf.Str("customerId")!;
            if (await CustomerGuard.Check(http, p, "portfolio:read", customerId) is { } g) return (g, null);
            var to = range.To > clock.Today ? clock.Today : range.To;
            var open = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{portfolioId}/valuation?date={Money.Date(range.From.AddDays(-1))}", forwardAuth: false);
            var close = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{portfolioId}/valuation?date={Money.Date(to)}", forwardAuth: false);
            var orders = await Downstream.Get(http, "order-management", $"/api/v1/portfolios/{portfolioId}/orders?status=EXECUTED");
            var moves = new List<object>(); decimal bought = 0, sold = 0;
            if (orders.Ok && orders.Json.ValueKind == JsonValueKind.Array)
                foreach (var o in orders.Json.EnumerateArray())
                {
                    var td = DateOnly.Parse(o.GetProperty("tradeDate").GetString()!);
                    if (td < range.From || td > to) continue;
                    var net = o.TryGetProperty("netAmountSek", out var n) && n.ValueKind == JsonValueKind.String ? decimal.Parse(n.GetString()!, Money.Inv) : 0;
                    if (o.GetProperty("side").GetString() == "BUY") bought += net; else sold += net;
                    moves.Add(new { tradeDate = Money.Date(td), side = o.GetProperty("side").GetString(), isin = o.GetProperty("isin").GetString(), fundName = o.GetProperty("fundName").GetString(),
                                    units = o.TryGetProperty("executedUnits", out var u) ? u.GetString() : null, nav = o.TryGetProperty("nav", out var nv) ? nv.GetString() : null, amount = Money.Format(net, 2) });
                }
            var openV = open.Ok ? decimal.Parse(open.Str("totalValueSek")!, Money.Inv) : 0;
            var closeV = close.Ok ? decimal.Parse(close.Str("totalValueSek")!, Money.Inv) : 0;
            var marketChange = closeV - openV - bought + sold;
            return (null, new
            {
                portfolioId, customerId, period, from = Money.Date(range.From), to = Money.Date(to), inProgress = range.To > clock.Today, currency = "SEK",
                openingValue = Money.Format(openV, 2), closingValue = Money.Format(closeV, 2), bought = Money.Format(bought, 2), sold = Money.Format(sold, 2),
                marketChange = Money.Format(marketChange, 2), transactions = moves, transactionCount = moves.Count,
            });
        }

        app.MapGet("/api/v1/portfolios/{portfolioId}/statements/{period}", async (HttpContext http, string portfolioId, string period) =>
        {
            var (fail, s) = await Build(http, portfolioId, period);
            return fail ?? Results.Ok(s);
        }).WithTags("Extractos").WithSummary("Extracto de un mes o un trimestre");

        app.MapGet("/api/v1/portfolios/{portfolioId}/statements/{period}/pdf", async (HttpContext http, string portfolioId, string period) =>
        {
            var (fail, s) = await Build(http, portfolioId, period);
            if (fail is not null) return fail;
            var j = JsonSerializer.SerializeToElement(s, Canonical.Web);
            var lines = new List<string>
            {
                $"Cartera: {portfolioId}", $"Periodo: {j.GetProperty("from").GetString()} a {j.GetProperty("to").GetString()}" + (j.GetProperty("inProgress").GetBoolean() ? " (en curso)" : ""),
                $"Valor inicial: {j.GetProperty("openingValue").GetString()} SEK", $"Compras: {j.GetProperty("bought").GetString()} SEK · Ventas: {j.GetProperty("sold").GetString()} SEK",
                $"Variación de mercado: {j.GetProperty("marketChange").GetString()} SEK", $"Valor final: {j.GetProperty("closingValue").GetString()} SEK", "",
            };
            foreach (var t in j.GetProperty("transactions").EnumerateArray())
                lines.Add($"{t.GetProperty("tradeDate").GetString()}  {t.GetProperty("side").GetString()}  {t.GetProperty("fundName").GetString()}  {t.GetProperty("amount").GetString()} SEK");
            return Results.File(PdfWriter.Create($"Nordvik · Extracto {period}", lines, "Simulador Nordvik Wealth & Pension"), "application/pdf", $"extracto-{period}.pdf");
        }).WithTags("Extractos").WithSummary("Extracto en PDF");
    }
}
