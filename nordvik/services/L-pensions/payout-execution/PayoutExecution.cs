using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PayoutRunRequest(string? Period);

/// <summary>
/// payout-execution: la nómina mensual de pensiones. Una ejecución por mes paga cada plan de cobro ACTIVE cuyo inicio ya llegó:
/// bruto, retención según tax-withholding (tabla municipal de la Agencia Tributaria, o SINK para no residentes; si no responde, la provisional
/// del 30 %) y neto, con su asiento en ledger.
/// Idempotente por (plan de cobro, mes): repetir la ejecución no paga dos veces (ALREADY_PAID). Si el plan de pensión está FROZEN, se salta.
/// Al completar los pagos de una renta temporal, el plan de cobro pasa a FINISHED.
/// </summary>
public sealed class PayoutExecution : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "payout-execution", "L-pensions", 7111, 3, "Pensiones",
        "Ejecución mensual de pagos de pensión: bruto, retención según tax-withholding (30 % provisional si no responde), neto y asiento; idempotente por plan y mes.",
        DependsOn: new[] { "payout-planner", "pension-plan-service", "ledger-service", "permission-service", "clock-service" }, Emits: new[] { "payout.paid", "payout-run.completed" },
        // Nivel 4: con tax-withholding encendido retiene según la tabla del municipio; en el nivel 3 aplica el 30 % provisional.
        OptionalDependsOn: new[] { "tax-withholding" });

    public const decimal ProvisionalTaxRate = 0.30m;

    public static (decimal Tax, decimal Net) Withhold(decimal gross) => Withhold(gross, ProvisionalTaxRate * 100);

    public static (decimal Tax, decimal Net) Withhold(decimal gross, decimal ratePct)
    {
        var tax = Math.Round(gross * ratePct / 100, 2, MidpointRounding.ToEven);
        return (tax, gross - tax);
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, period TEXT, paid INTEGER, skipped INTEGER, lines TEXT, at TEXT, by TEXT);
                     CREATE TABLE IF NOT EXISTS payments (id TEXT PRIMARY KEY, payout_plan_id TEXT, pension_plan_id TEXT, customer_id TEXT, period TEXT, gross TEXT, tax TEXT, net TEXT, ledger_entry TEXT, run_id TEXT, paid_at TEXT,
                        UNIQUE (payout_plan_id, period))");
    }

    private static object Payment((string Id, string Po, string Pp, string Cust, string Period, string Gross, string Tax, string Net, string? Ledger, string Run, string At) r) => new
    {
        id = r.Id, payoutPlanId = r.Po, pensionPlanId = r.Pp, period = r.Period, gross = r.Gross, taxWithheld = r.Tax, net = r.Net, currency = "SEK", ledgerEntryId = r.Ledger, runId = r.Run, paidAt = r.At,
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        const string Cols = "id, payout_plan_id, pension_plan_id, customer_id, period, gross, tax, net, ledger_entry, run_id, paid_at";

        app.MapPost("/api/v1/payout-runs", async (HttpContext http, PayoutRunRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin", "Service" }) is { } denied) return denied;
            var period = b.Period ?? clock.LocalNow.ToString("yyyy-MM");
            if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^\d{4}-(0[1-9]|1[0-2])$")) return Problems.BadRequest(http, "VALIDATION_FAILED", "period es AAAA-MM.", null);
            if (string.CompareOrdinal(period, clock.LocalNow.ToString("yyyy-MM")) > 0) return Problems.Unprocessable(http, "FUTURE_PERIOD", $"No se paga {period} por adelantado.", null);
            var runId = "run_" + Guid.NewGuid().ToString("N")[..12];
            var plans = await Downstream.Get(http, "payout-planner", "/api/v1/internal/payout-plans?status=ACTIVE", forwardAuth: false);
            var lines = new List<object>(); int paid = 0, skipped = 0;
            foreach (var po in plans.Json.GetProperty("items").EnumerateArray())
            {
                var poId = po.GetProperty("id").GetString()!;
                var ppId = po.GetProperty("pensionPlanId").GetString()!;
                if (string.CompareOrdinal(po.GetProperty("startPeriod").GetString(), period) > 0) { lines.Add(new { payoutPlanId = poId, result = "NOT_STARTED" }); skipped++; continue; }
                using var db = ctx.OpenDb();
                if (db.ExecuteScalar<string?>("SELECT id FROM payments WHERE payout_plan_id = @poId AND period = @period", new { poId, period }) is { } done)
                { lines.Add(new { payoutPlanId = poId, result = "ALREADY_PAID", paymentId = done }); skipped++; continue; }
                var pp = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{ppId}", forwardAuth: false);
                if (pp.Str("status") == "FROZEN") { lines.Add(new { payoutPlanId = poId, result = "SKIPPED_PLAN_FROZEN" }); skipped++; continue; }
                var gross = decimal.Parse(po.GetProperty("monthlyGross").GetProperty("amount").GetString()!, Money.Inv);
                var customerIdW = po.GetProperty("customerId").GetString()!;
                var wh = await Downstream.GetOptional(http, "tax-withholding", $"/api/v1/internal/customers/{customerIdW}/withholding?incomeType=PENSION&year={period[..4]}", forwardAuth: false);
                var ratePct = wh is { Ok: true } && Money.TryParse(wh.Str("ratePct"), out var rp) ? rp : ProvisionalTaxRate * 100;
                var basis = wh is { Ok: true } ? wh.Str("basis") ?? "TAX_TABLE" : "PROVISIONAL_30";
                var (tax, net) = Withhold(gross, ratePct);
                var payId = "pay_" + Guid.NewGuid().ToString("N")[..12];
                var entry = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
                {
                    reference = $"PAYOUT-{poId}-{period}", description = $"Pensión {period} del plan de cobro {poId}",
                    lines = new object[] { new { account = "bank:pension-assets", debit = Money.Format(gross, 2) }, new { account = "suspense:tax-withholding", credit = Money.Format(tax, 2) }, new { account = "bank:payouts-outgoing", credit = Money.Format(net, 2) } },
                }, forwardAuth: false);
                var customerId = po.GetProperty("customerId").GetString()!;
                using (var tx = db.BeginTransaction())
                {
                    db.Execute("INSERT INTO payments VALUES (@payId, @poId, @ppId, @customerId, @period, @g, @t, @n, @e, @runId, @at)",
                        new { payId, poId, ppId, customerId, period, g = Money.Format(gross, 2), t = Money.Format(tax, 2), n = Money.Format(net, 2), e = entry.Str("id"), runId, at = Nordic.Iso(clock.LocalNow) }, tx);
                    Outbox.Add(db, http, clock, "payout.paid", payId, new { paymentId = payId, payoutPlanId = poId, customerId, period, gross = Money.Format(gross, 2), net = Money.Format(net, 2) }, tx);
                    tx.Commit();
                }
                paid++;
                lines.Add(new { payoutPlanId = poId, result = "PAID", paymentId = payId, gross = Money.Format(gross, 2), taxRatePct = Money.Format(ratePct, 2), taxBasis = basis, taxWithheld = Money.Format(tax, 2), net = Money.Format(net, 2) });
                if (po.GetProperty("type").GetString() == "TERM" && db.ExecuteScalar<long>("SELECT COUNT(*) FROM payments WHERE payout_plan_id = @poId", new { poId }) >= po.GetProperty("expectedPayments").GetInt64())
                    await Downstream.Send(http, "payout-planner", HttpMethod.Post, $"/api/v1/internal/payout-plans/{poId}/status/FINISHED", null, forwardAuth: false);
            }
            using (var db = ctx.OpenDb())
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO runs VALUES (@runId, @period, @paid, @skipped, @l, @at, @by)", new { runId, period, paid, skipped, l = JsonSerializer.Serialize(lines, Canonical.Web), at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
                Outbox.Add(db, http, clock, "payout-run.completed", runId, new { runId, period, paid, skipped }, tx);
                tx.Commit();
            }
            return Results.Ok(new { id = runId, period, paid, skipped, lines });
        }).WithTags("Pagos").WithSummary("Ejecutar los pagos de un mes (idempotente por plan de cobro y mes)");

        app.MapGet("/api/v1/payout-runs/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin", "Compliance" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Period, long Paid, long Skipped, string Lines, string At, string By)>("SELECT id, period, paid, skipped, lines, at, by FROM runs WHERE id = @id", new { id });
            return r.Id is null ? Problems.NotFound(http, "RUN_NOT_FOUND", $"No existe la ejecución {id}.", null)
                : Results.Ok(new { id = r.Id, period = r.Period, paid = r.Paid, skipped = r.Skipped, lines = JsonDocument.Parse(r.Lines).RootElement, executedAt = r.At, executedBy = r.By });
        }).WithTags("Pagos").WithSummary("Una ejecución mensual con su resultado por plan");

        app.MapGet("/api/v1/internal/payments", (string? year, string? customerId) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<(string, string, string, string, string, string, string, string, string?, string, string)>(
                $"SELECT {Cols} FROM payments WHERE (@year IS NULL OR period LIKE @y) AND (@customerId IS NULL OR customer_id = @customerId) ORDER BY period, id",
                new { year, y = (year ?? "") + "-%", customerId }).ToList();
            return Results.Ok(new { items = rows.Select(r => new { id = r.Item1, payoutPlanId = r.Item2, pensionPlanId = r.Item3, customerId = r.Item4, period = r.Item5, gross = r.Item6, taxWithheld = r.Item7, net = r.Item8 }), count = rows.Count });
        }).WithTags("Interno").WithSummary("(interno) Pagos por año o cliente (tax-reporting)");

        app.MapGet("/api/v1/payout-plans/{id}/payments", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var rows = db.Query<(string, string, string, string, string, string, string, string, string?, string, string)>($"SELECT {Cols} FROM payments WHERE payout_plan_id = @id ORDER BY period", new { id }).ToList();
            var owner = rows.FirstOrDefault().Item4;
            if (owner is not null && await CustomerGuard.Check(http, p, "portfolio:read", owner) is { } g) return g;
            if (owner is null && p.Role == "Customer") return Problems.NotFound(http, "PAYOUT_PLAN_NOT_FOUND", $"No hay pagos del plan de cobro {id}.", null);
            var items = rows.Select(Payment).ToList();
            var gross = rows.Sum(r => decimal.Parse(r.Item6, Money.Inv)); var tax = rows.Sum(r => decimal.Parse(r.Item7, Money.Inv));
            return Results.Ok(new { payoutPlanId = id, items, count = items.Count, totals = new { gross = Money.Format(gross, 2), taxWithheld = Money.Format(tax, 2), net = Money.Format(gross - tax, 2) } });
        }).WithTags("Pagos").WithSummary("Pagos de un plan de cobro con totales");
    }
}
