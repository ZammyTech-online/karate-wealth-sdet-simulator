using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Execution(string Id, string OrderId, string PortfolioId, string CustomerId, string Isin, string Side, string Units, string Nav, string FundCurrency, string FxRate,
    string GrossAmountSek, string FeeSek, string NetAmountSek, string TradeDate, string SettlementDate, bool Settled, string? LedgerEntryId, string ReceivedAt);

/// <summary>
/// execution-service: recibe del custodio (vía eventos de custody-adapter) las ejecuciones y los rechazos, los asocia a la orden,
/// contabiliza el efectivo en el ledger y publica trade.executed / trade.rejected.
/// Compra: DEBE cliente (bruto) / HABER custody:payable · Venta: DEBE custody:receivable / HABER cliente (neto).
/// </summary>
public sealed class ExecutionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "execution-service", "J-investment", 6904, 2, "Inversiones",
        "Ejecuciones y liquidaciones del custodio: las enlaza con la orden, las contabiliza y publica trade.executed o trade.rejected.",
        DependsOn: new[] { "custody-adapter", "order-management", "ledger-service", "event-bus", "clock-service" },
        Emits: new[] { "trade.executed", "trade.rejected" }, Consumes: new[] { "custody.execution.received", "custody.order.rejected" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS executions (id TEXT PRIMARY KEY, order_id TEXT UNIQUE, portfolio_id TEXT, customer_id TEXT, isin TEXT, side TEXT, units TEXT, nav TEXT,
                     fund_currency TEXT, fx_rate TEXT, gross TEXT, fee TEXT, net TEXT, trade_date TEXT, settlement_date TEXT, ledger_entry_id TEXT, received_at TEXT, seq INTEGER)");
    }

    private static List<Execution> Query(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string where, object args)
    {
        var today = Money.Date(clock.Today);
        return db.Query<(string Id, string Ord, string Pf, string Cust, string Isin, string Side, string Units, string Nav, string Ccy, string Fx, string G, string F, string N, string Td, string Sd, string? Le, string At)>(
            "SELECT id, order_id, portfolio_id, customer_id, isin, side, units, nav, fund_currency, fx_rate, gross, fee, net, trade_date, settlement_date, ledger_entry_id, received_at FROM executions " + where, args)
            .Select(r => new Execution(r.Id, r.Ord, r.Pf, r.Cust, r.Isin, r.Side, r.Units, r.Nav, r.Ccy, r.Fx, r.G, r.F, r.N, r.Td, r.Sd, string.CompareOrdinal(r.Sd, today) <= 0, r.Le, r.At)).ToList();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/executions", (HttpContext http, string? orderId, string? portfolioId) =>
        {
            if (orderId is null && portfolioId is null) return Problems.BadRequest(http, "FILTER_REQUIRED", "Filtra por orderId o portfolioId.", null);
            using var db = ctx.OpenDb();
            return Results.Ok(Query(db, clock, "WHERE (@orderId IS NULL OR order_id = @orderId) AND (@portfolioId IS NULL OR portfolio_id = @portfolioId) ORDER BY seq", new { orderId, portfolioId }));
        }).WithTags("Ejecuciones").WithSummary("Ejecuciones de una orden o de una cartera").Produces<List<Execution>>();

        app.MapGet("/api/v1/executions/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var e = Query(db, clock, "WHERE id = @id", new { id }).SingleOrDefault();
            return e is null ? Problems.NotFound(http, "EXECUTION_NOT_FOUND", $"No existe la ejecución {id}.", null) : Results.Ok(e);
        }).WithTags("Ejecuciones").WithSummary("Una ejecución (settled = la fecha de liquidación ya pasó)").Produces<Execution>();

        EventConsumer.Start(app, ctx, new[] { "custody.execution.received", "custody.order.rejected" }, async ev =>
        {
            var orderId = ev.Key;
            var order = await Downstream.Get(null, "order-management", $"/api/v1/internal/orders/{orderId}", forwardAuth: false);
            if (order.Status == 404) return; // ejecución de una orden que no es nuestra (pruebas manuales del custodio)
            if (!order.Ok) throw new InvalidOperationException("order-management respondió " + order.Status);
            var customerId = order.Str("customerId")!;
            var portfolioId = order.Str("portfolioId")!;
            using var db = ctx.OpenDb();
            if (ev.Topic == "custody.order.rejected")
            {
                var reason = ev.Payload.GetProperty("rejection").GetProperty("reason").GetString();
                Outbox.Add(db, null, clock, "trade.rejected", orderId, new { orderId, customerId, portfolioId, reason }, correlationId: ev.CorrelationId);
                return;
            }
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM executions WHERE order_id = @orderId", new { orderId }) > 0) return;
            var x = ev.Payload.GetProperty("execution");
            string S(string p) => x.GetProperty(p).GetString()!;
            var side = S("side");
            var lines = side == "BUY"
                ? new object[] { new { account = $"customer:{customerId}:cash", debit = S("grossAmountSek") }, new { account = "custody:payable", credit = S("grossAmountSek") } }
                : new object[] { new { account = "custody:receivable", debit = S("netAmountSek") }, new { account = $"customer:{customerId}:cash", credit = S("netAmountSek") } };
            var je = await Downstream.Post(null, "ledger-service", "/api/v1/journal-entries", new
            {
                reference = "TRD-" + S("executionId"), description = $"{side} {S("units")} {S("isin")} a {S("nav")}", valueDate = S("tradeDate"), lines,
            }, forwardAuth: false);
            if (!je.Ok && je.Status != 409) throw new InvalidOperationException("ledger-service respondió " + je.Status + ": " + je.Raw);
            var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM executions") ?? 0) + 1;
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO executions VALUES (@id, @orderId, @portfolioId, @customerId, @isin, @side, @units, @nav, @ccy, @fx, @g, @f, @n, @td, @sd, @le, @at, @seq)",
                new { id = S("executionId"), orderId, portfolioId, customerId, isin = S("isin"), side, units = S("units"), nav = S("nav"), ccy = S("fundCurrency"), fx = S("fxRate"),
                      g = S("grossAmountSek"), f = S("feeSek"), n = S("netAmountSek"), td = S("tradeDate"), sd = S("settlementDate"), le = je.Ok ? je.Str("id") : null, at = Nordic.Iso(clock.LocalNow), seq }, tx);
            Outbox.Add(db, null, clock, "trade.executed", orderId, new
            {
                executionId = S("executionId"), orderId, portfolioId, customerId, isin = S("isin"), side, units = S("units"), nav = S("nav"), fundCurrency = S("fundCurrency"), fxRate = S("fxRate"),
                grossAmountSek = S("grossAmountSek"), feeSek = S("feeSek"), netAmountSek = S("netAmountSek"), tradeDate = S("tradeDate"), settlementDate = S("settlementDate"),
            }, tx, correlationId: ev.CorrelationId);
            tx.Commit();
        });
    }
}
