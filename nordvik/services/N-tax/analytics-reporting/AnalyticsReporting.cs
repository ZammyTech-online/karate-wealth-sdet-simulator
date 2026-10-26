using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// analytics-reporting: informes internos ANONIMIZADOS. Nunca salen ids ni personas, solo grupos; y un grupo con menos de k personas
/// (k ≥ 5, k-anonimato) se suprime: su recuento e importes salen como null y suppressed: true. Un k menor de 5 no se acepta.
/// </summary>
public sealed class AnalyticsReporting : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "analytics-reporting", "N-tax", 7304, 4, "Fiscalidad",
        "Informes internos anonimizados con k-anonimato (k ≥ 5): planes por tipo y edad, aportaciones por origen; los grupos pequeños se suprimen.",
        DependsOn: new[] { "pension-plan-service", "contribution-service", "customer-registry", "clock-service" });

    public const int MinK = 5;

    public static string AgeBand(int age) => age < 30 ? "<30" : age >= 70 ? "70+" : $"{age / 10 * 10}-{age / 10 * 10 + 9}";

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Compliance", "Admin" };

        IResult? Check(HttpContext http, int? k, out int kk)
        {
            kk = k ?? MinK;
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            if (kk < MinK) return Problems.BadRequest(http, "K_TOO_LOW", $"k mínimo es {MinK}: con grupos más pequeños se podría reconocer a una persona.", null);
            return null;
        }

        app.MapGet("/api/v1/reports/pension-plans", async (HttpContext http, int? k) =>
        {
            if (Check(http, k, out var kk) is { } bad) return bad;
            var plans = await Downstream.Get(http, "pension-plan-service", "/api/v1/internal/pension-plans", forwardAuth: false);
            var ages = new Dictionary<string, int>();
            var rows = new List<(string Type, string Band, string Customer)>();
            foreach (var pl in plans.Json.GetProperty("items").EnumerateArray())
            {
                var c = pl.GetProperty("customerId").GetString()!;
                if (!ages.TryGetValue(c, out var age))
                {
                    var r = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{c}", forwardAuth: false);
                    age = r.Ok ? r.Json.GetProperty("age").GetInt32() : -1;
                    ages[c] = age;
                }
                if (age >= 0) rows.Add((pl.GetProperty("type").GetString()!, AgeBand(age), c));
            }
            var groups = rows.GroupBy(r => (r.Type, r.Band)).OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Band).Select(g =>
            {
                var people = g.Select(x => x.Customer).Distinct().Count();
                return people < kk ? new { type = g.Key.Type, ageBand = g.Key.Band, plans = (int?)null, customers = (int?)null, suppressed = true }
                                   : new { type = g.Key.Type, ageBand = g.Key.Band, plans = (int?)g.Count(), customers = (int?)people, suppressed = false };
            }).ToList();
            return Results.Ok(new { k = kk, generatedAt = Nordic.Iso(clock.LocalNow), groups, suppressedGroups = groups.Count(x => x.suppressed), totalPlans = rows.Count });
        }).WithTags("Informes").WithSummary("Planes por tipo y franja de edad (k-anonimato)");

        app.MapGet("/api/v1/reports/contributions", async (HttpContext http, string? period, int? k) =>
        {
            if (Check(http, k, out var kk) is { } bad) return bad;
            var list = await Downstream.Get(http, "contribution-service", "/api/v1/internal/contributions" + (period is null ? "" : "?period=" + period), forwardAuth: false);
            var items = list.Json.GetProperty("items").EnumerateArray().Select(c => (Source: c.GetProperty("source").GetString()!, Plan: c.GetProperty("planId").GetString()!,
                Amount: decimal.Parse(c.GetProperty("amount").GetProperty("amount").GetString()!, Money.Inv))).ToList();
            var groups = items.GroupBy(i => i.Source).OrderBy(g => g.Key).Select(g =>
            {
                var plans = g.Select(x => x.Plan).Distinct().Count();
                return plans < kk ? new { source = g.Key, contributions = (int?)null, total = (string?)null, average = (string?)null, suppressed = true }
                                  : new { source = g.Key, contributions = (int?)g.Count(), total = (string?)Money.Format(g.Sum(x => x.Amount), 2), average = (string?)Money.Format(Math.Round(g.Average(x => x.Amount), 2), 2), suppressed = false };
            }).ToList();
            return Results.Ok(new { k = kk, period, generatedAt = Nordic.Iso(clock.LocalNow), groups, suppressedGroups = groups.Count(x => x.suppressed) });
        }).WithTags("Informes").WithSummary("Aportaciones por origen (k-anonimato por número de planes)");
    }
}
