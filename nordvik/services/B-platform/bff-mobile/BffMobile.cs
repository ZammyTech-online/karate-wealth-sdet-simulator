using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// bff-mobile: API de la app móvil. Respuestas pequeñas, paginación por cursor y selección de campos (?fields=id,status)
/// para gastar pocos datos. Mismos datos que bff-web, otra forma.
/// </summary>
public sealed class BffMobile : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "bff-mobile", "B-platform", 6105, 2, "Canales digitales",
        "API de la app: inicio compacto, órdenes paginadas por cursor y ?fields= para pedir solo algunos campos.",
        DependsOn: new[] { "portfolio-service", "valuation-service", "order-management", "cash-management", "clock-service" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static JsonNode Pick(JsonElement e, string[]? fields)
    {
        var node = JsonNode.Parse(e.GetRawText())!.AsObject();
        if (fields is null) return node;
        var r = new JsonObject();
        foreach (var f in fields) if (node.TryGetPropertyValue(f, out var v)) r[f] = v?.DeepClone();
        return r;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/mobile/home", async (HttpContext http) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Customer" });
            if (denied is not null) return denied;
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/customers/{p.CustomerId}/portfolios?status=ACTIVE");
            var lines = new List<(string Isin, string Name, decimal Value)>(); decimal total = 0, cost = 0;
            foreach (var pf in pfs.Ok ? pfs.Json.EnumerateArray().ToList() : new())
            {
                var v = await Downstream.Get(http, "valuation-service", $"/api/v1/portfolios/{pf.GetProperty("id").GetString()}/valuation");
                if (!v.Ok) continue;
                total += D(v.Str("totalValueSek")!); cost += D(v.Str("totalCostSek")!);
                lines.AddRange(v.Json.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("isin").GetString()!, l.GetProperty("fundName").GetString()!, D(l.GetProperty("valueSek").GetString()!))));
            }
            var cash = await Downstream.Get(http, "cash-management", $"/api/v1/customers/{p.CustomerId}/cash");
            return Results.Ok(new
            {
                total = Money.Format(total, 2), gainPct = cost == 0 ? null : Money.Format((total - cost) / cost * 100, 2), cash = cash.Ok ? cash.Str("available") : null,
                top = lines.GroupBy(l => (l.Isin, l.Name)).Select(g2 => new { isin = g2.Key.Isin, name = g2.Key.Name, value = Money.Format(g2.Sum(x => x.Value), 2) })
                    .OrderByDescending(x => D(x.value)).Take(3).ToList(),
            });
        }).WithTags("Móvil").WithSummary("Pantalla de inicio compacta (total, plusvalía, efectivo, 3 fondos principales)");

        app.MapGet("/api/v1/mobile/orders", async (HttpContext http, string? cursor, int? limit, string? fields) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Customer" });
            if (denied is not null) return denied;
            var size = Math.Clamp(limit ?? 10, 1, 50);
            var skip = 0;
            if (cursor is not null)
            {
                try { skip = int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Replace("offset:", "")); }
                catch { return Problems.BadRequest(http, "INVALID_CURSOR", "Cursor no válido.", "Usa el nextCursor tal cual."); }
            }
            var allowed = new[] { "id", "portfolioId", "isin", "fundName", "side", "amount", "units", "status", "tradeDate", "createdAt", "executedUnits", "nav" };
            var f = fields?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (f is not null && f.Except(allowed).ToList() is { Count: > 0 } bad)
                return Problems.BadRequest(http, "UNKNOWN_FIELDS", "Campos desconocidos: " + string.Join(", ", bad), "Campos: " + string.Join(", ", allowed));
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/customers/{p.CustomerId}/portfolios");
            var all = new List<JsonElement>();
            foreach (var pf in pfs.Ok ? pfs.Json.EnumerateArray().ToList() : new())
            {
                var o = await Downstream.Get(http, "order-management", $"/api/v1/portfolios/{pf.GetProperty("id").GetString()}/orders");
                if (o.Ok) all.AddRange(o.Json.EnumerateArray());
            }
            var ordered = all.OrderByDescending(o => o.GetProperty("createdAt").GetString()).ThenBy(o => o.GetProperty("id").GetString()).ToList();
            var page = ordered.Skip(skip).Take(size).Select(o => Pick(o, f ?? new[] { "id", "fundName", "side", "status", "tradeDate", "amount", "units" })).ToList();
            var next = skip + size < ordered.Count ? Convert.ToBase64String(Encoding.UTF8.GetBytes("offset:" + (skip + size))) : null;
            return Results.Ok(new { items = page, nextCursor = next, total = ordered.Count });
        }).WithTags("Móvil").WithSummary("Órdenes paginadas por cursor; ?fields=id,status para respuestas más pequeñas");
    }
}
