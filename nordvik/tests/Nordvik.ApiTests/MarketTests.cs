using System.Net;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class ProductCatalogTests
{
    private readonly NordvikFixture _f;
    public ProductCatalogTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Lists_all_14_funds_by_default()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds");
        Assert.Equal(14, r.Int("total"));
        Assert.Equal(1, r.Int("page"));
        Assert.Equal(20, r.Int("page_size"));
        Assert.Equal(14, r.Json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Uses_snake_case_everywhere()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000001");
        Assert.True(r.Has("base_currency"));
        Assert.True(r.Has("launch_date"));
        Assert.False(r.Has("baseCurrency"));
    }

    [Theory]
    [InlineData("EQUITY", 7)]
    [InlineData("FIXED_INCOME", 3)]
    [InlineData("MIXED", 3)]
    [InlineData("MONEY_MARKET", 1)]
    public async Task Filters_by_category(string category, int expected)
    {
        var r = await _f.Get(Ports.Catalog, $"/api/v1/funds?category={category}");
        Assert.Equal(expected, r.Int("total"));
        Assert.All(r.Json.GetProperty("items").EnumerateArray(), i => Assert.Equal(category, i.GetProperty("category").GetString()));
    }

    [Fact]
    public async Task Equity_funds_have_sri_between_4_and_7()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?category=EQUITY");
        Assert.All(r.Json.GetProperty("items").EnumerateArray(), i => Assert.InRange(i.GetProperty("sri").GetInt32(), 4, 7));
    }

    [Theory]
    [InlineData("equity")]
    [InlineData("STOCKS")]
    public async Task Invalid_category_explains_valid_values(string category)
    {
        var r = await _f.Get(Ports.Catalog, $"/api/v1/funds?category={category}");
        r.ShouldBeProblem(400, "INVALID_QUERY");
        Assert.Equal("category", r.Json.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Sort_by_fee_ascending_starts_with_the_cheapest()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?sort=fee");
        var fees = r.Json.GetProperty("items").EnumerateArray().Select(i => decimal.Parse(i.GetProperty("management_fee").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(fees.OrderBy(x => x), fees);
        Assert.Equal(0.10m, fees[0]);
    }

    [Fact]
    public async Task Sort_by_sri_descending_starts_with_the_riskiest()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?sort=sri&order=desc");
        Assert.Equal(7, r.Json.GetProperty("items")[0].GetProperty("sri").GetInt32());
    }

    [Fact]
    public async Task Pagination_splits_the_list()
    {
        var p1 = await _f.Get(Ports.Catalog, "/api/v1/funds?page_size=5&page=1");
        var p3 = await _f.Get(Ports.Catalog, "/api/v1/funds?page_size=5&page=3");
        Assert.Equal(3, p1.Int("total_pages"));
        Assert.Equal(5, p1.Json.GetProperty("items").GetArrayLength());
        Assert.Equal(4, p3.Json.GetProperty("items").GetArrayLength());
        var p4 = await _f.Get(Ports.Catalog, "/api/v1/funds?page_size=5&page=4");
        Assert.Equal(0, p4.Json.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page_size=101", "page_size")]
    [InlineData("sort=precio", "sort")]
    [InlineData("order=up", "order")]
    public async Task Invalid_paging_and_sorting_are_rejected(string query, string field)
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?" + query);
        r.ShouldBeProblem(400, "INVALID_QUERY");
        Assert.Contains(r.Json.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
    }

    [Fact]
    public async Task Filters_combine_currency_and_risk()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?currency=USD&min_sri=7");
        Assert.Equal(2, r.Int("total"));
    }

    [Fact]
    public async Task Only_one_fund_is_closed()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds?status=CLOSED");
        Assert.Equal(1, r.Int("total"));
        Assert.Equal("SE0000000007", r.Json.GetProperty("items")[0].GetProperty("isin").GetString());
    }

    [Fact]
    public async Task Detail_has_three_fees()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000003");
        var fees = r.Json.GetProperty("fees").EnumerateArray().ToList();
        Assert.Equal(new[] { "MANAGEMENT", "ENTRY", "EXIT" }, fees.Select(x => x.GetProperty("type").GetString()));
        Assert.Equal("1.25", fees[0].GetProperty("percent").GetString());
    }

    [Fact]
    public async Task Fees_endpoint_matches_detail()
    {
        var detail = await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000201");
        var fees = await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000201/fees");
        Assert.Equal(detail.Json.GetProperty("fees").GetRawText(), fees.Json.GetProperty("fees").GetRawText());
    }

    [Theory]
    [InlineData("se0000000001")]
    [InlineData("SE00000000")]
    [InlineData("SE000000000X")]
    public async Task Bad_isin_format_is_400(string isin) =>
        (await _f.Get(Ports.Catalog, $"/api/v1/funds/{isin}")).ShouldBeProblem(400, "INVALID_ISIN");

    [Fact]
    public async Task Unknown_isin_is_404() =>
        (await _f.Get(Ports.Catalog, "/api/v1/funds/SE9999999999")).ShouldBeProblem(404, "FUND_NOT_FOUND");
}

[Collection("nordvik")]
public class MarketDataTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public MarketDataTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze("2026-10-26T12:00:00+01:00"); // lunes, antes de las 18:00
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Latest_before_18h_is_the_previous_trading_day()
    {
        var r = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest");
        Assert.Equal("2026-10-23", r.Str("date"));
        Assert.Matches(@"^\d+\.\d{4}$", r.Str("nav"));
        Assert.Equal("SEK", r.Str("currency"));
    }

    [Fact]
    public async Task Latest_after_18h_is_today()
    {
        await _f.Freeze("2026-10-26T18:00:00+01:00");
        var r = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest");
        Assert.Equal("2026-10-26", r.Str("date"));
    }

    [Fact]
    public async Task Price_on_a_date_is_stable()
    {
        var a = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000002?date=2026-03-02");
        var b = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000002?date=2026-03-02");
        Assert.Equal(a.Str("nav"), b.Str("nav"));
        Assert.Equal("2026-03-02T18:00:00.000+01:00", a.Str("published_at"));
    }

    [Fact]
    public async Task Published_at_uses_summer_time_in_summer()
    {
        var r = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000002?date=2026-07-01");
        Assert.Equal("2026-07-01T18:00:00.000+02:00", r.Str("published_at"));
    }

    [Fact]
    public async Task Holiday_has_no_price_and_suggests_previous_day()
    {
        var r = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2026-06-19");
        r.ShouldBeProblem(404, "NO_PRICE_FOR_DATE");
        Assert.Contains("Víspera de San Juan", r.Str("detail"));
        Assert.Contains("2026-06-18", r.Str("hint"));
    }

    [Fact]
    public async Task Weekend_has_no_price() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2026-10-17")).ShouldBeProblem(404, "NO_PRICE_FOR_DATE");

    [Fact]
    public async Task Future_price_is_not_yet_published() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2026-10-27")).ShouldBeProblem(404, "PRICE_NOT_YET_PUBLISHED");

    [Fact]
    public async Task Today_before_18h_is_not_yet_published() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2026-10-26")).ShouldBeProblem(404, "PRICE_NOT_YET_PUBLISHED");

    [Theory]
    [InlineData("26-10-2026")]
    [InlineData("2026-13-01")]
    public async Task Bad_dates_are_400(string date) =>
        (await _f.Get(Ports.Market, $"/api/v1/prices/SE0000000001?date={date}")).ShouldBeProblem(400, "INVALID_DATE");

    [Fact]
    public async Task Dates_before_2025_are_out_of_range() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001?date=2024-06-03")).ShouldBeProblem(404, "DATE_OUT_OF_RANGE");

    [Fact]
    public async Task Unknown_instrument_is_404() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE9999999999/latest")).ShouldBeProblem(404, "INSTRUMENT_NOT_FOUND");

    [Fact]
    public async Task Etag_allows_304_not_modified()
    {
        var first = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest");
        var etag = first.Headers.ETag!.Tag;
        var second = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/latest", req => req.Headers.TryAddWithoutValidation("If-None-Match", etag));
        Assert.Equal(HttpStatusCode.NotModified, second.Status);
        Assert.Equal("", second.Body);
    }

    [Fact]
    public async Task History_has_one_price_per_trading_day_matching_calendar_service()
    {
        var hist = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/history?from=2026-01-01&to=2026-03-31");
        var days = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/trading-days?from=2026-01-01&to=2026-03-31");
        Assert.Equal(days.Int("count"), hist.Int("count"));
        var histDates = hist.Json.GetProperty("prices").EnumerateArray().Select(p => p.GetProperty("date").GetString());
        var calDates = days.Json.GetProperty("days").EnumerateArray().Select(d => d.GetString());
        Assert.Equal(calDates, histDates);
    }

    [Fact]
    public async Task History_is_cut_at_the_last_published_day()
    {
        var r = await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/history?from=2026-10-01&to=2026-12-31");
        Assert.Equal("2026-10-23", r.Str("to"));
    }

    [Fact]
    public async Task History_longer_than_366_days_is_rejected() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/history?from=2025-01-02&to=2026-06-30")).ShouldBeProblem(400, "RANGE_TOO_LARGE");

    [Fact]
    public async Task History_with_reversed_dates_is_rejected() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/history?from=2026-05-01&to=2026-04-01")).ShouldBeProblem(400, "INVALID_RANGE");

    [Fact]
    public async Task History_requires_from() =>
        (await _f.Get(Ports.Market, "/api/v1/prices/SE0000000001/history")).ShouldBeProblem(400, "INVALID_DATE");
}

[Collection("nordvik")]
public class FxRatesTests
{
    private readonly NordvikFixture _f;
    public FxRatesTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Six_currencies_are_supported()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/currencies");
        Assert.Equal(new[] { "DKK", "EUR", "GBP", "NOK", "SEK", "USD" }, r.Json.EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Rate_table_has_the_five_other_currencies()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/rates?base=EUR&date=2026-10-23");
        Assert.Equal(5, r.Json.GetProperty("rates").EnumerateObject().Count());
        Assert.False(r.Json.GetProperty("rates").TryGetProperty("EUR", out _));
    }

    [Fact]
    public async Task Weekend_uses_the_previous_trading_day_rate()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/rates/EUR/SEK?date=2026-10-25");
        Assert.Equal("2026-10-25", r.Str("requestedDate"));
        Assert.Equal("2026-10-23", r.Str("rateDate"));
        var friday = await _f.Get(Ports.Fx, "/api/v1/rates/EUR/SEK?date=2026-10-23");
        Assert.Equal(friday.Str("rate"), r.Str("rate"));
    }

    [Fact]
    public async Task Inverse_rates_multiply_to_one()
    {
        var a = decimal.Parse((await _f.Get(Ports.Fx, "/api/v1/rates/EUR/SEK?date=2026-10-23")).Str("rate"), System.Globalization.CultureInfo.InvariantCulture);
        var b = decimal.Parse((await _f.Get(Ports.Fx, "/api/v1/rates/SEK/EUR?date=2026-10-23")).Str("rate"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(a * b, 0.9999m, 1.0001m);
    }

    [Fact]
    public async Task Convert_rounds_to_two_decimals()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/convert?from=EUR&to=SEK&amount=100.00&date=2026-10-23");
        Assert.Matches(@"^\d+\.\d{2}$", r.Str("converted"));
        Assert.Equal("100.00", r.Str("amount"));
        var rate = decimal.Parse(r.Str("rate"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Math.Round(100m * rate, 2, MidpointRounding.ToEven).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), r.Str("converted"));
    }

    [Fact]
    public async Task Same_currency_conversion_is_identity()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/convert?from=SEK&to=SEK&amount=1234.56");
        Assert.Equal("1234.56", r.Str("converted"));
        Assert.Equal("1.000000", r.Str("rate"));
    }

    [Fact]
    public async Task Lowercase_currency_codes_are_accepted()
    {
        var r = await _f.Get(Ports.Fx, "/api/v1/rates/eur/sek?date=2026-10-23");
        Assert.Equal("EUR", r.Str("from"));
    }

    [Theory]
    [InlineData("100,00")]
    [InlineData("-5")]
    [InlineData("cien")]
    public async Task Invalid_amounts_are_rejected(string amount) =>
        (await _f.Get(Ports.Fx, $"/api/v1/convert?from=EUR&to=SEK&amount={amount}")).ShouldBeProblem(400, "INVALID_AMOUNT");

    [Theory]
    [InlineData("/api/v1/rates?base=XXX")]
    [InlineData("/api/v1/rates/EUR/JPY")]
    [InlineData("/api/v1/convert?from=BTC&to=SEK&amount=1.00")]
    public async Task Unsupported_currencies_are_rejected(string path) =>
        (await _f.Get(Ports.Fx, path)).ShouldBeProblem(400, "UNSUPPORTED_CURRENCY");

    [Fact]
    public async Task Missing_parameters_are_explained() =>
        (await _f.Get(Ports.Fx, "/api/v1/convert?from=EUR")).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Dates_out_of_range_are_404() =>
        (await _f.Get(Ports.Fx, "/api/v1/rates?date=2019-05-05")).ShouldBeProblem(404, "DATE_OUT_OF_RANGE");
}

[Collection("nordvik")]
public class CalendarServiceTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public CalendarServiceTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Four_nordic_markets()
    {
        var r = await _f.Get(Ports.Calendar, "/api/v1/markets");
        Assert.Equal(new[] { "XSTO", "XOSL", "XCSE", "XHEL" }, r.Json.EnumerateArray().Select(m => m.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Stockholm_holidays_2026()
    {
        var r = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/holidays?year=2026");
        Assert.Equal(12, r.Int("count"));
        Assert.Contains(r.Json.GetProperty("holidays").EnumerateArray(), h => h.GetProperty("date").GetString() == "2026-06-19");
    }

    [Theory]
    [InlineData("2026-12-25", false, "HOLIDAY")]
    [InlineData("2026-10-24", false, "WEEKEND")]
    [InlineData("2026-10-26", true, null)]
    public async Task Is_trading_day(string date, bool trading, string? reason)
    {
        var r = await _f.Get(Ports.Calendar, $"/api/v1/calendar/XSTO/is-trading-day?date={date}");
        Assert.Equal(trading, r.Json.GetProperty("tradingDay").GetBoolean());
        var actual = r.Json.GetProperty("reason");
        if (reason is null) Assert.Equal(System.Text.Json.JsonValueKind.Null, actual.ValueKind);
        else Assert.Equal(reason, actual.GetString());
    }

    [Fact]
    public async Task Holiday_name_is_given()
    {
        var r = await _f.Get(Ports.Calendar, "/api/v1/calendar/XOSL/is-trading-day?date=2027-05-17");
        Assert.Equal("Día de la Constitución de Noruega", r.Str("holidayName"));
    }

    [Theory]
    [InlineData("2026-10-23", 2, "2026-10-27")]
    [InlineData("2026-12-23", 1, "2026-12-28")]
    [InlineData("2026-10-26", -1, "2026-10-23")]
    public async Task Next_trading_day_t_plus_n(string from, int offset, string expected)
    {
        var r = await _f.Get(Ports.Calendar, $"/api/v1/calendar/XSTO/next-trading-day?date={from}&offset={offset}");
        Assert.Equal(expected, r.Str("date"));
    }

    [Fact]
    public async Task Offset_is_limited() =>
        (await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/next-trading-day?offset=31")).ShouldBeProblem(400, "OFFSET_OUT_OF_RANGE");

    [Fact]
    public async Task Trading_days_in_december_2026()
    {
        var r = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/trading-days?from=2026-12-01&to=2026-12-31");
        Assert.Equal(20, r.Int("count"));
    }

    [Theory]
    [InlineData("xsto")]
    [InlineData("NYSE")]
    public async Task Unknown_market_is_404(string market) =>
        (await _f.Get(Ports.Calendar, $"/api/v1/calendar/{market}/holidays")).ShouldBeProblem(404, "MARKET_NOT_FOUND");

    [Fact]
    public async Task Year_out_of_range_is_404() =>
        (await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/holidays?year=2019")).ShouldBeProblem(404, "YEAR_OUT_OF_RANGE");

    [Fact]
    public async Task Trading_days_range_is_limited() =>
        (await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/trading-days?from=2025-01-01&to=2026-12-31")).ShouldBeProblem(400, "RANGE_TOO_LARGE");

    [Fact]
    public async Task Today_follows_the_virtual_clock_and_the_cut_off()
    {
        await _f.Freeze("2026-10-26T14:59:59+01:00");
        var before = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/today");
        Assert.True(before.Json.GetProperty("beforeCutOff").GetBoolean());
        await _f.Freeze("2026-10-26T15:00:00+01:00");
        var after = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/today");
        Assert.False(after.Json.GetProperty("beforeCutOff").GetBoolean());
        Assert.Equal("2026-10-27", after.Str("nextTradingDay"));
    }

    [Fact]
    public async Task Today_on_christmas_is_not_a_trading_day()
    {
        await _f.Freeze("2026-12-25T10:00:00+01:00");
        var r = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/today");
        Assert.False(r.Json.GetProperty("tradingDay").GetBoolean());
        Assert.False(r.Json.GetProperty("beforeCutOff").GetBoolean());
        Assert.Equal("2026-12-28", r.Str("nextTradingDay"));
    }
}
