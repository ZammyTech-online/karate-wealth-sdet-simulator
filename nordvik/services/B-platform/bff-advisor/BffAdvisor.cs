using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>bff-advisor: escritorio del asesor. Su cartera de clientes con valor total, la ficha de un cliente y las alertas de desviación.</summary>
public sealed class BffAdvisor : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "bff-advisor", "B-platform", 6106, 2, "Canales digitales",
        "API del escritorio del asesor: sus clientes con carteras y valor, ficha del cliente (vía composition-service) y alertas de desviación.",
        DependsOn: new[] { "permission-service", "customer-registry", "portfolio-service", "valuation-service", "rebalancing-engine", "composition-service", "clock-service" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/advisor/customers", async (HttpContext http) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Advisor" });
            if (denied is not null) return denied;
            var assigned = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{p.AdvisorId}/customers", forwardAuth: false);
            var list = new List<object>();
            foreach (var a in assigned.Json.EnumerateArray())
            {
                var cid = a.GetProperty("customerId").GetString()!;
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/customers/{cid}");
                if (!c.Ok) continue;
                var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/customers/{cid}/portfolios?status=ACTIVE");
                decimal total = 0; var count = 0;
                foreach (var pf in pfs.Ok ? pfs.Json.EnumerateArray().ToList() : new())
                {
                    count++;
                    var v = await Downstream.Get(http, "valuation-service", $"/api/v1/portfolios/{pf.GetProperty("id").GetString()}/valuation");
                    if (v.Ok) total += D(v.Str("totalValueSek")!);
                }
                list.Add(new { customerId = cid, customerNumber = c.Str("customerNumber"), name = c.Str("firstName") + " " + c.Str("lastName"), nationalId = c.Str("nationalId"),
                    status = c.Str("status"), portfolios = count, totalValueSek = Money.Format(total, 2) });
            }
            return Results.Ok(new { advisorId = p.AdvisorId, customers = list, count = list.Count });
        }).WithTags("Asesor").WithSummary("Mis clientes con número de carteras y valor total");

        app.MapGet("/api/v1/advisor/customers/{customerId}/overview", async (HttpContext http, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Advisor" });
            if (denied is not null) return denied;
            var r = await Downstream.Get(http, "composition-service", $"/api/v2/compositions/customer-360/{customerId}");
            return Results.Content(r.Raw, r.Ok ? "application/json" : "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, r.Status);
        }).WithTags("Asesor").WithSummary("Ficha 360 de un cliente asignado");

        app.MapGet("/api/v1/advisor/alerts", async (HttpContext http) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Advisor" });
            if (denied is not null) return denied;
            var assigned = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{p.AdvisorId}/customers", forwardAuth: false);
            var alerts = new List<object>();
            foreach (var a in assigned.Json.EnumerateArray())
            {
                var cid = a.GetProperty("customerId").GetString()!;
                var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/customers/{cid}/portfolios?status=ACTIVE");
                foreach (var pf in pfs.Ok ? pfs.Json.EnumerateArray().ToList() : new())
                {
                    var pid = pf.GetProperty("id").GetString()!;
                    var d = await Downstream.Get(http, "rebalancing-engine", $"/api/v1/portfolios/{pid}/drift");
                    if (d.Ok && d.Json.GetProperty("needsRebalancing").GetBoolean())
                        alerts.Add(new { type = "DRIFT", customerId = cid, portfolioId = pid, portfolioName = pf.GetProperty("name").GetString(), maxDriftPp = d.Str("maxDriftPp"), thresholdPp = d.Str("thresholdPp") });
                }
            }
            return Results.Ok(new { advisorId = p.AdvisorId, alerts, count = alerts.Count });
        }).WithTags("Asesor").WithSummary("Carteras de mis clientes que se han desviado del objetivo");
    }
}
