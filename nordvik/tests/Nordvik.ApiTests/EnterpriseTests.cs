using System.Text.Json;
using System.Xml.Linq;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class PlatformRegistryTests
{
    private readonly NordvikFixture _f;
    public PlatformRegistryTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task The_registry_knows_every_service_its_dependencies_and_who_depends_on_it()
    {
        var all = await _f.Get(Ports.ServiceRegistry, "/api/v1/services?probe=true");
        Assert.Equal(200, all.Code);
        Assert.Equal(168, all.Int("count"));
        Assert.Equal(168, all.Int("up"));
        Assert.Equal(12, (await _f.Get(Ports.ServiceRegistry, "/api/v1/services?external=true")).Int("count"));
        Assert.Equal(156, (await _f.Get(Ports.ServiceRegistry, "/api/v1/services?external=false")).Int("count"));
        Assert.Equal(29, (await _f.Get(Ports.ServiceRegistry, "/api/v1/services?wave=4&external=false")).Int("count"));
        var one = await _f.Get(Ports.ServiceRegistry, "/api/v1/services/order-management");
        Assert.Equal(6902 + NordvikFixture.Offset, int.Parse(one.Str("url").Split(':')[2]));
        var deps = await _f.Get(Ports.ServiceRegistry, "/api/v1/services/payout-execution/dependencies");
        Assert.Contains("tax-withholding", deps.Json.GetProperty("direct").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("tax-agency-sim", deps.Json.GetProperty("transitive").EnumerateArray().Select(x => x.GetString()));
        var dependents = await _f.Get(Ports.ServiceRegistry, "/api/v1/services/tax-agency-sim/dependents");
        Assert.Contains("tax-agency-adapter", dependents.Json.GetProperty("direct").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("payout-execution", dependents.Json.GetProperty("transitive").EnumerateArray().Select(x => x.GetString()));
        (await _f.Get(Ports.ServiceRegistry, "/api/v1/services/no-existe")).ShouldBeProblem(404, "SERVICE_NOT_FOUND");
        var domains = await _f.Get(Ports.ServiceRegistry, "/api/v1/domains");
        Assert.Equal(18, domains.Json.GetProperty("items").GetArrayLength()); // 17 dominios de negocio + X-external
    }

    [Fact]
    public async Task Schemas_evolve_only_in_a_backward_compatible_way()
    {
        var admin = NordvikFixture.Bearer(await _f.Admin());
        var subject = "academy.thing-" + Guid.NewGuid().ToString("N")[..6] + ".created";
        string Body(object schema, string? compat = null) => JsonSerializer.Serialize(new { schema, compatibility = compat });
        var v1 = await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions",
            Body(new { required = new[] { "id" }, properties = new Dictionary<string, string> { ["id"] = "string", ["amount"] = "string" } }), admin);
        Assert.Equal(201, v1.Code);
        Assert.Equal(1, v1.Int("version"));
        var v2 = await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions",
            Body(new { required = new[] { "id" }, properties = new Dictionary<string, string> { ["id"] = "string", ["amount"] = "string", ["note"] = "string" } }), admin);
        Assert.Equal(2, v2.Int("version"));
        var bad = await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions",
            Body(new { required = new[] { "id", "currency" }, properties = new Dictionary<string, string> { ["id"] = "string", ["amount"] = "number", ["currency"] = "string" } }), admin);
        bad.ShouldBeProblem(409, "INCOMPATIBLE_SCHEMA");
        var reasons = bad.Json.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("campo obligatorio nuevo: currency", reasons);
        Assert.Contains("campo eliminado: note", reasons);
        Assert.Contains("tipo cambiado en amount: string → number", reasons);
        var same = await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions",
            Body(new { required = new[] { "id" }, properties = new Dictionary<string, string> { ["id"] = "string", ["amount"] = "string", ["note"] = "string" } }), admin);
        Assert.Equal(200, same.Code);
        Assert.Equal(2, same.Int("version"));
        var free = await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions",
            Body(new { required = new[] { "id", "x" }, properties = new Dictionary<string, string> { ["id"] = "integer", ["x"] = "boolean" } }, "NONE"), admin);
        Assert.Equal(201, free.Code);
        var ok = await _f.Post(Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/validate", "{\"payload\":{\"id\":7,\"x\":true}}");
        Assert.True(ok.Json.GetProperty("valid").GetBoolean());
        var ko = await _f.Post(Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/validate", "{\"payload\":{\"id\":\"7\"}}");
        Assert.False(ko.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(new[] { "falta x", "id debería ser integer" }, ko.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        var old = await _f.Post(Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/validate", "{\"version\":1,\"payload\":{\"id\":\"7\"}}");
        Assert.True(old.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(3, (await _f.Get(Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions/latest")).Int("version"));
        Assert.Contains("payment.received", (await _f.Get(Ports.SchemaRegistry, "/api/v1/subjects")).Body);
        (await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions", Body(new { properties = new Dictionary<string, string> { ["a"] = "date" } }), admin)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.SchemaRegistry, $"/api/v1/subjects/{subject}/versions", Body(new { properties = new Dictionary<string, string> { ["a"] = "string" } }), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Get(Ports.SchemaRegistry, "/api/v1/subjects/nada.de.nada/versions/1")).ShouldBeProblem(404, "SCHEMA_NOT_FOUND");
    }

    [Fact]
    public async Task Flags_roll_out_by_percentage_deterministically_with_allow_and_deny_lists()
    {
        var admin = NordvikFixture.Bearer(await _f.Admin());
        var key = "beta-" + Guid.NewGuid().ToString("N")[..6];
        var put = await _f.Send(HttpMethod.Put, Ports.FeatureFlags, $"/api/v1/flags/{key}", "{\"enabled\":true,\"percentage\":30,\"allowCustomers\":[\"vip-1\"],\"denyCustomers\":[\"bad-1\"]}", admin);
        Assert.Equal(201, put.Code);
        var on = 0;
        for (var i = 0; i < 200; i++)
        {
            var e = await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=c-{i}");
            if (e.Json.GetProperty("enabled").GetBoolean()) on++;
            Assert.Equal("PERCENTAGE", e.Str("reason"));
        }
        Assert.InRange(on, 40, 80); // ~30 % de 200
        var a = await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=c-7");
        var b = await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=c-7");
        Assert.Equal(a.Json.GetProperty("bucket").GetInt32(), b.Json.GetProperty("bucket").GetInt32());
        Assert.Equal("ALLOWLIST", (await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=vip-1")).Str("reason"));
        var deny = await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=bad-1");
        Assert.Equal("DENYLIST", deny.Str("reason"));
        Assert.False(deny.Json.GetProperty("enabled").GetBoolean());
        await _f.Send(HttpMethod.Put, Ports.FeatureFlags, $"/api/v1/flags/{key}", "{\"enabled\":false,\"percentage\":100}", admin);
        Assert.Equal("OFF", (await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/evaluate?customerId=vip-1")).Str("reason"));
        var hist = await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}/history");
        Assert.Equal(2, hist.Int("count"));
        Assert.Equal(2, (await _f.Get(Ports.FeatureFlags, $"/api/v1/flags/{key}")).Int("version"));
        (await _f.Send(HttpMethod.Put, Ports.FeatureFlags, $"/api/v1/flags/{key}", "{\"enabled\":true,\"percentage\":10,\"allowCustomers\":[\"x\"],\"denyCustomers\":[\"x\"]}", admin)).ShouldBeProblem(422, "CONFLICTING_LISTS");
        (await _f.Send(HttpMethod.Put, Ports.FeatureFlags, $"/api/v1/flags/{key}", "{\"enabled\":true,\"percentage\":101}", admin)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Put, Ports.FeatureFlags, $"/api/v1/flags/{key}", "{\"enabled\":true,\"percentage\":10}", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Get(Ports.FeatureFlags, "/api/v1/flags/no-existe/evaluate?customerId=x")).ShouldBeProblem(404, "FLAG_NOT_FOUND");
        Assert.True((await _f.Get(Ports.FeatureFlags, "/api/v1/flags")).Int("count") >= 4);
    }
}

[Collection("nordvik")]
public class IdentityExtrasTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public IdentityExtrasTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<string> HighAssuranceAna()
    {
        var ana = await _f.Ana();
        var s = await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", "{\"reason\":\"confiar en un dispositivo\"}", NordvikFixture.Bearer(ana));
        Assert.Equal(201, s.Code);
        var done = await Eventually.Until(() => _f.Get(Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}", NordvikFixture.Bearer(ana)), r => r.Str("status") != "PENDING", 8000, "step-up");
        return done.Str("accessToken");
    }

    private static string Device(string ua, string tz = "Europe/Stockholm") =>
        JsonSerializer.Serialize(new { customerId = SeedIds.CustomerAna, platform = "web", userAgent = ua, screen = "1920x1080", timezone = tz, name = "Portátil" });

    [Fact]
    public async Task Devices_need_step_up_to_be_trusted_and_at_most_three()
    {
        var ana = NordvikFixture.Bearer(await _f.Ana());
        var ua = "Mozilla/5.0 Nordvik-" + Guid.NewGuid().ToString("N")[..6];
        var d = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices", Device(ua), ana);
        Assert.Equal(201, d.Code);
        Assert.Equal("UNTRUSTED", d.Str("status"));
        Assert.Equal(64, d.Str("fingerprintHash").Length);
        var again = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices", Device(ua), ana);
        Assert.Equal(200, again.Code);
        Assert.Equal(d.Str("id"), again.Str("id"));
        (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{d.Str("id")}/trust", null, ana)).ShouldBeProblem(403, "STEP_UP_REQUIRED");
        var high = NordvikFixture.Bearer(await HighAssuranceAna());
        Assert.Equal("TRUSTED", (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{d.Str("id")}/trust", null, high)).Str("status"));
        var assessTrusted = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices/assess", Device(ua), ana);
        Assert.Equal("LOW", assessTrusted.Str("risk"));
        var assessNew = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices/assess", Device("Otro navegador " + Guid.NewGuid()), ana);
        Assert.Equal("HIGH", assessNew.Str("risk"));
        Assert.Contains("NEW_DEVICE", assessNew.Json.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
        var known = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices", Device("Tablet " + Guid.NewGuid(), "Asia/Tokyo"), ana);
        var assessTz = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices/assess", Device("Tablet desconocida " + Guid.NewGuid(), "America/Lima"), ana);
        Assert.Contains("UNUSUAL_TIMEZONE", assessTz.Json.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));

        var current = (await _f.Get(Ports.Devices, $"/api/v1/customers/{SeedIds.CustomerAna}/devices", ana)).Json.GetProperty("trusted").GetInt64();
        string? last = null;
        for (var i = current; i < 3; i++)
        {
            var x = await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices", Device("Extra " + Guid.NewGuid()), ana);
            Assert.Equal("TRUSTED", (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{x.Str("id")}/trust", null, high)).Str("status"));
            last = x.Str("id");
        }
        (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{known.Str("id")}/trust", null, high)).ShouldBeProblem(409, "DEVICE_LIMIT_REACHED");
        Assert.Equal("REVOKED", (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{d.Str("id")}/revoke", null, ana)).Str("status"));
        Assert.Equal("TRUSTED", (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{known.Str("id")}/trust", null, high)).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{d.Str("id")}/trust", null, high)).ShouldBeProblem(409, "DEVICE_REVOKED");
        (await _f.Send(HttpMethod.Post, Ports.Devices, $"/api/v1/devices/{d.Str("id")}/revoke", null, NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Send(HttpMethod.Post, Ports.Devices, "/api/v1/devices", "{}", ana)).ShouldBeProblem(400, "VALIDATION_FAILED");
        await TestData.Events(_f, "device.trusted", d.Str("id"));
    }

    private Task<Reply> NewDelegation(string grantor, string attorney, string token, string scopes = "[\"READ\",\"TRADE\"]", string from = "2026-01-01", string to = "2030-12-31", string type = "POWER_OF_ATTORNEY", string? doc = null) =>
        _f.Send(HttpMethod.Post, Ports.Delegations, "/api/v1/delegations",
            $"{{\"grantorId\":\"{grantor}\",\"attorneyId\":\"{attorney}\",\"type\":\"{type}\",\"scopes\":{scopes},\"validFrom\":\"{from}\",\"validTo\":\"{to}\"{(doc is null ? "" : $",\"documentRef\":\"{doc}\"")}}}",
            NordvikFixture.Bearer(token));

    [Fact]
    public async Task Delegations_are_verified_by_four_eyes_and_checked_by_scope_and_date()
    {
        var grantor = await People.Clean(_f);
        var attorney = await People.Clean(_f);
        var olof = await _f.Olof();
        var d = await NewDelegation(grantor, attorney, olof);
        Assert.Equal(201, d.Code);
        Assert.Equal("PENDING_VERIFICATION", d.Str("status"));
        var id = d.Str("id");
        string Check(string scope) => $"/api/v1/delegations/check?attorneyId={attorney}&grantorId={grantor}&scope={scope}";
        Assert.Equal("PENDING_VERIFICATION", (await _f.Get(Ports.Delegations, Check("READ"))).Str("reason"));
        (await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{id}/verify", null, NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "FOUR_EYES_REQUIRED");
        (await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{id}/verify", null, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var v = await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{id}/verify", null, NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("ACTIVE", v.Str("status"));
        Assert.Equal("compliance.karin", v.Str("verifiedBy"));
        Assert.True((await _f.Get(Ports.Delegations, Check("TRADE"))).Json.GetProperty("allowed").GetBoolean());
        var pay = await _f.Get(Ports.Delegations, Check("PAYMENTS"));
        Assert.False(pay.Json.GetProperty("allowed").GetBoolean());
        Assert.Equal("SCOPE_NOT_GRANTED", pay.Str("reason"));
        Assert.Equal("NO_DELEGATION", (await _f.Get(Ports.Delegations, $"/api/v1/delegations/check?attorneyId={grantor}&grantorId={attorney}&scope=READ")).Str("reason"));
        (await NewDelegation(grantor, attorney, olof)).ShouldBeProblem(409, "DELEGATION_EXISTS");
        (await NewDelegation(grantor, grantor, olof)).ShouldBeProblem(422, "SELF_DELEGATION");
        var other = await People.Clean(_f);
        (await NewDelegation(other, attorney, olof, type: "GUARDIANSHIP")).ShouldBeProblem(422, "DOCUMENT_REQUIRED");
        (await NewDelegation(other, attorney, olof, scopes: "[\"FLY\"]")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await NewDelegation(other, attorney, olof, from: "2026-01-01", to: "2040-01-01")).ShouldBeProblem(400, "VALIDATION_FAILED");
        var lists = await _f.Get(Ports.Delegations, $"/api/v1/customers/{attorney}/delegations", NordvikFixture.Bearer(olof));
        Assert.Equal(1, lists.Json.GetProperty("received").GetArrayLength());

        await _f.Freeze("2031-01-02T10:00:00");
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Delegations, $"/api/v1/delegations/{id}", NordvikFixture.Bearer(await _f.Olof()))).Str("status"));
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Delegations, Check("READ"))).Str("reason"));
        await _f.ResetClock();
        var g = await NewDelegation(other, attorney, olof, type: "GUARDIANSHIP", doc: "TR-2026-1234", from: "2027-01-01", to: "2028-01-01");
        await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{g.Str("id")}/verify", null, NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("NOT_YET_VALID", (await _f.Get(Ports.Delegations, $"/api/v1/delegations/{g.Str("id")}", NordvikFixture.Bearer(olof))).Str("status"));
        var rv = await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{g.Str("id")}/revoke", null, NordvikFixture.Bearer(olof));
        Assert.Equal("REVOKED", rv.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Delegations, $"/api/v1/delegations/{g.Str("id")}/revoke", null, NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        await TestData.Events(_f, "delegation.verified", id);
    }

    [Fact]
    public async Task Segments_follow_assets_age_and_rule_order()
    {
        async Task<string> Seg(string assets, int age) => (await _f.Post(Ports.Segments, "/api/v1/segments/simulate", $"{{\"assets\":\"{assets}\",\"age\":{age}}}")).Str("segment");
        Assert.Equal("RETAIL", await Seg("999999.99", 40));
        Assert.Equal("PREMIUM", await Seg("1000000.00", 40));
        Assert.Equal("PREMIUM", await Seg("1000000.00", 22)); // el patrimonio pesa más que la edad
        Assert.Equal("PRIVATE_BANKING", await Seg("5000000.00", 70));
        Assert.Equal("YOUNG_SAVER", await Seg("100.00", 25));
        Assert.Equal("RETAIL", await Seg("100.00", 26));
        var gap = await _f.Post(Ports.Segments, "/api/v1/segments/simulate", "{\"assets\":\"250000.00\",\"age\":40}");
        Assert.Equal("750000.00", gap.Str("gapToNext"));
        (await _f.Post(Ports.Segments, "/api/v1/segments/simulate", "{\"assets\":\"-1\",\"age\":40}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        var c = await People.Customer(_f, "Selma", "Segment", new DateOnly(1980, 1, 1));
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var s = await _f.Get(Ports.Segments, $"/api/v1/customers/{c}/segment", erik);
        Assert.Equal("RETAIL", s.Str("segment"));
        Assert.Equal("0.00", s.Str("assets"));
        Assert.False(s.Json.GetProperty("corporateClient").GetBoolean());
        await Pensions.PlanId(_f, c, "OCCUPATIONAL", Pensions.Org(), "ITP1");
        var s2 = await _f.Get(Ports.Segments, $"/api/v1/customers/{c}/segment", erik);
        Assert.True(s2.Json.GetProperty("corporateClient").GetBoolean());
        Assert.Equal(1, s2.Json.GetProperty("portfolios").GetArrayLength());
        (await _f.Get(Ports.Segments, $"/api/v1/customers/{c}/segment", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class GovernanceTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public GovernanceTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Complaints_have_legal_deadlines_in_business_days()
    {
        await _f.Freeze("2026-12-23T10:00:00"); // miércoles antes de Navidad
        var c = await People.Clean(_f);
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var r = await _f.Send(HttpMethod.Post, Ports.Complaints, "/api/v1/complaints", JsonSerializer.Serialize(new { customerId = c, category = "fees", description = "Me cobraron la comisión dos veces" }), erik);
        Assert.True(r.Code == 201, r.Body);
        Assert.Equal("OPEN", r.Str("status"));
        Assert.Equal(Money.Date(NordicCalendar.AddTradingDays("XSTO", new DateOnly(2026, 12, 23), 2)), r.Str("acknowledgeBy"));
        Assert.Equal("2026-12-29", r.Str("acknowledgeBy")); // 24, 25 y el fin de semana no cuentan
        Assert.StartsWith("case_", r.Str("caseId"));
        var id = r.Str("id");
        var olof = NordvikFixture.Bearer(await _f.Olof());
        (await _f.Send(HttpMethod.Post, Ports.Complaints, $"/api/v1/complaints/{id}/resolve", "{\"outcome\":\"UPHELD\",\"explanation\":\"x\"}", olof)).ShouldBeProblem(409, "INVALID_STATE_TRANSITION");
        Assert.Equal("ACKNOWLEDGED", (await _f.Send(HttpMethod.Post, Ports.Complaints, $"/api/v1/complaints/{id}/acknowledge", null, olof)).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Complaints, $"/api/v1/complaints/{id}/resolve", "{\"outcome\":\"REJECTED\",\"explanation\":\"No procede\",\"compensation\":\"10.00\"}", olof)).ShouldBeProblem(422, "COMPENSATION_ON_REJECTION");
        var res = await _f.Send(HttpMethod.Post, Ports.Complaints, $"/api/v1/complaints/{id}/resolve", "{\"outcome\":\"UPHELD\",\"explanation\":\"Se devuelve la comisión\",\"compensation\":\"250.00\"}", olof);
        Assert.Equal("RESOLVED", res.Str("status"));
        Assert.Equal("250.00", res.Json.GetProperty("compensation").GetProperty("amount").GetString());
        Assert.False(res.Json.GetProperty("breaches").GetProperty("acknowledgement").GetBoolean());

        var late = await _f.Send(HttpMethod.Post, Ports.Complaints, "/api/v1/complaints", JsonSerializer.Serialize(new { customerId = c, category = "SERVICE", description = "Nadie me contesta al teléfono" }), erik);
        await _f.Freeze("2026-12-30T10:00:00");
        var overdue = await _f.Get(Ports.Complaints, "/api/v1/complaints/overdue", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Contains(overdue.Json.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetString() == late.Str("id"));
        Assert.True((await _f.Get(Ports.Complaints, $"/api/v1/complaints/{late.Str("id")}", NordvikFixture.Bearer(await _f.Erik()))).Json.GetProperty("breaches").GetProperty("acknowledgement").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Complaints, "/api/v1/complaints", JsonSerializer.Serialize(new { customerId = c, category = "WEATHER", description = "corto" }), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Complaints, $"/api/v1/complaints/{id}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        await TestData.Events(_f, "complaint.resolved", id);
    }

    [Fact]
    public async Task Breaches_have_72_hours_and_high_risk_ones_are_told_to_the_people_affected()
    {
        await _f.Freeze("2026-10-20T10:00:00");
        var karin = await _f.Karin();
        var victim = (await _f.NewCustomer()).GetProperty("id").GetString()!;
        var body = JsonSerializer.Serialize(new { detectedAt = "2026-10-20T09:00:00+02:00", description = "Un listado con correos salió a un proveedor", dataCategories = new[] { "contact", "NATIONAL_ID" }, affectedCustomerIds = new[] { victim }, riskToRights = "HIGH" });
        var b = await _f.Send(HttpMethod.Post, Ports.Breaches, "/api/v1/breaches", body, NordvikFixture.Bearer(karin));
        Assert.True(b.Code == 201, b.Body);
        Assert.Equal(71.0, b.Json.GetProperty("hoursLeft").GetDouble(), 1);
        var id = b.Str("id");
        (await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/notify-subjects", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "AUTHORITY_FIRST");
        (await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/close", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "AUTHORITY_NOT_NOTIFIED");
        await _f.Freeze("2026-10-23T10:00:00");
        karin = await _f.Karin();
        var overdue = await _f.Get(Ports.Breaches, "/api/v1/breaches/overdue", NordvikFixture.Bearer(karin));
        Assert.Contains(overdue.Json.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetString() == id);
        var n = await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/notify-authority", "{\"reference\":\"IMY-2026-77\"}", NordvikFixture.Bearer(karin));
        Assert.Equal("AUTHORITY_NOTIFIED", n.Str("status"));
        Assert.True(n.Json.GetProperty("authorityNoticeLate").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/close", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "SUBJECTS_NOT_NOTIFIED");
        var s = await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/notify-subjects", null, NordvikFixture.Bearer(karin));
        Assert.Equal(1, s.Int("sent"));
        var email = (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{victim}")).Str("email");
        var box = await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={Uri.EscapeDataString(email)}");
        Assert.Contains("NATIONAL_ID", box.Body);
        Assert.Equal("CLOSED", (await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{id}/close", null, NordvikFixture.Bearer(karin))).Str("status"));
        var low = await _f.Send(HttpMethod.Post, Ports.Breaches, "/api/v1/breaches", body.Replace("\"HIGH\"", "\"LOW\""), NordvikFixture.Bearer(karin));
        await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{low.Str("id")}/notify-authority", null, NordvikFixture.Bearer(karin));
        (await _f.Send(HttpMethod.Post, Ports.Breaches, $"/api/v1/breaches/{low.Str("id")}/notify-subjects", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(422, "SUBJECT_NOTICE_NOT_REQUIRED");
        (await _f.Send(HttpMethod.Post, Ports.Breaches, "/api/v1/breaches", "{\"riskToRights\":\"MEDIUM\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Breaches, "/api/v1/breaches", body, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");

        // Informe de incidentes de octubre al supervisor
        await _f.Freeze("2026-11-02T10:00:00");
        karin = await _f.Karin();
        var rep = await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"INCIDENTS\",\"month\":\"2026-10\"}", NordvikFixture.Bearer(karin));
        Assert.True(rep.Code == 201, rep.Body);
        Assert.True(rep.Json.GetProperty("content").GetProperty("incidents").GetInt32() >= 2);
        Assert.True(rep.Json.GetProperty("content").GetProperty("lateAuthorityNotices").GetInt32() >= 1);
        (await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"INCIDENTS\",\"month\":\"2026-10\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "REPORT_ALREADY_SUBMITTED");
        (await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"INCIDENTS\",\"month\":\"2026-11\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(422, "MONTH_NOT_CLOSED");
    }

    [Fact]
    public async Task Transaction_reports_are_due_the_next_business_day_and_pseudonymised()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var (customer, _, _) = await Flows.FundedCustomer(_f, "8000.00");
        var pf = await Flows.Portfolio(_f, customer);
        var o = await Flows.Buy(_f, pf, "SE0000000003", "5000.00");
        await Flows.ExecuteAt1805(_f, o.Str("id"), TestData.Tuesday);
        await _f.Freeze("2026-10-21T09:00:00");
        var karin = NordvikFixture.Bearer(await _f.Karin());
        var r = await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", $"{{\"type\":\"TRANSACTIONS\",\"date\":\"{TestData.Tuesday}\"}}", karin);
        Assert.True(r.Code == 201, r.Body);
        Assert.Equal("SUBMITTED", r.Str("status"));
        var row = r.Json.GetProperty("content").GetProperty("byIsin").EnumerateArray().Single(x => x.GetProperty("isin").GetString() == "SE0000000003");
        Assert.True(row.GetProperty("buys").GetInt32() >= 1);
        Assert.Contains("tok_cu_" + Canonical.Sha256Hex(customer)[..6], row.GetProperty("clients").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain(customer, r.Body);
        var late = await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"TRANSACTIONS\",\"date\":\"2026-10-16\"}", karin);
        Assert.Equal("SUBMITTED_LATE", late.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"TRANSACTIONS\",\"date\":\"2026-10-18\"}", karin)).ShouldBeProblem(422, "NOT_A_TRADING_DAY");
        (await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"TRANSACTIONS\",\"date\":\"2026-10-21\"}", karin)).ShouldBeProblem(422, "DAY_NOT_CLOSED");
        (await _f.Send(HttpMethod.Post, Ports.RegReports, "/api/v1/regulatory-reports", "{\"type\":\"GOSSIP\"}", karin)).ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.True((await _f.Get(Ports.RegReports, "/api/v1/regulatory-reports?type=transactions", karin)).Int("count") >= 2);
    }
}

[Collection("nordvik")]
public class DataRightsTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public DataRightsTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Erasure_is_blocked_by_live_contracts_and_otherwise_shreds_withdraws_and_closes()
    {
        var karin = NordvikFixture.Bearer(await _f.Karin());
        var withPlan = await People.Clean(_f);
        await Pensions.PlanId(_f, withPlan);
        var blocked = await _f.Send(HttpMethod.Post, Ports.Erasure, "/api/v1/erasure-requests", JsonSerializer.Serialize(new { customerId = withPlan, reason = "RGPD art. 17" }), karin);
        Assert.Equal(201, blocked.Code);
        Assert.Equal("BLOCKED", blocked.Str("status"));
        Assert.Equal("PENSION_PLAN_ACTIVE", blocked.Json.GetProperty("blockers")[0].GetProperty("code").GetString());
        Assert.Equal("ACTIVE", (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{withPlan}")).Str("status"));

        var c = await People.Clean(_f);
        var token = (await PiiVaultTests.Tokenize(_f, "IBAN", TestData.Iban("5555"), c)).Str("token");
        await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", JsonSerializer.Serialize(new { customerId = c, purpose = "MARKETING", granted = true }), NordvikFixture.Bearer(await _f.Erik()));
        var e = await _f.Send(HttpMethod.Post, Ports.Erasure, "/api/v1/erasure-requests", JsonSerializer.Serialize(new { customerId = c, reason = "Lo pide el cliente" }), karin);
        Assert.True(e.Code == 201, e.Body);
        Assert.Equal("COMPLETED", e.Str("status"));
        Assert.Equal(64, e.Str("certificateSha256").Length);
        var steps = e.Json.GetProperty("steps").EnumerateArray().ToDictionary(s => s.GetProperty("step").GetString()!, s => s);
        Assert.True(steps["CRYPTO_SHRED"].GetProperty("ok").GetBoolean());
        Assert.Contains("1 consentimientos retirados", steps["CONSENTS_WITHDRAWN"].GetProperty("detail").GetString());
        Assert.True(steps["CUSTOMER_CLOSED"].GetProperty("ok").GetBoolean());
        Assert.Contains(e.Json.GetProperty("retained").EnumerateArray(), x => x.GetString()!.StartsWith("ledger-service"));
        Assert.DoesNotContain(c, e.Body);
        Assert.Equal("CLOSED", (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{c}")).Str("status"));
        await Eventually.Until(async () => await _f.Get(Ports.PiiVault, $"/api/v1/tokens/{token}", karin), r => r.Str("status") == "ERASED", 8000, "token borrado por crypto-shredding");
        Assert.False((await _f.Get(Ports.Consents, $"/api/v1/internal/consents/check?customerId={c}&purpose=MARKETING")).Json.GetProperty("granted").GetBoolean());
        (await _f.Send(HttpMethod.Post, Ports.Erasure, "/api/v1/erasure-requests", JsonSerializer.Serialize(new { customerId = c }), karin)).ShouldBeProblem(409, "ALREADY_ERASED");
        (await _f.Send(HttpMethod.Post, Ports.Erasure, "/api/v1/erasure-requests", JsonSerializer.Serialize(new { customerId = withPlan }), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        await TestData.Events(_f, "erasure.completed", e.Str("id"));
    }

    [Fact]
    public async Task Access_packages_gather_everything_and_can_be_downloaded_three_times()
    {
        var c = await People.Clean(_f);
        var nid = await TestData.NationalIdOf(_f, c);
        await Pensions.PlanId(_f, c);
        var karin = NordvikFixture.Bearer(await _f.Karin());
        (await _f.Send(HttpMethod.Post, Ports.Dsar, "/api/v1/dsar", JsonSerializer.Serialize(new { customerId = c }), NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var r = await _f.Send(HttpMethod.Post, Ports.Dsar, "/api/v1/dsar", JsonSerializer.Serialize(new { customerId = c, type = "ACCESS" }), karin);
        Assert.Equal(201, r.Code);
        Assert.Equal(3, r.Int("downloadsLeft"));
        var id = r.Str("id");
        var p1 = await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}/package", karin);
        Assert.Equal("nordvik-access-v1", p1.Str("format"));
        Assert.Equal(nid, p1.Json.GetProperty("sections").GetProperty("profile").GetProperty("data").GetProperty("nationalId").GetString());
        Assert.Equal(1, p1.Json.GetProperty("sections").GetProperty("pensionPlans").GetProperty("data").GetArrayLength());
        Assert.Equal("2", p1.Headers.GetValues("X-Downloads-Left").Single());
        await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}/package", karin);
        var p3 = await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}/package", karin);
        Assert.Equal("0", p3.Headers.GetValues("X-Downloads-Left").Single());
        (await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}/package", karin)).ShouldBeProblem(410, "DOWNLOAD_LIMIT_REACHED");
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}", karin)).Str("status"));
        var port = await _f.Send(HttpMethod.Post, Ports.Dsar, "/api/v1/dsar", JsonSerializer.Serialize(new { customerId = c, type = "PORTABILITY" }), karin);
        var pp = await _f.Get(Ports.Dsar, $"/api/v1/dsar/{port.Str("id")}/package", karin);
        Assert.Equal("nordvik-portability-v1", pp.Str("format"));
        Assert.False(pp.Has("sections"));
        await _f.Freeze(DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-ddTHH:mm:ss"));
        karin = NordvikFixture.Bearer(await _f.Karin());
        (await _f.Get(Ports.Dsar, $"/api/v1/dsar/{port.Str("id")}/package", karin)).ShouldBeProblem(410, "PACKAGE_EXPIRED");
        (await _f.Get(Ports.Dsar, $"/api/v1/dsar/{id}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task The_archive_is_a_tamper_evident_chain_that_cannot_be_changed()
    {
        var olof = NordvikFixture.Bearer(await _f.Olof());
        var admin = NordvikFixture.Bearer(await _f.Admin());
        string Doc(string content) => JsonSerializer.Serialize(new { sourceService = "contract-service", documentType = "SIGNED_CONTRACT", content, reference = "ctr-1" });
        var a = await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/archive", Doc("Contrato marco firmado " + Guid.NewGuid()), olof);
        var b = await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/archive", Doc("Anexo de fondos " + Guid.NewGuid()), olof);
        Assert.Equal(201, b.Code);
        Assert.Equal(a.Str("hash"), b.Str("previousHash"));
        Assert.Equal(a.Int("seq") + 1, b.Int("seq"));
        Assert.False(b.Has("content") && b.Json.GetProperty("content").ValueKind == JsonValueKind.String);
        var got = await _f.Get(Ports.Archive, $"/api/v1/archive/{b.Str("id")}", olof);
        Assert.Equal(Canonical.Sha256Hex(got.Str("content")), got.Str("contentSha256"));
        (await _f.Send(HttpMethod.Delete, Ports.Archive, $"/api/v1/archive/{a.Str("id")}", null, olof)).ShouldBeProblem(405, "ARCHIVE_IMMUTABLE");
        (await _f.Send(HttpMethod.Put, Ports.Archive, $"/api/v1/archive/{a.Str("id")}", "{}", olof)).ShouldBeProblem(405, "ARCHIVE_IMMUTABLE");
        (await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/archive", JsonSerializer.Serialize(new { sourceService = "x", documentType = "y", content = "z", retentionYears = 5 }), olof)).ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.True((await _f.Get(Ports.Archive, "/api/v1/archive-verify", olof)).Json.GetProperty("valid").GetBoolean());
        (await _f.Get(Ports.Archive, $"/api/v1/archive/{a.Str("id")}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");

        // Purga tras la retención: el contenido se va, la cadena sigue verificable
        await _f.Freeze("2037-01-05T10:00:00");
        admin = NordvikFixture.Bearer(await _f.Admin());
        Assert.True((await _f.Post(Ports.Archive, "/api/v1/archive-purge")).Code == 401);
        Assert.True((await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/archive-purge", null, admin)).Int("purged") >= 2);
        var purged = await _f.Get(Ports.Archive, $"/api/v1/archive/{a.Str("id")}", admin);
        Assert.True(purged.Json.GetProperty("purged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, purged.Json.GetProperty("content").ValueKind);
        Assert.True((await _f.Get(Ports.Archive, "/api/v1/archive-verify", admin)).Json.GetProperty("valid").GetBoolean());

        // Una manipulación directa en la base de datos se detecta
        var c = await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/archive", Doc("Extracto anual " + Guid.NewGuid()), admin);
        await _f.Send(HttpMethod.Post, Ports.Archive, "/api/v1/admin/tamper", JsonSerializer.Serialize(new { recordId = c.Str("id") }), admin);
        var v = await _f.Get(Ports.Archive, "/api/v1/archive-verify", admin);
        Assert.False(v.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(c.Str("id"), v.Json.GetProperty("brokenAt").GetProperty("id").GetString());
        Assert.Equal("CONTENT_MODIFIED", v.Json.GetProperty("brokenAt").GetProperty("problem").GetString());
    }

    [Fact]
    public async Task Esg_scores_are_deterministic_weighted_and_profiling_needs_consent()
    {
        var f5 = await _f.Get(Ports.Esg, "/api/v1/funds/SE0000000005/esg");
        Assert.Equal(9, f5.Int("sfdrArticle"));
        Assert.True(f5.Int("score") >= 85);
        Assert.Equal(f5.Body, (await _f.Get(Ports.Esg, "/api/v1/funds/se0000000005/esg")).Body);
        var rank = await _f.Get(Ports.Esg, "/api/v1/esg/ranking?minScore=60");
        var scores = rank.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("score").GetInt32()).ToList();
        Assert.Equal(scores.OrderByDescending(x => x), scores);
        Assert.All(scores, s => Assert.True(s >= 60));
        (await _f.Get(Ports.Esg, "/api/v1/funds/XX0000000000/esg")).ShouldBeProblem(404, "UNKNOWN_FUND");
        var c = await People.Clean(_f);
        var pf = await Flows.Portfolio(_f, c, "[{\"isin\":\"SE0000000005\",\"percent\":50},{\"isin\":\"SE0000000101\",\"percent\":50}]");
        var erik = NordvikFixture.Bearer(await _f.Erik());
        var esg = await _f.Get(Ports.Esg, $"/api/v1/portfolios/{pf}/esg", erik);
        Assert.Equal("TARGET_ALLOCATION", esg.Str("basis"));
        var s101 = (await _f.Get(Ports.Esg, "/api/v1/funds/SE0000000101/esg")).Int("score");
        Assert.Equal(Math.Round((f5.Int("score") + s101) / 2.0, 1), esg.Json.GetProperty("score").GetDouble(), 1);
        Assert.Equal(50.0, esg.Json.GetProperty("article9Share").GetDouble());
        (await _f.Get(Ports.Esg, $"/api/v1/customers/{c}/esg-profile", erik)).ShouldBeProblem(403, "CONSENT_REQUIRED");
        await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", JsonSerializer.Serialize(new { customerId = c, purpose = "ESG_PROFILING", granted = true }), erik);
        var prof = await _f.Get(Ports.Esg, $"/api/v1/customers/{c}/esg-profile", erik);
        Assert.Equal(200, prof.Code);
        Assert.Equal(2, prof.Int("funds"));
        (await _f.Get(Ports.Esg, $"/api/v1/portfolios/{pf}/esg", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class LegacyAndQualityTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public LegacyAndQualityTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public async Task DisposeAsync() { await _f.ResetClock(); await _f.Post(Ports.Chaos, "/api/v1/reset"); }

    [Fact]
    public async Task Dead_letters_from_consumers_can_be_retried_or_discarded_with_a_reason()
    {
        var olof = NordvikFixture.Bearer(await _f.Olof());
        var eventId = "evt_poison_" + Guid.NewGuid().ToString("N")[..8];
        var pub = await _f.Post(Ports.EventBus, "/api/v1/events", JsonSerializer.Serialize(new { eventId, topic = "key.shredded", key = "k-poison", payload = new { nothing = true } }));
        Assert.Equal(202, pub.Code);
        var key = $"consumer:pii-vault:{eventId}";
        var list = await Eventually.Until(async () => await _f.Get(Ports.DeadLetters, "/api/v1/dead-letters?source=consumer", olof),
            r => r.Json.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("key").GetString() == key && x.GetProperty("skipped").GetBoolean()), 20000, "evento envenenado en la cola");
        var item = list.Json.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
        Assert.Equal("key.shredded", item.GetProperty("topic").GetString());
        Assert.Contains("KeyNotFoundException", item.GetProperty("lastError").GetString());
        (await _f.Send(HttpMethod.Post, Ports.DeadLetters, $"/api/v1/dead-letters/{key}/retry", null, olof)).ShouldBeProblem(422, "RETRY_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.DeadLetters, $"/api/v1/dead-letters/{key}/discard", "{}", olof)).ShouldBeProblem(400, "VALIDATION_FAILED");
        var d = await _f.Send(HttpMethod.Post, Ports.DeadLetters, $"/api/v1/dead-letters/{key}/discard", "{\"reason\":\"Evento de prueba sin keyId\"}", olof);
        Assert.True(d.Json.GetProperty("discarded").GetBoolean());
        var after = await _f.Get(Ports.DeadLetters, "/api/v1/dead-letters", olof);
        Assert.DoesNotContain(after.Json.GetProperty("items").EnumerateArray(), x => x.GetProperty("key").GetString() == key);
        var decisions = await _f.Get(Ports.DeadLetters, "/api/v1/decisions", olof);
        Assert.Equal(new[] { "RETRY", "DISCARD" }, decisions.Json.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("key").GetString() == key).Select(x => x.GetProperty("action").GetString()));
        (await _f.Send(HttpMethod.Post, Ports.DeadLetters, "/api/v1/dead-letters/consumer:nada:evt/retry", null, olof)).ShouldBeProblem(404, "DEAD_LETTER_NOT_FOUND");
        (await _f.Get(Ports.DeadLetters, "/api/v1/dead-letters", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task The_mainframe_speaks_fixed_width_with_numeric_return_codes()
    {
        var plan = await Pensions.PlanId(_f, await People.Clean(_f));
        var olof = await _f.Olof();
        await _f.Freeze("2026-10-21T10:00:00");
        var closed = await _f.Send(HttpMethod.Post, Ports.Mainframe, "/api/v1/batches/run", null, NordvikFixture.Bearer(await _f.Olof()));
        closed.ShouldBeProblem(409, "MAINFRAME_RC_0016");
        Assert.Equal("0016", closed.Str("rc"));
        await _f.Freeze("2026-10-21T02:30:00");
        olof = await _f.Olof();
        var run = await _f.Send(HttpMethod.Post, Ports.Mainframe, "/api/v1/batches/run", null, NordvikFixture.Bearer(olof));
        Assert.True(run.Code == 200, run.Body);
        Assert.Equal("0000", run.Str("rc"));
        var file = await _f.Get(Ports.Mainframe, "/api/v1/batches/2026-10-21/export", NordvikFixture.Bearer(olof));
        Assert.StartsWith("text/plain", file.ContentType!.MediaType);
        var lines = file.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, l => Assert.Equal(80, l.Length));
        Assert.StartsWith("HD20261021", lines[0]);
        Assert.Equal(run.Int("records"), int.Parse(lines[^1][2..11]));
        Assert.Contains(lines, l => l.StartsWith("PL") && l[2..22].Trim() == plan);

        string Bl(string id, string cents, string date) => ("BL" + id.PadRight(20) + cents + date).PadRight(80);
        var inbound = string.Join("\n", new[]
        {
            Bl(plan, "000000012345678", "20141231"),
            Bl(plan, "000000000000000", "20141231"),
            Bl(plan, "000000012345678", "20141399"),
            Bl("pp_no_existe", "000000000100000", "20141231"),
            Bl(plan, "0000000ABC45678", "20141231"),
            "BL corta",
            "XX".PadRight(80),
        });
        var res = await _f.Send(HttpMethod.Post, Ports.Mainframe, "/api/v1/inbound/balances", null, req =>
        {
            NordvikFixture.Bearer(olof)(req);
            req.Content = new StringContent(inbound, System.Text.Encoding.UTF8, "text/plain");
        });
        Assert.True(res.Code == 200, res.Body);
        Assert.Equal(new[] { "0000", "0004", "0042", "0044", "0043", "0008", "0012" }, res.Json.GetProperty("results").EnumerateArray().Select(x => x.GetProperty("rc").GetString()));
        Assert.Equal("0044", res.Str("rc"));
        Assert.Equal(2, res.Int("accepted"));
        var bal = await _f.Get(Ports.Mainframe, $"/api/v1/legacy-balances/{plan}", NordvikFixture.Bearer(olof));
        Assert.Equal("0.00", bal.Json.GetProperty("balance").GetProperty("amount").GetString()); // la última línea válida (rc 0004) manda
        (await _f.Get(Ports.Mainframe, "/api/v1/legacy-balances/pp_nada", NordvikFixture.Bearer(olof))).ShouldBeProblem(404, "MAINFRAME_RC_0044");
        Assert.True((await _f.Get(Ports.Mainframe, "/api/v1/layouts")).Json.GetArrayLength() >= 4);
    }

    private async Task<(int Code, XDocument Xml)> Soap(string? action, string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, NordvikFixture.Url(Ports.LegacySoap, "/soap/LegacyPensionService"))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/xml"),
        };
        if (action is not null) req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{action}\"");
        var res = await _f.Http.SendAsync(req);
        return ((int)res.StatusCode, XDocument.Parse(await res.Content.ReadAsStringAsync()));
    }

    private static string Envelope(string inner) =>
        $"<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:leg=\"urn:nordvik:legacy:pensions:v1\"><soapenv:Header/><soapenv:Body>{inner}</soapenv:Body></soapenv:Envelope>";

    [Fact]
    public async Task The_old_pension_system_answers_soap_with_faults_as_http_500()
    {
        XNamespace leg = "urn:nordvik:legacy:pensions:v1";
        var old = NationalId.Create(new DateOnly(1960, 5, 5), 12, true);
        var young = NationalId.Create(new DateOnly(1990, 5, 5), 12, true);
        var (code, xml) = await Soap("GetLegacyPlans", Envelope($"<leg:GetLegacyPlansRequest><leg:personalNumber>{old}</leg:personalNumber></leg:GetLegacyPlansRequest>"));
        Assert.Equal(200, code);
        var plans = xml.Descendants(leg + "plan").ToList();
        Assert.InRange(plans.Count, 1, 2);
        Assert.Equal(plans.Count, int.Parse(xml.Descendants(leg + "count").Single().Value));
        Assert.All(plans, p => Assert.True(int.Parse(p.Element(leg + "startYear")!.Value) < 2015));
        var number = plans[0].Element(leg + "planNumber")!.Value;
        var (c2, one) = await Soap("GetLegacyPlan", Envelope($"<leg:GetLegacyPlanRequest><leg:personalNumber>{old}</leg:personalNumber><leg:planNumber>{number}</leg:planNumber></leg:GetLegacyPlanRequest>"));
        Assert.Equal(200, c2);
        Assert.Equal(plans[0].Element(leg + "value")!.Value, one.Descendants(leg + "value").Single().Value);
        Assert.Equal("0", (await Soap("GetLegacyPlans", Envelope($"<leg:GetLegacyPlansRequest><leg:personalNumber>{young}</leg:personalNumber></leg:GetLegacyPlansRequest>"))).Xml.Descendants(leg + "count").Single().Value);

        async Task Fault(string? action, string body, string errorCode)
        {
            var (c, x) = await Soap(action, body);
            Assert.Equal(500, c);
            Assert.Equal("soapenv:Client", x.Descendants("faultcode").Single().Value);
            Assert.Equal(errorCode, x.Descendants(leg + "errorCode").Single().Value);
        }
        await Fault("GetLegacyPlans", Envelope("<leg:GetLegacyPlansRequest><leg:personalNumber>19600505-000X</leg:personalNumber></leg:GetLegacyPlansRequest>"), "INVALID_PNR");
        await Fault(null, Envelope("<leg:GetLegacyPlansRequest/>"), "NO_SOAPACTION");
        await Fault("GetLegacyPlan", Envelope($"<leg:GetLegacyPlanRequest><leg:personalNumber>{old}</leg:personalNumber><leg:planNumber>LP-0000-XXXX</leg:planNumber></leg:GetLegacyPlanRequest>"), "PLAN_NOT_FOUND");
        await Fault("GetLegacyPlan", Envelope($"<leg:GetLegacyPlansRequest><leg:personalNumber>{old}</leg:personalNumber></leg:GetLegacyPlansRequest>"), "ACTION_MISMATCH");
        await Fault("GetLegacyPlans", "<esto no es xml", "MALFORMED_XML");
        await Fault("Borrar", Envelope("<leg:X/>"), "UNKNOWN_ACTION");
        var wsdl = await _f.Get(Ports.LegacySoap, "/soap/LegacyPensionService?wsdl");
        Assert.Contains("soapAction=\"GetLegacyPlans\"", wsdl.Body);
        Assert.Equal(XDocument.Parse(wsdl.Body).Root!.Name.LocalName, "definitions");
    }

    [Fact]
    public async Task Contract_check_finds_drift_between_the_openapi_and_the_real_response()
    {
        string Body(string service, string path) => JsonSerializer.Serialize(new { service, path });
        var ok = await _f.Post(Ports.ContractCheck, "/api/v1/checks", Body("product-catalog", "/api/v1/funds/SE0000000001"));
        Assert.True(ok.Code == 200, ok.Body);
        Assert.True(ok.Str("status") == "MATCHES", ok.Body);
        await _f.Send(HttpMethod.Put, Ports.Chaos, "/api/v1/traps/T9", "{\"enabled\":true}");
        try
        {
            var drift = await _f.Post(Ports.ContractCheck, "/api/v1/checks", Body("product-catalog", "/api/v1/funds/SE0000000001"));
            Assert.Equal("DRIFT", drift.Str("status"));
            var diffs = drift.Json.GetProperty("differences").EnumerateArray().Select(d => $"{d.GetProperty("kind").GetString()} {d.GetProperty("path").GetString()}").ToList();
            Assert.Contains("MISSING_FIELD $.isin", diffs);
            Assert.Contains("UNDOCUMENTED_FIELD $.instrumentId", diffs);
        }
        finally { await _f.Post(Ports.Chaos, "/api/v1/reset"); }
        (await _f.Post(Ports.ContractCheck, "/api/v1/checks", Body("no-existe", "/x"))).ShouldBeProblem(404, "SERVICE_NOT_FOUND");
        (await _f.Post(Ports.ContractCheck, "/api/v1/checks", Body("product-catalog", "/api/v1/nada"))).ShouldBeProblem(422, "OPERATION_NOT_DOCUMENTED");
        (await _f.Post(Ports.ContractCheck, "/api/v1/checks", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.True((await _f.Get(Ports.ContractCheck, "/api/v1/checks?service=product-catalog")).Int("count") >= 2);
    }

    [Fact]
    public async Task Test_insights_spot_flaky_and_slow_tests()
    {
        var suite = "regresion-" + Guid.NewGuid().ToString("N")[..6];
        string[] a = { "passed", "failed", "passed", "failed" }, c = { "passed", "passed", "failed", "failed" };
        for (var i = 0; i < 4; i++)
        {
            var r = await _f.Post(Ports.TestInsights, "/api/v1/runs", JsonSerializer.Serialize(new
            {
                suite, environment = "local", results = new object[]
                {
                    new { name = "A pago", status = a[i], durationMs = 100 },
                    new { name = "B login", status = "passed", durationMs = 50 },
                    new { name = "C orden", status = c[i], durationMs = 900 + i },
                    new { name = "D saltado", status = "skipped", durationMs = 0 },
                },
            }));
            Assert.Equal(201, r.Code);
            Assert.Equal(i + 1, r.Int("runNumber"));
        }
        var flaky = await _f.Get(Ports.TestInsights, $"/api/v1/suites/{suite}/flaky");
        var names = flaky.Json.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "A pago", "C orden" }, names);
        Assert.Equal(1.0, flaky.Json.GetProperty("items")[0].GetProperty("flipRate").GetDouble());
        Assert.Equal("PFPF", flaky.Json.GetProperty("items")[0].GetProperty("history").GetString());
        Assert.Equal(0.333, flaky.Json.GetProperty("items")[1].GetProperty("flipRate").GetDouble());
        var strict = await _f.Get(Ports.TestInsights, $"/api/v1/suites/{suite}/flaky?minFlipRate=0.5");
        Assert.Equal(1, strict.Int("count"));
        var trend = await _f.Get(Ports.TestInsights, $"/api/v1/suites/{suite}/trend");
        Assert.Equal(new[] { 100.0, 66.67, 66.67, 33.33 }, trend.Json.GetProperty("runs").EnumerateArray().Select(x => x.GetProperty("passRate").GetDouble()));
        Assert.Equal("WORSENING", trend.Str("direction"));
        Assert.Equal("C orden", (await _f.Get(Ports.TestInsights, $"/api/v1/suites/{suite}/slowest")).Json.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(4, (await _f.Get(Ports.TestInsights, $"/api/v1/tests/{Uri.EscapeDataString("A pago")}/history?suite={suite}")).Int("count"));
        (await _f.Post(Ports.TestInsights, "/api/v1/runs", JsonSerializer.Serialize(new { suite, results = new[] { new { name = "X", status = "green" } } }))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.TestInsights, "/api/v1/runs", JsonSerializer.Serialize(new { suite, results = new[] { new { name = "X", status = "passed" }, new { name = "X", status = "failed" } } }))).ShouldBeProblem(422, "DUPLICATE_TEST_NAMES");
        (await _f.Get(Ports.TestInsights, "/api/v1/suites/nada/trend")).ShouldBeProblem(404, "SUITE_NOT_FOUND");
    }
}
