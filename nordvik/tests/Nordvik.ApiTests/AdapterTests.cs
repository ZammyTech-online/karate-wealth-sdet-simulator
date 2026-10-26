using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class CustodyTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public CustodyTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public async Task DisposeAsync()
    {
        await _f.ResetClock();
        await _f.Post(Ports.CustodianSim, "/__admin/behaviour/reset");
    }

    private static string Batch(string id, string date, params object[] orders) =>
        JsonSerializer.Serialize(new { batchId = id, tradeDate = date, orders });

    [Fact]
    public async Task Batch_executes_at_the_nav_of_the_trade_date_after_18()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var account = "acc-" + Guid.NewGuid().ToString("N")[..8];
        var batchId = "BT-" + Guid.NewGuid().ToString("N")[..10];
        var sent = await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId, TestData.Tuesday,
            new { clientOrderId = batchId + "-1", accountId = account, isin = "SE0000000003", side = "BUY", amount = "10000.00" },
            new { clientOrderId = batchId + "-2", accountId = account, isin = "SE0000000001", side = "SELL", units = "5.0000" },
            new { clientOrderId = batchId + "-3", accountId = account, isin = "SE0000000007", side = "BUY", amount = "100.00" }));
        Assert.Equal(202, sent.Code);
        Assert.Equal("SENT", sent.Str("status"));
        await Task.Delay(800);
        Assert.Equal("SENT", (await _f.Get(Ports.CustodyAdapter, $"/api/v1/batches/{batchId}")).Str("status")); // antes de las 18:00 no hay NAV
        await _f.Freeze($"{TestData.Tuesday}T18:05:00");
        var done = await Eventually.Until(() => _f.Get(Ports.CustodyAdapter, $"/api/v1/batches/{batchId}"), r => r.Str("status") == "EXECUTED", 8000, "lote ejecutado");
        Assert.Equal(1, done.Int("executions"));
        Assert.Equal(2, done.Int("rejections"));

        var ev = await TestData.Events(_f, "custody.execution.received", batchId + "-1");
        var exec = ev.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("execution");
        var nav = (await _f.Get(Ports.Market, $"/api/v1/prices/SE0000000003?date={TestData.Tuesday}")).Str("nav");
        Assert.Equal(nav, exec.GetProperty("nav").GetString());
        // Nordic Small Cap cobra 1 % de entrada: 100.00 SEK; se invierten 9900.00
        Assert.Equal("100.00", exec.GetProperty("feeSek").GetString());
        Assert.Equal("9900.00", exec.GetProperty("netAmountSek").GetString());
        var units = Math.Floor(9900m / decimal.Parse(nav, CultureInfo.InvariantCulture) * 10000m) / 10000m;
        Assert.Equal(Money.Format(units, 4), exec.GetProperty("units").GetString());
        Assert.Equal("2026-10-22", exec.GetProperty("settlementDate").GetString());

        var rej = await TestData.Events(_f, "custody.order.rejected", batchId + "-2");
        Assert.Equal("INSUFFICIENT_HOLDINGS", rej.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("rejection").GetProperty("reason").GetString());
        var closed = await TestData.Events(_f, "custody.order.rejected", batchId + "-3");
        Assert.Equal("FUND_CLOSED", closed.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("rejection").GetProperty("reason").GetString());

        var holdings = await _f.Get(Ports.CustodyAdapter, $"/api/v1/holdings?accountId={account}");
        Assert.Equal(Money.Format(units, 4), holdings.Json.GetProperty("holdings")[0].GetProperty("units").GetString());
    }

    [Fact]
    public async Task Foreign_currency_funds_are_converted_with_the_fx_of_the_trade_date()
    {
        await _f.Freeze($"{TestData.Tuesday}T19:00:00");
        var batchId = "BT-" + Guid.NewGuid().ToString("N")[..10];
        await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId, TestData.Tuesday,
            new { clientOrderId = batchId + "-1", accountId = "acc-usd", isin = "SE0000000004", side = "BUY", amount = "5000.00" }));
        var ev = await TestData.Events(_f, "custody.execution.received", batchId + "-1");
        var exec = ev.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("execution");
        var rate = (await _f.Get(Ports.Fx, $"/api/v1/rates/USD/SEK?date={TestData.Tuesday}")).Str("rate");
        Assert.Equal(rate, exec.GetProperty("fxRate").GetString());
        Assert.Equal("USD", exec.GetProperty("fundCurrency").GetString());
        // Emerging Markets: 0.50 % de entrada
        Assert.Equal("25.00", exec.GetProperty("feeSek").GetString());
        var expected = Math.Floor(4975m / decimal.Parse(rate, CultureInfo.InvariantCulture) / decimal.Parse(exec.GetProperty("nav").GetString()!, CultureInfo.InvariantCulture) * 10000m) / 10000m;
        Assert.Equal(Money.Format(expected, 4), exec.GetProperty("units").GetString());
    }

    [Fact]
    public async Task Webhooks_with_a_bad_signature_are_rejected_and_duplicates_ignored()
    {
        var body = "{\"eventType\":\"BATCH_EXECUTED\",\"batch\":{\"batchId\":\"FAKE\",\"tradeDate\":\"2026-10-20\",\"executions\":[],\"rejections\":[]}}";
        var bad = await _f.Send(HttpMethod.Post, Ports.CustodyAdapter, "/api/v1/webhooks/custody", body, r => r.Headers.TryAddWithoutValidation("X-Custodian-Signature", "sha256=00"));
        bad.ShouldBeProblem(401, "INVALID_SIGNATURE");
        var sig = "sha256=" + Canonical.HmacHex("custody-webhook-secret-2026", body);
        var ok = await _f.Send(HttpMethod.Post, Ports.CustodyAdapter, "/api/v1/webhooks/custody", body, r => r.Headers.TryAddWithoutValidation("X-Custodian-Signature", sig));
        Assert.False(ok.Json.GetProperty("duplicate").GetBoolean());
        var again = await _f.Send(HttpMethod.Post, Ports.CustodyAdapter, "/api/v1/webhooks/custody", body, r => r.Headers.TryAddWithoutValidation("X-Custodian-Signature", sig));
        Assert.True(again.Json.GetProperty("duplicate").GetBoolean());
        var stats = await _f.Get(Ports.CustodyAdapter, "/api/v1/webhooks/stats");
        Assert.True(stats.Json.GetProperty("invalidSignature").GetInt64() >= 1);
        Assert.True(stats.Json.GetProperty("duplicates").GetInt64() >= 1);
    }

    [Fact]
    public async Task Custodian_errors_are_mapped_to_our_contract()
    {
        var batchId = "BT-" + Guid.NewGuid().ToString("N")[..10];
        var order = new { clientOrderId = batchId + "-1", accountId = "a", isin = "SE0000000001", side = "BUY", amount = "100.00" };
        Assert.Equal(202, (await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId, TestData.Tuesday, order))).Code);
        (await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId, TestData.Tuesday, order))).ShouldBeProblem(409, "BATCH_ALREADY_SENT");
        (await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId + "x", "2026-10-24", order))).ShouldBeProblem(502, "CUSTODIAN_REJECTED_BATCH");
        await _f.Post(Ports.CustodianSim, "/__admin/behaviour", "{\"mode\":\"down\"}");
        (await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", Batch(batchId + "y", TestData.Tuesday, order))).ShouldBeProblem(503, "CUSTODIAN_UNAVAILABLE");
        await _f.Post(Ports.CustodianSim, "/__admin/behaviour/reset");
        var raw = await _f.Post(Ports.CustodianSim, "/custody/v2/batches", "{}");
        Assert.Equal(401, raw.Code);
        Assert.Equal("UNAUTHORIZED", raw.Str("error"));
        (await _f.Post(Ports.CustodyAdapter, "/api/v1/batches", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }
}

[Collection("nordvik")]
public class PriceFeedTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public PriceFeedTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public async Task DisposeAsync()
    {
        await _f.ResetClock();
        await _f.Post(Ports.PriceFeedSim, "/__admin/behaviour/reset");
    }

    [Theory]
    [InlineData("SE0000000001", "2025-03-14")]
    [InlineData("SE0000000102", "2026-06-30")]
    [InlineData("SE0000000006", "2026-10-20")]
    public async Task Feed_prices_match_market_data(string isin, string date)
    {
        var feed = await _f.Send(HttpMethod.Get, Ports.PriceFeedSim, $"/feed/v1/nav/{isin}?date={date}", null, r => r.Headers.TryAddWithoutValidation("X-Api-Key", "feed-key-2026"));
        var market = await _f.Get(Ports.Market, $"/api/v1/prices/{isin}?date={date}");
        Assert.Equal(market.Str("nav"), feed.Str("nav"));
    }

    [Fact]
    public async Task Latest_nav_uses_etag_and_304()
    {
        var before = await _f.Get(Ports.PriceFeedAdapter, "/api/v1/feed/stats");
        var a = await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000002");
        var b = await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000002");
        Assert.Equal(a.Str("nav"), b.Str("nav"));
        Assert.Equal("cache-304", b.Str("source"));
        var after = await _f.Get(Ports.PriceFeedAdapter, "/api/v1/feed/stats");
        Assert.True(after.Json.GetProperty("notModified").GetInt64() > before.Json.GetProperty("notModified").GetInt64());
        var latest = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000002/latest");
        Assert.Equal(latest.Str("nav"), b.Str("nav"));
    }

    [Fact]
    public async Task Dated_navs_are_cached_and_stale_values_are_served_when_the_feed_is_down()
    {
        Assert.Equal("feed", (await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000101?date=2026-09-15")).Str("source"));
        Assert.Equal("cache", (await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000101?date=2026-09-15")).Str("source"));
        await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000201");
        await _f.Post(Ports.PriceFeedSim, "/__admin/behaviour", "{\"mode\":\"down\"}");
        var stale = await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000201");
        Assert.Equal(200, stale.Code);
        Assert.True(stale.Json.GetProperty("stale").GetBoolean());
        Assert.Contains("stale", stale.Headers.GetValues("Warning").Single());
        (await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000203")).ShouldBeProblem(503, "PRICE_FEED_UNAVAILABLE");
    }

    [Fact]
    public async Task Unpublished_navs_and_api_key()
    {
        await _f.Freeze($"{TestData.Tuesday}T12:00:00");
        (await _f.Get(Ports.PriceFeedAdapter, $"/api/v1/nav/SE0000000001?date={TestData.Tuesday}")).ShouldBeProblem(404, "NAV_NOT_PUBLISHED");
        (await _f.Get(Ports.PriceFeedAdapter, "/api/v1/nav/SE0000000001?date=2026-10-18")).ShouldBeProblem(404, "NO_TRADING_DAY");
        Assert.Equal(401, (await _f.Get(Ports.PriceFeedSim, "/feed/v1/nav/SE0000000001")).Code);
        var sync = await _f.Post(Ports.PriceFeedAdapter, "/api/v1/feed/sync?date=2026-10-19");
        Assert.Equal(14, sync.Json.GetProperty("received").GetInt32());
    }
}

[Collection("nordvik")]
public class NordPayTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public NordPayTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze($"{TestData.Tuesday}T11:00:00");
    public async Task DisposeAsync()
    {
        await _f.ResetClock();
        await _f.Post(Ports.NordPaySim, "/__admin/behaviour/reset");
    }

    private static string Pay(string msgId, string iban, string amount = "250.00") =>
        JsonSerializer.Serialize(new { msgId, endToEndId = "E2E-" + msgId, amount, creditorIban = iban, creditorName = "Ana Lindqvist", remittance = "Retirada " + msgId });

    [Theory]
    [InlineData("1234", "MATCH")]
    [InlineData("0009", "NO_MATCH")]
    [InlineData("0008", "CLOSE_MATCH")]
    [InlineData("0004", "ACCOUNT_CLOSED")]
    public async Task Account_ownership_rules(string ending, string result)
    {
        var r = await _f.Post(Ports.NordPayAdapter, "/api/v1/account-verifications", $"{{\"iban\":\"{TestData.Iban(ending)}\",\"nationalId\":\"19850314-1236\"}}");
        Assert.Equal(result, r.Str("result"));
    }

    [Fact]
    public async Task Invalid_iban_and_missing_fields()
    {
        (await _f.Post(Ports.NordPayAdapter, "/api/v1/account-verifications", "{\"iban\":\"SE00 1234\",\"nationalId\":\"19850314-1236\"}")).ShouldBeProblem(422, "INVALID_IBAN");
        (await _f.Post(Ports.NordPayAdapter, "/api/v1/account-verifications", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Payment_is_accepted_then_settled_by_signed_webhook()
    {
        var msg = "MSG-" + Guid.NewGuid().ToString("N")[..10];
        var r = await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban("1234")));
        Assert.Equal(202, r.Code);
        Assert.Equal("ACCP", r.Str("status"));
        var done = await Eventually.Until(() => _f.Get(Ports.NordPayAdapter, $"/api/v1/payments/E2E-{msg}"), x => x.Str("status") != "ACCP", 8000, "liquidación");
        Assert.Equal("ACSC", done.Str("status"));
        var ev = await TestData.Events(_f, "payment.bank-status.changed", "E2E-" + msg);
        Assert.Equal("ACSC", ev.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("status").GetString());
        var st = await _f.Get(Ports.NordPayAdapter, $"/api/v1/statements?date={TestData.Tuesday}");
        Assert.Contains(st.Json.GetProperty("entries").EnumerateArray(), e => e.GetProperty("endToEndId").GetString() == "E2E-" + msg && e.GetProperty("type").GetString() == "DBIT");
        (await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban("1234")))).ShouldBeProblem(409, "DUPLICATE_MESSAGE");
    }

    [Theory]
    [InlineData("0004", "250.00", "AC04")]
    [InlineData("0006", "250.00", "AC06")]
    [InlineData("1234", "1000001.00", "AM02")]
    public async Task Bank_rejections_carry_iso_reason_codes(string ending, string amount, string reason)
    {
        var msg = "MSG-" + Guid.NewGuid().ToString("N")[..10];
        await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban(ending), amount));
        var done = await Eventually.Until(() => _f.Get(Ports.NordPayAdapter, $"/api/v1/payments/E2E-{msg}"), x => x.Str("status") != "ACCP", 8000, "rechazo");
        Assert.Equal("RJCT", done.Str("status"));
        Assert.Equal(reason, done.Str("reasonCode"));
    }

    [Fact]
    public async Task Weekend_payments_wait_for_the_next_banking_day()
    {
        await _f.Freeze("2026-10-24T11:00:00");
        var msg = "MSG-" + Guid.NewGuid().ToString("N")[..10];
        await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban("1234")));
        await Task.Delay(1200);
        Assert.Equal("ACCP", (await _f.Get(Ports.NordPayAdapter, $"/api/v1/payments/E2E-{msg}")).Str("status"));
        await _f.Freeze("2026-10-26T09:00:00");
        var done = await Eventually.Until(() => _f.Get(Ports.NordPayAdapter, $"/api/v1/payments/E2E-{msg}"), x => x.Str("status") != "ACCP", 8000, "liquidación el lunes");
        Assert.Equal("ACSC", done.Str("status"));
    }

    [Fact]
    public async Task Incoming_credits_become_events_and_bad_signatures_are_rejected()
    {
        var reference = "OCR" + Random.Shared.Next(100000, 999999);
        var c = await _f.Post(Ports.NordPaySim, "/__admin/incoming-credits", $"{{\"debtorIban\":\"{TestData.Iban("1234")}\",\"debtorName\":\"Ana\",\"amount\":\"1500.00\",\"reference\":\"{reference}\"}}");
        Assert.Equal(201, c.Code);
        var ev = await TestData.Events(_f, "payment.credit.received", reference);
        Assert.Equal("1500.00", ev.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("amount").GetString());
        var fake = "{\"eventType\":\"CREDIT_NOTIFICATION\",\"creditId\":\"X\",\"reference\":\"R\",\"amount\":\"1.00\"}";
        (await _f.Send(HttpMethod.Post, Ports.NordPayAdapter, "/api/v1/webhooks/nordpay", fake, r => r.Headers.TryAddWithoutValidation("X-JWS-Signature", "a..b"))).ShouldBeProblem(401, "INVALID_SIGNATURE");
        var signed = await _f.Send(HttpMethod.Post, Ports.NordPayAdapter, "/api/v1/webhooks/nordpay", fake, r => r.Headers.TryAddWithoutValidation("X-JWS-Signature", Jws.SignDetached(fake, "nordpay-jws-secret-2026", "t")));
        Assert.Equal(200, signed.Code);
        var raw = await _f.Send(HttpMethod.Post, Ports.NordPaySim, "/nordpay/v1/payments", "{\"msgId\":\"x\"}", r => r.Headers.TryAddWithoutValidation("X-Api-Key", "nordpay-key-2026"));
        Assert.Equal("SIGNATURE_INVALID", raw.Str("code"));
    }

    [Fact]
    public async Task Bank_down_is_retryable()
    {
        await _f.Post(Ports.NordPaySim, "/__admin/behaviour", "{\"failNext\":1}");
        var msg = "MSG-" + Guid.NewGuid().ToString("N")[..10];
        (await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban("1234")))).ShouldBeProblem(503, "BANK_UNAVAILABLE");
        Assert.Equal(202, (await _f.Post(Ports.NordPayAdapter, "/api/v1/payments", Pay(msg, TestData.Iban("1234")))).Code);
    }
}
