using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

public static class People
{
    /// <summary>Un cliente nuevo de asesor.erik con el nombre y la fecha de nacimiento que digas (para casar con las listas de SanctionScreen).</summary>
    public static async Task<string> Customer(NordvikFixture f, string first, string last, DateOnly birth)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var nid = NationalId.Create(birth, Random.Shared.Next(0, 99), Random.Shared.Next(2) == 0);
            var r = await f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers",
                JsonSerializer.Serialize(new { firstName = first, lastName = last, nationalId = nid, email = $"{Guid.NewGuid().ToString("N")[..8]}@example.com" }), NordvikFixture.Bearer(await f.Erik()));
            if (r.Code == 201) return r.Str("id");
        }
        throw new Xunit.Sdk.XunitException("no se pudo crear el cliente " + first + " " + last);
    }

    public static async Task<string> Clean(NordvikFixture f) => (await f.NewCustomer()).GetProperty("id").GetString()!;

    public static async Task SwedishTaxResidence(NordvikFixture f, string customerId)
    {
        var nid = await TestData.NationalIdOf(f, customerId);
        var r = await f.Send(HttpMethod.Put, Ports.TaxResidency, $"/api/v1/customers/{customerId}/tax-residencies",
            JsonSerializer.Serialize(new { residencies = new[] { new { country = "SE", tin = nid, primary = true } } }), NordvikFixture.Bearer(await f.Erik()));
        Assert.True(r.Code == 200, r.Body);
    }
}

[Collection("nordvik")]
public class SanctionsTests
{
    private readonly NordvikFixture _f;
    public SanctionsTests(NordvikFixture f) => _f = f;

    private async Task<Reply> Screen(object body, string? token = null) =>
        await _f.Send(HttpMethod.Post, Ports.Sanctions, "/api/v1/screenings", JsonSerializer.Serialize(body), NordvikFixture.Bearer(token ?? await _f.Karin()));

    [Fact]
    public async Task A_clean_customer_is_clear_and_the_national_id_never_leaves_nordvik()
    {
        var id = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, id);
        var s = await Screen(new { customerId = id }, await _f.Erik());
        Assert.Equal(201, s.Code);
        Assert.Equal("CLEAR", s.Str("status"));
        Assert.Equal(0, s.Json.GetProperty("matches").GetArrayLength());
        Assert.Equal(85, s.Int("threshold"));
        Assert.StartsWith("tok_cu_", s.Str("subjectRef"));
        var log = await _f.Get(Ports.SanctionScreenSim, "/__admin/requests");
        Assert.DoesNotContain(nid, log.Body);
        Assert.DoesNotContain(nid.Replace("-", ""), log.Body);
        await TestData.Events(_f, "screening.completed", s.Str("id"));
        (await Screen(new { })).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Screen(new { customerId = "no-existe" })).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await Screen(new { name = "x" }, await _f.Ana())).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task A_listed_customer_is_a_potential_match_until_compliance_decides()
    {
        var id = await People.Customer(_f, "Viktor", "Draganov", new DateOnly(1961, 4, 12));
        var s = await Screen(new { customerId = id }, await _f.Erik());
        Assert.Equal("POTENTIAL_MATCH", s.Str("status"));
        var m = s.Json.GetProperty("matches")[0];
        Assert.Equal("EU-0001", m.GetProperty("entryId").GetString());
        Assert.Equal(100, m.GetProperty("score").GetInt32());
        var path = $"/api/v1/screenings/{s.Str("id")}/decision";
        (await _f.Send(HttpMethod.Post, Ports.Sanctions, path, "{\"decision\":\"FALSE_POSITIVE\",\"reason\":\"x\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.Sanctions, path, "{\"decision\":\"MAYBE\",\"reason\":\"x\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var d = await _f.Send(HttpMethod.Post, Ports.Sanctions, path, "{\"decision\":\"TRUE_MATCH\",\"reason\":\"Misma persona: nombre y fecha\"}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("CONFIRMED_MATCH", d.Str("status"));
        Assert.Equal("compliance.karin", d.Str("decidedBy"));
        (await _f.Send(HttpMethod.Post, Ports.Sanctions, path, "{\"decision\":\"FALSE_POSITIVE\",\"reason\":\"x\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(409, "NOTHING_TO_DECIDE");
    }

    [Fact]
    public async Task Fuzzy_matching_and_the_configurable_threshold()
    {
        var typo = await Screen(new { name = "Viktor Draganow", birthDate = "1961-04-12" });
        Assert.Equal("POTENTIAL_MATCH", typo.Str("status"));
        var accents = await Screen(new { name = "Draganov Viktor" });
        Assert.Equal("POTENTIAL_MATCH", accents.Str("status"));
        var weak = await Screen(new { name = "Elena Kravetz" });
        Assert.Equal("CLEAR", weak.Str("status"));
        Assert.Equal(1, weak.Int("ignoredBelowThreshold"));
        var admin = await _f.Admin();
        try
        {
            await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/sanctions-adapter/matchThreshold", "{\"value\":\"80\"}", NordvikFixture.Bearer(admin));
            var strict = await Screen(new { name = "Elena Kravetz" });
            Assert.Equal("POTENTIAL_MATCH", strict.Str("status"));
            Assert.Equal(80, strict.Int("threshold"));
        }
        finally { await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/sanctions-adapter/matchThreshold", "{\"value\":\"85\"}", NordvikFixture.Bearer(admin)); }
        (await Screen(new { name = "19610412-1234" })).ShouldBeProblem(502, "SCREENING_PROVIDER_ERROR");
    }

    [Fact]
    public async Task Partial_answers_are_never_clear_and_outages_are_explicit()
    {
        try
        {
            await _f.Post(Ports.SanctionScreenSim, "/__admin/partial?on=true");
            var p = await Screen(new { name = "Persona Corriente" });
            Assert.Equal("INCOMPLETE", p.Str("status"));
            Assert.True(p.Json.GetProperty("partial").GetBoolean());
            Assert.DoesNotContain("OFAC_SDN", p.Json.GetProperty("screenedLists").GetRawText());
        }
        finally { await _f.Post(Ports.SanctionScreenSim, "/__admin/partial?on=false"); }
        try
        {
            await _f.Send(HttpMethod.Put, Ports.MockControl, "/api/v1/mocks/sanctionscreen-sim/behaviour", "{\"mode\":\"down\"}");
            (await Screen(new { name = "Persona Corriente" })).ShouldBeProblem(503, "SCREENING_PROVIDER_DOWN");
        }
        finally { await _f.Post(Ports.MockControl, "/api/v1/mocks/reset"); }
    }

    [Fact]
    public async Task A_rotated_api_key_is_picked_up_without_a_restart()
    {
        var admin = await _f.Admin();
        try
        {
            await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets/sanctionscreen-api-key/rotate", "{\"value\":\"ss-key-revocada-1\"}", NordvikFixture.Bearer(admin));
            (await Screen(new { name = "Persona Corriente" })).ShouldBeProblem(502, "SCREENING_PROVIDER_ERROR");
            await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets/sanctionscreen-api-key/rotate", "{\"value\":\"ss-key-2026-a\"}", NordvikFixture.Bearer(admin));
            Assert.Equal("CLEAR", (await Screen(new { name = "Persona Corriente" })).Str("status"));
        }
        finally { await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets/sanctionscreen-api-key/rotate", "{\"value\":\"ss-key-2026-b\"}", NordvikFixture.Bearer(admin)); }
    }
}

[Collection("nordvik")]
public class PepAndRiskTests
{
    private readonly NordvikFixture _f;
    public PepAndRiskTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Pep_status_combines_declaration_and_screening()
    {
        var erik = await _f.Erik();
        var clean = await People.Clean(_f);
        var before = await _f.Get(Ports.Pep, $"/api/v1/customers/{clean}/pep", NordvikFixture.Bearer(erik));
        Assert.Equal("UNKNOWN", before.Str("status"));
        Assert.Contains("NEVER_SCREENED", before.Json.GetProperty("flags").GetRawText());
        var chk = await _f.Send(HttpMethod.Post, Ports.Pep, $"/api/v1/customers/{clean}/pep-checks", null, NordvikFixture.Bearer(erik));
        Assert.Equal("NOT_PEP", chk.Str("status"));

        var pep = await People.Customer(_f, "Annika", "Strömberg", new DateOnly(1972, 11, 21));
        (await _f.Send(HttpMethod.Put, Ports.Pep, $"/api/v1/customers/{pep}/pep-declaration", "{\"isPep\":true,\"isRelative\":false}", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "VALIDATION_FAILED");
        await _f.Send(HttpMethod.Put, Ports.Pep, $"/api/v1/customers/{pep}/pep-declaration", "{\"isPep\":false,\"isRelative\":false}", NordvikFixture.Bearer(erik));
        var s = await _f.Send(HttpMethod.Post, Ports.Pep, $"/api/v1/customers/{pep}/pep-checks", null, NordvikFixture.Bearer(erik));
        Assert.Equal("PEP", s.Str("status"));
        Assert.True(s.Json.GetProperty("enhancedDueDiligence").GetBoolean());
        Assert.Contains("DECLARATION_MISMATCH", s.Json.GetProperty("flags").GetRawText());
        await TestData.Events(_f, "pep.identified", pep);

        var rca = await People.Customer(_f, "Sofia", "Lindqvist", new DateOnly(1968, 7, 14));
        Assert.Equal("RCA", (await _f.Send(HttpMethod.Post, Ports.Pep, $"/api/v1/customers/{rca}/pep-checks", null, NordvikFixture.Bearer(erik))).Str("status"));
        (await _f.Get(Ports.Pep, $"/api/v1/customers/{SeedIds.CustomerAna}/pep", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Risk_is_explainable_and_grows_with_evidence()
    {
        var erik = await _f.Erik();
        var id = await People.Clean(_f);
        var missing = await _f.Send(HttpMethod.Post, Ports.Risk, $"/api/v1/customers/{id}/risk-assessments", null, NordvikFixture.Bearer(erik));
        Assert.Equal(201, missing.Code);
        var codes = missing.Json.GetProperty("factors").EnumerateArray().Select(f => f.GetProperty("code").GetString()).ToList();
        Assert.Contains("SCREENING_INCOMPLETE", codes);
        Assert.Contains("DATA_MISSING", codes);

        await People.SwedishTaxResidence(_f, id);
        await _f.Send(HttpMethod.Post, Ports.Sanctions, "/api/v1/screenings", JsonSerializer.Serialize(new { customerId = id }), NordvikFixture.Bearer(erik));
        await _f.Send(HttpMethod.Post, Ports.Pep, $"/api/v1/customers/{id}/pep-checks", null, NordvikFixture.Bearer(erik));
        var clean = await _f.Send(HttpMethod.Post, Ports.Risk, $"/api/v1/customers/{id}/risk-assessments", null, NordvikFixture.Bearer(erik));
        Assert.Equal(0, clean.Int("score"));
        Assert.Equal("LOW", clean.Str("level"));

        var olof = await _f.Olof();
        foreach (var a in new[] { "9000.00", "9000.00", "9000.00" })
            await _f.Send(HttpMethod.Post, Ports.Monitoring, "/api/v1/transactions", JsonSerializer.Serialize(new { customerId = id, direction = "IN", amount = a }), NordvikFixture.Bearer(olof));
        await _f.Send(HttpMethod.Post, Ports.Monitoring, "/api/v1/transactions", JsonSerializer.Serialize(new { customerId = id, direction = "IN", amount = "500.00", country = "XX" }), NordvikFixture.Bearer(olof));
        var risky = await _f.Send(HttpMethod.Post, Ports.Risk, $"/api/v1/customers/{id}/risk-assessments", null, NordvikFixture.Bearer(erik));
        Assert.Equal(50, risky.Int("score"));
        Assert.Equal("MEDIUM", risky.Str("level"));
        Assert.Equal(risky.Str("id"), (await _f.Get(Ports.Risk, $"/api/v1/customers/{id}/risk", NordvikFixture.Bearer(await _f.Karin()))).Str("id"));
        (await _f.Get(Ports.Risk, $"/api/v1/customers/{id}/risk", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        Assert.True((await _f.Get(Ports.Risk, "/api/v1/rules")).Json.GetProperty("items").GetArrayLength() >= 10);
    }

    [Fact]
    public async Task Confirmed_sanctions_make_a_customer_prohibited()
    {
        var id = await People.Customer(_f, "Marat", "Ilyasov", new DateOnly(1958, 9, 30));
        var s = await _f.Send(HttpMethod.Post, Ports.Sanctions, "/api/v1/screenings", JsonSerializer.Serialize(new { customerId = id }), NordvikFixture.Bearer(await _f.Karin()));
        await _f.Send(HttpMethod.Post, Ports.Sanctions, $"/api/v1/screenings/{s.Str("id")}/decision", "{\"decision\":\"TRUE_MATCH\",\"reason\":\"confirmado\"}", NordvikFixture.Bearer(await _f.Karin()));
        var r = await _f.Send(HttpMethod.Post, Ports.Risk, $"/api/v1/customers/{id}/risk-assessments", null, NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("PROHIBITED", r.Str("level"));
        Assert.Equal(100, r.Int("score"));
        var k = await _f.Send(HttpMethod.Post, Ports.Kyc, $"/api/v1/customers/{id}/kyc-cases", null, NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("REJECTED", k.Str("status"));
        await TestData.Events(_f, "kyc.rejected", id);
    }
}

[Collection("nordvik")]
public class MonitoringTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public MonitoringTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Tx(string customerId, string direction, string amount, string? country = null) =>
        await _f.Send(HttpMethod.Post, Ports.Monitoring, "/api/v1/transactions", JsonSerializer.Serialize(new { customerId, direction, amount, country }), NordvikFixture.Bearer(await _f.Olof()));

    private static List<string?> Rules(Reply r) => r.Json.GetProperty("alertsRaised").EnumerateArray().Select(a => a.GetProperty("rule").GetString()).ToList();

    [Fact]
    public async Task Structuring_is_caught_once_per_day_and_opens_an_aml_case()
    {
        await _f.Freeze($"{TestData.Tuesday}T09:00:00");
        var c = "cust-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Empty(Rules(await Tx(c, "IN", "9000.00")));
        Assert.Empty(Rules(await Tx(c, "IN", "9000.00")));
        var third = await Tx(c, "IN", "9000.00");
        Assert.Equal(new[] { "STRUCTURING" }, Rules(third));
        Assert.Empty(Rules(await Tx(c, "IN", "9000.00")));
        var alertId = third.Json.GetProperty("alertsRaised")[0].GetProperty("id").GetString();
        var alert = await _f.Get(Ports.Monitoring, $"/api/v1/alerts/{alertId}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal(3, alert.Json.GetProperty("evidence").GetArrayLength());
        var cases = await Eventually.Until(async () => await _f.Get(Ports.Cases, $"/api/v1/cases?customerId={c}", NordvikFixture.Bearer(await _f.Karin())), r => r.Int("count") >= 1, 10000, "caso AML abierto por el evento");
        var kase = cases.Json.GetProperty("items")[0];
        Assert.Equal("AML_ALERT", kase.GetProperty("type").GetString());
        Assert.Equal("Compliance", kase.GetProperty("ownerRole").GetString());
        Assert.Equal(alertId, kase.GetProperty("source").GetProperty("ref").GetString());
        var tasks = await _f.Get(Ports.Tasks, $"/api/v1/tasks?sourceRef={kase.GetProperty("id").GetString()}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal(1, tasks.Int("count"));
    }

    [Fact]
    public async Task Large_rapid_and_risky_country_rules()
    {
        var c = "cust-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(new[] { "LARGE_TRANSFER" }, Rules(await Tx(c, "IN", "200000.00")));
        Assert.Equal(new[] { "RAPID_MOVEMENT" }, Rules(await Tx(c, "OUT", "170000.00")));
        Assert.Equal(new[] { "HIGH_RISK_COUNTRY" }, Rules(await Tx(c, "IN", "100.00", "xx")));
        var karin = await _f.Karin();
        var open = await _f.Get(Ports.Monitoring, $"/api/v1/alerts?customerId={c}&status=OPEN", NordvikFixture.Bearer(karin));
        Assert.Equal(3, open.Int("count"));
        var id = open.Json.GetProperty("items")[0].GetProperty("id").GetString();
        (await _f.Send(HttpMethod.Post, Ports.Monitoring, $"/api/v1/alerts/{id}/close", "{\"resolution\":\"FALSE_POSITIVE\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Monitoring, $"/api/v1/alerts/{id}/close", "{\"resolution\":\"FALSE_POSITIVE\",\"comment\":\"herencia\"}", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var closed = await _f.Send(HttpMethod.Post, Ports.Monitoring, $"/api/v1/alerts/{id}/close", "{\"resolution\":\"FALSE_POSITIVE\",\"comment\":\"herencia documentada\"}", NordvikFixture.Bearer(karin));
        Assert.Equal("CLOSED", closed.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Monitoring, $"/api/v1/alerts/{id}/close", "{\"resolution\":\"ESCALATED\",\"comment\":\"x\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "ALERT_ALREADY_CLOSED");
        (await Tx(c, "SIDEWAYS", "1.00")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Monitoring, "/api/v1/alerts", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Real_deposits_are_watched_through_events()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1500.00");
        var t = await Eventually.Until(async () => await _f.Get(Ports.Monitoring, $"/api/v1/customers/{customerId}/transactions", NordvikFixture.Bearer(await _f.Karin())), r => r.Int("count") >= 1, 10000, "ingreso vigilado");
        var item = t.Json.GetProperty("items")[0];
        Assert.Equal("IN", item.GetProperty("direction").GetString());
        Assert.Equal("1500.00", item.GetProperty("amount").GetString());
        Assert.Equal("deposit.received", item.GetProperty("source").GetString());
    }
}

[Collection("nordvik")]
public class KycTests
{
    private readonly NordvikFixture _f;
    public KycTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Low_risk_is_approved_automatically_with_a_three_year_review()
    {
        var id = await People.Clean(_f);
        await People.SwedishTaxResidence(_f, id);
        var k = await _f.Send(HttpMethod.Post, Ports.Kyc, $"/api/v1/customers/{id}/kyc-cases", null, NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, k.Code);
        Assert.Equal("APPROVED", k.Str("status"));
        Assert.Equal("LOW", k.Str("riskLevel"));
        var steps = k.Json.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("step").GetString()).ToList();
        Assert.Equal(new[] { "SANCTIONS_SCREENING", "PEP_CHECK", "RISK_SCORING" }, steps);
        var today = DateOnly.Parse((await _f.Get(Ports.Clock, "/api/v1/clock/now")).Str("date"));
        Assert.Equal(today.AddYears(3).ToString("yyyy-MM-dd"), k.Str("nextReviewDue"));
        var view = await _f.Get(Ports.Kyc, $"/api/v1/customers/{id}/kyc", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("LOW", view.Str("riskLevel"));
        await TestData.Events(_f, "kyc.approved", id);
    }

    [Fact]
    public async Task Customers_see_their_kyc_but_not_their_risk_level()
    {
        await _f.Send(HttpMethod.Post, Ports.Kyc, $"/api/v1/customers/{SeedIds.CustomerAna}/kyc-cases", null, NordvikFixture.Bearer(await _f.Erik()));
        var own = await _f.Get(Ports.Kyc, $"/api/v1/customers/{SeedIds.CustomerAna}/kyc", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(200, own.Code);
        Assert.Equal(JsonValueKind.Null, own.Json.GetProperty("riskLevel").ValueKind);
        (await _f.Get(Ports.Kyc, $"/api/v1/customers/{SeedIds.CustomerAna}/kyc", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Get(Ports.Kyc, $"/api/v1/customers/{await People.Clean(_f)}/kyc", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(404, "KYC_NOT_FOUND");
    }

    [Fact]
    public async Task Potential_matches_go_to_four_eyes_review()
    {
        var id = await People.Customer(_f, "Ola", "Haugland", new DateOnly(1959, 5, 17));
        var karin = await _f.Karin();
        var k = await _f.Send(HttpMethod.Post, Ports.Kyc, $"/api/v1/customers/{id}/kyc-cases", null, NordvikFixture.Bearer(karin));
        Assert.Equal("PENDING_REVIEW", k.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Kyc, $"/api/v1/customers/{id}/kyc-cases", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "KYC_CASE_ALREADY_OPEN");
        var path = $"/api/v1/kyc-cases/{k.Str("id")}/decision";
        (await _f.Send(HttpMethod.Post, Ports.Kyc, path, "{\"decision\":\"APPROVE\",\"reason\":\"ok\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(403, "FOUR_EYES_VIOLATION");
        var other = (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"Compliance\",\"sub\":\"compliance.lena\",\"scope\":\"customers:read\"}")).Str("token");
        (await _f.Send(HttpMethod.Post, Ports.Kyc, path, "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(other))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var d = await _f.Send(HttpMethod.Post, Ports.Kyc, path, "{\"decision\":\"APPROVE\",\"reason\":\"PEP extranjero, diligencia reforzada hecha\"}", NordvikFixture.Bearer(other));
        Assert.Equal("APPROVED", d.Str("status"));
        Assert.Equal("compliance.lena", d.Str("decidedBy"));
        (await _f.Send(HttpMethod.Post, Ports.Kyc, path, "{\"decision\":\"REJECT\",\"reason\":\"x\"}", NordvikFixture.Bearer(other))).ShouldBeProblem(409, "KYC_CASE_NOT_PENDING");
        var cases = await Eventually.Until(async () => await _f.Get(Ports.Cases, $"/api/v1/cases?customerId={id}&type=KYC_REVIEW", NordvikFixture.Bearer(karin)), r => r.Int("count") == 1, 10000, "caso KYC_REVIEW");
        Assert.Equal("HIGH", cases.Json.GetProperty("items")[0].GetProperty("priority").GetString());
    }
}

[Collection("nordvik")]
public class SuitabilityTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public SuitabilityTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Assess(string id, string answers) =>
        await _f.Send(HttpMethod.Post, Ports.Suitability, $"/api/v1/customers/{id}/assessments", $"{{\"answers\":{answers}}}", NordvikFixture.Bearer(await _f.Erik()));
    private async Task<Reply> Check(string id, string isin) =>
        await _f.Send(HttpMethod.Post, Ports.Suitability, "/api/v1/suitability-checks", JsonSerializer.Serialize(new { customerId = id, isin }), NordvikFixture.Bearer(await _f.Erik()));

    [Fact]
    public async Task The_profile_decides_which_funds_are_suitable()
    {
        var q = await _f.Get(Ports.Suitability, "/api/v1/questionnaire");
        Assert.Equal(7, q.Json.GetProperty("questions").GetArrayLength());
        Assert.DoesNotContain("points", q.Body);
        var bold = await People.Clean(_f);
        (await Check(bold, "SE0000000001")).ShouldBeProblem(409, "ASSESSMENT_REQUIRED");
        var a = await Assess(bold, "{\"Q1\":\"C\",\"Q2\":\"C\",\"Q3\":\"C\",\"Q4\":\"D\",\"Q5\":\"C\",\"Q6\":\"C\",\"Q7\":\"A\"}");
        Assert.Equal(7, a.Int("riskProfile"));
        Assert.Equal("LONG", a.Str("horizon"));
        Assert.Equal("SUITABLE", (await Check(bold, "SE0000000006")).Str("result"));
        var closed = await Check(bold, "SE0000000007");
        Assert.Equal("NOT_SUITABLE", closed.Str("result"));

        var careful = await People.Clean(_f);
        var c = await Assess(careful, "{\"Q1\":\"C\",\"Q2\":\"C\",\"Q3\":\"C\",\"Q4\":\"A\",\"Q5\":\"C\",\"Q6\":\"C\",\"Q7\":\"B\"}");
        Assert.Equal(2, c.Int("riskProfile"));
        Assert.True(c.Json.GetProperty("sustainabilityPreference").GetBoolean());
        var no = await Check(careful, "SE0000000002");
        Assert.Equal("NOT_SUITABLE", no.Str("result"));
        Assert.Equal(2, no.Json.GetProperty("reasons").GetArrayLength());
        (await Check(careful, "SE9999999999")).ShouldBeProblem(422, "UNKNOWN_FUND");
        (await Assess(careful, "{\"Q1\":\"C\",\"Q9\":\"A\"}")).ShouldBeProblem(422, "INCOMPLETE_QUESTIONNAIRE");
    }

    [Fact]
    public async Task Assessments_expire_after_two_years()
    {
        await _f.Freeze("2026-11-02T10:00:00");
        var id = await People.Clean(_f);
        await Assess(id, "{\"Q1\":\"C\",\"Q2\":\"B\",\"Q3\":\"B\",\"Q4\":\"B\",\"Q5\":\"B\",\"Q6\":\"B\",\"Q7\":\"A\"}");
        Assert.Equal("SUITABLE", (await Check(id, "SE0000000101")).Str("result"));
        await _f.Freeze("2028-11-03T10:00:00");
        (await Check(id, "SE0000000101")).ShouldBeProblem(409, "ASSESSMENT_EXPIRED");
        Assert.True((await _f.Get(Ports.Suitability, $"/api/v1/customers/{id}/assessments/latest", NordvikFixture.Bearer(await _f.Erik()))).Json.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public async Task Complex_funds_need_an_appropriateness_test_and_a_warning_can_be_acknowledged()
    {
        Assert.False((await _f.Get(Ports.Appropriateness, "/api/v1/products/SE0000000101/complexity")).Json.GetProperty("complex").GetBoolean());
        Assert.True((await _f.Get(Ports.Appropriateness, "/api/v1/products/SE0000000006/complexity")).Json.GetProperty("complex").GetBoolean());
        var ana = await _f.Ana();
        string Path() => $"/api/v1/customers/{SeedIds.CustomerAna}/appropriateness-tests";
        Assert.Equal("NOT_REQUIRED", (await _f.Send(HttpMethod.Post, Ports.Appropriateness, Path(), "{\"isin\":\"SE0000000101\"}", NordvikFixture.Bearer(ana))).Str("result"));
        (await _f.Send(HttpMethod.Post, Ports.Appropriateness, Path(), "{\"isin\":\"SE0000000006\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(422, "INCOMPLETE_QUIZ");
        var bad = await _f.Send(HttpMethod.Post, Ports.Appropriateness, Path(), "{\"isin\":\"SE0000000006\",\"answers\":{\"K1\":\"A\",\"K2\":\"B\",\"K3\":\"A\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal("NOT_APPROPRIATE", bad.Str("result"));
        Assert.False(bad.Json.GetProperty("mayProceed").GetBoolean());
        Assert.Contains("complejo", bad.Str("warning"));
        var ack = $"/api/v1/appropriateness-tests/{bad.Str("id")}/acknowledge";
        (await _f.Send(HttpMethod.Post, Ports.Appropriateness, ack, null, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.Appropriateness, ack, null, NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "TEST_NOT_FOUND");
        Assert.True((await _f.Send(HttpMethod.Post, Ports.Appropriateness, ack, null, NordvikFixture.Bearer(ana))).Json.GetProperty("mayProceed").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Appropriateness, ack, null, NordvikFixture.Bearer(ana))).ShouldBeProblem(409, "ALREADY_ACKNOWLEDGED");
        var good = await _f.Send(HttpMethod.Post, Ports.Appropriateness, Path(), "{\"isin\":\"SE0000000006\",\"answers\":{\"K1\":\"B\",\"K2\":\"A\",\"K3\":\"B\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal("APPROPRIATE", good.Str("result"));
        Assert.Equal(2, good.Int("correctAnswers"));
    }
}
