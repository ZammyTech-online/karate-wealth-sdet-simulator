using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class CustomerRegistryTests
{
    private readonly NordvikFixture _f;
    public CustomerRegistryTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Advisor_creates_a_customer_with_number_and_etag()
    {
        var p = await _f.NewPerson();
        var body = JsonSerializer.Serialize(new { firstName = p.GetProperty("firstName").GetString(), lastName = p.GetProperty("lastName").GetString(), nationalId = p.GetProperty("nationalId").GetString() });
        var r = await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, r.Code);
        Assert.Matches(@"^CUS-\d{6}$", r.Str("customerNumber"));
        Assert.Equal("ACTIVE", r.Str("status"));
        Assert.Equal("\"1\"", r.Headers.ETag?.Tag);
        Assert.Equal($"/api/v1/customers/{r.Str("id")}", r.Headers.Location?.OriginalString);
        Assert.Contains("**-****", r.Str("nationalId"));
    }

    [Fact]
    public async Task Creator_advisor_is_assigned_automatically()
    {
        var c = await _f.NewCustomer();
        var mine = await _f.Get(Ports.Permission, $"/api/v1/advisors/{SeedIds.AdvisorErik}/customers");
        Assert.Contains(mine.Json.EnumerateArray(), a => a.GetProperty("customerId").GetString() == c.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Customer_role_cannot_create_customers()
    {
        var p = await _f.NewPerson();
        var body = JsonSerializer.Serialize(new { firstName = "X", lastName = "Y", nationalId = p.GetProperty("nationalId").GetString() });
        (await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "SCOPE_MISSING");
    }

    [Theory]
    [InlineData("19850314-1234", "nationalId")]
    [InlineData("1985-03-14", "nationalId")]
    [InlineData("19850230-1230", "nationalId")]
    public async Task Invalid_national_ids_are_explained(string nid, string field)
    {
        var body = JsonSerializer.Serialize(new { firstName = "Test", lastName = "Persona", nationalId = nid });
        var r = await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(await _f.Erik()));
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Contains(r.Json.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
    }

    [Fact]
    public async Task Several_validation_errors_at_once()
    {
        var body = JsonSerializer.Serialize(new { firstName = "", lastName = "1234", nationalId = "x", email = "no-email", language = "fr" });
        var r = await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(await _f.Erik()));
        var fields = r.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "firstName", "lastName", "nationalId", "email", "language" }, fields);
        Assert.Equal(5, fields.Count);
    }

    [Fact]
    public async Task Minors_cannot_be_customers()
    {
        var p = await _f.NewPerson(age: 16);
        var body = JsonSerializer.Serialize(new { firstName = "Joven", lastName = "Menor", nationalId = p.GetProperty("nationalId").GetString() });
        (await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(422, "CUSTOMER_UNDERAGE");
    }

    [Fact]
    public async Task Same_national_id_twice_is_a_conflict()
    {
        var p = await _f.NewPerson();
        var body = JsonSerializer.Serialize(new { firstName = "Doble", lastName = "Alta", nationalId = p.GetProperty("nationalId").GetString() });
        var erik = await _f.Erik();
        Assert.Equal(201, (await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(erik))).Code);
        (await _f.Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "CUSTOMER_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Each_role_sees_the_right_fields()
    {
        var id = SeedIds.CustomerAna;
        var asCustomer = await _f.Get(Ports.Customer, $"/api/v1/customers/{id}", NordvikFixture.Bearer(await _f.Ana()));
        var asAdvisor = await _f.Get(Ports.Customer, $"/api/v1/customers/{id}", NordvikFixture.Bearer(await _f.Erik()));
        var asCompliance = await _f.Get(Ports.Customer, $"/api/v1/customers/{id}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.DoesNotContain("*", asCustomer.Str("nationalId"));
        Assert.Contains("**-****", asAdvisor.Str("nationalId"));
        Assert.DoesNotContain("*", asCompliance.Str("nationalId"));
        Assert.True(asCustomer.Int("age") >= 41);
    }

    [Fact]
    public async Task Idor_another_customer_looks_like_not_found()
    {
        var r = await _f.Get(Ports.Customer, $"/api/v1/customers/{SeedIds.CustomerLars}", NordvikFixture.Bearer(await _f.Ana()));
        r.ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Unassigned_advisor_gets_403_with_reason()
    {
        var r = await _f.Get(Ports.Customer, $"/api/v1/customers/{SeedIds.CustomerSofia}", NordvikFixture.Bearer(await _f.Erik()));
        r.ShouldBeProblem(403, "ACCESS_DENIED");
        Assert.Contains("no está asignado", r.Str("detail"));
    }

    [Fact]
    public async Task Advisor_list_only_contains_assigned_customers()
    {
        var r = await _f.Get(Ports.Customer, "/api/v1/customers?pageSize=100", NordvikFixture.Bearer(await _f.Maja()));
        var ids = r.Json.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Contains(SeedIds.CustomerSofia, ids);
        Assert.DoesNotContain(SeedIds.CustomerAna, ids);
    }

    [Fact]
    public async Task Customers_cannot_list_customers() =>
        (await _f.Get(Ports.Customer, "/api/v1/customers", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");

    [Fact]
    public async Task Patch_requires_if_match_and_detects_conflicts()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString();
        var erik = await _f.Erik();
        (await _f.Send(HttpMethod.Patch, Ports.Customer, $"/api/v1/customers/{id}", "{\"phone\":\"+46 70 111 22 33\"}", NordvikFixture.Bearer(erik))).ShouldBeProblem(428, "PRECONDITION_REQUIRED");
        var ok = await _f.Send(HttpMethod.Patch, Ports.Customer, $"/api/v1/customers/{id}", "{\"phone\":\"+46 70 111 22 33\"}", NordvikFixture.Bearer(erik, "\"1\""));
        Assert.Equal(200, ok.Code);
        Assert.Equal(2, ok.Int("version"));
        Assert.Equal("\"2\"", ok.Headers.ETag?.Tag);
        (await _f.Send(HttpMethod.Patch, Ports.Customer, $"/api/v1/customers/{id}", "{\"language\":\"en\"}", NordvikFixture.Bearer(erik, "\"1\""))).ShouldBeProblem(412, "VERSION_CONFLICT");
        (await _f.Send(HttpMethod.Patch, Ports.Customer, $"/api/v1/customers/{id}", "{\"nationalId\":\"19800101-0000\"}", NordvikFixture.Bearer(erik, "\"2\""))).ShouldBeProblem(422, "FIELD_NOT_UPDATABLE");
    }

    [Fact]
    public async Task Only_admin_closes_and_closing_twice_conflicts()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString();
        (await _f.Delete(Ports.Customer, $"/api/v1/customers/{id}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
        (await _f.Send(HttpMethod.Delete, Ports.Customer, $"/api/v1/customers/{id}", null, NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ACCESS_DENIED");
        var admin = await _f.Admin();
        var closed = await _f.Send(HttpMethod.Delete, Ports.Customer, $"/api/v1/customers/{id}", null, NordvikFixture.Bearer(admin));
        Assert.Equal("CLOSED", closed.Str("status"));
        (await _f.Send(HttpMethod.Delete, Ports.Customer, $"/api/v1/customers/{id}", null, NordvikFixture.Bearer(admin))).ShouldBeProblem(409, "CUSTOMER_ALREADY_CLOSED");
    }

    [Fact]
    public async Task Lookup_by_customer_number()
    {
        var r = await _f.Get(Ports.Customer, "/api/v1/customers/by-number/CUS-900001", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(SeedIds.CustomerAna, r.Str("id"));
    }

    [Fact]
    public async Task Change_feed_records_creations()
    {
        var c = await _f.NewCustomer();
        var feed = await _f.Get(Ports.Customer, "/api/v1/internal/customers/changes?since=0&limit=500");
        Assert.Contains(feed.Json.GetProperty("items").EnumerateArray(), i => i.GetProperty("customerId").GetString() == c.GetProperty("id").GetString() && i.GetProperty("kind").GetString() == "CREATED");
    }

    [Fact]
    public async Task Search_finds_new_customers_after_a_short_delay()
    {
        var c = await _f.NewCustomer();
        var number = c.GetProperty("customerNumber").GetString();
        var erik = await _f.Erik();
        Reply? r = null;
        for (var i = 0; i < 30; i++)
        {
            r = await _f.Get(Ports.Search, $"/api/v1/search/customers?q={number}", NordvikFixture.Bearer(erik));
            if (r.Int("total") == 1) break;
            await Task.Delay(200);
        }
        Assert.Equal(1, r!.Int("total"));
        (await _f.Get(Ports.Search, "/api/v1/search/customers?q=a", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "QUERY_TOO_SHORT");
    }

    [Fact]
    public async Task Data_reset_removes_customers_by_external_ref()
    {
        var prefix = "RUN-TEST-" + Guid.NewGuid().ToString("N")[..6];
        var c = await _f.NewCustomer(externalRef: prefix + "-001");
        var report = await _f.Delete(Ports.Reset, $"/api/v1/runs/{prefix}");
        Assert.True(report.Int("totalDeleted") >= 1);
        (await _f.Get(Ports.Customer, $"/api/v1/customers/{c.GetProperty("id").GetString()}", NordvikFixture.Bearer(await _f.Admin()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class CustomerSatelliteTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public CustomerSatelliteTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Contact_verification_happy_path()
    {
        var ana = await _f.Ana();
        var email = "ana." + Guid.NewGuid().ToString("N")[..6] + "@correo.nordvik.test";
        var c = await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/customers/{SeedIds.CustomerAna}/contacts", $"{{\"type\":\"EMAIL\",\"value\":\"{email}\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal(201, c.Code);
        Assert.Equal("PENDING_VERIFICATION", c.Str("status"));
        Assert.False(c.Has("code"));
        var id = c.Str("id");
        var code = (await _f.Get(Ports.Contact, $"/_test/contacts/{id}/code")).Str("code");
        var v = await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{id}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal("VERIFIED", v.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{id}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(ana))).ShouldBeProblem(409, "ALREADY_VERIFIED");
    }

    [Fact]
    public async Task Three_wrong_codes_lock_the_contact()
    {
        var ana = await _f.Ana();
        var c = await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/customers/{SeedIds.CustomerAna}/contacts", "{\"type\":\"PHONE\",\"value\":\"+46 70 " + Random.Shared.Next(100, 999) + " 00 00\"}", NordvikFixture.Bearer(ana));
        var id = c.Str("id");
        (await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{id}/verify", "{\"code\":\"000000\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(422, "INVALID_CODE");
        (await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{id}/verify", "{\"code\":\"000000\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(422, "INVALID_CODE");
        (await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{id}/verify", "{\"code\":\"000000\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(423, "CONTACT_LOCKED");
    }

    [Fact]
    public async Task Codes_expire_after_10_virtual_minutes()
    {
        var c = await _f.NewCustomer();
        var erik = await _f.Erik();
        var id = c.GetProperty("id").GetString();
        var ct = await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/customers/{id}/contacts", "{\"type\":\"EMAIL\",\"value\":\"caduca@correo.nordvik.test\"}", NordvikFixture.Bearer(erik));
        var code = (await _f.Get(Ports.Contact, $"/_test/contacts/{ct.Str("id")}/code")).Str("code");
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":250}");
        erik = await _f.Erik();
        await _f.Post(Ports.Clock, "/api/v1/clock/advance", "{\"minutes\":8}");
        erik = await _f.Erik();
        (await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/contacts/{ct.Str("id")}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(erik))).ShouldBeProblem(410, "CODE_EXPIRED");
    }

    [Fact]
    public async Task Contact_for_other_customer_is_hidden()
    {
        var r = await _f.Send(HttpMethod.Post, Ports.Contact, $"/api/v1/customers/{SeedIds.CustomerLars}/contacts", "{\"type\":\"EMAIL\",\"value\":\"x@correo.nordvik.test\"}", NordvikFixture.Bearer(await _f.Ana()));
        r.ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Address_must_match_postal_code_and_keeps_history()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString();
        var erik = await _f.Erik();
        (await _f.Send(HttpMethod.Put, Ports.CustomerAddress, $"/api/v1/customers/{id}/address", "{\"street\":\"Storgatan\",\"number\":\"1\",\"postalCode\":\"111 51\",\"city\":\"Malmö\"}", NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "POSTAL_CODE_CITY_MISMATCH");
        (await _f.Send(HttpMethod.Put, Ports.CustomerAddress, $"/api/v1/customers/{id}/address", "{\"street\":\"Storgatan\",\"number\":\"1\",\"postalCode\":\"11151\",\"city\":\"Stockholm\"}", NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "INVALID_POSTAL_CODE");
        var first = await _f.Send(HttpMethod.Put, Ports.CustomerAddress, $"/api/v1/customers/{id}/address", "{\"street\":\"Storgatan\",\"number\":\"1\",\"postalCode\":\"111 51\",\"city\":\"stockholm\"}", NordvikFixture.Bearer(erik));
        Assert.Equal("Stockholm", first.Str("city"));
        await _f.Send(HttpMethod.Put, Ports.CustomerAddress, $"/api/v1/customers/{id}/address", "{\"street\":\"Kungsgatan\",\"number\":\"9\",\"postalCode\":\"411 19\",\"city\":\"Göteborg\"}", NordvikFixture.Bearer(erik));
        var history = await _f.Get(Ports.CustomerAddress, $"/api/v1/customers/{id}/address/history", NordvikFixture.Bearer(erik));
        Assert.Equal(2, history.Json.GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, history.Json[0].GetProperty("validTo").ValueKind);
        Assert.Equal(JsonValueKind.Null, history.Json[1].GetProperty("validTo").ValueKind);
    }

    [Fact]
    public async Task Missing_address_is_404()
    {
        var c = await _f.NewCustomer();
        (await _f.Get(Ports.CustomerAddress, $"/api/v1/customers/{c.GetProperty("id").GetString()}/address", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(404, "ADDRESS_NOT_FOUND");
    }

    [Fact]
    public async Task Preferences_default_then_saved()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString();
        var erik = await _f.Erik();
        var d = await _f.Get(Ports.Preferences, $"/api/v1/customers/{id}/preferences", NordvikFixture.Bearer(erik));
        Assert.True(d.Json.GetProperty("isDefault").GetBoolean());
        (await _f.Send(HttpMethod.Put, Ports.Preferences, $"/api/v1/customers/{id}/preferences", "{\"language\":\"es\"}", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var saved = await _f.Send(HttpMethod.Put, Ports.Preferences, $"/api/v1/customers/{id}/preferences", "{\"language\":\"es\",\"channel\":\"SMS\",\"marketingConsent\":false,\"quietHours\":{\"from\":\"21:00\",\"to\":\"07:30\"}}", NordvikFixture.Bearer(erik));
        Assert.False(saved.Json.GetProperty("isDefault").GetBoolean());
        var again = await _f.Get(Ports.Preferences, $"/api/v1/customers/{id}/preferences", NordvikFixture.Bearer(erik));
        Assert.Equal("SMS", again.Str("channel"));
        Assert.Equal("07:30", again.Json.GetProperty("quietHours").GetProperty("to").GetString());
    }
}
