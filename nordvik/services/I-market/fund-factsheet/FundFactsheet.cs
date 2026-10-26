using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FactsheetFee(string Type, string Percent);
public sealed record Performance(string? OneMonth, string? ThreeMonths, string? OneYear, string AsOf);
public sealed record Factsheet(string Isin, string Name, string Language, string Category, string Currency, int Sri, string Objective, string RiskText,
    IReadOnlyList<FactsheetFee> Fees, Performance Performance, string? Warning);

/// <summary>fund-factsheet: la ficha comercial (tipo KID) de un fondo en sueco, inglés o español. Combina product-catalog y market-data.</summary>
public sealed class FundFactsheet : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "fund-factsheet", "I-market", 6805, 1, "Inversiones",
        "Ficha comercial del fondo por idioma: objetivo, explicación del riesgo, comisiones y rentabilidad a 1, 3 y 12 meses.",
        Naming: JsonNaming.SnakeCase, DependsOn: new[] { "product-catalog", "market-data", "clock-service" });

    private static readonly Dictionary<string, Dictionary<string, string>> Objectives = new()
    {
        ["es"] = new() { ["EQUITY"] = "Busca hacer crecer el capital a largo plazo invirtiendo en acciones.", ["FIXED_INCOME"] = "Busca rentas estables invirtiendo en bonos.", ["MIXED"] = "Combina acciones y bonos para equilibrar crecimiento y estabilidad.", ["MONEY_MARKET"] = "Busca preservar el capital con inversiones a muy corto plazo." },
        ["en"] = new() { ["EQUITY"] = "Aims to grow capital over the long term by investing in equities.", ["FIXED_INCOME"] = "Aims for stable income by investing in bonds.", ["MIXED"] = "Combines equities and bonds to balance growth and stability.", ["MONEY_MARKET"] = "Aims to preserve capital with very short-term investments." },
        ["sv"] = new() { ["EQUITY"] = "Fonden strävar efter långsiktig kapitaltillväxt genom att investera i aktier.", ["FIXED_INCOME"] = "Fonden strävar efter stabil avkastning genom att investera i obligationer.", ["MIXED"] = "Fonden kombinerar aktier och obligationer.", ["MONEY_MARKET"] = "Fonden strävar efter att bevara kapitalet med kortfristiga placeringar." },
    };

    private static string RiskText(string lang, int sri) => lang switch
    {
        "en" => $"Risk class {sri} of 7. " + (sri >= 6 ? "The value can fall a lot in a short time." : sri >= 4 ? "The value fluctuates moderately." : "Low fluctuations."),
        "sv" => $"Riskklass {sri} av 7. " + (sri >= 6 ? "Värdet kan sjunka kraftigt på kort tid." : sri >= 4 ? "Värdet varierar måttligt." : "Låga svängningar."),
        _ => $"Riesgo {sri} sobre 7. " + (sri >= 6 ? "El valor puede caer mucho en poco tiempo." : sri >= 4 ? "El valor oscila de forma moderada." : "Oscilaciones bajas."),
    };

    private static async Task<decimal?> Nav(HttpContext http, string isin, DateOnly onOrBefore)
    {
        var from = onOrBefore.AddDays(-10);
        var r = await Downstream.Get(http, "market-data", $"/api/v1/prices/{isin}/history?from={from:yyyy-MM-dd}&to={onOrBefore:yyyy-MM-dd}");
        if (!r.Ok) return null;
        var prices = r.Json.GetProperty("prices");
        return prices.GetArrayLength() == 0 ? null : decimal.Parse(prices[prices.GetArrayLength() - 1].GetProperty("nav").GetString()!, CultureInfo.InvariantCulture);
    }

    private static string? Change(decimal? now, decimal? then) => now is null || then is null || then == 0 ? null : Money.Format((now.Value / then.Value - 1) * 100, 2);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/factsheets/{isin}", async (HttpContext http, IClock clock, string isin, string? lang) =>
        {
            var l = (lang ?? "es").ToLowerInvariant();
            if (!Objectives.ContainsKey(l))
                return Problems.BadRequest(http, "UNSUPPORTED_LANGUAGE", $"Idioma '{lang}' no disponible.", "Idiomas: es, en, sv.");
            var f = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{isin}");
            if (f.Status is 400 or 404) return Results.Json(f.Body, statusCode: f.Status, contentType: "application/problem+json; charset=utf-8");
            if (!f.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "product-catalog respondió " + f.Status, null);
            var fund = f.Json;
            var latest = await Downstream.Get(http, "market-data", $"/api/v1/prices/{isin}/latest");
            var asOf = latest.Ok ? DateOnly.Parse(latest.Str("date")!) : clock.Today;
            var now = latest.Ok ? decimal.Parse(latest.Str("nav")!, CultureInfo.InvariantCulture) : (decimal?)null;
            var perf = new Performance(Change(now, await Nav(http, isin, asOf.AddMonths(-1))), Change(now, await Nav(http, isin, asOf.AddMonths(-3))),
                Change(now, await Nav(http, isin, asOf.AddYears(-1))), asOf.ToString("yyyy-MM-dd"));
            var category = fund.GetProperty("category").GetString()!;
            var sri = fund.GetProperty("sri").GetInt32();
            var fees = fund.GetProperty("fees").EnumerateArray().Select(x => new FactsheetFee(x.GetProperty("type").GetString()!, x.GetProperty("percent").GetString()!)).ToList();
            var closed = fund.GetProperty("status").GetString() == "CLOSED";
            return Results.Ok(new Factsheet(isin, fund.GetProperty("name").GetString()!, l, category, fund.GetProperty("base_currency").GetString()!, sri,
                Objectives[l][category], RiskText(l, sri), fees, perf,
                closed ? l switch { "en" => "Closed to new subscriptions.", "sv" => "Stängd för nya teckningar.", _ => "Cerrado a nuevas suscripciones." } : null));
        }).WithTags("Fichas").WithSummary("Ficha comercial de un fondo (lang = es, en o sv)").Produces<Factsheet>();
    }
}
