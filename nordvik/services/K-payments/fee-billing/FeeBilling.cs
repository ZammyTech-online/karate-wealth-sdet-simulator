using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record BillingRequest(string? Month);
public sealed record BillingLine(string PortfolioId, string CustomerId, string Isin, string ValueSek, string AnnualFeePercent, string FeeSek);
public sealed record BillingCharge(string PortfolioId, string CustomerId, string TotalFeeSek, string? LedgerEntryId, string Status);
public sealed record BillingRun(string Id, string Month, string ValuationDate, string Status, int Portfolios, string TotalFeeSek, IReadOnlyList<BillingCharge> Charges, IReadOnlyList<BillingLine> Lines, string RunAt, string RunBy);

/// <summary>
/// fee-billing: cobro mensual de la comisión de gestión. Para cada cartera con participaciones: valor al último día hábil del mes
/// × comisión anual del fondo ÷ 12, redondeo bancario por línea. Se carga al efectivo del cliente (DEBE cliente / HABER fees:income).
/// Una sola ejecución por mes (409 si se repite) y solo de meses cerrados.
/// </summary>
public sealed class FeeBilling : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "fee-billing", "K-payments", 7006, 2, "Pagos y contabilidad",
        "Comisión de gestión mensual: valoración a fin de mes × comisión anual / 12, cargo en el ledger, una vez por mes.",
        DependsOn: new[] { "position-keeping", "valuation-service", "product-catalog", "ledger-service", "clock-service" }, Emits: new[] { "fees.billed" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, month TEXT UNIQUE, json TEXT, run_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/billing-runs", async (HttpContext http, IClock clock, BillingRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            if (b.Month is null || !DateOnly.TryParseExact(b.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "month es obligatorio (AAAA-MM).", "Ejemplo: { \"month\": \"2026-09\" }", new[] { new FieldError("month", "AAAA-MM") });
            var last = first.AddMonths(1).AddDays(-1);
            if (last >= clock.Today) return Problems.Unprocessable(http, "MONTH_NOT_CLOSED", $"El mes {b.Month} no ha terminado.", "Solo se facturan meses cerrados. Adelanta el reloj virtual si lo necesitas.");
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM runs WHERE month = @Month", b) > 0)
                return Problems.Conflict(http, "BILLING_RUN_EXISTS", $"El mes {b.Month} ya se facturó.", "Cobrar dos veces el mismo mes sería un incidente: compruébalo en GET /api/v1/billing-runs.");
            var valuationDate = NordicCalendar.PreviousOrSameTradingDay("XSTO", last);
            var positions = await Downstream.Get(http, "position-keeping", "/api/v1/internal/positions", forwardAuth: false);
            var lines = new List<BillingLine>(); var charges = new List<BillingCharge>();
            var feeCache = new Dictionary<string, decimal>();
            foreach (var pf in positions.Json.EnumerateArray().GroupBy(x => (x.GetProperty("portfolioId").GetString()!, x.GetProperty("customerId").GetString()!)))
            {
                var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{pf.Key.Item1}/valuation?date={Money.Date(valuationDate)}", forwardAuth: false);
                if (!val.Ok) continue;
                decimal total = 0;
                foreach (var l in val.Json.GetProperty("lines").EnumerateArray())
                {
                    var isin = l.GetProperty("isin").GetString()!;
                    if (!feeCache.TryGetValue(isin, out var pct))
                    {
                        var fees = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{isin}", forwardAuth: false);
                        pct = feeCache[isin] = D(fees.Json.GetProperty("fees").EnumerateArray().First(f => f.GetProperty("type").GetString() == "MANAGEMENT").GetProperty("percent").GetString()!);
                    }
                    var value = D(l.GetProperty("valueSek").GetString()!);
                    var fee = Math.Round(value * pct / 100 / 12, 2, MidpointRounding.ToEven);
                    total += fee;
                    lines.Add(new BillingLine(pf.Key.Item1, pf.Key.Item2, isin, Money.Format(value, 2), Money.Format(pct, 2), Money.Format(fee, 2)));
                }
                if (total <= 0) continue;
                var je = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
                {
                    reference = $"FEE-{b.Month}-{pf.Key.Item1}", description = $"Comisión de gestión {b.Month}", valueDate = Money.Date(last),
                    lines = new object[] { new { account = $"customer:{pf.Key.Item2}:cash", debit = Money.Format(total, 2) }, new { account = "fees:income", credit = Money.Format(total, 2) } },
                }, forwardAuth: false);
                charges.Add(new BillingCharge(pf.Key.Item1, pf.Key.Item2, Money.Format(total, 2), je.Ok ? je.Str("id") : null, je.Ok ? "CHARGED" : "LEDGER_ERROR"));
            }
            var id = "bill_" + Guid.NewGuid().ToString("N")[..12];
            var run = new BillingRun(id, b.Month, Money.Date(valuationDate), charges.All(c => c.Status == "CHARGED") ? "COMPLETED" : "PARTIAL", charges.Count,
                Money.Format(charges.Sum(c => D(c.TotalFeeSek)), 2), charges, lines, Nordic.Iso(clock.LocalNow), p.Sub);
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO runs VALUES (@id, @Month, @json, @at)", new { id, b.Month, json = System.Text.Json.JsonSerializer.Serialize(run, Canonical.Web), at = run.RunAt }, tx);
            Outbox.Add(db, http, clock, "fees.billed", id, new { billingRunId = id, month = b.Month, portfolios = charges.Count, totalFeeSek = run.TotalFeeSek }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/billing-runs/{id}", run);
        }).WithTags("Comisiones").WithSummary("Facturar la comisión de gestión de un mes cerrado (Operaciones)").Produces<BillingRun>(201);

        app.MapGet("/api/v1/billing-runs/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var json = db.ExecuteScalar<string?>("SELECT json FROM runs WHERE id = @id", new { id });
            return json is null ? Problems.NotFound(http, "BILLING_RUN_NOT_FOUND", $"No existe la facturación {id}.", null) : Results.Content(json, "application/json");
        }).WithTags("Comisiones").WithSummary("Detalle de una facturación");

        app.MapGet("/api/v1/billing-runs", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT json FROM runs ORDER BY month DESC").Select(j => System.Text.Json.JsonDocument.Parse(j).RootElement).ToList());
        }).WithTags("Comisiones").WithSummary("Facturaciones hechas");
    }
}
