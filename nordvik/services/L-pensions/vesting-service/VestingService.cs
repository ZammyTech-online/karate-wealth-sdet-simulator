using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// vesting-service: qué parte de un plan de empresa es ya del empleado. Depende del convenio (años para consolidar) y de los años
/// trabajados en esa empresa (employment-service). Los planes privados y los de intercambio salarial son siempre del 100 %.
/// </summary>
public sealed class VestingService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "vesting-service", "L-pensions", 7106, 3, "Pensiones",
        "Derechos consolidados de un plan: según el convenio y los años en la empresa (employment-service); privados e intercambio salarial, 100 %.",
        DependsOn: new[] { "pension-plan-service", "employment-service", "collective-agreement-rules", "valuation-service", "clock-service" });

    public static decimal VestedPct(int vestingYears, double yearsOfService) =>
        vestingYears <= 0 ? 100m : Math.Min(100m, Math.Floor((decimal)yearsOfService) * 100m / vestingYears);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/pension-plans/{planId}/vesting", async (HttpContext http, string planId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{planId}", forwardAuth: false);
            if (!plan.Ok) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {planId}.", null);
            var customerId = plan.Str("customerId")!;
            var d = await Permissions.Check(http, p, "portfolio:read", customerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PLAN_NOT_FOUND", $"el plan {planId}");
            var type = plan.Str("type");
            int vestingYears = 0; double years = 0; string? since = null;
            if (type == "OCCUPATIONAL")
            {
                var agr = await Downstream.Get(http, "collective-agreement-rules", $"/api/v1/agreements/{plan.Str("agreement")}", forwardAuth: false);
                vestingYears = agr.Ok ? agr.Json.GetProperty("vestingYears").GetInt32() : 0;
                var emp = await Downstream.Send(http, "employment-service", HttpMethod.Get, $"/api/v1/customers/{customerId}/employments", null, forwardAuth: false, headers: await ServiceToken.Header(clock));
                var org = plan.Str("employerOrgNumber");
                foreach (var e in emp.Ok ? emp.Json.GetProperty("items").EnumerateArray() : Enumerable.Empty<System.Text.Json.JsonElement>())
                {
                    if (e.GetProperty("orgNumber").GetString() != org) continue;
                    var start = DateOnly.Parse(e.GetProperty("startDate").GetString()!);
                    var end = e.GetProperty("endDate").ValueKind == System.Text.Json.JsonValueKind.String ? DateOnly.Parse(e.GetProperty("endDate").GetString()!) : clock.Today;
                    if (end > clock.Today) end = clock.Today;
                    years += (end.DayNumber - start.DayNumber) / 365.25;
                    since ??= Money.Date(start);
                }
            }
            var pct = type == "OCCUPATIONAL" ? VestedPct(vestingYears, years) : 100m;
            var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{plan.Str("portfolioId")}/valuation", forwardAuth: false);
            var value = val.Ok ? decimal.Parse(val.Str("totalValueSek")!, Money.Inv) : 0m;
            return Results.Ok(new
            {
                planId, type, agreement = plan.Str("agreement"), vestingYears, yearsOfService = Math.Round(years, 2), employedSince = since, vestedPct = pct,
                planValue = new { amount = Money.Format(value, 2), currency = "SEK" }, vestedValue = new { amount = Money.Format(Math.Round(value * pct / 100, 2), 2), currency = "SEK" },
                rule = type != "OCCUPATIONAL" ? "Plan privado o de intercambio salarial: siempre consolidado." : vestingYears == 0 ? "Convenio con consolidación inmediata." : $"{vestingYears} años en la empresa para consolidar el 100 % (proporcional por año completo).",
            });
        }).WithTags("Consolidación").WithSummary("Porcentaje y valor consolidado de un plan");
    }
}
