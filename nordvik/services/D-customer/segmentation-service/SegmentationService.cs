using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SegmentSimRequest(string? Assets, int? Age, bool? HasEmployerPlan);

/// <summary>
/// segmentation-service: el segmento del cliente según reglas versionadas, en orden: PRIVATE_BANKING (≥ 5 M SEK de patrimonio en Nordvik),
/// PREMIUM (≥ 1 M), YOUNG_SAVER (menos de 26 años), RETAIL. El patrimonio es la suma de todas sus carteras valoradas (incluidas las de
/// pensión). Dice también el siguiente segmento y cuánto falta, y si es cliente de empresa (tiene un plan OCCUPATIONAL).
/// </summary>
public sealed class SegmentationService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "segmentation-service", "D-customer", 6309, 4, "Cliente",
        "Segmento del cliente por reglas versionadas (patrimonio valorado y edad): PRIVATE_BANKING, PREMIUM, YOUNG_SAVER o RETAIL, con lo que falta para el siguiente.",
        DependsOn: new[] { "portfolio-service", "valuation-service", "pension-plan-service", "customer-registry", "permission-service", "clock-service" });

    public const string RulesVersion = "2026.1";
    public const decimal PrivateBanking = 5_000_000m, Premium = 1_000_000m;
    public const int YoungAge = 26;

    public static (string Segment, string Rule, string? Next, decimal? Gap) Classify(decimal assets, int age)
    {
        if (assets >= PrivateBanking) return ("PRIVATE_BANKING", "assets >= 5000000", null, null);
        if (assets >= Premium) return ("PREMIUM", "assets >= 1000000", "PRIVATE_BANKING", PrivateBanking - assets);
        if (age < YoungAge) return ("YOUNG_SAVER", "age < 26", "PREMIUM", Premium - assets);
        return ("RETAIL", "default", "PREMIUM", Premium - assets);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/segments/rules", () => Results.Ok(new
        {
            version = RulesVersion,
            rules = new object[]
            {
                new { order = 1, segment = "PRIVATE_BANKING", condition = "patrimonio >= 5000000.00 SEK" }, new { order = 2, segment = "PREMIUM", condition = "patrimonio >= 1000000.00 SEK" },
                new { order = 3, segment = "YOUNG_SAVER", condition = "edad < 26" }, new { order = 4, segment = "RETAIL", condition = "el resto" },
            },
        })).WithTags("Segmentos").WithSummary("Reglas vigentes, en orden de evaluación");

        app.MapPost("/api/v1/segments/simulate", (HttpContext http, SegmentSimRequest b) =>
        {
            if (!Money.TryParse(b.Assets, out var assets) || assets < 0 || b.Age is null or < 18 or > 120)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "assets (texto, ≥ 0) y age (18-120) son obligatorios.", "Ejemplo: { \"assets\": \"1500000.00\", \"age\": 45 }");
            var c = Classify(assets, b.Age.Value);
            return Results.Ok(new { segment = c.Segment, rule = c.Rule, nextSegment = c.Next, gapToNext = c.Gap is null ? null : Money.Format(c.Gap.Value, 2), rulesVersion = RulesVersion });
        }).WithTags("Segmentos").WithSummary("¿Qué segmento saldría con este patrimonio y esta edad?");

        app.MapGet("/api/v1/customers/{customerId}/segment", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios?customerId={customerId}", forwardAuth: false);
            decimal assets = 0; var breakdown = new List<object>();
            var items = pfs.Json.ValueKind == System.Text.Json.JsonValueKind.Array ? pfs.Json : pfs.Json.GetProperty("items");
            foreach (var pf in items.EnumerateArray())
            {
                var id = pf.GetProperty("id").GetString()!;
                var v = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{id}/valuation", forwardAuth: false);
                var value = v.Ok && Money.TryParse(v.Str("totalValueSek"), out var x) ? x : 0;
                assets += value;
                breakdown.Add(new { portfolioId = id, type = pf.GetProperty("type").GetString(), value = Money.Format(value, 2) });
            }
            var plans = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans?customerId={customerId}&type=OCCUPATIONAL", forwardAuth: false);
            var corporate = plans.Ok && plans.Json.GetProperty("count").GetInt32() > 0;
            var age = cust.Json.GetProperty("age").GetInt32();
            var c = Classify(assets, age);
            return Results.Ok(new
            {
                customerId, segment = c.Segment, rule = c.Rule, rulesVersion = RulesVersion, assets = Money.Format(assets, 2), currency = "SEK", age, corporateClient = corporate,
                nextSegment = c.Next, gapToNext = c.Gap is null ? null : Money.Format(c.Gap.Value, 2), portfolios = breakdown, evaluatedAt = Nordic.Iso(clock.LocalNow),
            });
        }).WithTags("Segmentos").WithSummary("Segmento del cliente con el detalle del cálculo");
    }
}
