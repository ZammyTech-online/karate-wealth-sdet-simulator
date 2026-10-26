using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>
/// Las trampas ACTIVABLES del catálogo de chaos-control (T1-T5, T7-T9, T15, T19, T20): cada test comprueba
/// el comportamiento correcto con la trampa apagada y el fallo concreto que aparece al encenderla.
/// </summary>
[Collection("nordvik")]
public class TrapTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public TrapTests(NordvikFixture f) => _f = f;
    public async Task InitializeAsync() { await _f.Post(Ports.Chaos, "/api/v1/reset"); await _f.Freeze($"{TestData.Tuesday}T10:00:00"); }
    public async Task DisposeAsync()
    {
        await _f.Post(Ports.Chaos, "/api/v1/reset");
        await _f.Post(Ports.Relay, "/api/v1/relay/resume");
        await _f.Post(Ports.CustodianSim, "/__admin/behaviour/reset");
        await _f.ResetClock();
    }

    private async Task Trap(string id, bool on = true)
    {
        var r = await _f.Send(HttpMethod.Put, Ports.Chaos, $"/api/v1/traps/{id}", on ? "{\"enabled\":true}" : "{\"enabled\":false}");
        Assert.True(r.Code == 200, r.Body);
        Assert.Equal(on, Chaos.TrapOn(id));
    }

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public async Task T1_custodian_rounds_half_up_instead_of_truncating()
    {
        await Trap("T1");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "20000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        var o = await Flows.Buy(_f, pf, "SE0000000003", "10000.00");
        var executed = await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday);
        var nav = D((await _f.Get(Ports.Market, $"/api/v1/prices/SE0000000003?date={TestData.Tuesday}")).Str("nav"));
        Assert.Equal(Money.Format(Math.Round(9900m / nav, 4, MidpointRounding.AwayFromZero), 4), executed.Str("executedUnits"));
    }

    [Fact]
    public async Task T2_cut_off_applies_the_stockholm_offset_twice()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        await _f.Freeze($"{TestData.Tuesday}T14:30:00");
        var before = await Flows.Buy(_f, pf, "SE0000000001", "200.00");
        Assert.True(before.Code == 201, before.Body);
        Assert.Equal(TestData.Tuesday, before.Str("tradeDate"));
        Assert.False(before.Json.GetProperty("cutOffApplied").GetBoolean());
        await Trap("T2");
        var after = await Flows.Buy(_f, pf, "SE0000000001", "200.00");
        Assert.True(after.Code == 201, after.Body);
        Assert.Equal("2026-10-21", after.Str("tradeDate")); // 14:30 + 2 h = 16:30, ya pasadas las 15:00
        Assert.True(after.Json.GetProperty("cutOffApplied").GetBoolean());
        var erik = await _f.Erik();
        foreach (var o in new[] { before, after })
            await _f.Send(HttpMethod.Post, Ports.Orders, $"/api/v1/orders/{o.Str("id")}/cancel", null, NordvikFixture.Bearer(erik));
    }

    [Fact]
    public async Task T3_summer_time_is_ignored()
    {
        var july = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var january = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(2), Nordic.ToStockholm(july).Offset);
        await Trap("T3");
        Assert.Equal(TimeSpan.FromHours(1), Nordic.ToStockholm(july).Offset);
        Assert.Equal(13, Nordic.ToStockholm(july).Hour); // debería ser 14:00
        Assert.Equal(TimeSpan.FromHours(1), Nordic.ToStockholm(january).Offset); // en invierno coincide: por eso cuesta verla
        await Trap("T3", false);
        Assert.Equal(14, Nordic.ToStockholm(july).Hour);
    }

    [Fact]
    public async Task T4_custodian_sends_every_webhook_twice()
    {
        var stats0 = (await _f.Get(Ports.CustodyAdapter, "/api/v1/webhooks/stats")).Json.GetProperty("duplicates").GetInt64();
        await Trap("T4");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "2000.00");
        var pf = await Flows.Portfolio(_f, customerId);
        var o = await Flows.Buy(_f, pf, "SE0000000003", "1000.00");
        var executed = await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday);
        Assert.Equal("EXECUTED", executed.Str("status")); // el adaptador es idempotente: el duplicado no ejecuta dos veces
        await Eventually.Until(() => _f.Get(Ports.CustodyAdapter, "/api/v1/webhooks/stats"),
            r => r.Json.GetProperty("duplicates").GetInt64() > stats0, 8000, "webhook duplicado detectado");
        var exec = await _f.Get(Ports.Executions, $"/api/v1/executions?orderId={o.Str("id")}");
        Assert.Equal(1, exec.Json.GetArrayLength());
    }

    [Fact]
    public async Task T5_relay_publishes_a_batch_in_reverse_order()
    {
        var a = await People.Clean(_f);
        var b = await People.Clean(_f);
        await _f.Post(Ports.Relay, "/api/v1/relay/pause");
        await People.SwedishTaxResidence(_f, a);
        await People.SwedishTaxResidence(_f, b);
        await Trap("T5");
        await _f.Post(Ports.Relay, "/api/v1/relay/flush");
        await _f.Post(Ports.Relay, "/api/v1/relay/resume");
        long Seq(Reply r) => r.Json.GetProperty("items")[0].GetProperty("seq").GetInt64();
        var ea = await TestData.Events(_f, "tax-residency.changed", a);
        var eb = await TestData.Events(_f, "tax-residency.changed", b);
        Assert.True(Seq(eb) < Seq(ea), "con T5 el evento posterior llega antes al bus");
        Assert.True(string.CompareOrdinal(ea.Json.GetProperty("items")[0].GetProperty("occurredAt").GetString(),
            eb.Json.GetProperty("items")[0].GetProperty("occurredAt").GetString()) <= 0, "pero occurredAt sigue diciendo el orden real");
    }

    [Fact]
    public async Task T7_cache_answers_304_to_yesterdays_etag()
    {
        var first = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest");
        var tag = first.Headers.ETag!.Tag;
        var date = DateOnly.ParseExact(tag.Trim('"')[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stale = $"\"SE0000000001-{Money.Date(NordicCalendar.AddTradingDays("XSTO", date, -1))}\"";
        var ok = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest", req => req.Headers.TryAddWithoutValidation("If-None-Match", stale));
        Assert.Equal(200, ok.Code);
        await Trap("T7");
        var bad = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest", req => req.Headers.TryAddWithoutValidation("If-None-Match", stale));
        Assert.Equal(304, bad.Code);
        Assert.Equal(stale, bad.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task T8_search_order_is_not_stable_between_pages()
    {
        var c = await _f.NewCustomer();
        var prefix = c.GetProperty("customerNumber").GetString()![..3];
        var erik = await _f.Erik();
        static bool Sorted(Reply r)
        {
            var n = r.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("customerNumber").GetString()!).ToList();
            return n.SequenceEqual(n.OrderBy(x => x, StringComparer.Ordinal));
        }
        var normal = await _f.Get(Ports.Search, $"/api/v1/search/customers?q={prefix}&pageSize=20", NordvikFixture.Bearer(erik));
        Assert.True(normal.Int("total") >= 5, normal.Body);
        Assert.True(Sorted(normal));
        await Trap("T8");
        var unsorted = 0;
        for (var i = 0; i < 4; i++)
            if (!Sorted(await _f.Get(Ports.Search, $"/api/v1/search/customers?q={prefix}&pageSize=20", NordvikFixture.Bearer(erik)))) unsorted++;
        Assert.True(unsorted >= 1, "con T8 el orden cambia entre peticiones");
    }

    [Fact]
    public async Task T15_conflict_detail_leaks_the_national_id()
    {
        var p = await _f.NewPerson();
        var nid = p.GetProperty("nationalId").GetString()!;
        var body = JsonSerializer.Serialize(new { firstName = "Doble", lastName = "Fuga", nationalId = nid });
        var erik = await _f.Erik();
        Assert.Equal(201, (await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(erik))).Code);
        var clean = await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(erik));
        clean.ShouldBeProblem(409, "CUSTOMER_ALREADY_EXISTS");
        var digits = NationalId.Check(nid).Normalized!;
        Assert.DoesNotContain(digits, clean.Body);
        await Trap("T15");
        var leak = await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(erik));
        leak.ShouldBeProblem(409, "CUSTOMER_ALREADY_EXISTS");
        Assert.Contains(digits, leak.Str("detail"));
    }

    [Fact]
    public async Task T19_custodian_takes_eight_seconds_to_accept_a_batch()
    {
        var sw = Stopwatch.StartNew();
        Assert.Equal(401, (await _f.Post(Ports.CustodianSim, "/custody/v2/batches", "{}")).Code);
        Assert.True(sw.ElapsedMilliseconds < 3000);
        await Trap("T19");
        sw.Restart();
        Assert.Equal(401, (await _f.Post(Ports.CustodianSim, "/custody/v2/batches", "{}")).Code);
        Assert.True(sw.ElapsedMilliseconds >= 7500, $"tardó {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task T20_valuation_skips_the_fx_conversion()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "20000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000004\",\"percent\":100}]");
        var o = await Flows.Buy(_f, pf, "SE0000000004", "10000.00");
        Assert.Equal("EXECUTED", (await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday)).Str("status"));
        var erik = await _f.Erik();
        var path = $"/api/v1/portfolios/{pf}/valuation";
        var right = (await Eventually.Until(() => _f.Get(Ports.Valuation, path, NordvikFixture.Bearer(erik)),
            r => r.Code == 200 && r.Json.GetProperty("lines").GetArrayLength() == 1, 8000, "valoración")).Json.GetProperty("lines")[0];
        Assert.Equal("USD", right.GetProperty("fundCurrency").GetString());
        Assert.NotEqual("1.000000", right.GetProperty("fxRate").GetString());
        await Trap("T20");
        var wrong = (await _f.Get(Ports.Valuation, path, NordvikFixture.Bearer(erik))).Json.GetProperty("lines")[0];
        Assert.Equal("1.000000", wrong.GetProperty("fxRate").GetString());
        var units = D(wrong.GetProperty("units").GetString()!); var nav = D(wrong.GetProperty("nav").GetString()!);
        Assert.Equal(Money.Format(Math.Round(units * nav, 2, MidpointRounding.ToEven), 2), wrong.GetProperty("valueSek").GetString());
        Assert.True(D(wrong.GetProperty("valueSek").GetString()!) < D(right.GetProperty("valueSek").GetString()!) / 5, "un USD vale ~10 SEK");
    }
}
