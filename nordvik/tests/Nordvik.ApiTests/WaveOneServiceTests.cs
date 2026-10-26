using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class TestDataServiceTests
{
    private readonly NordvikFixture _f;
    public TestDataServiceTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Persons_are_reproducible_with_a_seed()
    {
        var a = await _f.Post(Ports.Person, "/api/v1/persons", "{\"count\":3,\"seed\":42}");
        var b = await _f.Post(Ports.Person, "/api/v1/persons", "{\"count\":3,\"seed\":42}");
        Assert.Equal(a.Body, b.Body);
        Assert.Equal(3, a.Json.GetProperty("persons").GetArrayLength());
        Assert.Equal(42, a.Int("seed"));
    }

    [Fact]
    public async Task Generated_national_ids_are_valid_and_match_age_and_gender()
    {
        var r = await _f.Post(Ports.Person, "/api/v1/persons", "{\"age\":67,\"gender\":\"F\",\"count\":5}");
        foreach (var p in r.Json.GetProperty("persons").EnumerateArray())
        {
            Assert.Equal(67, p.GetProperty("age").GetInt32());
            Assert.Equal("F", p.GetProperty("gender").GetString());
            Assert.Matches(@"^\d{8}-\d{4}$", p.GetProperty("nationalId").GetString());
            var check = await _f.Get(Ports.Person, "/api/v1/national-ids/validate?value=" + p.GetProperty("nationalId").GetString());
            Assert.True(check.Json.GetProperty("valid").GetBoolean());
            Assert.Equal("F", check.Str("gender"));
            Assert.Equal(p.GetProperty("birthDate").GetString(), check.Str("birthDate"));
            Assert.EndsWith("@correo.nordvik.test", p.GetProperty("email").GetString());
        }
    }

    [Fact]
    public async Task Person_request_validation_lists_every_field()
    {
        var r = await _f.Post(Ports.Person, "/api/v1/persons", "{\"country\":\"NO\",\"minAge\":50,\"maxAge\":40,\"gender\":\"X\",\"count\":99}");
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        var fields = r.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "country", "minAge", "gender", "count" }, fields);
    }

    [Theory]
    [InlineData("19850314-1234", false)]
    [InlineData("not-a-number", false)]
    public async Task Bad_national_ids_say_why(string value, bool valid)
    {
        var r = await _f.Get(Ports.Person, "/api/v1/national-ids/validate?value=" + value);
        Assert.Equal(valid, r.Json.GetProperty("valid").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(r.Str("reason")));
    }

    [Fact]
    public async Task Addresses_respect_country_city_and_count()
    {
        var r = await _f.Get(Ports.Address, "/api/v1/addresses?country=se&city=Göteborg&count=4&seed=7");
        Assert.Equal(4, r.Json.GetProperty("addresses").GetArrayLength());
        foreach (var a in r.Json.GetProperty("addresses").EnumerateArray())
        {
            Assert.Equal("Göteborg", a.GetProperty("city").GetString());
            Assert.Matches(@"^\d{3} \d{2}$", a.GetProperty("postalCode").GetString());
        }
        (await _f.Get(Ports.Address, "/api/v1/addresses?country=ES")).ShouldBeProblem(400, "UNSUPPORTED_COUNTRY");
        (await _f.Get(Ports.Address, "/api/v1/addresses?country=SE&city=Madrid")).ShouldBeProblem(404, "CITY_NOT_FOUND");
        (await _f.Get(Ports.Address, "/api/v1/addresses?count=101")).ShouldBeProblem(400, "COUNT_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData("SE", "111 51", true, "Stockholm")]
    [InlineData("SE", "11151", false, null)]
    [InlineData("NO", "0150", true, "Oslo")]
    public async Task Postal_codes_are_checked(string country, string code, bool valid, string? city)
    {
        var r = await _f.Get(Ports.Address, $"/api/v1/postal-codes/{country}/{Uri.EscapeDataString(code)}");
        Assert.Equal(valid, r.Json.GetProperty("valid").GetBoolean());
        if (city is not null) Assert.Equal(city, r.Str("city"));
    }

    [Fact]
    public async Task Cities_can_be_filtered_by_country()
    {
        var r = await _f.Get(Ports.Address, "/api/v1/cities?country=FI");
        Assert.True(r.Json.GetArrayLength() >= 1);
        Assert.All(r.Json.EnumerateArray(), c => Assert.Equal("FI", c.GetProperty("country").GetString()));
    }

    [Theory]
    [InlineData("SE", 24)]
    [InlineData("NO", 15)]
    [InlineData("DK", 18)]
    [InlineData("FI", 18)]
    public async Task Generated_ibans_validate(string country, int length)
    {
        var created = await _f.Post(Ports.Iban, "/api/v1/ibans", $"{{\"country\":\"{country}\"}}");
        Assert.Equal(201, created.Code);
        var iban = created.Str("electronic");
        Assert.Equal(length, iban.Length);
        Assert.Equal(iban, created.Str("print").Replace(" ", ""));
        var v = await _f.Post(Ports.Iban, "/api/v1/ibans/validate", $"{{\"iban\":\"{created.Str("print")}\"}}");
        Assert.True(v.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(created.Str("bic"), v.Str("bic"));
    }

    [Fact]
    public async Task Ibans_with_a_wrong_digit_fail_mod97()
    {
        var iban = (await _f.Post(Ports.Iban, "/api/v1/ibans", "{\"country\":\"SE\",\"bankCode\":\"300\",\"seed\":1}")).Str("electronic");
        var last = iban[^1] == '9' ? '0' : (char)(iban[^1] + 1);
        var v = await _f.Post(Ports.Iban, "/api/v1/ibans/validate", $"{{\"iban\":\"{iban[..^1]}{last}\"}}");
        Assert.False(v.Json.GetProperty("valid").GetBoolean());
        Assert.Contains("mod 97", v.Str("reason"));
        var shortOne = await _f.Post(Ports.Iban, "/api/v1/ibans/validate", "{\"iban\":\"SE12 3456\"}");
        Assert.Contains("24 caracteres", shortOne.Str("reason"));
        (await _f.Post(Ports.Iban, "/api/v1/ibans/validate", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.Iban, "/api/v1/ibans", "{\"country\":\"SE\",\"bankCode\":\"999\"}")).ShouldBeProblem(404, "BANK_NOT_FOUND");
    }

    [Fact]
    public async Task Isins_are_generated_and_validated_with_luhn()
    {
        var c = await _f.Post(Ports.Isin, "/api/v1/isins", "{\"country\":\"NO\"}");
        Assert.Equal(201, c.Code);
        Assert.Matches("^NO[A-Z0-9]{9}[0-9]$", c.Str("isin"));
        Assert.True((await _f.Post(Ports.Isin, "/api/v1/isins/validate", $"{{\"isin\":\"{c.Str("isin")}\"}}")).Json.GetProperty("valid").GetBoolean());
        var apple = await _f.Post(Ports.Isin, "/api/v1/isins/validate", "{\"isin\":\"US0378331005\"}");
        Assert.True(apple.Json.GetProperty("valid").GetBoolean());
        var wrong = await _f.Post(Ports.Isin, "/api/v1/isins/validate", "{\"isin\":\"US0378331006\"}");
        Assert.True(wrong.Json.GetProperty("formatOk").GetBoolean());
        Assert.False(wrong.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(5, wrong.Int("expectedCheckDigit"));
        var lower = await _f.Post(Ports.Isin, "/api/v1/isins/validate", "{\"isin\":\"us0378331005\"}");
        Assert.False(lower.Json.GetProperty("formatOk").GetBoolean());
        (await _f.Post(Ports.Isin, "/api/v1/isins", "{\"country\":\"Suecia\"}")).ShouldBeProblem(400, "INVALID_COUNTRY");
    }

    [Fact]
    public async Task Fixture_loader_builds_a_customer_with_portfolio_and_reset_removes_it()
    {
        var list = await _f.Get(Ports.Fixture, "/api/v1/fixtures");
        Assert.Equal(3, list.Json.GetArrayLength());
        var prefix = "RUN-FX-" + Guid.NewGuid().ToString("N")[..6];
        var r = await _f.Post(Ports.Fixture, "/api/v1/fixtures/cliente-con-cartera", $"{{\"externalRef\":\"{prefix}-1\"}}");
        Assert.True(r.Code == 201, r.Body);
        var created = r.Json.GetProperty("created");
        var customerId = created.GetProperty("customer").GetProperty("id").GetString();
        var portfolio = created.GetProperty("portfolio");
        Assert.Equal(customerId, portfolio.GetProperty("customerId").GetString());
        Assert.Equal(2, portfolio.GetProperty("targetAllocation").GetArrayLength());
        Assert.True(r.Json.GetProperty("steps").GetArrayLength() >= 4);
        var report = await _f.Delete(Ports.Reset, $"/api/v1/runs/{prefix}");
        Assert.True(report.Int("totalDeleted") >= 2, report.Body);
        (await _f.Post(Ports.Fixture, "/api/v1/fixtures/no-existe", "{}")).ShouldBeProblem(404, "FIXTURE_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class MarketWaveOneTests
{
    private readonly NordvikFixture _f;
    public MarketWaveOneTests(NordvikFixture f) => _f = f;

    [Theory]
    [InlineData("es", "Cerrado a nuevas suscripciones.")]
    [InlineData("en", "Closed to new subscriptions.")]
    [InlineData("sv", "Stängd för nya teckningar.")]
    public async Task Factsheet_of_a_closed_fund_warns_in_each_language(string lang, string warning)
    {
        var r = await _f.Get(Ports.Factsheet, $"/api/v1/factsheets/SE0000000007?lang={lang}");
        Assert.Equal(200, r.Code);
        Assert.Equal(warning, r.Str("warning"));
        Assert.Equal(lang, r.Str("language"));
        Assert.True(r.Has("risk_text"));
        Assert.Equal(7, r.Int("sri"));
    }

    [Fact]
    public async Task Factsheet_uses_snake_case_and_has_performance()
    {
        var r = await _f.Get(Ports.Factsheet, "/api/v1/factsheets/SE0000000001");
        Assert.Equal("es", r.Str("language"));
        Assert.Equal(JsonValueKind.Null, r.Json.GetProperty("warning").ValueKind);
        var perf = r.Json.GetProperty("performance");
        Assert.True(perf.TryGetProperty("one_year", out _));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", perf.GetProperty("as_of").GetString());
        Assert.Contains(r.Json.GetProperty("fees").EnumerateArray(), f => f.GetProperty("type").GetString() == "MANAGEMENT" && f.GetProperty("percent").GetString() == "0.45");
        (await _f.Get(Ports.Factsheet, "/api/v1/factsheets/SE0000000001?lang=fr")).ShouldBeProblem(400, "UNSUPPORTED_LANGUAGE");
        Assert.Equal(404, (await _f.Get(Ports.Factsheet, "/api/v1/factsheets/SE9999999999")).Code);
    }

    [Fact]
    public async Task Buy_fee_uses_entry_percent()
    {
        var r = await _f.Post(Ports.Fee, "/api/v1/fees/calculate", "{\"isin\":\"SE0000000003\",\"amount\":\"10000.00\",\"action\":\"buy\"}");
        Assert.Equal("100.00", r.Str("fee"));
        Assert.Equal("9900.00", r.Str("net_amount"));
        Assert.Equal("1.00", r.Str("fee_percent"));
        Assert.Equal("SEK", r.Str("currency"));
        Assert.Equal(0, r.Json.GetProperty("projection").GetArrayLength());
    }

    [Fact]
    public async Task Hold_projects_management_cost_per_year()
    {
        var r = await _f.Post(Ports.Fee, "/api/v1/fees/calculate", "{\"isin\":\"SE0000000001\",\"amount\":\"10000\",\"action\":\"HOLD\",\"years\":3}");
        var rows = r.Json.GetProperty("projection");
        Assert.Equal(3, rows.GetArrayLength());
        Assert.Equal("45.00", rows[0].GetProperty("cost").GetString());
        Assert.Equal("135.00", rows[2].GetProperty("cumulative").GetString());
        Assert.Equal("135.00", r.Str("fee"));
    }

    [Fact]
    public async Task Fee_errors()
    {
        (await _f.Post(Ports.Fee, "/api/v1/fees/calculate", "{\"isin\":\"SE0000000007\",\"amount\":\"500.00\",\"action\":\"BUY\"}")).ShouldBeProblem(422, "FUND_CLOSED");
        Assert.Equal(200, (await _f.Post(Ports.Fee, "/api/v1/fees/calculate", "{\"isin\":\"SE0000000007\",\"amount\":\"500.00\",\"action\":\"SELL\"}")).Code);
        var bad = await _f.Post(Ports.Fee, "/api/v1/fees/calculate", "{\"amount\":\"10,5\",\"action\":\"SWAP\"}");
        bad.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(3, bad.Json.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Benchmarks_levels_and_returns()
    {
        var all = await _f.Get(Ports.Benchmark, "/api/v1/benchmarks");
        Assert.Equal(4, all.Json.GetArrayLength());
        var levels = await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-SE/levels?from=2026-01-05&to=2026-01-09");
        Assert.Equal(levels.Json.GetProperty("levels").GetArrayLength(), levels.Int("count"));
        Assert.Equal(4, levels.Int("count")); // 6 de enero (Trettondedag jul) no hay mercado en Estocolmo
        var ret = await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-SE/return?from=2026-01-05&to=2026-01-09");
        var expected = (decimal.Parse(ret.Str("endLevel"), Money.Inv) / decimal.Parse(ret.Str("startLevel"), Money.Inv) - 1) * 100;
        Assert.Equal(Money.Format(expected, 2), ret.Str("returnPercent"));
    }

    [Fact]
    public async Task Benchmark_errors()
    {
        (await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-XX/levels?from=2026-01-05&to=2026-01-09")).ShouldBeProblem(404, "BENCHMARK_NOT_FOUND");
        (await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-SE/levels")).ShouldBeProblem(400, "INVALID_DATE");
        (await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-SE/levels?from=2026-02-01&to=2026-01-01")).ShouldBeProblem(400, "INVALID_RANGE");
        (await _f.Get(Ports.Benchmark, "/api/v1/benchmarks/NVX-SE/levels?from=2024-02-01&to=2026-01-01")).ShouldBeProblem(404, "DATE_OUT_OF_RANGE");
        (await _f.Get(Ports.Benchmark, "/api/v1/compare?isin=SE0000000002")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Compare_fund_against_benchmark_is_consistent()
    {
        var r = await _f.Get(Ports.Benchmark, "/api/v1/compare?isin=SE0000000002&benchmark=NVX-SE&from=2026-01-02&to=2026-06-30");
        Assert.True(r.Code == 200, r.Body);
        var fund = decimal.Parse(r.Str("fundReturnPercent"), Money.Inv);
        var bench = decimal.Parse(r.Str("benchmarkReturnPercent"), Money.Inv);
        Assert.Equal(fund > bench, r.Json.GetProperty("beatBenchmark").GetBoolean());
    }
}

[Collection("nordvik")]
public class InvestmentWaveOneTests
{
    private readonly NordvikFixture _f;
    public InvestmentWaveOneTests(NordvikFixture f) => _f = f;

    private static string Body(string customerId, string name, string allocation, string type = "ISK") =>
        $"{{\"customerId\":\"{customerId}\",\"name\":\"{name}\",\"type\":\"{type}\",\"targetAllocation\":{allocation}}}";

    private async Task<Reply> Create(string allocation, string? customerId = null, string? token = null)
    {
        var name = "Cartera " + Guid.NewGuid().ToString("N")[..6];
        return await _f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios", Body(customerId ?? SeedIds.CustomerAna, name, allocation), NordvikFixture.Bearer(token ?? await _f.Erik()));
    }

    [Fact]
    public async Task Advisor_creates_a_portfolio_and_customer_reads_it()
    {
        var r = await Create("[{\"isin\":\"SE0000000001\",\"percent\":33.33},{\"isin\":\"SE0000000002\",\"percent\":33.33},{\"isin\":\"SE0000000101\",\"percent\":33.34}]");
        Assert.True(r.Code == 201, r.Body);
        Assert.Equal("\"1\"", r.Headers.ETag?.Tag);
        Assert.Equal("ACTIVE", r.Str("status"));
        var id = r.Str("id");
        var mine = await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{id}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(200, mine.Code);
        var list = await _f.Get(Ports.Portfolio, $"/api/v1/customers/{SeedIds.CustomerAna}/portfolios", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Contains(list.Json.EnumerateArray(), p => p.GetProperty("id").GetString() == id);
    }

    [Theory]
    [InlineData("[{\"isin\":\"SE0000000001\",\"percent\":60},{\"isin\":\"SE0000000101\",\"percent\":30}]", "ALLOCATION_NOT_100")]
    [InlineData("[{\"isin\":\"SE0000000001\",\"percent\":50},{\"isin\":\"SE0000000001\",\"percent\":50}]", "DUPLICATE_FUND")]
    [InlineData("[{\"isin\":\"SE0000000001\",\"percent\":100.001}]", "INVALID_PERCENT")]
    [InlineData("[{\"isin\":\"SE0000000001\",\"percent\":-10},{\"isin\":\"SE0000000101\",\"percent\":110}]", "INVALID_PERCENT")]
    [InlineData("[{\"isin\":\"SE9999999999\",\"percent\":100}]", "UNKNOWN_FUND")]
    [InlineData("[{\"isin\":\"SE0000000007\",\"percent\":100}]", "FUND_CLOSED")]
    public async Task Allocation_rules(string allocation, string code) => (await Create(allocation)).ShouldBeProblem(422, code);

    [Fact]
    public async Task More_than_ten_funds_is_rejected()
    {
        var isins = new[] { "001", "002", "003", "004", "005", "006", "101", "102", "103", "201", "202" };
        var json = "[" + string.Join(",", isins.Select((s, i) => $"{{\"isin\":\"SE0000000{s}\",\"percent\":{(i == 0 ? 10 : 9)}}}")) + "]";
        (await Create(json)).ShouldBeProblem(422, "TOO_MANY_FUNDS");
    }

    [Fact]
    public async Task Missing_fields_and_allocation()
    {
        var r = await _f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios", "{\"type\":\"PENSION\"}", NordvikFixture.Bearer(await _f.Erik()));
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(3, r.Json.GetProperty("errors").GetArrayLength());
        var noLines = await _f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios", Body(SeedIds.CustomerAna, "Vacía", "[]"), NordvikFixture.Bearer(await _f.Erik()));
        noLines.ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Same_active_name_twice_conflicts()
    {
        var erik = await _f.Erik();
        var name = "Duplicada " + Guid.NewGuid().ToString("N")[..4];
        var body = Body(SeedIds.CustomerLars, name, "[{\"isin\":\"SE0000000301\",\"percent\":100}]");
        Assert.Equal(201, (await _f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios", body, NordvikFixture.Bearer(erik))).Code);
        (await _f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios", body, NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "PORTFOLIO_NAME_TAKEN");
    }

    [Fact]
    public async Task Other_customers_portfolio_is_hidden_and_unassigned_advisor_is_denied()
    {
        var id = (await Create("[{\"isin\":\"SE0000000301\",\"percent\":100}]", SeedIds.CustomerLars)).Str("id");
        (await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{id}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "PORTFOLIO_NOT_FOUND");
        Assert.Equal(403, (await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{id}", NordvikFixture.Bearer(await _f.Maja()))).Code);
        Assert.Equal(403, (await Create("[{\"isin\":\"SE0000000301\",\"percent\":100}]", SeedIds.CustomerSofia)).Code);
        (await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{Guid.NewGuid()}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(404, "PORTFOLIO_NOT_FOUND");
    }

    [Fact]
    public async Task Changing_allocation_needs_if_match_and_closing_is_final()
    {
        var erik = await _f.Erik();
        var id = (await Create("[{\"isin\":\"SE0000000001\",\"percent\":100}]")).Str("id");
        var change = "{\"targetAllocation\":[{\"isin\":\"SE0000000001\",\"percent\":70},{\"isin\":\"SE0000000102\",\"percent\":30}]}";
        (await _f.Send(HttpMethod.Put, Ports.Portfolio, $"/api/v1/portfolios/{id}/allocation", change, NordvikFixture.Bearer(erik))).ShouldBeProblem(428, "PRECONDITION_REQUIRED");
        var ok = await _f.Send(HttpMethod.Put, Ports.Portfolio, $"/api/v1/portfolios/{id}/allocation", change, NordvikFixture.Bearer(erik, "\"1\""));
        Assert.Equal(2, ok.Int("version"));
        Assert.Equal(2, ok.Json.GetProperty("targetAllocation").GetArrayLength());
        (await _f.Send(HttpMethod.Put, Ports.Portfolio, $"/api/v1/portfolios/{id}/allocation", change, NordvikFixture.Bearer(erik, "\"1\""))).ShouldBeProblem(412, "VERSION_CONFLICT");
        var closed = await _f.Send(HttpMethod.Delete, Ports.Portfolio, $"/api/v1/portfolios/{id}", null, NordvikFixture.Bearer(erik));
        Assert.Equal("CLOSED", closed.Str("status"));
        (await _f.Send(HttpMethod.Delete, Ports.Portfolio, $"/api/v1/portfolios/{id}", null, NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "PORTFOLIO_ALREADY_CLOSED");
        (await _f.Send(HttpMethod.Put, Ports.Portfolio, $"/api/v1/portfolios/{id}/allocation", change, NordvikFixture.Bearer(erik, "\"3\""))).ShouldBeProblem(409, "PORTFOLIO_CLOSED");
    }

    [Fact]
    public async Task Portfolio_requires_a_token() =>
        (await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{SeedIds.PortfolioAna}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");

    [Theory]
    [InlineData(1, 10, false)]
    [InlineData(2, 2, false)]
    [InlineData(4, 10, true)]
    [InlineData(6, 20, false)]
    [InlineData(7, 30, true)]
    public async Task Proposals_always_sum_100_and_are_accepted_by_portfolio_service(int risk, int horizon, bool esg)
    {
        var r = await _f.Post(Ports.Allocation, "/api/v1/allocations/propose", $"{{\"riskProfile\":{risk},\"horizonYears\":{horizon},\"esg\":{esg.ToString().ToLowerInvariant()}}}");
        Assert.Equal(200, r.Code);
        var lines = r.Json.GetProperty("allocation");
        Assert.Equal(100m, lines.EnumerateArray().Sum(l => l.GetProperty("percent").GetDecimal()));
        if (horizon < 3) Assert.True(r.Json.GetProperty("equityPercent").GetDecimal() <= 30);
        if (esg) Assert.Contains(lines.EnumerateArray(), l => l.GetProperty("isin").GetString() == "SE0000000005");
        Assert.True(r.Json.GetProperty("rationale").GetArrayLength() >= 1);
        var allocation = JsonSerializer.Serialize(lines.EnumerateArray().Select(l => new { isin = l.GetProperty("isin").GetString(), percent = l.GetProperty("percent").GetDecimal() }));
        var created = await Create(allocation);
        Assert.True(created.Code == 201, created.Body);
    }

    [Fact]
    public async Task Proposal_validation()
    {
        var r = await _f.Post(Ports.Allocation, "/api/v1/allocations/propose", "{\"riskProfile\":8}");
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(2, r.Json.GetProperty("errors").GetArrayLength());
    }
}
