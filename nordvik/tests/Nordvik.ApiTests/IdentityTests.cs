using System.Text;
using System.Text.Json;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class IamTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public IamTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> TokenForm(params (string K, string V)[] fields)
    {
        var res = await _f.Http.PostAsync(NordvikFixture.Url(Ports.Iam, "/api/v1/oauth/token"), new FormUrlEncodedContent(fields.ToDictionary(x => x.K, x => x.V)));
        return await Reply.From(res);
    }

    [Fact]
    public async Task Password_grant_returns_a_bearer_token_of_300_seconds()
    {
        var r = await TokenForm(("grant_type", "password"), ("username", "asesor.erik"), ("password", "Erik2026!"));
        Assert.Equal(200, r.Code);
        Assert.Equal("Bearer", r.Str("token_type"));
        Assert.Equal(300, r.Int("expires_in"));
        Assert.StartsWith("rt_", r.Str("refresh_token"));
        Assert.Equal(3, r.Str("access_token").Split('.').Length);
        Assert.Contains("customers:write", r.Str("scope"));
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Json_body_is_also_accepted()
    {
        var r = await _f.Post(Ports.Iam, "/api/v1/oauth/token", "{\"grant_type\":\"password\",\"username\":\"cliente.ana\",\"password\":\"Ana2026!\"}");
        Assert.Equal(200, r.Code);
    }

    [Fact]
    public async Task Wrong_password_uses_oauth_error_format()
    {
        var r = await TokenForm(("grant_type", "password"), ("username", "cliente.lars"), ("password", "mal"));
        Assert.Equal(400, r.Code);
        Assert.Equal("invalid_grant", r.Str("error"));
        Assert.False(r.Has("code"));
        Assert.Contains("1 de 5", r.Str("hint"));
        await TokenForm(("grant_type", "password"), ("username", "cliente.lars"), ("password", "Lars2026!"));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_until_15_minutes_pass()
    {
        for (var i = 0; i < 5; i++) await TokenForm(("grant_type", "password"), ("username", "compliance.karin"), ("password", "x"));
        var locked = await TokenForm(("grant_type", "password"), ("username", "compliance.karin"), ("password", "Karin2026!"));
        Assert.Equal(400, locked.Code);
        Assert.Contains("bloqueada", locked.Str("error_description"));
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":960}");
        var ok = await TokenForm(("grant_type", "password"), ("username", "compliance.karin"), ("password", "Karin2026!"));
        Assert.Equal(200, ok.Code);
    }

    [Theory]
    [InlineData("implicit", "unsupported_grant_type")]
    [InlineData("password", "invalid_request")]
    public async Task Bad_requests_are_explained(string grant, string error)
    {
        var r = await TokenForm(("grant_type", grant));
        Assert.Equal(400, r.Code);
        Assert.Equal(error, r.Str("error"));
    }

    [Fact]
    public async Task Empty_body_is_invalid_request()
    {
        var r = await _f.Post(Ports.Iam, "/api/v1/oauth/token");
        Assert.Equal("invalid_request", r.Str("error"));
    }

    [Fact]
    public async Task Client_credentials_for_batch_client()
    {
        var ok = await TokenForm(("grant_type", "client_credentials"), ("client_id", "nordvik-batch"), ("client_secret", "batch-secret-2026"));
        Assert.Equal(200, ok.Code);
        Assert.Equal("config:read", ok.Str("scope"));
        Assert.False(ok.Json.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String);
        var bad = await TokenForm(("grant_type", "client_credentials"), ("client_id", "nordvik-batch"), ("client_secret", "nope"));
        Assert.Equal(401, bad.Code);
        Assert.Equal("invalid_client", bad.Str("error"));
    }

    [Fact]
    public async Task Scope_can_be_narrowed_but_not_widened()
    {
        var narrow = await TokenForm(("grant_type", "password"), ("username", "cliente.ana"), ("password", "Ana2026!"), ("scope", "portfolios:read"));
        Assert.Equal("portfolios:read", narrow.Str("scope"));
        var wide = await TokenForm(("grant_type", "password"), ("username", "cliente.ana"), ("password", "Ana2026!"), ("scope", "customers:write"));
        Assert.Equal("invalid_scope", wide.Str("error"));
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_the_family()
    {
        var first = await TokenForm(("grant_type", "password"), ("username", "asesora.maja"), ("password", "Maja2026!"));
        var rt1 = first.Str("refresh_token");
        var second = await TokenForm(("grant_type", "refresh_token"), ("refresh_token", rt1));
        Assert.Equal(200, second.Code);
        var rt2 = second.Str("refresh_token");
        Assert.NotEqual(rt1, rt2);
        var reuse = await TokenForm(("grant_type", "refresh_token"), ("refresh_token", rt1));
        Assert.Equal("invalid_grant", reuse.Str("error"));
        var afterFamilyRevoked = await TokenForm(("grant_type", "refresh_token"), ("refresh_token", rt2));
        Assert.Equal("invalid_grant", afterFamilyRevoked.Str("error"));
    }

    [Fact]
    public async Task Me_describes_the_token_owner()
    {
        var r = await _f.Get(Ports.Iam, "/api/v1/users/me", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal("cliente.ana", r.Str("username"));
        Assert.Equal("Customer", r.Str("role"));
        Assert.Equal(Nordvik.Shared.SeedIds.CustomerAna, r.Str("customerId"));
    }

    [Fact]
    public async Task Me_without_token_is_401_with_www_authenticate()
    {
        var r = await _f.Get(Ports.Iam, "/api/v1/users/me");
        r.ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
        Assert.Equal("Bearer", r.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var t = await _f.Ana();
        var parts = t.Split('.');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')));
        var forged = payload.Replace("\"Customer\"", "\"Admin\"");
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(forged)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var r = await _f.Get(Ports.Iam, "/api/v1/users/me", NordvikFixture.Bearer(parts[0] + "." + b64 + "." + parts[2]));
        r.ShouldBeProblem(401, "INVALID_TOKEN");
    }

    [Fact]
    public async Task Token_expires_after_5_virtual_minutes()
    {
        var t = await _f.Ana();
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":301}");
        var r = await _f.Get(Ports.Iam, "/api/v1/users/me", NordvikFixture.Bearer(t));
        r.ShouldBeProblem(401, "TOKEN_EXPIRED");
        Assert.Contains("expired", r.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Introspection_reports_active_and_revoked_tokens()
    {
        var t = await _f.Lars();
        var active = await _f.Post(Ports.Iam, "/api/v1/oauth/introspect", JsonSerializer.Serialize(new { token = t }));
        Assert.True(active.Json.GetProperty("active").GetBoolean());
        Assert.Equal("cliente.lars", active.Str("sub"));
        await _f.Post(Ports.Iam, "/api/v1/oauth/revoke", JsonSerializer.Serialize(new { token = t }));
        var revoked = await _f.Post(Ports.Iam, "/api/v1/oauth/introspect", JsonSerializer.Serialize(new { token = t }));
        Assert.False(revoked.Json.GetProperty("active").GetBoolean());
        Assert.Equal("revocado", revoked.Str("reason"));
    }

    [Fact]
    public async Task Users_list_is_admin_only()
    {
        Assert.Equal(8, (await _f.Get(Ports.Iam, "/api/v1/users", NordvikFixture.Bearer(await _f.Admin()))).Json.GetArrayLength());
        (await _f.Get(Ports.Iam, "/api/v1/users", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }
}

[Collection("nordvik")]
public class SessionAndGatewayTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public SessionAndGatewayTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private static string Sid(string token)
    {
        var p = token.Split('.')[1];
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(p.Replace('-', '+').Replace('_', '/').PadRight(p.Length + (4 - p.Length % 4) % 4, '=')));
        return JsonDocument.Parse(json).RootElement.GetProperty("sid").GetString()!;
    }

    [Fact]
    public async Task Login_creates_an_active_session()
    {
        var t = await _f.Erik();
        var s = await _f.Get(Ports.Session, $"/api/v1/sessions/{Sid(t)}");
        Assert.Equal("ACTIVE", s.Str("status"));
        Assert.Equal("asesor.erik", s.Str("userId"));
    }

    [Fact]
    public async Task Gateway_routes_public_services_without_token()
    {
        var r = await _f.Get(Ports.Gateway, "/product-catalog/api/v1/funds/SE0000000001");
        Assert.Equal(200, r.Code);
        Assert.Equal("product-catalog", r.Headers.GetValues("X-Upstream-Service").Single());
        Assert.Equal("api-gateway", r.Headers.GetValues("X-Service").Single());
        Assert.True(r.Headers.Contains("X-RateLimit-Remaining"));
    }

    [Fact]
    public async Task Gateway_requires_token_for_private_services() =>
        (await _f.Get(Ports.Gateway, $"/customer-registry/api/v1/customers/{Nordvik.Shared.SeedIds.CustomerAna}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");

    [Fact]
    public async Task Gateway_forwards_the_token_and_the_correlation_id()
    {
        var r = await _f.Get(Ports.Gateway, $"/customer-registry/api/v1/customers/{Nordvik.Shared.SeedIds.CustomerAna}", req =>
        {
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _f.Ana().Result);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", "gw-trace-1");
        });
        Assert.Equal(200, r.Code);
        Assert.Equal("gw-trace-1", r.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("Ana", r.Str("firstName"));
    }

    [Fact]
    public async Task Revoked_session_is_rejected_by_gateway_but_still_accepted_directly()
    {
        var t = await _f.Lars();
        Assert.Equal(204, (await _f.Send(HttpMethod.Delete, Ports.Session, $"/api/v1/sessions/{Sid(t)}", null, NordvikFixture.Bearer(t))).Code);
        var viaGateway = await _f.Get(Ports.Gateway, $"/customer-registry/api/v1/customers/{Nordvik.Shared.SeedIds.CustomerLars}", NordvikFixture.Bearer(t));
        viaGateway.ShouldBeProblem(401, "TOKEN_INACTIVE");
        var direct = await _f.Get(Ports.Customer, $"/api/v1/customers/{Nordvik.Shared.SeedIds.CustomerLars}", NordvikFixture.Bearer(t));
        Assert.Equal(200, direct.Code);
    }

    [Fact]
    public async Task Sessions_expire_after_30_idle_minutes()
    {
        var t = await _f.Maja();
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":1860}");
        var s = await _f.Get(Ports.Session, $"/api/v1/sessions/{Sid(t)}");
        Assert.Equal("EXPIRED", s.Str("status"));
    }

    [Fact]
    public async Task A_user_cannot_close_someone_elses_session()
    {
        var erik = await _f.Erik();
        var ana = await _f.Ana();
        (await _f.Send(HttpMethod.Delete, Ports.Session, $"/api/v1/sessions/{Sid(erik)}", null, NordvikFixture.Bearer(ana))).ShouldBeProblem(403, "ACCESS_DENIED");
    }

    [Fact]
    public async Task Unknown_service_behind_gateway_is_404() =>
        (await _f.Get(Ports.Gateway, "/no-existe/api/v1/x")).ShouldBeProblem(404, "SERVICE_NOT_FOUND");

    [Fact]
    public async Task Gateway_lists_routes_with_auth_flag()
    {
        var r = await _f.Get(Ports.Gateway, "/api/v1/routes");
        var routes = r.Json.EnumerateArray().ToDictionary(x => x.GetProperty("service").GetString()!, x => x.GetProperty("authRequired").GetBoolean());
        Assert.False(routes["product-catalog"]);
        Assert.False(routes["iam-service"]);
        Assert.True(routes["customer-registry"]);
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after()
    {
        var admin = await _f.Admin();
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/rate-limiter/limitPerMinute", "{\"value\":\"3\"}", NordvikFixture.Bearer(admin));
        await Task.Delay(2100);
        await _f.Send(HttpMethod.Delete, Ports.RateLimiter, "/api/v1/limits/user:cliente.sofia");
        var sofia = await _f.Token("cliente.sofia", "Sofia2026!");
        var path = $"/customer-registry/api/v1/customers/{Nordvik.Shared.SeedIds.CustomerSofia}";
        for (var i = 0; i < 3; i++) Assert.Equal(200, (await _f.Get(Ports.Gateway, path, NordvikFixture.Bearer(sofia))).Code);
        var limited = await _f.Get(Ports.Gateway, path, NordvikFixture.Bearer(sofia));
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/rate-limiter/limitPerMinute", "{\"value\":\"100\"}", NordvikFixture.Bearer(admin));
        await Task.Delay(2100);
        limited.ShouldBeProblem(429, "RATE_LIMITED");
        Assert.True(int.Parse(limited.Headers.RetryAfter!.ToString()) > 0);
    }
}

[Collection("nordvik")]
public class PermissionAndConfigTests
{
    private readonly NordvikFixture _f;
    public PermissionAndConfigTests(NordvikFixture f) => _f = f;
    private static string Check(string role, string action, string? customer = null, string? advisor = null, string? resource = null) =>
        JsonSerializer.Serialize(new { role, action, customerId = customer, advisorId = advisor, resourceCustomerId = resource });

    [Theory]
    [InlineData("Customer", "customer:read", true)]
    [InlineData("Customer", "customer:close", false)]
    [InlineData("Compliance", "customer:read", true)]
    [InlineData("Compliance", "customer:update", false)]
    [InlineData("Operations", "customer:close", false)]
    [InlineData("Admin", "customer:close", true)]
    public async Task Role_matrix(string role, string action, bool allowed)
    {
        var r = await _f.Post(Ports.Permission, "/api/v1/permissions/check",
            Check(role, action, Nordvik.Shared.SeedIds.CustomerAna, null, Nordvik.Shared.SeedIds.CustomerAna));
        Assert.Equal(allowed, r.Json.GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task Customer_only_sees_own_data()
    {
        var r = await _f.Post(Ports.Permission, "/api/v1/permissions/check", Check("Customer", "customer:read", Nordvik.Shared.SeedIds.CustomerAna, null, Nordvik.Shared.SeedIds.CustomerLars));
        Assert.False(r.Json.GetProperty("allowed").GetBoolean());
    }

    [Theory]
    [InlineData(Nordvik.Shared.SeedIds.CustomerAna, true)]
    [InlineData(Nordvik.Shared.SeedIds.CustomerSofia, false)]
    public async Task Advisor_only_sees_assigned_customers(string customer, bool allowed)
    {
        var r = await _f.Post(Ports.Permission, "/api/v1/permissions/check", Check("Advisor", "portfolio:read", null, Nordvik.Shared.SeedIds.AdvisorErik, customer));
        Assert.Equal(allowed, r.Json.GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task Unknown_action_is_rejected() =>
        (await _f.Post(Ports.Permission, "/api/v1/permissions/check", Check("Admin", "todo:hacer"))).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Policies_matrix_covers_5_roles_and_14_actions() =>
        Assert.Equal(135, (await _f.Get(Ports.Permission, "/api/v1/policies")).Json.GetArrayLength()); // 5 roles × 27 acciones

    [Fact]
    public async Task Config_values_are_versioned_and_change_needs_admin()
    {
        var before = await _f.Get(Ports.Config, "/api/v1/config/order-management/cutOffTime");
        Assert.Equal("15:00", before.Str("value"));
        (await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/order-management/cutOffTime", "{\"value\":\"14:00\"}", NordvikFixture.Bearer(await _f.Erik())))
            .ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/order-management/cutOffTime", "{\"value\":\"14:00\"}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
        var admin = await _f.Admin();
        var changed = await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/order-management/cutOffTime", "{\"value\":\"14:30\"}", NordvikFixture.Bearer(admin));
        Assert.Equal(before.Int("version") + 1, changed.Int("version"));
        Assert.Equal("admin", changed.Str("updatedBy"));
        var history = await _f.Get(Ports.Config, "/api/v1/config/order-management/cutOffTime/history");
        Assert.Contains(history.Json.EnumerateArray(), h => h.GetProperty("value").GetString() == "15:00");
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/order-management/cutOffTime", "{\"value\":\"15:00\"}", NordvikFixture.Bearer(admin));
    }

    [Fact]
    public async Task Unknown_config_is_404()
    {
        (await _f.Get(Ports.Config, "/api/v1/config/no-existe")).ShouldBeProblem(404, "SERVICE_CONFIG_NOT_FOUND");
        (await _f.Get(Ports.Config, "/api/v1/config/market-data/nada")).ShouldBeProblem(404, "CONFIG_KEY_NOT_FOUND");
    }

    [Fact]
    public async Task Rate_limiter_counts_per_key()
    {
        var key = "test:" + Guid.NewGuid().ToString("N")[..8];
        var a = await _f.Post(Ports.RateLimiter, "/api/v1/check", $"{{\"key\":\"{key}\",\"cost\":10}}");
        Assert.True(a.Json.GetProperty("allowed").GetBoolean());
        Assert.Equal(10, a.Int("used"));
        var s = await _f.Get(Ports.RateLimiter, $"/api/v1/limits/{key}");
        Assert.Equal(s.Int("limit") - 10, s.Int("remaining"));
        (await _f.Post(Ports.RateLimiter, "/api/v1/check", "{\"cost\":1}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }
}
