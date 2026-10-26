using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record WithholdingCalcRequest(string? CustomerId, string? Gross, string? IncomeType, int? Year);

/// <summary>
/// tax-withholding: cuánto se retiene a cada cliente. Pensiones: si su residencia fiscal principal registrada no es Suecia, SINK (25 % plano para
/// no residentes); si no, la tabla municipal de la Agencia Tributaria (E8, por número nacional y año). Rentas del capital: 30 % plano.
/// </summary>
public sealed class TaxWithholding : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "tax-withholding", "N-tax", 7300, 4, "Fiscalidad",
        "Tipo de retención por cliente: tabla municipal de la Agencia Tributaria, SINK del 25 % para no residentes y 30 % para rentas del capital.",
        DependsOn: new[] { "tax-agency-adapter", "tax-residency-service", "customer-registry", "permission-service", "clock-service" });

    public const decimal SinkPct = 25m, CapitalPct = 30m;
    public static readonly string[] IncomeTypes = { "PENSION", "INTEREST", "DIVIDEND", "CAPITAL_GAIN" };

    public static async Task<(int Status, object Body)> Resolve(HttpContext http, string customerId, string incomeType, int year)
    {
        var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
        if (!cust.Ok) return (404, new { code = "CUSTOMER_NOT_FOUND" });
        if (incomeType != "PENSION")
            return (200, new { customerId, year, incomeType, basis = "CAPITAL_INCOME", ratePct = Money.Format(CapitalPct, 2), municipality = (string?)null, table = (int?)null });
        var res = await Downstream.Get(http, "tax-residency-service", $"/api/v1/internal/customers/{customerId}/tax-residencies", forwardAuth: false);
        string? primary = null;
        if (res.Ok && res.Json.TryGetProperty("residencies", out var list))
            foreach (var r in list.EnumerateArray())
                if (r.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True) primary = r.GetProperty("country").GetString();
        if (primary is not null && primary != "SE")
            return (200, new { customerId, year, incomeType, basis = "SINK", ratePct = Money.Format(SinkPct, 2), municipality = (string?)null, table = (int?)null, residence = primary });
        var t = await Downstream.Get(http, "tax-agency-adapter", $"/api/v1/internal/tax-tables/{cust.Str("nationalId")}?year={year}", forwardAuth: false);
        if (!t.Ok) return (502, new { code = "TAX_TABLE_UNAVAILABLE" });
        var table = t.Json.GetProperty("table");
        return (200, new
        {
            customerId, year, incomeType, basis = "TAX_TABLE", ratePct = table.GetProperty("ratePct").GetString(), municipality = table.GetProperty("municipality").GetString(),
            table = (int?)table.GetProperty("table").GetInt32(), residence = primary ?? "SE",
        });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        IResult Map(HttpContext http, (int Status, object Body) r) => r.Status switch
        {
            200 => Results.Ok(r.Body),
            404 => Problems.NotFound(http, "CUSTOMER_NOT_FOUND", "No existe el cliente.", null),
            _ => Problems.Of(http, 502, "TAX_TABLE_UNAVAILABLE", "Sin tabla de retención", "La Agencia Tributaria no respondió.", "Reintenta más tarde; los pagos usan la retención provisional del 30 %."),
        };

        app.MapGet("/api/v1/customers/{customerId}/withholding", async (HttpContext http, string customerId, string? incomeType, int? year) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var type = (incomeType ?? "PENSION").ToUpperInvariant();
            if (!IncomeTypes.Contains(type)) return Problems.BadRequest(http, "VALIDATION_FAILED", "incomeType: " + string.Join(", ", IncomeTypes), null);
            return Map(http, await Resolve(http, customerId, type, year ?? clock.Today.Year));
        }).WithTags("Retenciones").WithSummary("Tipo de retención de un cliente");

        app.MapPost("/api/v1/withholding/calculate", async (HttpContext http, WithholdingCalcRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var type = (b.IncomeType ?? "PENSION").ToUpperInvariant();
            if (b.CustomerId is null || !Money.TryParse(b.Gross, out var gross) || gross < 0 || !IncomeTypes.Contains(type))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId, gross (texto, ≥ 0) e incomeType son obligatorios.", "Ejemplo: { \"customerId\": \"...\", \"gross\": \"12000.00\", \"incomeType\": \"PENSION\" }");
            if (await CustomerGuard.Check(http, p, "customer:read", b.CustomerId) is { } g) return g;
            var r = await Resolve(http, b.CustomerId, type, b.Year ?? clock.Today.Year);
            if (r.Status != 200) return Map(http, r);
            var j = JsonSerializer.SerializeToElement(r.Body, Canonical.Web);
            var rate = decimal.Parse(j.GetProperty("ratePct").GetString()!, Money.Inv);
            var tax = Math.Round(gross * rate / 100, 2, MidpointRounding.ToEven);
            return Results.Ok(new { customerId = b.CustomerId, incomeType = type, basis = j.GetProperty("basis").GetString(), ratePct = Money.Format(rate, 2), gross = Money.Format(gross, 2), tax = Money.Format(tax, 2), net = Money.Format(gross - tax, 2) });
        }).WithTags("Retenciones").WithSummary("Calcular la retención de un importe");

        app.MapGet("/api/v1/internal/customers/{customerId}/withholding", async (HttpContext http, string customerId, string? incomeType, int? year) =>
            Map(http, await Resolve(http, customerId, (incomeType ?? "PENSION").ToUpperInvariant(), year ?? clock.Today.Year)))
            .WithTags("Interno").WithSummary("(interno) Tipo de retención (payout-execution)");
    }
}
