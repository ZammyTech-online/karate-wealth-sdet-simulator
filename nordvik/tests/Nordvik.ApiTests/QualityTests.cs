using System.Diagnostics;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Oleada 3 · calidad y seguridad: caos, control de simuladores, espía de eventos, jwt-forge, secretos e intercambio de tokens.</summary>
[Collection("nordvik")]
public class ChaosTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public ChaosTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Post(Ports.Chaos, "/api/v1/reset");
    public Task DisposeAsync() => _f.Post(Ports.Chaos, "/api/v1/reset");

    [Fact]
    public async Task A_limited_fault_fires_exactly_n_times_and_disappears()
    {
        var f = await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"uuid-service\",\"pathPrefix\":\"/api/v1/uuids\",\"status\":503,\"times\":2}");
        Assert.Equal(201, f.Code);
        var id = f.Str("id");
        var a = await _f.Get(Ports.Uuid, "/api/v1/uuids");
        a.ShouldBeProblem(503, "CHAOS_INJECTED");
        Assert.Equal(id, a.Headers.GetValues("X-Chaos-Injected").Single());
        (await _f.Get(Ports.Uuid, "/api/v1/uuids")).ShouldBeProblem(503, "CHAOS_INJECTED");
        Assert.Equal(200, (await _f.Get(Ports.Uuid, "/api/v1/uuids")).Code);
        (await _f.Get(Ports.Chaos, $"/api/v1/faults/{id}")).ShouldBeProblem(404, "FAULT_NOT_FOUND");
        Assert.Equal(200, (await _f.Get(Ports.Uuid, "/health")).Code);
    }

    [Fact]
    public async Task Latency_faults_and_scoping_by_method()
    {
        await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"calendar-service\",\"method\":\"GET\",\"pathPrefix\":\"/api/v1/calendar\",\"delayMs\":400}");
        var sw = Stopwatch.StartNew();
        var r = await _f.Get(Ports.Calendar, "/api/v1/calendar/XSTO/is-trading-day?date=2026-10-26");
        sw.Stop();
        Assert.Equal(200, r.Code);
        Assert.True(sw.ElapsedMilliseconds >= 380, $"tardó {sw.ElapsedMilliseconds} ms");
        Assert.True(r.Headers.Contains("X-Chaos-Injected"));
        Assert.False((await _f.Get(Ports.Market, "/health")).Headers.Contains("X-Chaos-Injected"));
        var list = await _f.Get(Ports.Chaos, "/api/v1/faults");
        Assert.Equal(1, list.Int("count"));
        Assert.True(list.Json.GetProperty("items")[0].GetProperty("hits").GetInt64() >= 1);
        var del = await _f.Delete(Ports.Chaos, "/api/v1/faults");
        Assert.Equal(1, del.Int("removed"));
    }

    [Fact]
    public async Task One_request_chaos_with_the_scenario_header()
    {
        var bad = await _f.Get(Ports.Uuid, "/health", r => r.Headers.TryAddWithoutValidation("X-Chaos-Scenario", "status=500"));
        bad.ShouldBeProblem(500, "CHAOS_INJECTED");
        Assert.Equal("header", bad.Headers.GetValues("X-Chaos-Injected").Single());
        var sw = Stopwatch.StartNew();
        var slow = await _f.Get(Ports.Uuid, "/health", r => r.Headers.TryAddWithoutValidation("X-Chaos-Scenario", "latency=300"));
        Assert.Equal(200, slow.Code);
        Assert.True(sw.ElapsedMilliseconds >= 280);
        (await _f.Get(Ports.Uuid, "/health", r => r.Headers.TryAddWithoutValidation("X-Chaos-Scenario", "explode=1"))).ShouldBeProblem(400, "INVALID_CHAOS_SCENARIO");
        (await _f.Get(Ports.Uuid, "/health", r => r.Headers.TryAddWithoutValidation("X-Chaos-Scenario", "status=200"))).ShouldBeProblem(400, "INVALID_CHAOS_SCENARIO");
        Assert.Equal(200, (await _f.Get(Ports.Uuid, "/health")).Code);
    }

    [Fact]
    public async Task Faults_are_validated_and_traps_are_catalogued()
    {
        (await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"no-existe\",\"status\":500}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"uuid-service\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"uuid-service\",\"status\":302}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.Chaos, "/api/v1/faults", "{\"service\":\"uuid-service\",\"status\":500,\"probability\":1.5}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        var traps = await _f.Get(Ports.Chaos, "/api/v1/traps");
        Assert.Equal(20, traps.Int("count"));
        Assert.True(traps.Json.GetProperty("items").EnumerateArray().First(t => t.GetProperty("id").GetString() == "T6").GetProperty("enabled").GetBoolean());
        (await _f.Send(HttpMethod.Put, Ports.Chaos, "/api/v1/traps/T6", "{\"enabled\":false}")).ShouldBeProblem(409, "TRAP_ALWAYS_ON");
        var on = await _f.Send(HttpMethod.Put, Ports.Chaos, "/api/v1/traps/t4", "{\"enabled\":true}");
        Assert.True(on.Json.GetProperty("enabled").GetBoolean());
        Assert.True(Chaos.TrapOn("T4"));
        (await _f.Send(HttpMethod.Put, Ports.Chaos, "/api/v1/traps/T99", "{\"enabled\":true}")).ShouldBeProblem(404, "TRAP_NOT_FOUND");
        await _f.Post(Ports.Chaos, "/api/v1/reset");
        Assert.False(Chaos.TrapOn("T4"));
        var activable = await _f.Get(Ports.Chaos, "/api/v1/traps?nature=activable");
        Assert.All(activable.Json.GetProperty("items").EnumerateArray(), t => Assert.Equal("ACTIVABLE", t.GetProperty("nature").GetString()));
    }
}

[Collection("nordvik")]
public class MockControlTests
{
    private readonly NordvikFixture _f;
    public MockControlTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task One_panel_for_every_simulator()
    {
        var list = await _f.Get(Ports.MockControl, "/api/v1/mocks");
        var names = list.Json.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "nordid-sim", "population-sim", "sanctionscreen-sim", "custodian-sim", "pricefeed-sim", "nordpay-sim" }, names);
        Assert.All(list.Json.GetProperty("items").EnumerateArray(), m => Assert.True(m.GetProperty("up").GetBoolean()));
        try
        {
            var down = await _f.Send(HttpMethod.Put, Ports.MockControl, "/api/v1/mocks/nordpay-sim/behaviour", "{\"mode\":\"down\"}");
            Assert.Equal("down", down.Str("mode"));
            Assert.Equal("down", (await _f.Get(Ports.NordPaySim, "/__admin/behaviour")).Str("mode"));
            (await _f.Send(HttpMethod.Put, Ports.MockControl, "/api/v1/mocks/nordpay-sim/behaviour", "{\"mode\":\"crazy\"}")).ShouldBeProblem(400, "INVALID_MODE");
            (await _f.Send(HttpMethod.Put, Ports.MockControl, "/api/v1/mocks/no-sim/behaviour", "{\"mode\":\"down\"}")).ShouldBeProblem(404, "MOCK_NOT_FOUND");
        }
        finally
        {
            var reset = await _f.Post(Ports.MockControl, "/api/v1/mocks/reset");
            Assert.Equal(names.Count, reset.Json.GetProperty("reset").GetArrayLength());
        }
        Assert.Equal("normal", (await _f.Get(Ports.NordPaySim, "/__admin/behaviour")).Str("mode"));
    }
}

[Collection("nordvik")]
public class EventSpyTests
{
    private readonly NordvikFixture _f;
    public EventSpyTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Mark_then_wait_for_the_event_you_caused()
    {
        var mark = await _f.Post(Ports.EventSpy, "/api/v1/marks");
        var seq = mark.Json.GetProperty("seq").GetInt64();
        var id = (await _f.NewCustomer()).GetProperty("id").GetString()!;
        await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", JsonSerializer.Serialize(new { customerId = id, purpose = "MARKETING", granted = true }), NordvikFixture.Bearer(await _f.Erik()));
        var w = await _f.Get(Ports.EventSpy, $"/api/v1/events/wait?topic=consent.granted&key={id}&after={seq}&timeoutMs=8000");
        Assert.Equal(200, w.Code);
        Assert.True(w.Json.GetProperty("found").GetBoolean());
        Assert.Equal("consent-ledger", w.Json.GetProperty("first").GetProperty("producer").GetString());
        Assert.True(w.Json.GetProperty("first").GetProperty("seq").GetInt64() > seq);
        var byPayload = await _f.Get(Ports.EventSpy, $"/api/v1/events?topic=consent.*&after={seq}&payloadField=customerId&payloadValue={id}");
        Assert.True(byPayload.Int("count") >= 1);
        (await _f.Get(Ports.EventSpy, $"/api/v1/events/wait?topic=nada.de.nada&after={seq}&timeoutMs=300")).ShouldBeProblem(404, "EVENT_NOT_SEEN");
        (await _f.Get(Ports.EventSpy, "/api/v1/events/wait")).ShouldBeProblem(400, "FILTER_REQUIRED");
        var dups = await _f.Get(Ports.EventSpy, $"/api/v1/events/duplicates?after={seq}");
        Assert.Equal(200, dups.Code);
        Assert.True(dups.Int("examined") >= 1);
    }
}

[Collection("nordvik")]
public class JwtForgeTests
{
    private readonly NordvikFixture _f;
    public JwtForgeTests(NordvikFixture f) => _f = f;

    private async Task<string> Forge(object body)
    {
        var r = await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", JsonSerializer.Serialize(body));
        Assert.True(r.Code == 200, r.Body);
        return r.Str("token");
    }

    private Task<Reply> ReadAna(string token) => _f.Get(Ports.Customer, $"/api/v1/customers/{SeedIds.CustomerAna}", NordvikFixture.Bearer(token));

    [Fact]
    public async Task Forged_tokens_reproduce_every_classic_auth_failure()
    {
        var advisor = new { role = "Advisor", sub = "asesor.erik", advisorId = SeedIds.AdvisorErik, scope = "customers:read" };
        Assert.Equal(200, (await ReadAna(await Forge(advisor))).Code);
        (await ReadAna(await Forge(new { role = "Advisor", advisorId = SeedIds.AdvisorErik, scope = "customers:read", expiresInSeconds = -60 }))).ShouldBeProblem(401, "TOKEN_EXPIRED");
        (await ReadAna(await Forge(new { role = "Advisor", advisorId = SeedIds.AdvisorErik, scope = "customers:read", tamper = "signature" }))).ShouldBeProblem(401, "INVALID_TOKEN");
        (await ReadAna(await Forge(new { role = "Customer", customerId = SeedIds.CustomerLars, scope = "profile:read", tamper = "payload" }))).ShouldBeProblem(401, "INVALID_TOKEN");
        (await ReadAna(await Forge(new { role = "Admin", scope = "*", tamper = "alg-none" }))).ShouldBeProblem(401, "INVALID_TOKEN");
        (await ReadAna(await Forge(new { role = "Admin", scope = "*", issuer = "evil-iam" }))).ShouldBeProblem(401, "INVALID_TOKEN");
        (await ReadAna(await Forge(new { role = "Admin", scope = "*", tamper = "truncate" }))).ShouldBeProblem(401, "INVALID_TOKEN");
        (await ReadAna(await Forge(new { role = "Advisor", advisorId = SeedIds.AdvisorErik, scope = "" }))).ShouldBeProblem(403, "SCOPE_MISSING");
        (await ReadAna(await Forge(new { role = "Customer", sub = "cliente.lars", customerId = SeedIds.CustomerLars, scope = "customers:read profile:read" }))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Forge_is_validated_and_decode_explains_tokens()
    {
        (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"King\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"Customer\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"Admin\",\"tamper\":\"magic\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
        var expired = await Forge(new { role = "Operations", expiresInSeconds = -10 });
        var d = await _f.Post(Ports.JwtForge, "/api/v1/tokens/decode", JsonSerializer.Serialize(new { token = "Bearer " + expired }));
        Assert.False(d.Json.GetProperty("valid").GetBoolean());
        Assert.Equal("expired", d.Str("reason"));
        Assert.Equal("Operations", d.Json.GetProperty("payload").GetProperty("role").GetString());
        Assert.True(d.Json.GetProperty("secondsLeft").GetInt64() < 0);
        var real = await _f.Post(Ports.JwtForge, "/api/v1/tokens/decode", JsonSerializer.Serialize(new { token = await _f.Ana() }));
        Assert.True(real.Json.GetProperty("valid").GetBoolean());
        Assert.Equal("HS256", real.Json.GetProperty("header").GetProperty("alg").GetString());
        (await _f.Post(Ports.JwtForge, "/api/v1/tokens/decode", "{\"token\":\"abc\"}")).ShouldBeProblem(400, "MALFORMED_TOKEN");
        var recipes = await _f.Get(Ports.JwtForge, "/api/v1/tokens/recipes");
        Assert.True(recipes.Json.GetProperty("recipes").GetArrayLength() >= 6);
    }
}

[Collection("nordvik")]
public class SecretsTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public SecretsTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Listing_never_shows_values_and_reading_is_restricted()
    {
        var list = await _f.Get(Ports.Secrets, "/api/v1/secrets", NordvikFixture.Bearer(await _f.Admin()));
        Assert.True(list.Int("count") >= 4);
        Assert.DoesNotContain("nordpay-webhook-secret-2026", list.Body);
        (await _f.Get(Ports.Secrets, "/api/v1/secrets/nordpay-webhook-hmac", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Get(Ports.Secrets, "/api/v1/secrets", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var s = await _f.Get(Ports.Secrets, "/api/v1/secrets/pricefeed-api-key", NordvikFixture.Bearer(await _f.Admin()));
        Assert.Equal("pf-live-key-2026", s.Str("value"));
        await TestData.Events(_f, "secret.read", "pricefeed-api-key");
        (await _f.Get(Ports.Secrets, "/api/v1/secrets/nope", NordvikFixture.Bearer(await _f.Admin()))).ShouldBeProblem(404, "SECRET_NOT_FOUND");
    }

    [Fact]
    public async Task Rotation_keeps_the_old_value_valid_during_the_grace_period()
    {
        await _f.Freeze($"{TestData.Tuesday}T08:00:00");
        var admin = await _f.Admin();
        var name = "test-" + Guid.NewGuid().ToString("N")[..8];
        var c = await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets", JsonSerializer.Serialize(new { name, value = "valor-inicial-123" }), NordvikFixture.Bearer(admin));
        Assert.Equal(201, c.Code);
        (await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets", JsonSerializer.Serialize(new { name, value = "otro-valor-1234" }), NordvikFixture.Bearer(admin))).ShouldBeProblem(409, "SECRET_ALREADY_EXISTS");
        (await _f.Send(HttpMethod.Post, Ports.Secrets, "/api/v1/secrets", "{\"name\":\"X\",\"value\":\"corto\"}", NordvikFixture.Bearer(admin))).ShouldBeProblem(400, "VALIDATION_FAILED");
        var rot = await _f.Send(HttpMethod.Post, Ports.Secrets, $"/api/v1/secrets/{name}/rotate", "{\"value\":\"valor-nuevo-4567\",\"graceSeconds\":120}", NordvikFixture.Bearer(admin));
        Assert.Equal(2, rot.Int("currentVersion"));
        Assert.Equal(1, rot.Int("previousVersion"));
        async Task<JsonElement> Verify(string v) => (await _f.Send(HttpMethod.Post, Ports.Secrets, $"/api/v1/secrets/{name}/verify", JsonSerializer.Serialize(new { value = v }), NordvikFixture.Bearer(await _f.Olof()))).Json;
        Assert.True((await Verify("valor-nuevo-4567")).GetProperty("current").GetBoolean());
        var old = await Verify("valor-inicial-123");
        Assert.True(old.GetProperty("valid").GetBoolean());
        Assert.False(old.GetProperty("current").GetBoolean());
        Assert.False((await Verify("inventado-000000")).GetProperty("valid").GetBoolean());
        await _f.Freeze($"{TestData.Tuesday}T08:02:01");
        Assert.False((await Verify("valor-inicial-123")).GetProperty("valid").GetBoolean());
        Assert.False((await _f.Get(Ports.Secrets, $"/api/v1/secrets", NordvikFixture.Bearer(await _f.Admin()))).Json.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("name").GetString() == name).TryGetProperty("previousVersion", out var pv) && pv.ValueKind == JsonValueKind.Number);
    }
}

[Collection("nordvik")]
public class TokenExchangeTests
{
    private readonly NordvikFixture _f;
    public TokenExchangeTests(NordvikFixture f) => _f = f;

    private async Task<Reply> Exchange(string subjectToken, string audience, string? scope = null) =>
        await _f.Post(Ports.TokenExchange, "/api/v1/token-exchange", JsonSerializer.Serialize(new { subjectToken, audience, scope }));

    [Fact]
    public async Task Exchanged_tokens_are_smaller_and_shorter_never_bigger()
    {
        var erik = await _f.Erik();
        var x = await Exchange(erik, "portfolio-service", "portfolios:read");
        Assert.Equal(200, x.Code);
        Assert.Equal("portfolios:read", x.Str("scope"));
        Assert.True(x.Int("expires_in") <= 60);
        var token = x.Str("access_token");
        Assert.Equal("asesor.erik", TestData.JwtPayload(token).GetProperty("sub").GetString());
        Assert.Equal(200, (await _f.Get(Ports.Portfolio, $"/api/v1/portfolios/{SeedIds.PortfolioAna}", NordvikFixture.Bearer(token))).Code);
        (await _f.Get(Ports.Customer, $"/api/v1/customers/{SeedIds.CustomerAna}", NordvikFixture.Bearer(token))).ShouldBeProblem(403, "SCOPE_MISSING");
        (await Exchange(erik, "payments-gateway", "payments:write")).ShouldBeProblem(403, "SCOPE_ESCALATION");
        (await Exchange(erik, "portfolio-service", "orders:write")).ShouldBeProblem(400, "INVALID_SCOPE");
        (await Exchange(erik, "mainframe")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Exchange("no.es.token", "portfolio-service")).ShouldBeProblem(401, "INVALID_TOKEN");
        var expired = (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"Operations\",\"scope\":\"portfolios:read\",\"expiresInSeconds\":-5}")).Str("token");
        (await Exchange(expired, "portfolio-service")).ShouldBeProblem(401, "TOKEN_EXPIRED");
        var service = (await _f.Post(Ports.JwtForge, "/api/v1/tokens/forge", "{\"role\":\"Service\",\"scope\":\"portfolios:read\"}")).Str("token");
        (await Exchange(service, "portfolio-service")).ShouldBeProblem(403, "SUBJECT_NOT_EXCHANGEABLE");
    }
}
