using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class CompositionTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public CompositionTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task C1_bundle_is_the_exact_union_of_its_sources()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna, "ADVISORY_MANDATE");
        var id = c.Str("id");
        await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/annexes", "{\"kind\":\"FEE_SCHEDULE\",\"content\":{\"managementFee\":\"0.45\",\"entryFee\":\"0.00\",\"currency\":\"SEK\"}}", NordvikFixture.Bearer(await _f.Erik()));
        var ana = await _f.Ana();
        var contract = await _f.Get(Ports.Contracts, $"/api/v2/contracts/{id}", NordvikFixture.Bearer(ana));
        var annex = await _f.Get(Ports.Vault, $"/api/v1/documents/{contract.Json.GetProperty("annexes")[0].GetProperty("documentId").GetString()}", NordvikFixture.Bearer(ana));
        var bundle = await _f.Get(Ports.Composition, $"/api/v2/compositions/contract-bundle/{id}", NordvikFixture.Bearer(ana));
        Assert.Equal("COMPLETE", bundle.Str("status"));
        Assert.Equal(Canonical.Of(contract.Json), Canonical.Of(bundle.Json.GetProperty("bundle").GetProperty("contract")));
        Assert.Equal(Canonical.Of(annex.Json), Canonical.Of(bundle.Json.GetProperty("bundle").GetProperty("annex")));
        Assert.Equal(id, bundle.Json.GetProperty("bundle").GetProperty("annex").GetProperty("contractId").GetString());
        var sources = bundle.Json.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(new[] { "contract-service", "document-vault" }, sources.Select(s => s.GetProperty("service").GetString()));
        Assert.Equal(contract.Int("version"), (int)sources[0].GetProperty("version").GetInt64());
        Assert.Equal(Canonical.Hash(annex.Json.GetProperty("content")), annex.Str("contentHash"));
        var traced = await _f.Get(Ports.Composition, $"/api/v2/compositions/contract-bundle/{id}", r => { NordvikFixture.Bearer(ana)(r); r.Headers.TryAddWithoutValidation("X-Composition-Trace", "true"); });
        Assert.True(traced.Json.GetProperty("trace").TryGetProperty("annex", out _));
        Assert.False(bundle.Has("trace"));
    }

    [Fact]
    public async Task C1_without_annex_is_partial_and_respects_ownership()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna);
        var r = await _f.Get(Ports.Composition, $"/api/v2/compositions/contract-bundle/{c.Str("id")}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(200, r.Code);
        Assert.Equal("PARTIAL", r.Str("status"));
        Assert.Equal(new[] { "annex" }, r.Json.GetProperty("missing").EnumerateArray().Select(x => x.GetString()));
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/contract-bundle/{c.Str("id")}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CONTRACT_NOT_FOUND");
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/contract-bundle/{c.Str("id")}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task C2_C3_holdings_and_monthly_statement_add_up()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "6000.00");
        var pf = await Flows.Portfolio(_f, customerId, "[{\"isin\":\"SE0000000004\",\"percent\":50},{\"isin\":\"SE0000000102\",\"percent\":50}]");
        var a = await Flows.Buy(_f, pf, "SE0000000004", "2500.00");
        var b = await Flows.Buy(_f, pf, "SE0000000102", "2500.00");
        await Flows.ExecuteAt1805(_f, a.Str("id"), TestData.Tuesday);
        await Flows.WaitOrder(_f, b.Str("id"), "EXECUTED");
        await Eventually.Until(() => _f.Get(Ports.Positions, $"/api/v1/internal/portfolios/{pf}/positions"), r => r.Json.GetProperty("positions").GetArrayLength() == 2, 8000, "posiciones");
        var erik = await _f.Erik();
        var c2 = await _f.Get(Ports.Composition, $"/api/v2/compositions/holdings-enriched/{pf}", NordvikFixture.Bearer(erik));
        Assert.Equal("COMPLETE", c2.Str("status"));
        foreach (var p in c2.Json.GetProperty("bundle").GetProperty("positions").EnumerateArray())
        {
            var fund = await _f.Get(Ports.Catalog, $"/api/v1/funds/{p.GetProperty("isin").GetString()}");
            Assert.Equal(fund.Str("name"), p.GetProperty("fundName").GetString());
            Assert.Equal(fund.Int("sri"), p.GetProperty("sri").GetInt32());
            Assert.Equal(5, p.EnumerateObject().Count()); // ni un campo más ni uno menos
        }
        var c3 = await _f.Get(Ports.Composition, $"/api/v2/compositions/monthly-statement/{pf}?month=2026-10", NordvikFixture.Bearer(erik));
        var st = c3.Json.GetProperty("bundle");
        Assert.All(st.GetProperty("holdings").EnumerateArray(), h => Assert.True(h.GetProperty("check").GetBoolean()));
        Assert.Equal(3, st.GetProperty("cashMovements").GetArrayLength()); // ingreso y dos compras
        var hv = decimal.Parse(st.GetProperty("holdingsValueSek").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal("1000.00", st.GetProperty("closingCashSek").GetString());
        Assert.Equal(Money.Format(hv + 1000m, 2), st.GetProperty("totalAssetsSek").GetString());
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/monthly-statement/{pf}", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task C4_customer_360_is_partial_until_wave_3_and_masks_for_advisors()
    {
        var r = await _f.Get(Ports.Composition, $"/api/v2/compositions/customer-360/{SeedIds.CustomerAna}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("PARTIAL", r.Str("status"));
        Assert.Equal(new[] { "kyc" }, r.Json.GetProperty("missing").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(6, r.Json.GetProperty("bundle").GetProperty("consents").GetArrayLength());
        Assert.Contains("consent-ledger", r.Json.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("service").GetString()));
        Assert.Contains("**-****", r.Json.GetProperty("bundle").GetProperty("customer").GetProperty("nationalId").GetString());
        var own = await _f.Get(Ports.Composition, $"/api/v2/compositions/customer-360/{SeedIds.CustomerAna}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.DoesNotContain("*", own.Json.GetProperty("bundle").GetProperty("customer").GetProperty("nationalId").GetString());
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/customer-360/{SeedIds.CustomerAna}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task C5_deep_merge_keeps_every_clause_ordered_and_the_customer_language_wins()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerLars);
        var lars = await _f.Lars();
        await _f.Send(HttpMethod.Put, Ports.Preferences, $"/api/v1/customers/{SeedIds.CustomerLars}/preferences", "{\"language\":\"en\",\"channel\":\"EMAIL\",\"marketingConsent\":false}", NordvikFixture.Bearer(lars));
        var r = await _f.Send(HttpMethod.Post, Ports.Composition, "/api/v2/compositions/master-agreement", $"{{\"customerId\":\"{SeedIds.CustomerLars}\"}}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(200, r.Code);
        var merged = r.Json.GetProperty("bundle").GetProperty("merged");
        Assert.Equal("en", merged.GetProperty("language").GetString());
        var nos = merged.GetProperty("clauses").EnumerateArray().Select(x => x.GetProperty("clauseNo").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(1, 10), nos); // la plantilla v3 tiene 10 cláusulas: la fusión no pierde ninguna
        var mine = merged.GetProperty("clauses").EnumerateArray().Single(x => x.GetProperty("clauseNo").GetInt32() == 6);
        Assert.Equal("ISK-01", mine.GetProperty("code").GetString());
        Assert.True(mine.TryGetProperty("condition", out _)); // viene de la plantilla
        await _f.Send(HttpMethod.Put, Ports.Preferences, $"/api/v1/customers/{SeedIds.CustomerLars}/preferences", "{\"language\":\"sv\",\"channel\":\"EMAIL\",\"marketingConsent\":false}", NordvikFixture.Bearer(lars));
        Assert.Equal(501, (await _f.Get(Ports.Composition, "/api/v2/compositions/transfer-dossier/x", NordvikFixture.Bearer(lars))).Code);
    }
}

[Collection("nordvik")]
public class ChannelTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public ChannelTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Web_dashboard_is_one_call_for_customers_only()
    {
        var r = await _f.Get(Ports.BffWeb, "/api/v1/web/dashboard", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(200, r.Code);
        Assert.Equal("CUS-900001", r.Json.GetProperty("customer").GetProperty("customerNumber").GetString());
        Assert.True(r.Json.GetProperty("portfolios").GetArrayLength() >= 1);
        var sum = r.Json.GetProperty("portfolios").EnumerateArray().Sum(p => decimal.Parse(p.GetProperty("valueSek").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(Money.Format(sum, 2), r.Str("totalPortfolioValueSek"));
        (await _f.Get(Ports.BffWeb, "/api/v1/web/dashboard", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Mobile_orders_page_by_cursor_and_select_fields()
    {
        var lars = await _f.Lars();
        await Flows.Deposit(_f, SeedIds.CustomerLars, "1000.00");
        var pfs = await _f.Get(Ports.Portfolio, $"/api/v1/customers/{SeedIds.CustomerLars}/portfolios?status=ACTIVE", NordvikFixture.Bearer(lars));
        var pf = pfs.Json.GetArrayLength() > 0 ? pfs.Json[0].GetProperty("id").GetString()! : await Flows.Portfolio(_f, SeedIds.CustomerLars, "[{\"isin\":\"SE0000000301\",\"percent\":100}]");
        for (var i = 0; i < 3; i++) Assert.Equal(201, (await Flows.Buy(_f, pf, "SE0000000301", "100.00", lars)).Code);
        var p1 = await _f.Get(Ports.BffMobile, "/api/v1/mobile/orders?limit=2&fields=id,status", NordvikFixture.Bearer(await _f.Lars()));
        Assert.Equal(2, p1.Json.GetProperty("items").GetArrayLength());
        Assert.All(p1.Json.GetProperty("items").EnumerateArray(), o => Assert.Equal(new[] { "id", "status" }, o.EnumerateObject().Select(x => x.Name)));
        var p2 = await _f.Get(Ports.BffMobile, $"/api/v1/mobile/orders?limit=2&cursor={Uri.EscapeDataString(p1.Str("nextCursor"))}", NordvikFixture.Bearer(await _f.Lars()));
        Assert.True(p2.Json.GetProperty("items").GetArrayLength() >= 1);
        (await _f.Get(Ports.BffMobile, "/api/v1/mobile/orders?fields=id,password", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(400, "UNKNOWN_FIELDS");
        var home = await _f.Get(Ports.BffMobile, "/api/v1/mobile/home", NordvikFixture.Bearer(await _f.Lars()));
        Assert.True(home.Has("total"));
    }

    [Fact]
    public async Task Advisor_desk_lists_only_assigned_customers()
    {
        var erik = await _f.Get(Ports.BffAdvisor, "/api/v1/advisor/customers", NordvikFixture.Bearer(await _f.Erik()));
        var ids = erik.Json.GetProperty("customers").EnumerateArray().Select(c => c.GetProperty("customerId").GetString()).ToList();
        Assert.Contains(SeedIds.CustomerAna, ids);
        Assert.DoesNotContain(SeedIds.CustomerSofia, ids);
        Assert.All(erik.Json.GetProperty("customers").EnumerateArray(), c => Assert.Contains("**-****", c.GetProperty("nationalId").GetString()));
        var maja = await _f.Get(Ports.BffAdvisor, "/api/v1/advisor/customers", NordvikFixture.Bearer(await _f.Maja()));
        Assert.Equal(new[] { SeedIds.CustomerSofia }, maja.Json.GetProperty("customers").EnumerateArray().Select(c => c.GetProperty("customerId").GetString()));
        var overview = await _f.Get(Ports.BffAdvisor, $"/api/v1/advisor/customers/{SeedIds.CustomerSofia}/overview", NordvikFixture.Bearer(await _f.Erik()));
        overview.ShouldBeProblem(403, "ACCESS_DENIED");
        Assert.Equal(200, (await _f.Get(Ports.BffAdvisor, "/api/v1/advisor/alerts", NordvikFixture.Bearer(await _f.Erik()))).Code);
        (await _f.Get(Ports.BffAdvisor, "/api/v1/advisor/customers", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }
}

[Collection("nordvik")]
public class OnboardingTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public OnboardingTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<string> Body(string ibanEnding, string? postalCode = null)
    {
        var p = await _f.NewPerson();
        var a = p.GetProperty("address");
        return JsonSerializer.Serialize(new
        {
            person = new { firstName = p.GetProperty("firstName").GetString(), lastName = p.GetProperty("lastName").GetString(), nationalId = p.GetProperty("nationalId").GetString(), email = p.GetProperty("email").GetString(), phone = p.GetProperty("phone").GetString() },
            address = new { street = a.GetProperty("street").GetString(), number = a.GetProperty("number").GetString(), postalCode = postalCode ?? a.GetProperty("postalCode").GetString(), city = a.GetProperty("city").GetString() },
            iban = TestData.Iban(ibanEnding), riskProfile = 4, horizonYears = 15, esg = true, accountType = "ISK",
        });
    }

    [Fact]
    public async Task Full_onboarding_waits_for_the_signature()
    {
        var r = await _f.Send(HttpMethod.Post, Ports.Onboarding, "/api/v1/onboardings", await Body("1234"), NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, r.Code);
        Assert.Equal("AWAITING_SIGNATURE", r.Str("status"));
        Assert.All(r.Json.GetProperty("steps").EnumerateArray(), s => Assert.Equal("DONE", s.GetProperty("status").GetString()));
        Assert.Equal(8, r.Json.GetProperty("steps").GetArrayLength());
        var pf = await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{r.Str("portfolioId")}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Contains(pf.Json.GetProperty("targetAllocation").EnumerateArray(), l => l.GetProperty("isin").GetString() == "SE0000000005"); // ESG
        Assert.Equal("PENDING_SIGNATURE", (await ContractFlows.Get(_f, r.Str("contractId"))).Str("status"));
        var email = (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{r.Str("customerId")}")).Str("email");
        Assert.Contains("CUS-", (await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={email}")).Body);
        // Si el cliente no firma en 14 días, el alta acaba en FAILED
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":1209601}");
        await ContractFlows.Get(_f, r.Str("contractId"));
        var failed = await Eventually.Until(async () => await _f.Get(Ports.Onboarding, $"/api/v1/onboardings/{r.Str("id")}", NordvikFixture.Bearer(await _f.Erik())), x => x.Str("status") != "AWAITING_SIGNATURE", 10000, "fin del alta");
        Assert.Equal("FAILED", failed.Str("status"));
    }

    [Fact]
    public async Task Rejected_bank_account_compensates_the_customer()
    {
        var r = await _f.Send(HttpMethod.Post, Ports.Onboarding, "/api/v1/onboardings", await Body("0009"), NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("FAILED", r.Str("status"));
        Assert.Equal("BANK_ACCOUNT", r.Str("failedStep"));
        var steps = r.Json.GetProperty("steps").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString());
        Assert.Equal("COMPENSATED", steps["CUSTOMER"]);
        Assert.Equal("FAILED", steps["BANK_ACCOUNT"]);
        Assert.Equal("SKIPPED", steps["PORTFOLIO"]);
        Assert.Equal("CLOSED", (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{r.Str("customerId")}")).Str("status"));
    }

    [Fact]
    public async Task Address_errors_stop_early_and_validation()
    {
        var r = await _f.Send(HttpMethod.Post, Ports.Onboarding, "/api/v1/onboardings", await Body("1234", "999 99"), NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("ADDRESS", r.Str("failedStep"));
        Assert.Contains("POSTAL", r.Str("failureReason"));
        (await _f.Send(HttpMethod.Post, Ports.Onboarding, "/api/v1/onboardings", "{}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Onboarding, "/api/v1/onboardings", "{}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }
}
