using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

public static class Insurance
{
    public static object Healthy => new { smoker = false, heightCm = 180, weightKg = 75, conditions = Array.Empty<string>(), hospitalizedLast5y = false };

    public static async Task<Reply> Declare(NordvikFixture f, string customerId, object answers, bool consent = true, string? token = null) =>
        await f.Send(HttpMethod.Post, Ports.HealthDeclarations, "/api/v1/health-declarations",
            JsonSerializer.Serialize(new { customerId, consentToProcessHealthData = consent, answers }), NordvikFixture.Bearer(token ?? await f.Erik()));

    public static async Task<Reply> Policy(NordvikFixture f, string customerId, string planId, string declarationId, string coverage = "LIFE", string capital = "1000000.00", int termYears = 10) =>
        await f.Send(HttpMethod.Post, Ports.InsurancePolicies, "/api/v1/insurance-policies",
            JsonSerializer.Serialize(new { customerId, pensionPlanId = planId, coverage, capital, termYears, healthDeclarationId = declarationId }), NordvikFixture.Bearer(await f.Erik()));

    /// <summary>Cliente nuevo con plan PRIVATE, declaración STANDARD y una póliza de la cobertura indicada.</summary>
    public static async Task<(string Customer, string Plan, string PolicyId, JsonElement Policy)> Insured(NordvikFixture f, string coverage = "LIFE")
    {
        var c = await People.Customer(f, "Signe", "Försäkrad", new DateOnly(1980, 2, 2));
        var plan = await Pensions.PlanId(f, c);
        var d = await Declare(f, c, Healthy);
        Assert.True(d.Code == 201, d.Body);
        var p = await Policy(f, c, plan, d.Str("id"), coverage);
        Assert.True(p.Code == 201, p.Body);
        return (c, plan, p.Str("id"), p.Json);
    }
}

[Collection("nordvik")]
public class InsuranceTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public InsuranceTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private Task<Reply> Quote(object body) => _f.Post(Ports.Premiums, "/api/v1/premiums/quote", JsonSerializer.Serialize(body));

    [Fact]
    public async Task Premiums_are_deterministic_loaded_and_validated()
    {
        var a = await Quote(new { age = 40, capital = "1000000.00", coverage = "LIFE", loadingPct = 0, termYears = 20 });
        var b = await Quote(new { age = 40, capital = "1000000.00", coverage = "LIFE", loadingPct = 0, termYears = 20 });
        Assert.Equal(200, a.Code);
        Assert.Equal(a.Body, b.Body);
        decimal M(Reply r) => Pensions.D(r.Json.GetProperty("monthlyPremium").GetProperty("amount").GetString()!);
        Assert.InRange(M(a), 50m, 2000m);
        var loaded = await Quote(new { age = 40, capital = "1000000.00", coverage = "LIFE", loadingPct = 50, termYears = 20 });
        Assert.InRange(M(loaded) - M(a) * 1.5m, -0.02m, 0.02m);
        var older = await Quote(new { age = 55, capital = "1000000.00", coverage = "LIFE", loadingPct = 0, termYears = 10 });
        Assert.True(M(older) > M(a), "a más edad, más prima");
        var dis = await Quote(new { age = 40, capital = "1000000.00", coverage = "DISABILITY", loadingPct = 0, termYears = 20 });
        Assert.NotEqual(M(a), M(dis));
        var bad = await Quote(new { age = 60, capital = "10.00", coverage = "PET", loadingPct = 30, termYears = 20 });
        bad.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(new[] { "capital", "coverage", "loadingPct", "termYears" }, bad.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()));
    }

    [Fact]
    public async Task Health_declarations_keep_only_the_outcome_and_need_explicit_consent()
    {
        var c = await People.Customer(_f, "Hedda", "Frisk", new DateOnly(1985, 5, 5)); // < 60 años: la edad no suma riesgo
        (await Insurance.Declare(_f, c, Insurance.Healthy, consent: false)).ShouldBeProblem(422, "EXPLICIT_CONSENT_REQUIRED");
        (await Insurance.Declare(_f, c, new { smoker = true })).ShouldBeProblem(400, "VALIDATION_FAILED");
        var ok = await Insurance.Declare(_f, c, Insurance.Healthy);
        Assert.Equal(201, ok.Code);
        Assert.Equal("STANDARD", ok.Str("outcome"));
        Assert.Equal(0, ok.Int("loadingPct"));
        Assert.False(ok.Json.GetProperty("answersStored").GetBoolean());
        Assert.DoesNotContain("heightCm", ok.Body);
        Assert.DoesNotContain("smoker", ok.Body);
        var got = await _f.Get(Ports.HealthDeclarations, $"/api/v1/health-declarations/{ok.Str("id")}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("VALID", got.Str("status"));
        Assert.DoesNotContain("weightKg", got.Body);
        var rated = await Insurance.Declare(_f, c, new { smoker = true, heightCm = 175, weightKg = 95, conditions = Array.Empty<string>(), hospitalizedLast5y = false });
        Assert.Equal("RATED", rated.Str("outcome"));
        Assert.Equal(50, rated.Int("loadingPct")); // fumador (2) + IMC 31 (1)
        var declined = await Insurance.Declare(_f, c, new { smoker = false, heightCm = 170, weightKg = 70, conditions = new[] { "CANCER_ACTIVE" }, hospitalizedLast5y = true });
        Assert.Equal("DECLINED", declined.Str("outcome"));
        var uw = await _f.Get(Ports.InsurerSim, "/__admin/underwriting");
        Assert.DoesNotContain("\"bmi\"", uw.Body);
        Assert.DoesNotContain("heightCm", uw.Body);
        Assert.All(uw.Json.GetProperty("items").EnumerateArray(), x => Assert.False(x.GetProperty("answersRetained").GetBoolean()));
        (await _f.Get(Ports.HealthDeclarations, $"/api/v1/health-declarations/{ok.Str("id")}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        await TestData.Events(_f, "health-declaration.completed", ok.Str("id"));
    }

    [Fact]
    public async Task A_policy_needs_a_live_declaration_an_active_plan_and_is_issued_by_the_insurer()
    {
        var (c, plan, id, pol) = await Insurance.Insured(_f);
        Assert.Equal("ACTIVE", pol.GetProperty("status").GetString());
        Assert.StartsWith("INS-", pol.GetProperty("insurerPolicyNumber").GetString());
        var age = (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{c}")).Int("age");
        var q = await Quote(new { age, capital = "1000000.00", coverage = "LIFE", loadingPct = 0, termYears = 10 });
        Assert.Equal(q.Json.GetProperty("monthlyPremium").GetProperty("amount").GetString(), pol.GetProperty("monthlyPremium").GetProperty("amount").GetString());
        var decl = pol.GetProperty("healthDeclarationId").GetString()!;
        (await Insurance.Policy(_f, c, plan, decl)).ShouldBeProblem(409, "POLICY_ALREADY_EXISTS");
        Assert.Equal(201, (await Insurance.Policy(_f, c, plan, decl, "DISABILITY")).Code);
        var declined = await Insurance.Declare(_f, c, new { smoker = true, heightCm = 170, weightKg = 120, conditions = new[] { "HEART_DISEASE" }, hospitalizedLast5y = true });
        var plan2 = await Pensions.PlanId(_f, c);
        (await Insurance.Policy(_f, c, plan2, declined.Str("id"))).ShouldBeProblem(422, "APPLICANT_DECLINED");
        var other = await People.Clean(_f);
        var otherDecl = await Insurance.Declare(_f, other, Insurance.Healthy);
        (await Insurance.Policy(_f, c, plan2, otherDecl.Str("id"))).ShouldBeProblem(422, "DECLARATION_NOT_FOUND");
        (await Insurance.Policy(_f, c, "pp_no_existe", decl)).ShouldBeProblem(422, "PLAN_NOT_FOUND");
        (await _f.Send(HttpMethod.Post, Ports.InsurancePolicies, "/api/v1/insurance-policies", "{}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var list = await _f.Get(Ports.InsurancePolicies, $"/api/v1/customers/{c}/insurance-policies", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(2, list.Int("count"));
        var cancel = await _f.Send(HttpMethod.Post, Ports.InsurancePolicies, $"/api/v1/insurance-policies/{id}/cancel", null, NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("CANCELLED", cancel.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.InsurancePolicies, $"/api/v1/insurance-policies/{id}/cancel", null, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        await TestData.Events(_f, "insurance-policy.issued", id);

        // 91 días después la declaración ha caducado
        await _f.Freeze(DateTime.UtcNow.AddDays(91).ToString("yyyy-MM-ddTHH:mm:ss"));
        Assert.Equal("EXPIRED", (await _f.Get(Ports.HealthDeclarations, $"/api/v1/health-declarations/{decl}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
        var plan3 = await Pensions.PlanId(_f, c);
        (await Insurance.Policy(_f, c, plan3, decl)).ShouldBeProblem(422, "HEALTH_DECLARATION_EXPIRED");
    }

    [Fact]
    public async Task Claims_go_to_the_insurer_and_are_paid_when_approved()
    {
        var (_, _, life, pol) = await Insurance.Insured(_f);
        var olof = NordvikFixture.Bearer(await _f.Olof());
        string Body(string policyId, string type, string date = "2026-10-01") => JsonSerializer.Serialize(new { policyId, type, eventDate = date });
        (await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(life, "DEATH"), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(life, "DISABILITY"), olof)).ShouldBeProblem(422, "COVERAGE_MISMATCH");
        (await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(life, "DEATH", "2099-01-01"), olof)).ShouldBeProblem(422, "DATE_IN_FUTURE");
        (await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body("ip_nada", "DEATH"), olof)).ShouldBeProblem(404, "POLICY_NOT_FOUND");
        var cl = await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(life, "DEATH"), olof);
        Assert.True(cl.Code == 201, cl.Body);
        Assert.Equal("SUBMITTED", cl.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(life, "DEATH"), olof)).ShouldBeProblem(409, "CLAIM_ALREADY_OPEN");
        var id = cl.Str("id");
        Assert.Equal("SUBMITTED", (await _f.Get(Ports.InsuranceClaims, $"/api/v1/insurance-claims/{id}", olof)).Str("status"));
        Assert.Equal(200, (await _f.Post(Ports.InsurerSim, $"/__admin/claims/{cl.Str("insurerClaimId")}/decide", "{\"decision\":\"APPROVED\"}")).Code);
        var paid = await _f.Get(Ports.InsuranceClaims, $"/api/v1/insurance-claims/{id}", olof);
        Assert.Equal("PAID", paid.Str("status"));
        Assert.Equal(pol.GetProperty("capital").GetProperty("amount").GetString(), paid.Json.GetProperty("paidAmount").GetProperty("amount").GetString());
        Assert.False(string.IsNullOrEmpty(paid.Str("ledgerEntryId")));
        Assert.Equal("CLAIMED", (await _f.Get(Ports.InsurancePolicies, $"/api/v1/insurance-policies/{life}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
        await TestData.Events(_f, "insurance-claim.paid", id);

        var (_, _, dis, _) = await Insurance.Insured(_f, "DISABILITY");
        var c2 = await _f.Send(HttpMethod.Post, Ports.InsuranceClaims, "/api/v1/insurance-claims", Body(dis, "DISABILITY"), olof);
        await _f.Post(Ports.InsurerSim, $"/__admin/claims/{c2.Str("insurerClaimId")}/decide", "{\"decision\":\"REJECTED\"}");
        Assert.Equal("REJECTED", (await _f.Get(Ports.InsuranceClaims, $"/api/v1/insurance-claims/{c2.Str("id")}", olof)).Str("status"));
        Assert.Equal("ACTIVE", (await _f.Get(Ports.InsurancePolicies, $"/api/v1/insurance-policies/{dis}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
    }

    [Fact]
    public async Task The_adapter_renews_its_oauth_token_once_after_a_401()
    {
        var olof = NordvikFixture.Bearer(await _f.Olof());
        var c = await People.Clean(_f);
        Assert.Equal(201, (await Insurance.Declare(_f, c, Insurance.Healthy)).Code);
        var before = await _f.Get(Ports.InsurerAdapter, "/api/v1/token-status", olof);
        Assert.True(before.Json.GetProperty("hasToken").GetBoolean());
        Assert.DoesNotContain("ins_", before.Body);
        var expired = await _f.Post(Ports.InsurerSim, "/__admin/tokens/expire-all");
        Assert.True(expired.Int("expired") >= 1);
        Assert.Equal(201, (await Insurance.Declare(_f, c, Insurance.Healthy)).Code);
        var after = await _f.Get(Ports.InsurerAdapter, "/api/v1/token-status", olof);
        Assert.Equal(before.Json.GetProperty("renewedAfter401").GetInt64() + 1, after.Json.GetProperty("renewedAfter401").GetInt64());
        Assert.Equal(before.Json.GetProperty("tokensFetched").GetInt64() + 1, after.Json.GetProperty("tokensFetched").GetInt64());
        (await _f.Get(Ports.InsurerAdapter, "/api/v1/token-status", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var unauth = await _f.Send(HttpMethod.Post, Ports.InsurerSim, "/v1/underwriting", "{\"applicantRef\":\"x\",\"age\":40,\"bmi\":22}");
        Assert.Equal(401, unauth.Code);
    }

    [Fact]
    public async Task A_death_claim_points_to_the_life_policies_to_claim()
    {
        var (c, _, life, _) = await Insurance.Insured(_f);
        var claim = await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", JsonSerializer.Serialize(new { customerId = c, dateOfDeath = "2026-10-01" }), NordvikFixture.Bearer(await _f.Olof()));
        Assert.True(claim.Code == 201, claim.Body);
        var part = claim.Json.GetProperty("insurancePart");
        Assert.Equal("CLAIM_REQUIRED", part.GetProperty("status").GetString());
        Assert.Equal(life, part.GetProperty("policies")[0].GetProperty("policyId").GetString());
        Assert.Equal("1000000.00", part.GetProperty("policies")[0].GetProperty("capital").GetString());
    }
}

[Collection("nordvik")]
public class TaxTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public TaxTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Withholding_uses_the_municipal_table_sink_for_non_residents_and_30_percent_for_capital()
    {
        var c = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, c);
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var w = await _f.Get(Ports.Withholding, $"/api/v1/customers/{c}/withholding?year=2026", erik);
        Assert.True(w.Code == 200, w.Body);
        Assert.Equal("TAX_TABLE", w.Str("basis"));
        var table = await _f.Get(Ports.TaxAgencySim, $"/v1/tax-tables/{nid}?year=2026", r => r.Headers.TryAddWithoutValidation("X-Api-Key", "e8-skatteverket-key-2026"));
        Assert.Equal(table.Str("ratePct"), w.Str("ratePct"));
        Assert.Equal(table.Str("municipality"), w.Str("municipality"));
        Assert.Equal(401, (await _f.Get(Ports.TaxAgencySim, $"/v1/tax-tables/{nid}")).Code);
        var cap = await _f.Get(Ports.Withholding, $"/api/v1/customers/{c}/withholding?incomeType=dividend", erik);
        Assert.Equal("CAPITAL_INCOME", cap.Str("basis"));
        Assert.Equal("30.00", cap.Str("ratePct"));
        var calc = await _f.Send(HttpMethod.Post, Ports.Withholding, "/api/v1/withholding/calculate", JsonSerializer.Serialize(new { customerId = c, gross = "10000.00", incomeType = "INTEREST" }), erik);
        Assert.Equal("3000.00", calc.Str("tax"));
        Assert.Equal("7000.00", calc.Str("net"));
        (await _f.Get(Ports.Withholding, $"/api/v1/customers/{c}/withholding?incomeType=LOTTERY", erik)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Withholding, $"/api/v1/customers/{c}/withholding", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");

        var put = await _f.Send(HttpMethod.Put, Ports.TaxResidency, $"/api/v1/customers/{c}/tax-residencies",
            JsonSerializer.Serialize(new { residencies = new object[] { new { country = "NO", tin = "12345678901", primary = true }, new { country = "SE", tin = nid, primary = false } } }), erik);
        Assert.True(put.Code == 200, put.Body);
        var sink = await _f.Get(Ports.Withholding, $"/api/v1/customers/{c}/withholding", erik);
        Assert.Equal("SINK", sink.Str("basis"));
        Assert.Equal("25.00", sink.Str("ratePct"));
        Assert.Equal("NO", sink.Str("residence"));

        var olof = NordvikFixture.Bearer(await _f.Olof());
        var c2 = await People.Clean(_f);
        await _f.Get(Ports.Withholding, $"/api/v1/customers/{c2}/withholding?year=2026", erik);
        var before = await _f.Get(Ports.TaxAgencyAdapter, "/api/v1/cache", olof);
        await _f.Get(Ports.Withholding, $"/api/v1/customers/{c2}/withholding?year=2026", erik);
        var after = await _f.Get(Ports.TaxAgencyAdapter, "/api/v1/cache", olof);
        Assert.Equal(before.Json.GetProperty("hits").GetInt64() + 1, after.Json.GetProperty("hits").GetInt64());
        Assert.Equal(before.Json.GetProperty("misses").GetInt64(), after.Json.GetProperty("misses").GetInt64());
    }

    [Fact]
    public async Task The_annual_report_is_filed_once_corrected_and_feeds_the_customer_certificate()
    {
        // Un pago de pensión de verdad en 2026
        var (customer, plan) = await Pensions.FundedPlan(_f, new DateOnly(1959, 1, 15), "120000.00");
        var po = await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/pension-plans/{plan}/payout-plans", "{\"type\":\"TERM\",\"termYears\":5,\"startPeriod\":\"2026-11\"}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.True(po.Code == 201, po.Body);
        await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/payout-plans/{po.Str("id")}/activate", null, NordvikFixture.Bearer(await _f.Erik()));
        await _f.Freeze("2026-11-05T10:00:00");
        var run = await _f.Send(HttpMethod.Post, Ports.PayoutExecution, "/api/v1/payout-runs", "{\"period\":\"2026-11\"}", NordvikFixture.Bearer(await _f.Olof()));
        var line = run.Json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("payoutPlanId").GetString() == po.Str("id"));

        var olof = NordvikFixture.Bearer(await _f.Olof());
        var erik = NordvikFixture.Bearer(await _f.Erik());
        (await _f.Get(Ports.AnnualStatements, $"/api/v1/customers/{customer}/annual-statements/2026", erik)).ShouldBeProblem(404, "STATEMENT_NOT_AVAILABLE");
        (await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026,\"kind\":\"CORRECTION\"}", olof)).ShouldBeProblem(409, "NOTHING_TO_CORRECT");
        (await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2030}", olof)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026}", erik)).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        await _f.Post(Ports.TaxAgencySim, "/__admin/reject-next", "{\"count\":1,\"reason\":\"SCHEMA_ERROR\"}");
        (await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026}", olof)).ShouldBeProblem(422, "SCHEMA_ERROR");
        var report = await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026}", olof);
        Assert.True(report.Code == 201, report.Body);
        Assert.Equal("FILED", report.Str("status"));
        Assert.StartsWith("skv_", report.Json.GetProperty("receipt").GetProperty("receiptId").GetString());
        Assert.Equal(report.Int("customers"), report.Json.GetProperty("receipt").GetProperty("accepted").GetInt32());
        var nid = await TestData.NationalIdOf(_f, customer);
        Assert.DoesNotContain(nid, report.Body);
        (await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026}", olof)).ShouldBeProblem(409, "REPORT_ALREADY_FILED");
        var corr = await _f.Send(HttpMethod.Post, Ports.TaxReports, "/api/v1/tax-reports", "{\"year\":2026,\"kind\":\"CORRECTION\"}", olof);
        Assert.Equal(201, corr.Code);
        Assert.Equal("CORRECTION", corr.Str("kind"));
        await TestData.Events(_f, "tax-report.filed", corr.Str("id"));

        var st = await _f.Get(Ports.AnnualStatements, $"/api/v1/customers/{customer}/annual-statements/2026", erik);
        Assert.True(st.Code == 200, st.Body);
        Assert.Equal(line.GetProperty("gross").GetString(), st.Str("pensionGross"));
        Assert.Equal(line.GetProperty("taxWithheld").GetString(), st.Str("taxWithheld"));
        Assert.Equal(corr.Str("id"), st.Str("reportId"));
        Assert.Equal(1, st.Int("payments"));
        var req = new HttpRequestMessage(HttpMethod.Get, NordvikFixture.Url(Ports.AnnualStatements, $"/api/v1/customers/{customer}/annual-statements/2026/pdf"));
        erik(req);
        var res = await _f.Http.SendAsync(req);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        Assert.True(PdfWriter.LooksLikePdf(await res.Content.ReadAsByteArrayAsync()));
        var filings = await _f.Get(Ports.TaxAgencySim, "/__admin/filings");
        Assert.True(filings.Int("count") >= 2);
    }

    [Fact]
    public async Task Portfolio_statements_show_opening_closing_and_the_trades_of_the_period()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var (customer, _, _) = await Flows.FundedCustomer(_f, "20000.00");
        var pf = await Flows.Portfolio(_f, customer);
        var o = await Flows.Buy(_f, pf, "SE0000000003", "10000.00");
        await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday);
        await _f.Freeze($"{TestData.Tuesday}T19:00:00");
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var s = await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-10", erik);
        Assert.True(s.Code == 200, s.Body);
        Assert.True(s.Json.GetProperty("inProgress").GetBoolean());
        Assert.Equal("0.00", s.Str("openingValue"));
        Assert.Equal(1, s.Int("transactionCount"));
        Assert.Equal("BUY", s.Json.GetProperty("transactions")[0].GetProperty("side").GetString());
        Assert.True(Pensions.D(s.Str("closingValue")) > 0);
        var q = await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-Q4", erik);
        Assert.Equal("2026-10-01", q.Str("from"));
        Assert.Equal(1, q.Int("transactionCount"));
        var sep = await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-09", erik);
        Assert.Equal(0, sep.Int("transactionCount"));
        Assert.False(sep.Json.GetProperty("inProgress").GetBoolean());
        (await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2027-01", erik)).ShouldBeProblem(422, "PERIOD_NOT_STARTED");
        (await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-13", erik)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-10", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Get(Ports.Statements, "/api/v1/portfolios/p-nada/statements/2026-10", erik)).ShouldBeProblem(404, "PORTFOLIO_NOT_FOUND");
        var req = new HttpRequestMessage(HttpMethod.Get, NordvikFixture.Url(Ports.Statements, $"/api/v1/portfolios/{pf}/statements/2026-10/pdf"));
        erik(req);
        var res = await _f.Http.SendAsync(req);
        Assert.True(PdfWriter.LooksLikePdf(await res.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Analytics_never_show_groups_smaller_than_k()
    {
        for (var i = 0; i < 5; i++)
        {
            var c = await People.Customer(_f, new[] { "Alva", "Bror", "Cilla", "Dag", "Ebba" }[i], "Grupp", new DateOnly(1991, 3, 3 + i));
            await Pensions.PlanId(_f, c);
        }
        var karin = NordvikFixture.Bearer(await _f.Karin());
        var r = await _f.Get(Ports.Analytics, "/api/v1/reports/pension-plans", karin);
        Assert.Equal(200, r.Code);
        Assert.Equal(5, r.Int("k"));
        var groups = r.Json.GetProperty("groups").EnumerateArray().ToList();
        var mine = groups.Single(g => g.GetProperty("type").GetString() == "PRIVATE" && g.GetProperty("ageBand").GetString() == "30-39");
        Assert.False(mine.GetProperty("suppressed").GetBoolean());
        Assert.True(mine.GetProperty("customers").GetInt32() >= 5);
        Assert.All(groups.Where(g => g.GetProperty("suppressed").GetBoolean()), g => Assert.Equal(JsonValueKind.Null, g.GetProperty("plans").ValueKind));
        Assert.All(groups.Where(g => !g.GetProperty("suppressed").GetBoolean()), g => Assert.True(g.GetProperty("customers").GetInt32() >= 5));
        Assert.DoesNotContain("customerId", r.Body);
        var big = await _f.Get(Ports.Analytics, "/api/v1/reports/pension-plans?k=1000", karin);
        Assert.All(big.Json.GetProperty("groups").EnumerateArray(), g => Assert.True(g.GetProperty("suppressed").GetBoolean()));
        (await _f.Get(Ports.Analytics, "/api/v1/reports/pension-plans?k=3", karin)).ShouldBeProblem(400, "K_TOO_LOW");
        (await _f.Get(Ports.Analytics, "/api/v1/reports/pension-plans", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var contrib = await _f.Get(Ports.Analytics, "/api/v1/reports/contributions", karin);
        Assert.Equal(200, contrib.Code);
        Assert.All(contrib.Json.GetProperty("groups").EnumerateArray(), g =>
            Assert.Equal(g.GetProperty("suppressed").GetBoolean(), g.GetProperty("total").ValueKind == JsonValueKind.Null));
    }
}
