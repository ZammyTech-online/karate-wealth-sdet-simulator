using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Pasos reutilizables de pensiones.</summary>
public static class Pensions
{
    public static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Un número de organización sueco válido (tercer dígito ≥ 2 y Luhn), distinto cada vez.</summary>
    public static string Org()
    {
        var d = new int[10];
        for (var i = 0; i < 9; i++) d[i] = Random.Shared.Next(0, 10);
        d[0] = 5; d[2] = Random.Shared.Next(2, 10);
        var sum = 0;
        for (var i = 0; i < 9; i++) { var x = d[i] * (i % 2 == 0 ? 2 : 1); sum += x > 9 ? x - 9 : x; }
        d[9] = (10 - sum % 10) % 10;
        var s = string.Concat(d);
        return s[..6] + "-" + s[6..];
    }

    public static async Task<Reply> Plan(NordvikFixture f, string customerId, string type = "PRIVATE", string? org = null, string? agreement = null, string isin = "SE0000000203")
    {
        var r = await f.Send(HttpMethod.Post, Ports.PensionPlans, "/api/v1/pension-plans",
            JsonSerializer.Serialize(new { customerId, type, employerOrgNumber = org, agreement, allocation = new[] { new { isin, percent = 100 } } }), NordvikFixture.Bearer(await f.Erik()));
        return r;
    }

    public static async Task<string> PlanId(NordvikFixture f, string customerId, string type = "PRIVATE", string? org = null, string? agreement = null)
    {
        var r = await Plan(f, customerId, type, org, agreement);
        Assert.True(r.Code == 201, r.Body);
        return r.Str("id");
    }

    public static async Task<Reply> Contribute(NordvikFixture f, string planId, string amount, string period = "2026-10", string? reference = null, string source = "PRIVATE") =>
        await f.Send(HttpMethod.Post, Ports.Contributions, $"/api/v1/pension-plans/{planId}/contributions",
            JsonSerializer.Serialize(new { amount, source, period, reference = reference ?? Guid.NewGuid().ToString("N")[..8] }), NordvikFixture.Bearer(await f.Olof()));

    /// <summary>Plan PRIVATE con dinero invertido: aporta el martes de prueba y ejecuta las órdenes a las 18:05.</summary>
    public static async Task<(string CustomerId, string PlanId)> FundedPlan(NordvikFixture f, DateOnly birth, string amount = "250000.00")
    {
        await f.Freeze($"{TestData.Tuesday}T10:00:00");
        var customer = await People.Customer(f, "Greta", "Pensionär", birth);
        var plan = await PlanId(f, customer);
        var c = await Contribute(f, plan, amount);
        Assert.True(c.Code == 201, c.Body);
        var orderId = c.Json.GetProperty("orders")[0].GetProperty("orderId").GetString()!;
        var exec = await Flows.ExecuteAt1805(f, orderId, TestData.Tuesday);
        Assert.Equal("EXECUTED", exec.Str("status"));
        return (customer, plan);
    }

    public static async Task<Reply> PlanStatus(NordvikFixture f, string planId) => await f.Get(Ports.PensionPlans, $"/api/v1/internal/pension-plans/{planId}");
}

[Collection("nordvik")]
public class PensionRulesTests
{
    private readonly NordvikFixture _f;
    public PensionRulesTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Agreements_compute_employer_contributions_by_tranche()
    {
        var all = await _f.Get(Ports.Agreements, "/api/v1/agreements");
        Assert.Equal(200, all.Code);
        Assert.Equal(new[] { "ITP1", "ITP2", "SAF-LO", "AKAP-KR", "PA16", "NONE" }, all.Json.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("code").GetString()));
        Assert.Equal("80600.00", all.Str("incomeBaseAmount"));
        Assert.Equal("50375.00", all.Str("capMonthlyAt75"));
        var low = await _f.Post(Ports.Agreements, "/api/v1/agreements/ITP1/contribution", "{\"monthlySalary\":\"40000.00\",\"age\":40}");
        Assert.Equal("1800.00", low.Json.GetProperty("employerMonthly").GetProperty("amount").GetString());
        var high = await _f.Post(Ports.Agreements, "/api/v1/agreements/itp1/contribution", "{\"monthlySalary\":\"60375.00\",\"age\":40}");
        // 50375 × 4,5 % + 10000 × 30 %
        Assert.Equal("5266.88", high.Json.GetProperty("employerMonthly").GetProperty("amount").GetString());
        var young = await _f.Post(Ports.Agreements, "/api/v1/agreements/ITP1/contribution", "{\"monthlySalary\":\"30000.00\",\"age\":22}");
        Assert.False(young.Json.GetProperty("eligible").GetBoolean());
        (await _f.Get(Ports.Agreements, "/api/v1/agreements/XYZ")).ShouldBeProblem(404, "AGREEMENT_NOT_FOUND");
        (await _f.Post(Ports.Agreements, "/api/v1/agreements/ITP1/contribution", "{\"age\":40}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Annuities_are_deterministic_and_validated()
    {
        const string body = "{\"capital\":\"1200000.00\",\"age\":65,\"gender\":\"F\",\"type\":\"TERM\",\"termYears\":10}";
        var a = await _f.Post(Ports.Annuities, "/api/v1/annuities/quote", body);
        var b = await _f.Post(Ports.Annuities, "/api/v1/annuities/quote", body);
        Assert.Equal(200, a.Code);
        Assert.Equal(a.Body, b.Body);
        Assert.Equal(120, a.Int("expectedPayments"));
        var monthly = Pensions.D(a.Json.GetProperty("monthlyPayment").GetProperty("amount").GetString()!);
        Assert.InRange(monthly, 10000m, 11200m); // 1,2 M en 120 pagos con interés técnico del 2 %: algo más de 10 000
        var life = await _f.Post(Ports.Annuities, "/api/v1/annuities/quote", "{\"capital\":\"1200000.00\",\"age\":65,\"gender\":\"M\",\"type\":\"LIFE\"}");
        var lifeF = await _f.Post(Ports.Annuities, "/api/v1/annuities/quote", "{\"capital\":\"1200000.00\",\"age\":65,\"gender\":\"F\",\"type\":\"LIFE\"}");
        Assert.True(Pensions.D(life.Json.GetProperty("monthlyPayment").GetProperty("amount").GetString()!) > Pensions.D(lifeF.Json.GetProperty("monthlyPayment").GetProperty("amount").GetString()!),
            "con la misma prima, una mujer (vive más) cobra menos al mes");
        var m = await _f.Get(Ports.Annuities, "/api/v1/mortality?age=65&gender=M");
        Assert.InRange(m.Json.GetProperty("lifeExpectancyYears").GetDouble(), 18, 25);
        var bad = await _f.Post(Ports.Annuities, "/api/v1/annuities/quote", "{\"capital\":\"-1\",\"age\":50,\"type\":\"TERM\"}");
        bad.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(new[] { "capital", "age", "termYears" }, bad.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()));
        (await _f.Get(Ports.Annuities, "/api/v1/mortality?age=10")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Salary_exchange_warns_below_the_threshold_and_needs_acknowledgement()
    {
        var sim = await _f.Post(Ports.SalaryExchange, "/api/v1/salary-exchanges/simulate", "{\"monthlySalary\":\"56000.00\",\"exchangeAmount\":\"5000.00\",\"agreement\":\"ITP1\"}");
        Assert.Equal(200, sim.Code);
        Assert.Equal("54203.50", sim.Str("thresholdMonthly"));
        Assert.True(sim.Json.GetProperty("belowThreshold").GetBoolean());
        var ok = await _f.Post(Ports.SalaryExchange, "/api/v1/salary-exchanges/simulate", "{\"monthlySalary\":\"80000.00\",\"exchangeAmount\":\"5000.00\"}");
        Assert.False(ok.Json.GetProperty("belowThreshold").GetBoolean());
        (await _f.Post(Ports.SalaryExchange, "/api/v1/salary-exchanges/simulate", "{\"monthlySalary\":\"5000.00\",\"exchangeAmount\":\"6000.00\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");

        var customer = await People.Clean(_f);
        var org = Pensions.Org();
        var privatePlan = await Pensions.PlanId(_f, customer);
        var sxPlan = await Pensions.PlanId(_f, customer, "SALARY_EXCHANGE", org);
        var erik = NordvikFixture.Bearer(await _f.Erik());
        string Body(string plan, bool? ack = null) => JsonSerializer.Serialize(new { planId = plan, monthlySalary = "56000.00", exchangeAmount = "5000.00", startMonth = "2026-11", acknowledgeBelowThreshold = ack });
        (await _f.Send(HttpMethod.Post, Ports.SalaryExchange, $"/api/v1/customers/{customer}/salary-exchanges", Body(privatePlan), erik)).ShouldBeProblem(422, "WRONG_PLAN_TYPE");
        (await _f.Send(HttpMethod.Post, Ports.SalaryExchange, $"/api/v1/customers/{customer}/salary-exchanges", Body(sxPlan), erik)).ShouldBeProblem(422, "SALARY_BELOW_THRESHOLD");
        var created = await _f.Send(HttpMethod.Post, Ports.SalaryExchange, $"/api/v1/customers/{customer}/salary-exchanges", Body(sxPlan, true), erik);
        Assert.True(created.Code == 201, created.Body);
        var list = await _f.Get(Ports.SalaryExchange, $"/api/v1/customers/{customer}/salary-exchanges", erik);
        Assert.Equal(1, list.Int("count"));
        await TestData.Events(_f, "salary-exchange.created", created.Str("id"));
    }

    [Fact]
    public async Task Plans_validate_type_employer_and_agreement_and_check_eligibility()
    {
        var customer = await People.Customer(_f, "Ida", "Yngre", new DateOnly(1990, 6, 1));
        (await Pensions.Plan(_f, customer, "BOGUS")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Pensions.Plan(_f, customer, "OCCUPATIONAL", Pensions.Org())).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Pensions.Plan(_f, customer, "OCCUPATIONAL", Pensions.Org(), "XYZ")).ShouldBeProblem(422, "AGREEMENT_NOT_FOUND");
        var org = Pensions.Org();
        var occ = await Pensions.Plan(_f, customer, "OCCUPATIONAL", org, "itp1");
        Assert.Equal(201, occ.Code);
        Assert.Equal("ITP1", occ.Str("agreement"));
        Assert.Equal("ACTIVE", occ.Str("status"));
        Assert.StartsWith("tok_cu_", occ.Str("customerRef"));
        (await Pensions.Plan(_f, customer, "OCCUPATIONAL", org, "ITP1")).ShouldBeProblem(409, "PLAN_ALREADY_EXISTS");
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var el = await _f.Get(Ports.PensionPlans, $"/api/v1/pension-plans/{occ.Str("id")}/eligibility", erik);
        Assert.False(el.Json.GetProperty("eligible").GetBoolean()); // 36 años: antes de los 55 no se cobra
        var vest = await _f.Get(Ports.Vesting, $"/api/v1/pension-plans/{occ.Str("id")}/vesting", erik);
        Assert.Equal(200, vest.Code);
        Assert.Equal(100m, vest.Json.GetProperty("vestedPct").GetDecimal()); // ITP1 consolida desde el primer día
        var itp2 = await Pensions.PlanId(_f, customer, "OCCUPATIONAL", Pensions.Org(), "ITP2");
        var v2 = await _f.Get(Ports.Vesting, $"/api/v1/pension-plans/{itp2}/vesting", erik);
        Assert.Equal(3, v2.Int("vestingYears"));
        Assert.Equal(0m, v2.Json.GetProperty("vestedPct").GetDecimal()); // ITP2 pide 3 años en la empresa y no hay empleo registrado
        var priv = await Pensions.PlanId(_f, customer);
        Assert.Equal(100m, (await _f.Get(Ports.Vesting, $"/api/v1/pension-plans/{priv}/vesting", erik)).Json.GetProperty("vestedPct").GetDecimal());
        (await _f.Get(Ports.PensionPlans, $"/api/v1/pension-plans/{priv}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "PLAN_NOT_FOUND");
        var plans = await _f.Get(Ports.PensionPlans, $"/api/v1/customers/{customer}/pension-plans", erik);
        Assert.Equal(3, plans.Int("count"));
        await TestData.Events(_f, "pension-plan.created", priv);
    }

    [Fact]
    public async Task C7_and_C10_compose_the_whole_plan_and_the_portability_export()
    {
        var c = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, c);
        var plan = await Pensions.PlanId(_f, c);
        Assert.Equal(201, (await Pensions.Contribute(_f, plan, "1000.00")).Code);
        Assert.Equal(201, (await Pensions.Contribute(_f, plan, "500.00")).Code);
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var r = await _f.Get(Ports.Composition, $"/api/v2/compositions/pension-plan/{plan}", erik);
        Assert.True(r.Code == 200, r.Body);
        Assert.Equal("COMPLETE", r.Str("status"));
        var b = r.Json.GetProperty("bundle");
        Assert.Equal(2, b.GetProperty("contributions").GetProperty("count").GetInt32());
        Assert.Equal("1500.00", b.GetProperty("contributions").GetProperty("total").GetString());
        Assert.Equal("PRIVATE", b.GetProperty("contributions").GetProperty("bySource")[0].GetProperty("source").GetString());
        Assert.Equal(100m, b.GetProperty("vesting").GetProperty("vestedPct").GetDecimal());
        Assert.Equal(0, b.GetProperty("payoutPlans").GetArrayLength());
        Assert.Equal(6, r.Json.GetProperty("sources").GetArrayLength());
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/pension-plan/{plan}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "PLAN_NOT_FOUND");
        var x = await _f.Get(Ports.Composition, $"/api/v2/compositions/portability-export/{c}", erik);
        Assert.Equal("nordvik-portability-v1", x.Json.GetProperty("bundle").GetProperty("format").GetString());
        Assert.DoesNotContain(nid, x.Body); // el asesor ve el número enmascarado: cada origen aplica sus permisos
        Assert.Equal(1, x.Json.GetProperty("bundle").GetProperty("pensionPlans").GetArrayLength());
    }

    [Fact]
    public async Task Beneficiaries_need_if_match_close_family_and_exactly_100_percent()
    {
        var customer = await People.Clean(_f);
        var plan = await Pensions.PlanId(_f, customer);
        var erik = await _f.Erik();
        var get = await _f.Get(Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries", NordvikFixture.Bearer(erik));
        Assert.Equal("\"0\"", get.Headers.ETag?.Tag);
        const string ok = "{\"beneficiaries\":[{\"name\":\"Elsa\",\"relation\":\"CHILD\",\"percent\":60},{\"name\":\"Nils\",\"relation\":\"SPOUSE\",\"percent\":40}]}";
        (await _f.Send(HttpMethod.Put, Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries", ok, NordvikFixture.Bearer(erik))).ShouldBeProblem(428, "PRECONDITION_REQUIRED");
        var bad = await _f.Send(HttpMethod.Put, Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries",
            "{\"beneficiaries\":[{\"name\":\"A\",\"relation\":\"CHILD\",\"percent\":60},{\"name\":\"B\",\"relation\":\"NEIGHBOUR\",\"percent\":30}]}", NordvikFixture.Bearer(erik, "\"0\""));
        bad.ShouldBeProblem(422, "INVALID_BENEFICIARIES");
        var put = await _f.Send(HttpMethod.Put, Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries", ok, NordvikFixture.Bearer(erik, "\"0\""));
        Assert.True(put.Code == 200, put.Body);
        Assert.Equal("\"1\"", put.Headers.ETag?.Tag);
        (await _f.Send(HttpMethod.Put, Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries", ok, NordvikFixture.Bearer(erik, "\"0\""))).ShouldBeProblem(412, "VERSION_MISMATCH");
    }

    [Fact]
    public async Task Public_pension_rights_come_from_a_signed_envelope_and_respect_maintenance()
    {
        var customer = await People.Clean(_f);
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var r = await _f.Get(Ports.PublicPensionAdapter, $"/api/v1/customers/{customer}/public-pension", erik);
        Assert.True(r.Code == 200, r.Body);
        Assert.InRange(r.Int("earliestWithdrawalAge"), 61, 65);
        Assert.Equal(66, r.Int("guaranteePensionAge"));
        Assert.Equal("SEK", r.Json.GetProperty("estimatedMonthlyPension").GetProperty("currency").GetString());
        var again = await _f.Get(Ports.PublicPensionAdapter, $"/api/v1/customers/{customer}/public-pension", erik);
        Assert.Equal(r.Json.GetProperty("estimatedMonthlyPension").GetProperty("amount").GetString(), again.Json.GetProperty("estimatedMonthlyPension").GetProperty("amount").GetString());
        // Un sobre mal firmado: el simulador lo rechaza
        (await _f.Post(Ports.PublicPensionSim, "/v1/envelope", "{\"header\":{\"signature\":\"00\"},\"body\":{\"operation\":\"GetPensionRights\"}}")).Code.Equals(401);
        await _f.Post(Ports.PublicPensionSim, "/__admin/maintenance", "{\"on\":true,\"retryAfterSeconds\":90}");
        try
        {
            var m = await _f.Get(Ports.PublicPensionAdapter, $"/api/v1/customers/{customer}/public-pension", erik);
            m.ShouldBeProblem(503, "PUBLIC_PENSION_MAINTENANCE");
            Assert.Equal("90", m.Headers.RetryAfter?.ToString());
        }
        finally { await _f.Post(Ports.PublicPensionSim, "/__admin/maintenance", "{\"on\":false}"); }
        (await _f.Get(Ports.PublicPensionAdapter, "/api/v1/customers/no-existe/public-pension", erik)).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class EmployerPensionTests
{
    private readonly NordvikFixture _f;
    public EmployerPensionTests(NordvikFixture f) => _f = f;

    private async Task<(string Org, string Key)> Employer(string agreement = "ITP1")
    {
        var org = Pensions.Org();
        var r = await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employers", JsonSerializer.Serialize(new { orgNumber = org, name = "Fjällbygg AB", agreement }), NordvikFixture.Bearer(await _f.Admin()));
        Assert.True(r.Code == 201, r.Body);
        Assert.StartsWith("ek_", r.Str("apiKey"));
        return (org, r.Str("apiKey"));
    }

    private static Action<HttpRequestMessage> Key(string key) => req => req.Headers.TryAddWithoutValidation("X-Employer-Key", key);

    [Fact]
    public async Task Employers_get_a_key_once_and_only_see_their_own_masked_employees()
    {
        var (org, key) = await Employer();
        (await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employers", JsonSerializer.Serialize(new { orgNumber = org, name = "Otra", agreement = "ITP1" }), NordvikFixture.Bearer(await _f.Admin())))
            .ShouldBeProblem(409, "EMPLOYER_ALREADY_EXISTS");
        (await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employers", "{\"orgNumber\":\"556000-0000\",\"name\":\"X\",\"agreement\":\"ITP1\"}", NordvikFixture.Bearer(await _f.Admin())))
            .ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employers", JsonSerializer.Serialize(new { orgNumber = Pensions.Org(), name = "X", agreement = "ITP1" }), NordvikFixture.Bearer(await _f.Erik())))
            .ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var customer = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, customer);
        var add = await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employer/employees", JsonSerializer.Serialize(new { personalNumber = nid }), Key(key));
        Assert.True(add.Code == 201, add.Body);
        Assert.Equal("ITP1", add.Str("agreement"));
        Assert.DoesNotContain(nid[^4..], add.Str("personalNumberMasked"));
        var plan = await Pensions.PlanStatus(_f, add.Str("planId"));
        Assert.Equal("OCCUPATIONAL", plan.Str("type"));
        Assert.Equal(org, plan.Str("employerOrgNumber"));
        (await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employer/employees", JsonSerializer.Serialize(new { personalNumber = nid }), Key(key))).ShouldBeProblem(409, "PLAN_ALREADY_EXISTS");
        var stranger = (await _f.NewPerson()).GetProperty("nationalId").GetString();
        (await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employer/employees", JsonSerializer.Serialize(new { personalNumber = stranger }), Key(key))).ShouldBeProblem(422, "EMPLOYEE_NOT_CUSTOMER");
        (await _f.Get(Ports.EmployerPortal, "/api/v1/employer/employees")).ShouldBeProblem(401, "EMPLOYER_KEY_REQUIRED");
        (await _f.Get(Ports.EmployerPortal, "/api/v1/employer/employees", Key("ek_falsa"))).ShouldBeProblem(401, "EMPLOYER_KEY_REQUIRED");
        var mine = await _f.Get(Ports.EmployerPortal, "/api/v1/employer/employees", Key(key));
        Assert.Equal(1, mine.Int("count"));
        Assert.DoesNotContain(nid, mine.Body);
        var (_, otherKey) = await Employer();
        Assert.Equal(0, (await _f.Get(Ports.EmployerPortal, "/api/v1/employer/employees", Key(otherKey))).Int("count"));
    }

    [Fact]
    public async Task A_clearing_file_is_signed_validated_line_by_line_contributed_and_acknowledged()
    {
        var (org, key) = await Employer();
        var customer = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, customer);
        var add = await _f.Send(HttpMethod.Post, Ports.EmployerPortal, "/api/v1/employer/employees", JsonSerializer.Serialize(new { personalNumber = nid }), Key(key));
        Assert.Equal(201, add.Code);
        var stranger = (await _f.NewPerson()).GetProperty("nationalId").GetString();
        var other = await People.Clean(_f); // cliente, pero sin plan con esta empresa
        var otherNid = await TestData.NationalIdOf(_f, other);
        var file = await _f.Post(Ports.ClearingSim, "/__admin/files", JsonSerializer.Serialize(new
        {
            employerOrgNumber = org, period = "2026-10",
            lines = new[]
            {
                new { personalNumber = nid, amount = "2500.00", agreement = "ITP1" },
                new { personalNumber = nid, amount = "2500.00", agreement = "ITP1" },
                new { personalNumber = stranger, amount = "1000.00", agreement = "ITP1" },
                new { personalNumber = "19991399-0000", amount = "1000.00", agreement = "ITP1" },
                new { personalNumber = otherNid, amount = "1000.00", agreement = "ITP1" },
                new { personalNumber = nid, amount = "-5.00", agreement = "ITP1" },
            },
        }));
        Assert.True(file.Code == 201, file.Body);
        Assert.Equal(200, file.Int("webhookStatus"));
        var fileId = file.Str("fileId");
        var receipt = await _f.Get(Ports.ClearingSim, $"/__admin/files/{fileId}/receipt");
        Assert.True(receipt.Code == 200, receipt.Body);
        Assert.Equal(1, receipt.Int("accepted"));
        Assert.Equal("2500.00", receipt.Json.GetProperty("acceptedAmount").GetProperty("amount").GetString());
        var codes = receipt.Json.GetProperty("rejected").EnumerateArray().ToDictionary(x => x.GetProperty("line").GetInt32(), x => x.GetProperty("code").GetString());
        Assert.Equal("DUPLICATE_MONTH", codes[2]);
        Assert.Equal("UNKNOWN_EMPLOYEE", codes[3]);
        Assert.Equal("INVALID_PERSONAL_NUMBER", codes[4]);
        Assert.Equal("NO_PLAN_WITH_EMPLOYER", codes[5]);
        Assert.Equal("NEGATIVE_AMOUNT", codes[6]);
        Assert.DoesNotContain(nid, receipt.Body);

        var olof = NordvikFixture.Bearer(await _f.Olof());
        var deliveries = await _f.Get(Ports.ClearingAdapter, "/api/v1/deliveries", olof);
        var mine = deliveries.Json.GetProperty("items").EnumerateArray().Single(d => d.GetProperty("fileId").GetString() == fileId);
        var ingestId = mine.GetProperty("ingestId").GetString()!;
        await TestData.Events(_f, "employer-file.processed", ingestId);
        var detail = await _f.Get(Ports.EmployerFiles, $"/api/v1/employer-files/{ingestId}", olof);
        Assert.Equal(6, detail.Json.GetProperty("lines").GetArrayLength());
        Assert.StartsWith("ctb_", detail.Json.GetProperty("lines")[0].GetProperty("contributionId").GetString());
        var queue = await _f.Get(Ports.EmployerFiles, "/api/v1/error-queue?code=UNKNOWN_EMPLOYEE", olof);
        Assert.Contains(queue.Json.GetProperty("items").EnumerateArray(), x => x.GetProperty("fileId").GetString() == fileId);

        // El mismo fileId otra vez: el ingest devuelve el resultado guardado (no aporta dos veces)
        var replay = await _f.Send(HttpMethod.Post, Ports.EmployerFiles, "/api/v1/employer-files",
            JsonSerializer.Serialize(new { fileId, employerOrgNumber = org, period = "2026-10", csv = "personnummer;belopp;avtal;period\n" }), olof);
        Assert.Equal(200, replay.Code);
        Assert.Equal(ingestId, replay.Str("id"));
        // Un mes ya aportado por otro fichero: DUPLICATE_MONTH
        var again = await _f.Send(HttpMethod.Post, Ports.EmployerFiles, "/api/v1/employer-files",
            JsonSerializer.Serialize(new { employerOrgNumber = org, period = "2026-10", csv = $"personnummer;belopp;avtal;period\n{nid};2500.00;ITP1;2026-10\n{nid};2500.00;SAF-LO;2026-11\n" }), olof);
        Assert.Equal(201, again.Code);
        Assert.Equal("DUPLICATE_MONTH", again.Json.GetProperty("receipt").GetProperty("rejected")[0].GetProperty("code").GetString());
        Assert.Equal("AGREEMENT_MISMATCH", again.Json.GetProperty("receipt").GetProperty("rejected")[1].GetProperty("code").GetString());

        var contribs = await _f.Get(Ports.EmployerPortal, "/api/v1/employer/contributions?period=2026-10", Key(key));
        Assert.Equal(1, contribs.Int("count"));
        Assert.Equal("2500.00", contribs.Json.GetProperty("total").GetProperty("amount").GetString());
        (await _f.Get(Ports.EmployerFiles, $"/api/v1/employer-files/{ingestId}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task A_webhook_with_a_wrong_signature_is_rejected()
    {
        var body = "{\"eventType\":\"FILE_AVAILABLE\",\"fileId\":\"cf_x\",\"employerOrgNumber\":\"556036-0793\",\"period\":\"2026-10\"}";
        var r = await _f.Send(HttpMethod.Post, Ports.ClearingAdapter, "/api/v1/webhooks/clearing", body, req => req.Headers.TryAddWithoutValidation("X-Signature", "sha256=" + Canonical.HmacHex("otro-secreto", body)));
        r.ShouldBeProblem(401, "INVALID_SIGNATURE");
        (await _f.Post(Ports.ClearingAdapter, "/api/v1/webhooks/clearing", body)).ShouldBeProblem(401, "INVALID_SIGNATURE");
        var good = await _f.Send(HttpMethod.Post, Ports.ClearingAdapter, "/api/v1/webhooks/clearing", body, req => req.Headers.TryAddWithoutValidation("X-Signature", "sha256=" + Canonical.HmacHex("e9-clearing-webhook-2026", body)));
        good.ShouldBeProblem(502, "FILE_DOWNLOAD_FAILED"); // firma buena, pero el fichero cf_x no existe en el clearing
    }
}

[Collection("nordvik")]
public class TransferTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public TransferTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze($"{TestData.Tuesday}T10:00:00");
    public Task DisposeAsync() => _f.ResetClock();

    private static string Policy() => "P" + Random.Shared.Next(100000, 999999);

    private async Task Seed(string provider, string policy, string value, int delayDays, decimal driftPct, string? personalNumber = null) =>
        Assert.Equal(200, (await _f.Post(Ports.ProviderSim, "/__admin/policies",
            JsonSerializer.Serialize(new { providerCode = provider, policyNumber = policy, value, delayDays, driftPct, personalNumber }))).Code);

    private async Task<Reply> Start(string customer, string plan, string provider, string policy) =>
        await _f.Send(HttpMethod.Post, Ports.Transfers, "/api/v1/transfers", JsonSerializer.Serialize(new { customerId = customer, targetPlanId = plan, providerCode = provider, policyNumber = policy }), NordvikFixture.Bearer(await _f.Erik()));

    private async Task<Reply> Act(string id, string verb, string? token = null, string? body = null) =>
        await _f.Send(HttpMethod.Post, Ports.Transfers, $"/api/v1/transfers/{id}/{verb}", body, NordvikFixture.Bearer(token ?? await _f.Erik()));

    private async Task<Reply> Get(string id) => await _f.Get(Ports.Transfers, $"/api/v1/transfers/{id}", NordvikFixture.Bearer(await _f.Erik()));

    [Fact]
    public async Task A_transfer_waits_for_the_valuation_then_needs_confirmation_and_approval()
    {
        var customer = await People.Clean(_f);
        var plan = await Pensions.PlanId(_f, customer);
        var policy = Policy();
        await Seed("FJORDLIV", policy, "120000.00", 2, 0.5m);
        var t = await Start(customer, plan, "FJORDLIV", policy);
        Assert.True(t.Code == 201, t.Body);
        Assert.Equal("VALUATION_PENDING", t.Str("status"));
        var id = t.Str("id");
        (await Start(customer, plan, "FJORDLIV", policy)).ShouldBeProblem(409, "TRANSFER_ALREADY_IN_PROGRESS");
        (await Act(id, "confirm")).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await Act(id, "approve", await _f.Olof())).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await Act(id, "approve")).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");

        await _f.Freeze("2026-10-21T10:00:00"); // 1 día: la gestora aún no contesta
        Assert.Equal("VALUATION_PENDING", (await Get(id)).Str("status"));
        await _f.Freeze("2026-10-22T10:30:00"); // 2 días: ya está
        var valued = await Get(id);
        Assert.Equal("VALUED", valued.Str("status"));
        Assert.Equal("120000.00", valued.Str("valuedAmount"));
        var val = await _f.Get(Ports.TransferValuations, $"/api/v1/transfer-valuations/{valued.Str("valuationId")}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("READY", val.Str("status"));
        Assert.StartsWith("2026-11-2", val.Str("validUntil")); // 30 días de validez

        Assert.Equal("AWAITING_APPROVAL", (await Act(id, "confirm")).Str("status"));
        var done = await Act(id, "approve", await _f.Olof());
        Assert.True(done.Code == 200, done.Body);
        Assert.Equal("COMPLETED", done.Str("status"));
        Assert.Equal("120600.00", done.Str("receivedAmount"));
        Assert.Equal("0.50", done.Str("deviationPct"));
        Assert.StartsWith("ctb_", done.Str("contributionId"));
        Assert.Equal(new[] { "VALUATION_PENDING", "VALUED", "AWAITING_APPROVAL", "COMPLETED" }, done.Json.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("to").GetString()));
        (await Act(id, "cancel")).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        var contrib = await _f.Get(Ports.Contributions, $"/api/v1/contributions/{done.Str("contributionId")}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("TRANSFER", contrib.Str("source"));
        Assert.Equal("120600.00", contrib.Json.GetProperty("amount").GetProperty("amount").GetString());
        await TestData.Events(_f, "transfer.completed", id);
        Assert.Equal(1, (await _f.Get(Ports.Transfers, $"/api/v1/customers/{customer}/transfers", NordvikFixture.Bearer(await _f.Erik()))).Int("count"));

        // C8 · el expediente completo en una sola llamada
        var dossier = await _f.Get(Ports.Composition, $"/api/v2/compositions/transfer-dossier/{id}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.True(dossier.Code == 200, dossier.Body);
        Assert.Equal("COMPLETE", dossier.Str("status"));
        var bundle = dossier.Json.GetProperty("bundle");
        Assert.Equal(4, bundle.GetProperty("timeline").GetArrayLength());
        Assert.Equal("TRANSFER", bundle.GetProperty("contribution").GetProperty("source").GetString());
        Assert.Equal("READY", bundle.GetProperty("valuation").GetProperty("status").GetString());
        Assert.Equal(plan, bundle.GetProperty("targetPlan").GetProperty("id").GetString());
        Assert.Equal(4, dossier.Json.GetProperty("sources").GetArrayLength());
        (await _f.Get(Ports.Composition, $"/api/v2/compositions/transfer-dossier/{id}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task An_amount_outside_the_2_percent_tolerance_goes_to_manual_review()
    {
        var customer = await People.Clean(_f);
        var plan = await Pensions.PlanId(_f, customer);
        var p1 = Policy(); var p2 = Policy();
        await Seed("SUNDPENSION", p1, "50000.00", 0, -3m);
        await Seed("SUNDPENSION", p2, "50000.00", 0, 5m);
        var a = await Start(customer, plan, "SUNDPENSION", p1);
        Assert.Equal("VALUED", a.Str("status")); // demora 0: la valoración llega al momento
        await Act(a.Str("id"), "confirm");
        var review = await Act(a.Str("id"), "approve", await _f.Olof());
        Assert.Equal("MANUAL_REVIEW", review.Str("status"));
        Assert.Equal("-3.00", review.Str("deviationPct"));
        Assert.Equal("48500.00", review.Str("receivedAmount"));
        (await Act(a.Str("id"), "cancel")).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await Act(a.Str("id"), "reject", await _f.Olof(), "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        var rejected = await Act(a.Str("id"), "reject", await _f.Olof(), "{\"reason\":\"La gestora debe explicar la diferencia\"}");
        Assert.Equal("REJECTED", rejected.Str("status"));

        var b = await Start(customer, plan, "SUNDPENSION", p2);
        await Act(b.Str("id"), "confirm");
        Assert.Equal("MANUAL_REVIEW", (await Act(b.Str("id"), "approve", await _f.Olof())).Str("status"));
        (await Act(b.Str("id"), "accept-amount", await _f.Erik())).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var accepted = await Act(b.Str("id"), "accept-amount", await _f.Olof());
        Assert.Equal("COMPLETED", accepted.Str("status"));
        Assert.Equal("52500.00", accepted.Str("receivedAmount"));
    }

    [Fact]
    public async Task Expired_valuations_unknown_policies_and_forged_signatures_are_refused()
    {
        var customer = await People.Clean(_f);
        var plan = await Pensions.PlanId(_f, customer);
        var policy = Policy();
        await Seed("NORRBO", policy, "70000.00", 0, 0);
        var t = await Start(customer, plan, "NORRBO", policy);
        await Act(t.Str("id"), "confirm");
        await _f.Freeze("2026-11-25T10:00:00"); // más de 30 días
        (await Act(t.Str("id"), "approve", await _f.Olof())).ShouldBeProblem(409, "VALUATION_EXPIRED");
        Assert.Equal("EXPIRED", (await Get(t.Str("id"))).Str("status"));
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");

        (await Start(customer, plan, "NORRBO", "X-1")).ShouldBeProblem(404, "POLICY_NOT_FOUND");
        (await Start(customer, plan, "NOBODY", "P123456")).ShouldBeProblem(404, "PROVIDER_NOT_FOUND");
        var someoneElse = Policy();
        await Seed("NORRBO", someoneElse, "1000.00", 0, 0, (await _f.NewPerson()).GetProperty("nationalId").GetString());
        (await Start(customer, plan, "NORRBO", someoneElse)).ShouldBeProblem(422, "POLICY_HOLDER_MISMATCH");
        (await Start(customer, "pp_no_existe", "NORRBO", Policy())).ShouldBeProblem(404, "PLAN_NOT_FOUND");

        await _f.Post(Ports.ProviderSim, "/__admin/tamper", "{\"on\":true}");
        try { (await Start(customer, plan, "NORRBO", Policy())).ShouldBeProblem(502, "INVALID_PROVIDER_SIGNATURE"); }
        finally { await _f.Post(Ports.ProviderSim, "/__admin/tamper", "{\"on\":false}"); }
        var log = await _f.Get(Ports.ProviderSim, "/__admin/requests");
        Assert.All(log.Json.GetProperty("items").EnumerateArray(), x => Assert.True(x.GetProperty("signed").GetBoolean()));
        var unsigned = await _f.Post(Ports.ProviderSim, "/v1/valuations", "{\"providerCode\":\"NORRBO\",\"policyNumber\":\"P123456\"}");
        Assert.Equal(401, unsigned.Code);
        var providers = await _f.Get(Ports.ProviderAdapter, "/api/v1/providers");
        Assert.Equal(3, providers.Json.GetProperty("items").GetArrayLength());
    }
}

[Collection("nordvik")]
public class PayoutTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public PayoutTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze($"{TestData.Tuesday}T10:00:00");
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> NewPayout(string plan, string body = "{\"type\":\"TERM\",\"termYears\":5}") =>
        await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/pension-plans/{plan}/payout-plans", body, NordvikFixture.Bearer(await _f.Erik()));

    private async Task<Reply> Payout(string id, string verb, string? token = null) =>
        await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/payout-plans/{id}/{verb}", null, NordvikFixture.Bearer(token ?? await _f.Erik()));

    private async Task<Reply> Run(string period) =>
        await _f.Send(HttpMethod.Post, Ports.PayoutExecution, "/api/v1/payout-runs", $"{{\"period\":\"{period}\"}}", NordvikFixture.Bearer(await _f.Olof()));

    [Fact]
    public async Task Young_customers_and_empty_plans_cannot_start_a_payout()
    {
        var young = await People.Customer(_f, "Ung", "Sparare", new DateOnly(1990, 3, 3));
        var plan = await Pensions.PlanId(_f, young);
        (await NewPayout(plan)).ShouldBeProblem(422, "NOT_ELIGIBLE");
        var old = await People.Customer(_f, "Gammal", "Sparare", new DateOnly(1958, 3, 3));
        var empty = await Pensions.PlanId(_f, old);
        (await NewPayout(empty)).ShouldBeProblem(422, "NOTHING_TO_PAY");
        (await NewPayout("pp_no_existe")).ShouldBeProblem(404, "PLAN_NOT_FOUND");
    }

    [Fact]
    public async Task A_term_payout_is_planned_activated_paid_monthly_with_table_withholding_once_per_month()
    {
        var (customer, plan) = await Pensions.FundedPlan(_f, new DateOnly(1960, 5, 10));
        var po = await NewPayout(plan, "{\"type\":\"TERM\",\"termYears\":5,\"startPeriod\":\"2026-11\"}");
        Assert.True(po.Code == 201, po.Body);
        Assert.Equal("DRAFT", po.Str("status"));
        Assert.Equal(60, po.Int("expectedPayments"));
        var capital = Pensions.D(po.Json.GetProperty("capital").GetProperty("amount").GetString()!);
        Assert.InRange(capital, 200000m, 300000m);
        var monthly = Pensions.D(po.Json.GetProperty("monthlyGross").GetProperty("amount").GetString()!);
        Assert.InRange(monthly, capital / 60, capital / 60 * 1.1m); // con interés técnico del 2 % se cobra algo más que capital/60
        var id = po.Str("id");
        (await NewPayout(plan)).ShouldBeProblem(409, "PAYOUT_PLAN_EXISTS");
        (await Payout(id, "resume", await _f.Olof())).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await Payout(id, "suspend", await _f.Olof())).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        Assert.Equal("ACTIVE", (await Payout(id, "activate")).Str("status"));
        (await Payout(id, "activate")).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        Assert.Equal("PAYOUT", (await Pensions.PlanStatus(_f, plan)).Str("status"));
        (await Pensions.Contribute(_f, plan, "1000.00")).ShouldBeProblem(409, "PLAN_NOT_ACTIVE");

        (await Run("2026-11")).ShouldBeProblem(422, "FUTURE_PERIOD");
        await _f.Freeze("2026-11-05T10:00:00");
        var run = await Run("2026-11");
        Assert.True(run.Code == 200, run.Body);
        var line = run.Json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("payoutPlanId").GetString() == id);
        Assert.Equal("PAID", line.GetProperty("result").GetString());
        var gross = Pensions.D(line.GetProperty("gross").GetString()!);
        var tax = Pensions.D(line.GetProperty("taxWithheld").GetString()!);
        Assert.Equal(monthly, gross);
        Assert.Equal("TAX_TABLE", line.GetProperty("taxBasis").GetString()); // sin residencia registrada: tabla municipal de la Agencia (E8)
        var rate = Pensions.D(line.GetProperty("taxRatePct").GetString()!);
        Assert.InRange(rate, 29m, 35m);
        Assert.Equal(Math.Round(gross * rate / 100, 2, MidpointRounding.ToEven), tax);
        Assert.Equal(gross - tax, Pensions.D(line.GetProperty("net").GetString()!));
        var twice = await Run("2026-11");
        Assert.Equal("ALREADY_PAID", twice.Json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("payoutPlanId").GetString() == id).GetProperty("result").GetString());
        await TestData.Events(_f, "payout.paid", line.GetProperty("paymentId").GetString()!);
        var stored = await _f.Get(Ports.PayoutExecution, $"/api/v1/payout-runs/{run.Str("id")}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(run.Int("paid"), stored.Int("paid"));

        Assert.Equal("SUSPENDED", (await Payout(id, "suspend", await _f.Olof())).Str("status"));
        (await Payout(id, "suspend")).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        await _f.Freeze("2026-12-03T10:00:00");
        var dec = await Run("2026-12");
        Assert.DoesNotContain(dec.Json.GetProperty("lines").EnumerateArray(), l => l.GetProperty("payoutPlanId").GetString() == id);
        Assert.Equal("ACTIVE", (await Payout(id, "resume", await _f.Olof())).Str("status"));
        await Run("2026-12");
        var payments = await _f.Get(Ports.PayoutExecution, $"/api/v1/payout-plans/{id}/payments", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(2, payments.Int("count"));
        Assert.Equal(new[] { "2026-11", "2026-12" }, payments.Json.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("period").GetString()));
        Assert.Equal(Money.Format(gross * 2, 2), payments.Json.GetProperty("totals").GetProperty("gross").GetString());
        (await _f.Get(Ports.PayoutExecution, $"/api/v1/payout-plans/{id}/payments", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");

        // Fallecimiento: el plan se congela, el cobro se suspende y no se puede reanudar
        var olof = NordvikFixture.Bearer(await _f.Olof());
        await _f.Send(HttpMethod.Put, Ports.Beneficiaries, $"/api/v1/pension-plans/{plan}/beneficiaries",
            "{\"beneficiaries\":[{\"name\":\"Elsa\",\"relation\":\"CHILD\",\"percent\":33.33},{\"name\":\"Nils\",\"relation\":\"CHILD\",\"percent\":33.33},{\"name\":\"Mia\",\"relation\":\"CHILD\",\"percent\":33.34}]}",
            NordvikFixture.Bearer(await _f.Erik(), "\"0\""));
        var claim = await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", JsonSerializer.Serialize(new { customerId = customer, dateOfDeath = "2026-12-01", reportedBy = "Hija" }), olof);
        Assert.True(claim.Code == 201, claim.Body);
        Assert.Equal("FROZEN", (await Pensions.PlanStatus(_f, plan)).Str("status"));
        Assert.Equal("SUSPENDED", (await _f.Get(Ports.PayoutPlanner, $"/api/v1/payout-plans/{id}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
        (await Payout(id, "resume", await _f.Olof())).ShouldBeProblem(409, "PLAN_FROZEN");
        var verified = await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{claim.Str("id")}/verify", "{\"certificateRef\":\"SKV-2026-0042\"}", olof);
        var dist = verified.Json.GetProperty("distribution")[0];
        var shares = dist.GetProperty("shares").EnumerateArray().Select(s => Pensions.D(s.GetProperty("amount").GetString()!)).ToList();
        Assert.Equal(3, shares.Count);
        Assert.Equal(Pensions.D(dist.GetProperty("value").GetString()!), shares.Sum());
        Assert.True(Pensions.D(dist.GetProperty("value").GetString()!) > 0);
        Assert.Equal("SETTLED", (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{claim.Str("id")}/settle", null, olof)).Str("status"));
        Assert.Equal("CLOSED", (await Pensions.PlanStatus(_f, plan)).Str("status"));
        Assert.Equal("FINISHED", (await _f.Get(Ports.PayoutPlanner, $"/api/v1/payout-plans/{id}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
    }
}

[Collection("nordvik")]
public class DeathClaimTests
{
    private readonly NordvikFixture _f;
    public DeathClaimTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task A_claim_freezes_plans_follows_its_states_and_pays_the_estate_without_beneficiaries()
    {
        var customer = await People.Clean(_f);
        var plan = await Pensions.PlanId(_f, customer);
        var other = await Pensions.PlanId(_f, customer, "OCCUPATIONAL", Pensions.Org(), "SAF-LO");
        var olof = NordvikFixture.Bearer(await _f.Olof());
        string Body(string date) => JsonSerializer.Serialize(new { customerId = customer, dateOfDeath = date, reportedBy = "Cónyuge" });
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", Body("2026-10-01"), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", Body("2099-01-01"), olof)).ShouldBeProblem(422, "DATE_IN_FUTURE");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", "{\"customerId\":\"x\"}", olof)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", JsonSerializer.Serialize(new { customerId = "no-existe", dateOfDeath = "2026-10-01" }), olof)).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        var claim = await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", Body("2026-10-01"), olof);
        Assert.True(claim.Code == 201, claim.Body);
        var id = claim.Str("id");
        Assert.Equal("OPEN", claim.Str("status"));
        Assert.Equal(2, claim.Json.GetProperty("frozenPlans").GetArrayLength());
        Assert.Equal("NO_POLICIES", claim.Json.GetProperty("insurancePart").GetProperty("status").GetString());
        foreach (var p in new[] { plan, other }) Assert.Equal("FROZEN", (await Pensions.PlanStatus(_f, p)).Str("status"));
        (await Pensions.Contribute(_f, plan, "500.00")).ShouldBeProblem(409, "PLAN_FROZEN");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims", Body("2026-10-01"), olof)).ShouldBeProblem(409, "CLAIM_ALREADY_OPEN");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/settle", null, olof)).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/verify", "{}", olof)).ShouldBeProblem(400, "VALIDATION_FAILED");
        var v = await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/verify", "{\"certificateRef\":\"SKV-1\"}", olof);
        Assert.Equal("VERIFIED", v.Str("status"));
        Assert.All(v.Json.GetProperty("distribution").EnumerateArray(), d => Assert.Equal("ESTATE", d.GetProperty("shares")[0].GetProperty("relation").GetString()));
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/verify", "{\"certificateRef\":\"SKV-1\"}", olof)).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/reject", "{\"reason\":\"x\"}", olof)).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        Assert.Equal("SETTLED", (await _f.Send(HttpMethod.Post, Ports.DeathClaims, $"/api/v1/death-claims/{id}/settle", null, olof)).Str("status"));
        Assert.Equal("CLOSED", (await Pensions.PlanStatus(_f, other)).Str("status"));
        await TestData.Events(_f, "death-claim.settled", id);
        await TestData.Events(_f, "pension-plan.frozen", plan);
        (await _f.Get(Ports.DeathClaims, "/api/v1/death-claims/dc_nada", olof)).ShouldBeProblem(404, "CLAIM_NOT_FOUND");
    }
}
