using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// esg-data: sostenibilidad de cada fondo (puntuación 0-100, rating AAA a CCC, artículo SFDR 6/8/9, intensidad de carbono), determinista por ISIN,
/// y la ESG de una cartera ponderada por su valoración. Ver la ESG de una cartera no exige consentimiento; usarla para perfilar al cliente,
/// sí (ESG_PROFILING): /customers/{id}/esg-profile lo comprueba.
/// </summary>
public sealed class EsgData : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "esg-data", "I-market", 6808, 4, "Mercado",
        "Datos de sostenibilidad por fondo (puntuación, rating, SFDR, carbono), ranking, ESG ponderada de una cartera y perfil ESG del cliente con consentimiento.",
        DependsOn: new[] { "product-catalog", "portfolio-service", "valuation-service", "consent-ledger", "permission-service", "clock-service" });

    public static (int Score, string Rating, int Sfdr, decimal Carbon) For(string isin, string name, string category)
    {
        var h = Convert.ToInt32(Canonical.Sha256Hex("esg:" + isin)[..4], 16);
        var score = category switch { "MONEY_MARKET" => 60, "FIXED_INCOME" => 55, "MIXED" => 50, _ => 40 } + h % 35;
        if (name.Contains("ESG")) score = Math.Max(score, 85);
        score = Math.Min(score, 100);
        var rating = score switch { >= 86 => "AAA", >= 71 => "AA", >= 57 => "A", >= 43 => "BBB", >= 29 => "BB", >= 14 => "B", _ => "CCC" };
        var sfdr = name.Contains("ESG") ? 9 : score >= 70 ? 8 : 6;
        var carbon = category is "MONEY_MARKET" or "FIXED_INCOME" ? 20 + h % 60 : 60 + h % 240;
        return (score, rating, sfdr, carbon);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        object View(FundSeedRow f) { var e = For(f.Isin, f.Name, f.Category); return new { isin = f.Isin, name = f.Name, category = f.Category, score = e.Score, rating = e.Rating, sfdrArticle = e.Sfdr, carbonIntensity = e.Carbon, carbonUnit = "tCO2e/MSEK", provider = "Nordvik ESG (sintético)" }; }

        app.MapGet("/api/v1/funds/{isin}/esg", (HttpContext http, string isin) =>
            FundSeed.All.FirstOrDefault(f => f.Isin == isin.ToUpperInvariant()) is { } f ? Results.Ok(View(f)) : Problems.NotFound(http, "UNKNOWN_FUND", $"No hay datos ESG de {isin}.", null))
            .WithTags("ESG").WithSummary("Datos ESG de un fondo");

        app.MapGet("/api/v1/esg/ranking", (int? minScore, int? sfdr) =>
        {
            var items = FundSeed.All.Select(f => (f, e: For(f.Isin, f.Name, f.Category))).Where(x => (minScore == null || x.e.Score >= minScore) && (sfdr == null || x.e.Sfdr == sfdr))
                .OrderByDescending(x => x.e.Score).ThenBy(x => x.f.Isin).Select(x => View(x.f)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("ESG").WithSummary("Fondos ordenados por puntuación (filtros minScore y sfdr)");

        app.MapGet("/api/v1/portfolios/{portfolioId}/esg", async (HttpContext http, string portfolioId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            if (await CustomerGuard.Check(http, p, "portfolio:read", pf.Str("customerId")!) is { } g) return g;
            var v = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{portfolioId}/valuation", forwardAuth: false);
            var lines = v.Ok && v.Json.TryGetProperty("lines", out var ls) ? ls.EnumerateArray().ToList() : new();
            var total = lines.Sum(l => decimal.Parse(l.GetProperty("valueSek").GetString()!, Money.Inv));
            var basis = "VALUATION";
            List<(string Isin, decimal Weight)> weights;
            if (total > 0) weights = lines.Select(l => (l.GetProperty("isin").GetString()!, decimal.Parse(l.GetProperty("valueSek").GetString()!, Money.Inv) / total)).ToList();
            else
            {
                basis = "TARGET_ALLOCATION";
                weights = pf.Json.GetProperty("targetAllocation").EnumerateArray().Select(a => (a.GetProperty("isin").GetString()!, a.GetProperty("percent").GetDecimal() / 100)).ToList();
            }
            var detail = weights.Select(w => { var f = FundSeed.All.First(x => x.Isin == w.Isin); var e = For(f.Isin, f.Name, f.Category); return (w, e); }).ToList();
            var score = Math.Round(detail.Sum(d => d.w.Weight * d.e.Score), 1);
            return Results.Ok(new
            {
                portfolioId, basis, score, carbonIntensity = Math.Round(detail.Sum(d => d.w.Weight * d.e.Carbon), 1),
                article9Share = Math.Round(detail.Where(d => d.e.Sfdr == 9).Sum(d => d.w.Weight) * 100, 2),
                funds = detail.Select(d => new { isin = d.w.Isin, weightPct = Math.Round(d.w.Weight * 100, 2), score = d.e.Score, sfdrArticle = d.e.Sfdr }),
            });
        }).WithTags("ESG").WithSummary("ESG de una cartera, ponderada por valor (o por la asignación si aún no vale nada)");

        app.MapGet("/api/v1/customers/{customerId}/esg-profile", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var c = await Downstream.Get(http, "consent-ledger", $"/api/v1/internal/consents/check?customerId={customerId}&purpose=ESG_PROFILING", forwardAuth: false);
            if (!c.Ok || !c.Json.GetProperty("granted").GetBoolean())
                return Problems.Of(http, 403, "CONSENT_REQUIRED", "Sin consentimiento", "Para perfilar al cliente por sostenibilidad hace falta el consentimiento ESG_PROFILING.", "consent-ledger (6502): POST /api/v1/consents { purpose: ESG_PROFILING, granted: true }");
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios?customerId={customerId}", forwardAuth: false);
            var isins = pfs.Json.EnumerateArray().SelectMany(pf => pf.GetProperty("targetAllocation").EnumerateArray().Select(a => a.GetProperty("isin").GetString()!)).Distinct().ToList();
            var scores = isins.Select(i => FundSeed.All.First(f => f.Isin == i)).Select(f => For(f.Isin, f.Name, f.Category).Score).ToList();
            var avg = scores.Count == 0 ? 0 : scores.Average();
            return Results.Ok(new { customerId, funds = isins.Count, averageScore = Math.Round(avg, 1), profile = scores.Count == 0 ? "UNKNOWN" : avg >= 70 ? "SUSTAINABILITY_FOCUSED" : avg >= 50 ? "BALANCED" : "CONVENTIONAL" });
        }).WithTags("ESG").WithSummary("Perfil ESG del cliente (exige el consentimiento ESG_PROFILING)");
    }
}
