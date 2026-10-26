using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class OrderTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public OrderTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze($"{TestData.Tuesday}T10:00:00");
    public Task DisposeAsync() => _f.ResetClock();

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Buy_order_full_lifecycle()
    {
        var (customerId, email, _) = await Flows.FundedCustomer(_f, "20000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        var o = await Flows.Buy(_f, pf, "SE0000000003", "10000.00");
        Assert.Equal(201, o.Code);
        Assert.Equal("RECEIVED", o.Str("status"));
        Assert.Equal(TestData.Tuesday, o.Str("tradeDate"));
        Assert.False(o.Json.GetProperty("cutOffApplied").GetBoolean());
        Assert.Equal("Nordvik Nordic Small Cap", o.Str("fundName"));
        Assert.Equal("10000.00", (await Flows.Cash(_f, customerId)).GetProperty("available").GetString());

        var executed = await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday);
        Assert.Equal("EXECUTED", executed.Str("status"));
        Assert.StartsWith("BATCH-20261020-", executed.Str("batchId"));
        var nav = D((await _f.Get(Ports.Market, $"/api/v1/prices/SE0000000003?date={TestData.Tuesday}")).Str("nav"));
        var units = Math.Floor(9900m / nav * 10000m) / 10000m;
        Assert.Equal(Money.Format(units, 4), executed.Str("executedUnits"));
        Assert.Equal("100.00", executed.Str("feeSek"));
        Assert.Equal("2026-10-22", executed.Str("settlementDate"));

        var cash = await Flows.Cash(_f, customerId);
        Assert.Equal("10000.00", cash.GetProperty("booked").GetString());
        Assert.Equal("0.00", cash.GetProperty("reserved").GetString());
        var positions = await Eventually.Until(async () => await _f.Get(Ports.Positions, $"/api/v1/portfolios/{pf}/positions", NordvikFixture.Bearer(await _f.Erik())),
            r => r.Json.GetProperty("positions").GetArrayLength() == 1, 8000, "posición");
        var pos = positions.Json.GetProperty("positions")[0];
        Assert.Equal(Money.Format(units, 4), pos.GetProperty("units").GetString());
        Assert.Equal("10000.00", pos.GetProperty("costBasisSek").GetString());

        var val = await _f.Get(Ports.Valuation, $"/api/v1/portfolios/{pf}/valuation", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(Money.Format(Math.Round(units * nav, 2, MidpointRounding.ToEven), 2), val.Str("totalValueSek"));
        Assert.Equal("100.00", val.Json.GetProperty("lines")[0].GetProperty("weightPercent").GetString());
        Assert.Equal("0.00", (await _f.Get(Ports.Valuation, $"/api/v1/portfolios/{pf}/valuation?date=2026-10-19", NordvikFixture.Bearer(await _f.Erik()))).Str("totalValueSek"));

        var exec = await _f.Get(Ports.Executions, $"/api/v1/executions?orderId={o.Str("id")}");
        Assert.False(exec.Json[0].GetProperty("settled").GetBoolean()); // liquida el jueves
        var ledger = await _f.Get(Ports.Ledger, $"/api/v1/journal-entries?reference=TRD-{exec.Json[0].GetProperty("id").GetString()}");
        Assert.Equal("custody:payable", ledger.Json[0].GetProperty("lines")[1].GetProperty("account").GetString());
        var mail = await Eventually.Until(() => _f.Get(Ports.Notification, $"/api/v1/mailbox?to={email}"), r => r.Body.Contains("Nordic Small Cap"), 8000, "aviso de ejecución");
        Assert.Contains(Money.Format(units, 4), mail.Body);
    }

    [Theory]
    [InlineData("2026-10-20T14:59:59", "2026-10-20", false)]
    [InlineData("2026-10-20T15:00:00", "2026-10-21", true)]
    [InlineData("2026-10-23T16:00:00", "2026-10-26", true)]
    [InlineData("2026-10-24T09:00:00", "2026-10-26", true)]
    [InlineData("2026-12-23T16:00:00", "2026-12-28", true)]
    public async Task Cut_off_decides_the_trade_date(string now, string tradeDate, bool moved)
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "500.00");
        var pf = await Flows.Portfolio(_f, customerId);
        await _f.Freeze(now);
        var o = await Flows.Buy(_f, pf, "SE0000000001", "200.00");
        Assert.Equal(tradeDate, o.Str("tradeDate"));
        Assert.Equal(moved, o.Json.GetProperty("cutOffApplied").GetBoolean());
        Assert.Equal("CANCELLED", (await _f.Send(HttpMethod.Post, Ports.Orders, $"/api/v1/orders/{o.Str("id")}/cancel", null, NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
    }

    [Fact]
    public async Task Idempotency_key_rules_for_orders()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        var key = Flows.Key();
        var a = await Flows.Buy(_f, pf, "SE0000000001", "300.00", key: key);
        var b = await Flows.Buy(_f, pf, "SE0000000001", "300.00", key: key);
        Assert.Equal(a.Str("id"), b.Str("id"));
        Assert.Equal("true", b.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal("700.00", (await Flows.Cash(_f, customerId)).GetProperty("available").GetString());
        (await Flows.Buy(_f, pf, "SE0000000001", "301.00", key: key)).ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await _f.Send(HttpMethod.Post, Ports.Orders, "/api/v1/orders", $"{{\"portfolioId\":\"{pf}\",\"isin\":\"SE0000000001\",\"side\":\"BUY\",\"amount\":\"300.00\"}}", NordvikFixture.Bearer(await _f.Erik())))
            .ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
        (await Flows.Buy(_f, pf, "SE0000000001", "300.00", key: "corta")).ShouldBeProblem(400, "IDEMPOTENCY_KEY_INVALID");
        // Un error también se guarda: repetir la misma petición fallida devuelve el mismo error sin volver a evaluarla
        var k2 = Flows.Key();
        var fail1 = await Flows.Buy(_f, pf, "SE0000000001", "5000.00", key: k2);
        fail1.ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        var fail2 = await Flows.Buy(_f, pf, "SE0000000001", "5000.00", key: k2);
        Assert.Equal(422, fail2.Code);
        Assert.Equal("true", fail2.Headers.GetValues("Idempotent-Replayed").Single());
    }

    [Fact]
    public async Task Business_rules_for_orders()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        (await Flows.Buy(_f, pf, "SE0000000007", "500.00")).ShouldBeProblem(422, "FUND_CLOSED");
        (await Flows.Buy(_f, pf, "SE0000000001", "99.99")).ShouldBeProblem(422, "BELOW_MINIMUM");
        (await Flows.Buy(_f, pf, "SE9999999999", "500.00")).ShouldBeProblem(422, "UNKNOWN_FUND");
        (await Flows.Buy(_f, pf, "SE0000000001", "1000.01")).ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        (await Flows.Sell(_f, pf, "SE0000000001", "1.0000")).ShouldBeProblem(422, "INSUFFICIENT_UNITS");
        var v = await _f.Send(HttpMethod.Post, Ports.Orders, "/api/v1/orders", $"{{\"portfolioId\":\"{pf}\",\"isin\":\"SE0000000001\",\"side\":\"HOLD\",\"amount\":\"1\",\"units\":\"2\"}}", Flows.With(await _f.Erik(), Flows.Key()));
        v.ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Flows.Buy(_f, pf, "SE0000000001", "500.00", await _f.Lars())).ShouldBeProblem(404, "PORTFOLIO_NOT_FOUND");
        (await Flows.Buy(_f, pf, "SE0000000001", "500.00", await _f.Maja())).ShouldBeProblem(403, "ACCESS_DENIED");
        (await Flows.Buy(_f, "no-existe", "SE0000000001", "500.00")).ShouldBeProblem(404, "PORTFOLIO_NOT_FOUND");
    }

    [Fact]
    public async Task Orders_can_only_be_cancelled_before_they_are_sent()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "2000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        var a = await Flows.Buy(_f, pf, "SE0000000001", "500.00");
        var b = await Flows.Buy(_f, pf, "SE0000000002", "500.00");
        var erik = await _f.Erik();
        Assert.Equal("CANCELLED", (await _f.Send(HttpMethod.Post, Ports.Orders, $"/api/v1/orders/{a.Str("id")}/cancel", null, NordvikFixture.Bearer(erik))).Str("status"));
        Assert.Equal("1500.00", (await Flows.Cash(_f, customerId)).GetProperty("available").GetString());
        var run = await _f.Post(Ports.Router, "/api/v1/batches/run");
        Assert.True(run.Int("orders") >= 1);
        Assert.Equal("SENT", (await _f.Get(Ports.Orders, $"/api/v1/orders/{b.Str("id")}", NordvikFixture.Bearer(erik))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Orders, $"/api/v1/orders/{b.Str("id")}/cancel", null, NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "ORDER_NOT_CANCELLABLE");
        var list = await _f.Get(Ports.Orders, $"/api/v1/portfolios/{pf}/orders", NordvikFixture.Bearer(erik));
        Assert.Equal(new[] { "SENT", "CANCELLED" }, list.Json.EnumerateArray().Select(x => x.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Sells_check_pending_units_and_credit_net_proceeds()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "5000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000001\",\"percent\":100}]");
        var buy = await Flows.Buy(_f, pf, "SE0000000001", "5000.00");
        var exec = await Flows.ExecuteAt1805(_f, buy.Str("id"), TestData.Tuesday);
        var units = D(exec.Str("executedUnits"));
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions/SE0000000001"), r => r.Str("units") == Money.Format(units, 4), 8000, "posición");
        await _f.Freeze("2026-10-21T10:00:00");
        var half = Math.Round(units / 2, 4);
        var s1 = await Flows.Sell(_f, pf, "SE0000000001", Money.Format(half, 4));
        Assert.Equal(201, s1.Code);
        (await Flows.Sell(_f, pf, "SE0000000001", Money.Format(units - half + 0.0001m, 4))).ShouldBeProblem(422, "INSUFFICIENT_UNITS");
        var sold = await Flows.ExecuteAt1805(_f, s1.Str("id"), "2026-10-21");
        var nav = D((await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2026-10-21")).Str("nav"));
        var gross = Math.Round(half * nav, 2, MidpointRounding.ToEven);
        Assert.Equal(Money.Format(gross, 2), sold.Str("netAmountSek")); // Global Equity no cobra salida
        var cash = await Flows.Cash(_f, customerId);
        Assert.Equal(Money.Format(gross, 2), cash.GetProperty("booked").GetString());
        var pos = await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions"),
            r => r.Json.GetProperty("positions")[0].GetProperty("units").GetString() == Money.Format(units - half, 4), 8000, "venta en posiciones");
        var p = pos.Json.GetProperty("positions")[0];
        var removed = Math.Round(5000m * half / units, 2, MidpointRounding.ToEven);
        Assert.Equal(Money.Format(5000m - removed, 2), p.GetProperty("costBasisSek").GetString());
        Assert.Equal(Money.Format(gross - removed, 2), p.GetProperty("realizedGainSek").GetString());
    }

    [Fact]
    public async Task Custodian_rejection_rejects_the_order_and_releases_nothing_for_sells()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000101\",\"percent\":100}]");
        var buy = await Flows.Buy(_f, pf, "SE0000000101", "1000.00");
        var exec = await Flows.ExecuteAt1805(_f, buy.Str("id"), TestData.Tuesday);
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions/SE0000000101"), r => r.Str("units") == exec.Str("executedUnits"), 8000, "posición");
        // El custodio "pierde" participaciones: nuestra venta será rechazada por él
        await _f.Post(Ports.CustodianSim, "/__admin/break-holding", $"{{\"accountId\":\"{pf}\",\"isin\":\"SE0000000101\",\"deltaUnits\":\"-{exec.Str("executedUnits")}\"}}");
        await _f.Freeze("2026-10-21T10:00:00");
        var sell = await Flows.Sell(_f, pf, "SE0000000101", exec.Str("executedUnits"));
        var done = await Flows.ExecuteAt1805(_f, sell.Str("id"), "2026-10-21");
        Assert.Equal("REJECTED", done.Str("status"));
        Assert.Equal("INSUFFICIENT_HOLDINGS", done.Str("rejectionReason"));
        // Y la conciliación lo detecta como descuadre
        var rec = await _f.Send(HttpMethod.Post, Ports.Reconciliation, "/api/v1/reconciliations", "{\"date\":\"2026-10-21\"}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(201, rec.Code);
        Assert.Contains(rec.Json.GetProperty("positionBreaks").EnumerateArray(), b => b.GetProperty("accountId").GetString() == pf && b.GetProperty("custodianUnits").GetString() == "0.0000");
        Assert.Equal("BREAKS", rec.Str("status"));
    }
}

[Collection("nordvik")]
public class PortfolioAnalyticsTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public PortfolioAnalyticsTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public async Task September_position_is_billed_valued_and_measured()
    {
        await _f.Freeze("2026-09-15T10:00:00");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "10000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000002\",\"percent\":100}]");
        var buy = await Flows.Buy(_f, pf, "SE0000000002", "10000.00");
        var exec = await Flows.ExecuteAt1805(_f, buy.Str("id"), "2026-09-15");
        var units = D(exec.Str("executedUnits"));
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions/SE0000000002"), r => D(r.Str("units")) == units, 8000, "posición");
        await _f.ResetClock();
        var olof = await _f.Olof();

        // Comisión de gestión de septiembre: valor a 30-09 × 0.20 % / 12
        (await _f.Send(HttpMethod.Post, Ports.FeeBilling, "/api/v1/billing-runs", "{\"month\":\"2026-10\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(422, "MONTH_NOT_CLOSED");
        var run = await _f.Send(HttpMethod.Post, Ports.FeeBilling, "/api/v1/billing-runs", "{\"month\":\"2026-09\"}", NordvikFixture.Bearer(olof));
        Assert.Equal(201, run.Code);
        Assert.Equal("2026-09-30", run.Str("valuationDate"));
        var navSep30 = D((await _f.Get(Ports.Market, "/api/v1/prices/SE0000000002?date=2026-09-30")).Str("nav"));
        var value = Math.Round(units * navSep30, 2, MidpointRounding.ToEven);
        var fee = Math.Round(value * 0.20m / 100 / 12, 2, MidpointRounding.ToEven);
        var charge = run.Json.GetProperty("charges").EnumerateArray().Single(c => c.GetProperty("portfolioId").GetString() == pf);
        Assert.Equal(Money.Format(fee, 2), charge.GetProperty("totalFeeSek").GetString());
        (await _f.Send(HttpMethod.Post, Ports.FeeBilling, "/api/v1/billing-runs", "{\"month\":\"2026-09\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "BILLING_RUN_EXISTS");
        Assert.Equal(Money.Format(-fee, 2), await Flows.Balance(_f, $"customer:{customerId}:cash"));

        // Devolución de la comisión
        var refund = await _f.Send(HttpMethod.Post, Ports.Refunds, "/api/v1/refunds", $"{{\"entryId\":\"{charge.GetProperty("ledgerEntryId").GetString()}\",\"reason\":\"Cliente nuevo: primer mes gratis\"}}", NordvikFixture.Bearer(olof));
        Assert.Equal(201, refund.Code);
        Assert.Equal("0.00", await Flows.Balance(_f, $"customer:{customerId}:cash"));
        (await _f.Send(HttpMethod.Post, Ports.Refunds, "/api/v1/refunds", $"{{\"entryId\":\"{charge.GetProperty("ledgerEntryId").GetString()}\",\"reason\":\"otra vez\"}}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "ALREADY_REFUNDED");

        // Rentabilidad: con un solo flujo, TWR = V_fin / V_día_de_compra − 1
        var perf = await _f.Get(Ports.Performance, $"/api/v1/portfolios/{pf}/performance?from=2026-09-14&to=2026-10-19&series=true", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(200, perf.Code);
        var series = perf.Json.GetProperty("series").EnumerateArray().ToList();
        var buyDay = series.First(x => x.GetProperty("date").GetString() == "2026-09-15");
        Assert.Equal("10000.00", buyDay.GetProperty("flowSek").GetString());
        var vBuy = D(buyDay.GetProperty("valueSek").GetString()!);
        var vEnd = D(perf.Str("endValueSek"));
        Assert.Equal(Money.Format((vEnd / vBuy - 1) * 100, 2), perf.Str("twrPercent"));
        var days = DateOnly.Parse("2026-10-19").DayNumber - DateOnly.Parse("2026-09-15").DayNumber;
        var irr = Math.Pow((double)(vEnd / 10000m), 365.0 / days) - 1;
        Assert.InRange(D(perf.Str("mwrPercentAnnualized")!), (decimal)(irr * 100) - 0.05m, (decimal)(irr * 100) + 0.05m);
        Assert.Equal(Money.Format(vEnd - 10000m, 2), perf.Str("gainSek"));
    }

    [Fact]
    public async Task Gl_export_csv_has_totals_and_checksum()
    {
        var olof = await _f.Olof();
        var r = await _f.Send(HttpMethod.Post, Ports.GlExport, "/api/v1/exports", $"{{\"date\":\"{TestData.Tuesday}\"}}", NordvikFixture.Bearer(olof));
        if (r.Code == 409) r = await _f.Send(HttpMethod.Post, Ports.GlExport, "/api/v1/exports?force=true", $"{{\"date\":\"{TestData.Tuesday}\"}}", NordvikFixture.Bearer(olof));
        Assert.Equal(201, r.Code);
        Assert.Equal(r.Str("totalDebit"), r.Str("totalCredit"));
        var file = await _f.Http.GetAsync(NordvikFixture.Url(Ports.GlExport, $"/api/v1/exports/{r.Str("id")}/file"));
        var csv = await file.Content.ReadAsStringAsync();
        Assert.Equal("text/csv", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(r.Str("fileName"), file.Content.Headers.ContentDisposition?.FileNameStar ?? file.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(Canonical.Sha256Hex(csv), file.Headers.GetValues("X-Checksum-SHA256").Single());
        Assert.StartsWith("entryId;reference;valueDate;lineNo;account;debit;credit", csv);
        Assert.EndsWith($";{r.Str("totalDebit")};{r.Str("totalCredit")}\n", csv);
        (await _f.Send(HttpMethod.Post, Ports.GlExport, "/api/v1/exports", $"{{\"date\":\"{TestData.Tuesday}\"}}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "EXPORT_EXISTS");
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        (await _f.Send(HttpMethod.Post, Ports.GlExport, "/api/v1/exports", $"{{\"date\":\"{today}\"}}", NordvikFixture.Bearer(olof))).ShouldBeProblem(422, "DAY_NOT_CLOSED");
        (await _f.Send(HttpMethod.Post, Ports.GlExport, "/api/v1/exports", $"{{\"date\":\"{TestData.Tuesday}\"}}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Rebalancing_saga_sells_then_buys()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "10000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000001\",\"percent\":50},{\"isin\":\"SE0000000101\",\"percent\":50}]");
        var buy = await Flows.Buy(_f, pf, "SE0000000001", "10000.00");
        await Flows.ExecuteAt1805(_f, buy.Str("id"), TestData.Tuesday);
        var erik = await _f.Erik();
        var drift = await Eventually.Until(() => _f.Get(Ports.Rebalancing, $"/api/v1/portfolios/{pf}/drift", NordvikFixture.Bearer(erik)), r => r.Json.GetProperty("needsRebalancing").GetBoolean(), 8000, "desviación");
        Assert.Equal("50.00", drift.Str("maxDriftPp"));
        var rb = await _f.Send(HttpMethod.Post, Ports.Rebalancing, $"/api/v1/portfolios/{pf}/rebalance", "{}", NordvikFixture.Bearer(erik));
        Assert.Equal(201, rb.Code);
        Assert.Equal("SELLING", rb.Str("status"));
        var sell = rb.Json.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("side").GetString() == "SELL");
        Assert.Equal("PLACED", sell.GetProperty("status").GetString());
        (await _f.Send(HttpMethod.Post, Ports.Rebalancing, $"/api/v1/portfolios/{pf}/rebalance", "{}", NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "REBALANCE_IN_PROGRESS");
        // Las ventas se dieron después de la hora de corte: se negocian el miércoles
        await Flows.ExecuteAt1805(_f, sell.GetProperty("orderId").GetString()!, "2026-10-21");
        static JsonElement BuyOf(Reply r) => r.Json.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("side").GetString() == "BUY");
        var buying = await Eventually.Until(async () => await _f.Get(Ports.Rebalancing, $"/api/v1/rebalances/{rb.Str("id")}", NordvikFixture.Bearer(await _f.Erik())),
            r => r.Str("status") is "COMPLETED" or "FAILED" || BuyOf(r).GetProperty("status").GetString() != "PLANNED", 10000, "fase de compras");
        Assert.True(buying.Str("status") == "BUYING", buying.Body);
        var buyOrder = BuyOf(buying);
        Assert.Equal("PLACED", buyOrder.GetProperty("status").GetString());
        await Flows.ExecuteAt1805(_f, buyOrder.GetProperty("orderId").GetString()!, "2026-10-22");
        var done = await Eventually.Until(async () => await _f.Get(Ports.Rebalancing, $"/api/v1/rebalances/{rb.Str("id")}", NordvikFixture.Bearer(await _f.Erik())), r => r.Str("status") is "COMPLETED" or "FAILED", 10000, "fin de la saga");
        Assert.Equal("COMPLETED", done.Str("status"));
        var after = await _f.Get(Ports.Rebalancing, $"/api/v1/portfolios/{pf}/drift", NordvikFixture.Bearer(await _f.Erik()));
        Assert.False(after.Json.GetProperty("needsRebalancing").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Rebalancing, $"/api/v1/portfolios/{pf}/rebalance", "{}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(422, "NO_REBALANCING_NEEDED");
    }

    [Fact]
    public async Task Corporate_actions_split_dividend_and_merger()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "3000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000202\",\"percent\":50},{\"isin\":\"SE0000000201\",\"percent\":50}]");
        var a = await Flows.Buy(_f, pf, "SE0000000202", "1500.00");
        var b = await Flows.Buy(_f, pf, "SE0000000201", "1500.00");
        var ea = await Flows.ExecuteAt1805(_f, a.Str("id"), TestData.Tuesday);
        var eb = await Flows.WaitOrder(_f, b.Str("id"), "EXECUTED");
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions"), r => r.Json.GetProperty("positions").GetArrayLength() == 2, 8000, "posiciones");
        var u202 = D(ea.Str("executedUnits")); var u201 = D(eb.Str("executedUnits"));
        await _f.Freeze("2026-10-22T19:00:00");
        var olof = await _f.Olof();

        var split = await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{\"type\":\"SPLIT\",\"isin\":\"SE0000000202\",\"effectiveDate\":\"2026-10-22\",\"ratio\":\"3\"}", NordvikFixture.Bearer(olof));
        Assert.Equal("ANNOUNCED", split.Str("status"));
        var applied = await _f.Send(HttpMethod.Post, Ports.CorporateActions, $"/api/v1/corporate-actions/{split.Str("id")}/apply", null, NordvikFixture.Bearer(olof));
        var mine = applied.Json.GetProperty("affected").EnumerateArray().Single(x => x.GetProperty("portfolioId").GetString() == pf);
        Assert.Equal(Money.Format(u202 * 3, 4), mine.GetProperty("unitsAfter").GetString());
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions/SE0000000202"), r => r.Str("units") == Money.Format(u202 * 3, 4), 8000, "desdoblamiento");
        (await _f.Send(HttpMethod.Post, Ports.CorporateActions, $"/api/v1/corporate-actions/{split.Str("id")}/apply", null, NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "ALREADY_APPLIED");

        var div = await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{\"type\":\"DIVIDEND\",\"isin\":\"SE0000000201\",\"effectiveDate\":\"2026-10-22\",\"amountPerUnit\":\"1.25\"}", NordvikFixture.Bearer(olof));
        await _f.Send(HttpMethod.Post, Ports.CorporateActions, $"/api/v1/corporate-actions/{div.Str("id")}/apply", null, NordvikFixture.Bearer(olof));
        var dividend = Math.Round(u201 * 1.25m, 2, MidpointRounding.ToEven);
        Assert.Equal(Money.Format(dividend, 2), await Flows.Balance(_f, $"customer:{customerId}:cash"));

        var merger = await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{\"type\":\"MERGER\",\"isin\":\"SE0000000201\",\"effectiveDate\":\"2026-10-22\",\"ratio\":\"0.5\",\"targetIsin\":\"SE0000000202\"}", NordvikFixture.Bearer(olof));
        await _f.Send(HttpMethod.Post, Ports.CorporateActions, $"/api/v1/corporate-actions/{merger.Str("id")}/apply", null, NordvikFixture.Bearer(olof));
        var expected = u202 * 3 + Math.Floor(u201 * 0.5m * 10000m) / 10000m;
        var pos = await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions"), r => r.Json.GetProperty("positions").EnumerateArray().Count(x => D(x.GetProperty("units").GetString()!) != 0) == 1, 8000, "fusión");
        Assert.Equal(Money.Format(expected, 4), pos.Json.GetProperty("positions").EnumerateArray().Single(x => x.GetProperty("isin").GetString() == "SE0000000202").GetProperty("units").GetString());

        var future = await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{\"type\":\"SPLIT\",\"isin\":\"SE0000000001\",\"effectiveDate\":\"2026-11-30\",\"ratio\":\"2\"}", NordvikFixture.Bearer(olof));
        (await _f.Send(HttpMethod.Post, Ports.CorporateActions, $"/api/v1/corporate-actions/{future.Str("id")}/apply", null, NordvikFixture.Bearer(olof))).ShouldBeProblem(422, "NOT_YET_EFFECTIVE");
        (await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{\"type\":\"SPLIT\",\"isin\":\"SE0000000001\",\"effectiveDate\":\"2026-10-24\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.CorporateActions, "/api/v1/corporate-actions", "{}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }
}
