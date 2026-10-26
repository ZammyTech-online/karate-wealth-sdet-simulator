using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class CustomerDataTests
{
    private readonly NordvikFixture _f;
    public CustomerDataTests(NordvikFixture f) => _f = f;

    public static string OrgNumber(string nine)
    {
        for (var d = 0; d <= 9; d++) { var c = nine + d; if (Nordvik.Services.EmploymentService.OrgNumberValid(c[..6] + "-" + c[6..])) return c[..6] + "-" + c[6..]; }
        throw new InvalidOperationException();
    }

    [Fact]
    public async Task Tax_residencies_are_validated_per_country_and_flag_fatca()
    {
        var erik = await _f.Erik();
        var id = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, id);
        string Path() => $"/api/v1/customers/{id}/tax-residencies";
        (await _f.Get(Ports.TaxResidency, Path(), NordvikFixture.Bearer(erik))).ShouldBeProblem(404, "TAX_RESIDENCY_NOT_FOUND");
        (await _f.Send(HttpMethod.Put, Ports.TaxResidency, Path(), JsonSerializer.Serialize(new { residencies = new object[] { new { country = "SE", tin = nid, primary = true }, new { country = "NO", tin = "12345678901", primary = true } } }), NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "INVALID_TAX_RESIDENCY");
        (await _f.Send(HttpMethod.Put, Ports.TaxResidency, Path(), JsonSerializer.Serialize(new { residencies = new object[] { new { country = "SE", tin = "19900101-0000", primary = true } } }), NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "INVALID_TAX_RESIDENCY");
        (await _f.Send(HttpMethod.Put, Ports.TaxResidency, Path(), JsonSerializer.Serialize(new { residencies = new object[] { new { country = "ZZ", tin = "1", primary = true } } }), NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "INVALID_TAX_RESIDENCY");
        var ok = await _f.Send(HttpMethod.Put, Ports.TaxResidency, Path(),
            JsonSerializer.Serialize(new { residencies = new object[] { new { country = "se", tin = nid, primary = true }, new { country = "US", tin = "123-45-6789", primary = false }, new { country = "DE", tin = "12345678901", primary = false } } }),
            NordvikFixture.Bearer(erik));
        Assert.Equal(200, ok.Code);
        Assert.True(ok.Json.GetProperty("fatcaReportable").GetBoolean());
        Assert.Equal(new[] { "DE" }, ok.Json.GetProperty("crsReportableCountries").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("SE", ok.Str("primaryCountry"));
        Assert.DoesNotContain(nid, ok.Body);
        Assert.Equal(1, ok.Int("version"));
        await TestData.Events(_f, "tax-residency.changed", id);
        (await _f.Get(Ports.TaxResidency, $"/api/v1/customers/{SeedIds.CustomerAna}/tax-residencies", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Household_members_are_coherent_and_their_ids_are_tokenized()
    {
        var erik = await _f.Erik();
        var id = await People.Clean(_f);
        var spouseNid = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        string Path() => $"/api/v1/customers/{id}/household";
        Assert.Equal(1, (await _f.Get(Ports.Household, Path(), NordvikFixture.Bearer(erik))).Json.GetProperty("summary").GetProperty("size").GetInt32());
        var h = await _f.Send(HttpMethod.Put, Ports.Household, Path(), JsonSerializer.Serialize(new
        {
            members = new object[]
            {
                new { relation = "SPOUSE", name = "Pareja", birthDate = "1980-02-02", nationalId = spouseNid },
                new { relation = "CHILD", name = "Elsa", birthDate = "2020-06-01" },
            },
        }), NordvikFixture.Bearer(erik));
        Assert.Equal(200, h.Code);
        Assert.DoesNotContain(spouseNid, h.Body);
        var spouse = h.Json.GetProperty("members").EnumerateArray().First(m => m.GetProperty("relation").GetString() == "SPOUSE");
        Assert.StartsWith("tok_nid_", spouse.GetProperty("nationalIdToken").GetString());
        Assert.Contains("**-****", spouse.GetProperty("nationalIdMasked").GetString());
        Assert.Equal(1, h.Json.GetProperty("summary").GetProperty("minorChildren").GetInt32());
        Assert.True(h.Json.GetProperty("members").EnumerateArray().First(m => m.GetProperty("relation").GetString() == "CHILD").GetProperty("dependent").GetBoolean());
        (await _f.Send(HttpMethod.Put, Ports.Household, Path(), "{\"members\":[{\"relation\":\"CHILD\",\"name\":\"Abuelo\",\"birthDate\":\"1900-01-01\"}]}", NordvikFixture.Bearer(erik))).ShouldBeProblem(422, "INVALID_HOUSEHOLD");
        (await _f.Send(HttpMethod.Put, Ports.Household, Path(), "{\"members\":[{\"relation\":\"SPOUSE\",\"name\":\"A\",\"birthDate\":\"1980-01-01\"},{\"relation\":\"PARTNER\",\"name\":\"B\",\"birthDate\":\"1981-01-01\"}]}", NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "INVALID_HOUSEHOLD");
        (await _f.Send(HttpMethod.Put, Ports.Household, Path(), "{\"members\":[{\"relation\":\"PET\",\"name\":\"Rex\",\"birthDate\":\"2019-01-01\"}]}", NordvikFixture.Bearer(erik))).ShouldBeProblem(422, "INVALID_HOUSEHOLD");
        (await _f.Get(Ports.Household, Path(), NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Employment_history_never_overlaps_and_salary_is_shown_to_whom_it_should()
    {
        var ana = await _f.Ana();
        string Path() => $"/api/v1/customers/{SeedIds.CustomerAna}/employments";
        var org1 = OrgNumber("556012579");
        var org2 = OrgNumber("556677889");
        Assert.True((await _f.Get(Ports.Employment, $"/api/v1/org-numbers/{org1}/validate")).Json.GetProperty("valid").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Employment, Path(), "{\"employerName\":\"X\",\"orgNumber\":\"123456-7890\",\"startDate\":\"2020-01-01\",\"monthlySalary\":\"1.00\"}", NordvikFixture.Bearer(ana)))
            .ShouldBeProblem(400, "VALIDATION_FAILED");
        var e1 = await _f.Send(HttpMethod.Post, Ports.Employment, Path(), JsonSerializer.Serialize(new { employerName = "Volvo AB", orgNumber = org1, startDate = "2018-03-01", monthlySalary = "38000.00", collectiveAgreement = "ITP1" }), NordvikFixture.Bearer(ana));
        Assert.Equal(201, e1.Code);
        var e2 = await _f.Send(HttpMethod.Post, Ports.Employment, Path(), JsonSerializer.Serialize(new { employerName = "Ericsson AB", orgNumber = org2, startDate = "2024-09-01", monthlySalary = "52000.00", collectiveAgreement = "ITP2" }), NordvikFixture.Bearer(ana));
        var items = e2.Json.GetProperty("items");
        Assert.Equal("Ericsson AB", items[0].GetProperty("employerName").GetString());
        Assert.Equal("2024-08-31", items[1].GetProperty("endDate").GetString());
        Assert.True(items[0].GetProperty("current").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Employment, Path(), JsonSerializer.Serialize(new { employerName = "Solape AB", orgNumber = org2, startDate = "2019-01-01", endDate = "2019-12-31", monthlySalary = "1000.00" }), NordvikFixture.Bearer(ana)))
            .ShouldBeProblem(422, "EMPLOYMENT_OVERLAP");
        var asAdvisor = await _f.Get(Ports.Employment, Path(), NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(JsonValueKind.Null, asAdvisor.Json.GetProperty("items")[0].GetProperty("monthlySalary").ValueKind);
        Assert.Equal("40 000 - 59 999", asAdvisor.Json.GetProperty("items")[0].GetProperty("salaryBand").GetString());
        Assert.Equal("52000.00", (await _f.Get(Ports.Employment, Path(), NordvikFixture.Bearer(ana))).Json.GetProperty("items")[0].GetProperty("monthlySalary").GetString());
    }
}

[Collection("nordvik")]
public class TaskAndApprovalTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public TaskAndApprovalTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<string> Forge(string role, string sub) =>
        (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", JsonSerializer.Serialize(new { role, sub, scope = "*" }))).Str("token");

    [Fact]
    public async Task Tasks_are_claimed_and_completed_by_their_role()
    {
        await _f.Freeze($"{TestData.Tuesday}T09:00:00");
        var t = await _f.Send(HttpMethod.Post, Ports.Tasks, "/api/v1/tasks", "{\"type\":\"review\",\"title\":\"Revisar documento\",\"assigneeRole\":\"Compliance\",\"dueInHours\":2}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(201, t.Code);
        Assert.Equal("REVIEW", t.Str("type"));
        var id = t.Str("id");
        (await _f.Get(Ports.Tasks, "/api/v1/tasks?role=Compliance", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.Tasks, $"/api/v1/tasks/{id}/claim", null, NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(403, "NOT_YOUR_ROLE");
        var karin = await _f.Karin();
        (await _f.Send(HttpMethod.Post, Ports.Tasks, $"/api/v1/tasks/{id}/complete", "{\"outcome\":\"DONE\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "TASK_WRONG_STATE");
        Assert.Equal("CLAIMED", (await _f.Send(HttpMethod.Post, Ports.Tasks, $"/api/v1/tasks/{id}/claim", null, NordvikFixture.Bearer(karin))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Tasks, $"/api/v1/tasks/{id}/complete", "{\"outcome\":\"DONE\"}", NordvikFixture.Bearer(await Forge("Compliance", "compliance.otra")))).ShouldBeProblem(403, "NOT_YOUR_TASK");
        await _f.Freeze($"{TestData.Tuesday}T11:30:00");
        karin = await _f.Karin();
        var overdue = await _f.Get(Ports.Tasks, "/api/v1/tasks?overdue=true", NordvikFixture.Bearer(karin));
        Assert.Contains(id, overdue.Body);
        var done = await _f.Send(HttpMethod.Post, Ports.Tasks, $"/api/v1/tasks/{id}/complete", "{\"outcome\":\"approved\",\"comment\":\"ok\"}", NordvikFixture.Bearer(karin));
        Assert.Equal("DONE", done.Str("status"));
        Assert.Equal("APPROVED", done.Str("outcome"));
        Assert.False(done.Json.GetProperty("overdue").GetBoolean());
        await TestData.Events(_f, "task.completed", id);
    }

    [Fact]
    public async Task Four_eyes_two_distinct_approvers_and_one_rejection_closes()
    {
        var olof = await _f.Olof();
        var a = await _f.Send(HttpMethod.Post, Ports.Approvals, "/api/v1/approvals",
            "{\"action\":\"payment.release\",\"subjectRef\":\"pay_123\",\"summary\":\"Retirada de 250 000 SEK\",\"approverRole\":\"Operations\",\"approvalsNeeded\":2}", NordvikFixture.Bearer(olof));
        Assert.Equal(201, a.Code);
        Assert.Equal("PENDING", a.Str("status"));
        var id = a.Str("id");
        var task = await _f.Get(Ports.Tasks, $"/api/v1/tasks/{a.Str("taskId")}", NordvikFixture.Bearer(olof));
        Assert.Equal("APPROVE", task.Str("type"));
        string Path() => $"/api/v1/approvals/{id}/decisions";
        (await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(403, "FOUR_EYES_VIOLATION");
        (await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(403, "NOT_AN_APPROVER");
        var ines = await Forge("Operations", "operaciones.ines");
        var first = await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(ines));
        Assert.Equal("PENDING", first.Str("status"));
        Assert.Equal(1, first.Int("approvalsGiven"));
        (await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(ines))).ShouldBeProblem(409, "ALREADY_DECIDED");
        var second = await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(await _f.Admin()));
        Assert.Equal("APPROVED", second.Str("status"));
        await TestData.Events(_f, "approval.granted", id);
        Assert.Equal("CANCELLED", (await _f.Get(Ports.Tasks, $"/api/v1/tasks/{a.Str("taskId")}", NordvikFixture.Bearer(olof))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Approvals, Path(), "{\"decision\":\"REJECT\",\"comment\":\"x\"}", NordvikFixture.Bearer(await Forge("Operations", "operaciones.otro")))).ShouldBeProblem(409, "APPROVAL_CLOSED");

        var r = await _f.Send(HttpMethod.Post, Ports.Approvals, "/api/v1/approvals", "{\"action\":\"fee.waive\",\"subjectRef\":\"fee_9\",\"approverRole\":\"Compliance\"}", NordvikFixture.Bearer(olof));
        (await _f.Send(HttpMethod.Post, Ports.Approvals, $"/api/v1/approvals/{r.Str("id")}/decisions", "{\"decision\":\"REJECT\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var rej = await _f.Send(HttpMethod.Post, Ports.Approvals, $"/api/v1/approvals/{r.Str("id")}/decisions", "{\"decision\":\"REJECT\",\"comment\":\"No procede\"}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("REJECTED", rej.Str("status"));
    }

    [Fact]
    public async Task Approvals_expire_with_the_virtual_clock()
    {
        await _f.Freeze($"{TestData.Tuesday}T09:00:00");
        var a = await _f.Send(HttpMethod.Post, Ports.Approvals, "/api/v1/approvals", "{\"action\":\"limit.raise\",\"subjectRef\":\"c1\",\"approverRole\":\"Compliance\",\"expiresInHours\":1}", NordvikFixture.Bearer(await _f.Olof()));
        await _f.Freeze($"{TestData.Tuesday}T10:00:01");
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Approvals, $"/api/v1/approvals/{a.Str("id")}", NordvikFixture.Bearer(await _f.Olof()))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Approvals, $"/api/v1/approvals/{a.Str("id")}/decisions", "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(410, "APPROVAL_EXPIRED");
        (await _f.Send(HttpMethod.Post, Ports.Approvals, "/api/v1/approvals", "{\"action\":\"x\",\"subjectRef\":\"y\",\"approverRole\":\"Customer\"}", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Cases_measure_their_sla_on_the_virtual_clock()
    {
        await _f.Freeze($"{TestData.Tuesday}T09:00:00");
        var c = await _f.Send(HttpMethod.Post, Ports.Cases, "/api/v1/cases", JsonSerializer.Serialize(new { type = "COMPLAINT", customerId = SeedIds.CustomerAna, priority = "HIGH", summary = "Comisión cobrada dos veces" }), NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(201, c.Code);
        Assert.Equal("Operations", c.Str("ownerRole"));
        Assert.StartsWith($"{"2026-10-21"}T09:00:00", c.Str("slaDue"));
        Assert.False(c.Json.GetProperty("slaBreached").GetBoolean());
        var id = c.Str("id");
        (await _f.Send(HttpMethod.Post, Ports.Cases, $"/api/v1/cases/{id}/resolve", "{\"resolution\":\"x\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(403, "NOT_YOUR_QUEUE");
        await _f.Send(HttpMethod.Post, Ports.Cases, $"/api/v1/cases/{id}/notes", "{\"text\":\"Llamado el cliente\"}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal("IN_PROGRESS", (await _f.Send(HttpMethod.Post, Ports.Cases, $"/api/v1/cases/{id}/assign", "{}", NordvikFixture.Bearer(await _f.Olof()))).Str("status"));
        await _f.Freeze("2026-10-21T09:00:01");
        var olof = await _f.Olof();
        var breached = await _f.Get(Ports.Cases, "/api/v1/cases?breached=true&type=COMPLAINT", NordvikFixture.Bearer(olof));
        Assert.Contains(id, breached.Body);
        var res = await _f.Send(HttpMethod.Post, Ports.Cases, $"/api/v1/cases/{id}/resolve", "{\"resolution\":\"Devolución hecha\"}", NordvikFixture.Bearer(olof));
        Assert.Equal("RESOLVED", res.Str("status"));
        Assert.True(res.Json.GetProperty("slaBreached").GetBoolean());
        Assert.Equal(1, res.Json.GetProperty("notes").GetArrayLength());
        var ev = await TestData.Events(_f, "case.resolved", id);
        Assert.False(ev.Json.GetProperty("items")[0].GetProperty("payload").GetProperty("withinSla").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Cases, $"/api/v1/cases/{id}/resolve", "{\"resolution\":\"x\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "CASE_RESOLVED");
        (await _f.Send(HttpMethod.Post, Ports.Cases, "/api/v1/cases", "{\"type\":\"GOSSIP\",\"summary\":\"x\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(400, "VALIDATION_FAILED");
    }
}

[Collection("nordvik")]
public class PopulationTests
{
    private readonly NordvikFixture _f;
    public PopulationTests(NordvikFixture f) => _f = f;

    private async Task<Reply> Lookup(object body) =>
        await _f.Send(HttpMethod.Post, Ports.PopulationAdapter, "/api/v1/population/lookups", JsonSerializer.Serialize(body), NordvikFixture.Bearer(await _f.Olof()));

    [Fact]
    public async Task The_register_requires_our_client_certificate()
    {
        var nid = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        Assert.Equal(401, (await _f.Get(Ports.PopulationSim, $"/v1/persons/{nid}")).Code);
        Assert.Equal(403, (await _f.Get(Ports.PopulationSim, $"/v1/persons/{nid}", r => r.Headers.TryAddWithoutValidation("X-Client-Cert", "SHA256:otro"))).Code);
        (await _f.Post(Ports.PopulationAdapter, "/api/v1/population/lookups", JsonSerializer.Serialize(new { nationalId = nid }))).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
        var r = await Lookup(new { nationalId = nid });
        Assert.Equal(200, r.Code);
        Assert.False(r.Json.GetProperty("protectedIdentity").GetBoolean());
        Assert.Equal(JsonValueKind.Object, r.Json.GetProperty("address").ValueKind);
        Assert.Contains("**-****", r.Str("nationalIdMasked"));
        (await Lookup(new { nationalId = "19900101-0000" })).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Protected_deceased_and_unknown_people()
    {
        var p1 = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        await _f.Post(Ports.PopulationSim, "/__admin/persons", JsonSerializer.Serialize(new { personalNumber = p1, givenName = "Skyddad", surname = "Person", street = "Hemlig 1", postalCode = "111 11", city = "Stockholm", protectedIdentity = true, citizenship = "SE" }));
        var prot = await Lookup(new { nationalId = p1 });
        Assert.True(prot.Json.GetProperty("protectedIdentity").GetBoolean());
        Assert.Equal(JsonValueKind.Null, prot.Json.GetProperty("address").ValueKind);
        Assert.DoesNotContain("Hemlig", prot.Body);
        var p2 = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        await _f.Post(Ports.PopulationSim, "/__admin/persons", JsonSerializer.Serialize(new { personalNumber = p2, givenName = "Avliden", surname = "Person", protectedIdentity = false, deceasedDate = "2026-09-30", citizenship = "SE" }));
        var dead = await Lookup(new { nationalId = p2 });
        Assert.True(dead.Json.GetProperty("deceased").GetBoolean());
        Assert.Equal("2026-09-30", dead.Str("deceasedDate"));
        var p3 = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        await _f.Post(Ports.PopulationSim, $"/__admin/unknown/{p3}");
        (await Lookup(new { nationalId = p3 })).ShouldBeProblem(404, "PERSON_NOT_REGISTERED");
        var token = (await PiiVaultTests.Tokenize(_f, "NATIONAL_ID", p1, null)).Str("token");
        Assert.True((await Lookup(new { token })).Json.GetProperty("protectedIdentity").GetBoolean());
        try
        {
            await _f.Send(HttpMethod.Put, Ports.MockControl, "/api/v1/mocks/population-sim/behaviour", "{\"mode\":\"error\"}");
            (await Lookup(new { nationalId = p1 })).ShouldBeProblem(503, "POPULATION_REGISTER_UNAVAILABLE");
        }
        finally { await _f.Post(Ports.MockControl, "/api/v1/mocks/reset"); }
    }

    [Fact]
    public async Task The_customer_file_is_compared_with_the_register()
    {
        var erik = await _f.Erik();
        var id = await People.Customer(_f, "Greta", "Holm", new DateOnly(1990, 3, 3));
        var nid = await TestData.NationalIdOf(_f, id);
        await _f.Post(Ports.PopulationSim, "/__admin/persons", JsonSerializer.Serialize(new { personalNumber = nid, givenName = "Greta", surname = "Holm-Berg", street = "Storgatan 1", postalCode = "753 31", city = "Uppsala", protectedIdentity = false, citizenship = "SE" }));
        var c = await _f.Get(Ports.PopulationAdapter, $"/api/v1/customers/{id}/population-check", NordvikFixture.Bearer(erik));
        Assert.False(c.Json.GetProperty("matches").GetBoolean());
        Assert.Equal(new[] { "lastName" }, c.Json.GetProperty("differences").EnumerateArray().Select(d => d.GetString()));
        await _f.Post(Ports.PopulationSim, "/__admin/persons", JsonSerializer.Serialize(new { personalNumber = nid, givenName = "GRETA", surname = "holm", protectedIdentity = false, citizenship = "SE" }));
        Assert.True((await _f.Get(Ports.PopulationAdapter, $"/api/v1/customers/{id}/population-check", NordvikFixture.Bearer(erik))).Json.GetProperty("matches").GetBoolean());
        (await _f.Get(Ports.PopulationAdapter, $"/api/v1/customers/{SeedIds.CustomerSofia}/population-check", NordvikFixture.Bearer(erik))).ShouldBeProblem(403, "ACCESS_DENIED");
    }
}
