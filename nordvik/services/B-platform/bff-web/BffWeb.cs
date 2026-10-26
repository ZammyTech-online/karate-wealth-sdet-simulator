using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// bff-web: "backend for frontend" de la web del cliente. Una sola llamada da todo lo que pinta el panel:
/// datos del cliente, efectivo, carteras valoradas, últimas órdenes y notificaciones. Solo para el rol Customer.
/// </summary>
public sealed class BffWeb : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "bff-web", "B-platform", 6104, 2, "Canales digitales",
        "API a medida de la web del cliente: panel en una llamada (cliente, efectivo, carteras valoradas, órdenes recientes, notificaciones).",
        DependsOn: new[] { "customer-registry", "cash-management", "portfolio-service", "valuation-service", "order-management", "notification-service", "clock-service" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/web/dashboard", async (HttpContext http) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Customer" });
            if (denied is not null) return denied;
            var id = p.CustomerId!;
            var customer = await Downstream.Get(http, "customer-registry", $"/api/v1/customers/{id}");
            var cash = await Downstream.Get(http, "cash-management", $"/api/v1/customers/{id}/cash");
            var portfolios = await Downstream.Get(http, "portfolio-service", $"/api/v1/customers/{id}/portfolios?status=ACTIVE");
            var cards = new List<object>(); var orders = new List<JsonElement>(); decimal total = 0;
            foreach (var pf in portfolios.Ok ? portfolios.Json.EnumerateArray().ToList() : new())
            {
                var pid = pf.GetProperty("id").GetString()!;
                var v = await Downstream.Get(http, "valuation-service", $"/api/v1/portfolios/{pid}/valuation");
                var value = v.Ok ? D(v.Str("totalValueSek")!) : 0;
                total += value;
                cards.Add(new { id = pid, name = pf.GetProperty("name").GetString(), type = pf.GetProperty("type").GetString(), valueSek = Money.Format(value, 2),
                    unrealizedGainPercent = v.Ok ? v.Str("unrealizedGainPercent") : null });
                var o = await Downstream.Get(http, "order-management", $"/api/v1/portfolios/{pid}/orders");
                if (o.Ok) orders.AddRange(o.Json.EnumerateArray());
            }
            var notes = await Downstream.Get(http, "notification-service", $"/api/v1/customers/{id}/notifications");
            return Results.Ok(new
            {
                customer = new { firstName = customer.Str("firstName"), customerNumber = customer.Str("customerNumber"), language = customer.Str("language") },
                cash = cash.Ok ? new { available = cash.Str("available"), booked = cash.Str("booked") } : null,
                totalPortfolioValueSek = Money.Format(total, 2),
                totalAssetsSek = Money.Format(total + (cash.Ok ? D(cash.Str("booked")!) : 0), 2),
                portfolios = cards,
                recentOrders = orders.OrderByDescending(o => o.GetProperty("createdAt").GetString()).Take(5).Select(o => new
                {
                    id = o.GetProperty("id").GetString(), fundName = o.GetProperty("fundName").GetString(), side = o.GetProperty("side").GetString(),
                    status = o.GetProperty("status").GetString(), tradeDate = o.GetProperty("tradeDate").GetString(),
                    amount = o.GetProperty("amount").ValueKind == JsonValueKind.String ? o.GetProperty("amount").GetString() : null,
                }).ToList(),
                notifications = notes.Ok ? notes.Json.EnumerateArray().Take(3).Select(n => new { templateKey = n.GetProperty("templateKey").GetString(), status = n.GetProperty("status").GetString(), createdAt = n.GetProperty("createdAt").GetString() }).ToList() : null,
                generatedAt = Nordic.Iso(clock.LocalNow),
            });
        }).WithTags("Web").WithSummary("Panel del cliente en una sola llamada (token de cliente)");
    }
}
