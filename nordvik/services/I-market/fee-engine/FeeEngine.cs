using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FeeRequest(string? Isin, string? Amount, string? Action, int? Years);
public sealed record YearCost(int Year, string Cost, string Cumulative);
public sealed record FeeQuote(string Isin, string Action, string Amount, string Currency, string Fee, string FeePercent, string NetAmount, IReadOnlyList<YearCost> Projection);

/// <summary>fee-engine: calcula comisiones de compra, venta y el coste de mantener un fondo N años. Equipo de Inversiones (snake_case).</summary>
public sealed class FeeEngine : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "fee-engine", "I-market", 6806, 1, "Inversiones",
        "Calcula la comisión de entrada (BUY), de salida (SELL) o el coste anual de gestión proyectado (HOLD) de un importe en un fondo.",
        Naming: JsonNaming.SnakeCase, DependsOn: new[] { "product-catalog" });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/fees/calculate", async (HttpContext http, FeeRequest body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Isin)) errors.Add(new FieldError("isin", "obligatorio"));
            if (!Money.TryParse(body.Amount, out var amount) || amount <= 0) errors.Add(new FieldError("amount", "importe positivo como texto, con punto: \"10000.00\""));
            var action = body.Action?.ToUpperInvariant();
            if (action is not ("BUY" or "SELL" or "HOLD")) errors.Add(new FieldError("action", "BUY, SELL o HOLD"));
            var years = body.Years ?? 1;
            if (action == "HOLD" && years is < 1 or > 40) errors.Add(new FieldError("years", "entre 1 y 40"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "La petición de comisiones no es válida.",
                "Ejemplo: { \"isin\": \"SE0000000003\", \"amount\": \"10000.00\", \"action\": \"BUY\" }. Este equipo usa snake_case en las respuestas.", errors);
            var f = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{body.Isin}");
            if (f.Status is 400 or 404) return Results.Json(f.Body, statusCode: f.Status, contentType: "application/problem+json; charset=utf-8");
            var fund = f.Json;
            if (action == "BUY" && fund.GetProperty("status").GetString() == "CLOSED")
                return Problems.Unprocessable(http, "FUND_CLOSED", "El fondo está cerrado a nuevas compras.", "Solo se puede vender (SELL) o mantener (HOLD).");
            string Pct(string type) => fund.GetProperty("fees").EnumerateArray().First(x => x.GetProperty("type").GetString() == type).GetProperty("percent").GetString()!;
            var currency = fund.GetProperty("base_currency").GetString()!;
            if (action == "HOLD")
            {
                var pct = decimal.Parse(Pct("MANAGEMENT"), CultureInfo.InvariantCulture);
                var rows = new List<YearCost>();
                decimal cum = 0;
                for (var y = 1; y <= years; y++) { var c = amount * pct / 100; cum += c; rows.Add(new YearCost(y, Money.Format(c, 2), Money.Format(cum, 2))); }
                return Results.Ok(new FeeQuote(body.Isin!, action, Money.Format(amount, 2), currency, Money.Format(cum, 2), Pct("MANAGEMENT"), Money.Format(amount, 2), rows));
            }
            var percent = Pct(action == "BUY" ? "ENTRY" : "EXIT");
            var fee = Math.Round(amount * decimal.Parse(percent, CultureInfo.InvariantCulture) / 100, 2, MidpointRounding.ToEven);
            return Results.Ok(new FeeQuote(body.Isin!, action!, Money.Format(amount, 2), currency, Money.Format(fee, 2), percent, Money.Format(amount - fee, 2), Array.Empty<YearCost>()));
        }).WithTags("Comisiones").WithSummary("Calcular comisiones de una operación").Produces<FeeQuote>();
    }
}
