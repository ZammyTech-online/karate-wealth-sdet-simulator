using System.Globalization;
using System.Text;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

public static class TestData
{
    public static string Iban(string ending) => IbanMath.Make("SE", "300" + Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString() + ending);

    public static async Task<string> NationalIdOf(NordvikFixture f, string customerId) =>
        (await f.Get(Ports.Customer, $"/api/v1/internal/customers/{customerId}")).Str("nationalId");

    public static JsonElement JwtPayload(string token)
    {
        var p = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '='))).RootElement;
    }

    /// <summary>Lee eventos del bus por tema y clave (consistencia eventual: reintenta).</summary>
    public static Task<Reply> Events(NordvikFixture f, string topic, string key, int count = 1) =>
        Eventually.Until(() => f.Get(Ports.EventBus, $"/api/v1/events?topics={topic}&key={Uri.EscapeDataString(key)}"),
            r => r.Json.GetProperty("items").GetArrayLength() >= count, 10000, $"evento {topic} con clave {key}");

    /// <summary>Un martes laborable cualquiera a la hora indicada (hora de Estocolmo).</summary>
    public const string Tuesday = "2026-10-20";
}

[Collection("nordvik")]
public class EidAndMfaTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public EidAndMfaTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public async Task DisposeAsync()
    {
        await _f.ResetClock();
        await _f.Post(Ports.NordIdSim, "/__admin/behaviour/reset");
    }

    private async Task<Reply> PollEid(string orderRef, string until = "COMPLETE") =>
        await Eventually.Until(() => _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}"), r => r.Code != 200 || r.Str("status") == until || r.Str("status") is "FAILED" or "EXPIRED", 8000, "orden NordID");

    [Fact]
    public async Task Nordid_auth_goes_pending_then_complete()
    {
        var pn = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var start = await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\",\"purpose\":\"login\"}}");
        Assert.Equal(201, start.Code);
        var orderRef = start.Str("orderRef");
        var first = await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}");
        Assert.Equal("PENDING", first.Str("status"));
        Assert.Equal("outstandingTransaction", first.Str("hintCode"));
        Assert.Contains("**-****", first.Str("maskedPersonalNumber"));
        var second = await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}");
        Assert.Equal("userSign", second.Str("hintCode"));
        var done = await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}");
        Assert.Equal("COMPLETE", done.Str("status"));
        Assert.Equal(pn, done.Json.GetProperty("user").GetProperty("personalNumber").GetString());
        Assert.False(string.IsNullOrWhiteSpace(done.Json.GetProperty("user").GetProperty("name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(done.Str("signature")));
    }

    [Theory]
    [InlineData("userCancel", "FAILED", "USER_CANCELLED")]
    [InlineData("certificateErr", "FAILED", "CERTIFICATE_ERROR")]
    [InlineData("expiredTransaction", "EXPIRED", "EXPIRED")]
    public async Task Provider_outcomes_are_translated(string outcome, string status, string reason)
    {
        var pn = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        await _f.Post(Ports.NordIdSim, "/__admin/scenarios", $"{{\"personalNumber\":\"{pn}\",\"outcome\":\"{outcome}\",\"pendingPolls\":0}}");
        var orderRef = (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\"}}")).Str("orderRef");
        var r = await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}");
        Assert.Equal(status, r.Str("status"));
        Assert.Equal(reason, r.Str("failureReason"));
        Assert.Equal(JsonValueKind.Null, r.Json.GetProperty("user").ValueKind);
    }

    [Fact]
    public async Task Orders_expire_after_3_virtual_minutes_and_results_self_destruct_after_10()
    {
        var pn = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var orderRef = (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\"}}")).Str("orderRef");
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":181}");
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{orderRef}")).Str("status"));
        await _f.ResetClock();

        var pn2 = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var ref2 = (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn2}\"}}")).Str("orderRef");
        var done = await PollEid(ref2);
        Assert.Equal("COMPLETE", done.Str("status"));
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":601}");
        (await _f.Get(Ports.Eid, $"/api/v1/eid/orders/{ref2}")).ShouldBeProblem(410, "EID_RESULT_PURGED");
    }

    [Fact]
    public async Task One_order_at_a_time_per_person_and_cancel()
    {
        var pn = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var orderRef = (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\"}}")).Str("orderRef");
        (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\"}}")).ShouldBeProblem(409, "EID_ALREADY_IN_PROGRESS");
        Assert.Equal("CANCELLED", (await _f.Post(Ports.Eid, $"/api/v1/eid/orders/{orderRef}/cancel")).Str("status"));
        (await _f.Post(Ports.Eid, $"/api/v1/eid/orders/{orderRef}/cancel")).ShouldBeProblem(409, "EID_ORDER_FINISHED");
        Assert.Equal(201, (await _f.Post(Ports.Eid, "/api/v1/eid/auth", $"{{\"personalNumber\":\"{pn}\"}}")).Code);
    }

    [Fact]
    public async Task Sign_needs_visible_text_and_valid_personal_number()
    {
        var r = await _f.Post(Ports.Eid, "/api/v1/eid/sign", "{\"personalNumber\":\"19850314-1234\"}");
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(2, r.Json.GetProperty("errors").GetArrayLength());
        (await _f.Get(Ports.Eid, "/api/v1/eid/orders/no-existe")).ShouldBeProblem(404, "EID_ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Provider_down_is_a_503_and_its_own_errors_use_its_own_format()
    {
        await _f.Post(Ports.NordIdSim, "/__admin/behaviour", "{\"mode\":\"down\"}");
        (await _f.Post(Ports.Eid, "/api/v1/eid/auth", "{}")).ShouldBeProblem(503, "EID_PROVIDER_UNAVAILABLE");
        await _f.Post(Ports.NordIdSim, "/__admin/behaviour/reset");
        var raw = await _f.Post(Ports.NordIdSim, "/rp/v6/auth", "{\"personalNumber\":\"19850314-1236\"}");
        Assert.Equal(400, raw.Code);
        Assert.Equal("invalidParameters", raw.Str("errorCode"));
        Assert.False(raw.Has("type"));
    }

    [Fact]
    public async Task Mfa_code_arrives_in_the_mailbox_and_is_single_use()
    {
        var phone = "+46 70 " + Random.Shared.Next(100, 999) + " " + Random.Shared.Next(10, 99) + " " + Random.Shared.Next(10, 99);
        var ana = await _f.Ana();
        var ch = await _f.Send(HttpMethod.Post, Ports.Mfa, "/api/v1/mfa/challenges", $"{{\"channel\":\"SMS\",\"destination\":\"{phone}\",\"language\":\"es\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal(201, ch.Code);
        Assert.StartsWith("******", ch.Str("maskedDestination"));
        Assert.DoesNotContain("code", ch.Body);
        var box = await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={Uri.EscapeDataString(phone)}");
        var body = box.Json.GetProperty("items")[0].GetProperty("body").GetString()!;
        Assert.StartsWith("Tu código de Nordvik es ", body);
        var code = System.Text.RegularExpressions.Regex.Match(body, @"\d{6}").Value;
        var ok = await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{ch.Str("id")}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(ana));
        Assert.Equal("VERIFIED", ok.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{ch.Str("id")}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(ana))).ShouldBeProblem(409, "ALREADY_VERIFIED");
        (await _f.Get(Ports.Mfa, $"/api/v1/mfa/challenges/{ch.Str("id")}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CHALLENGE_NOT_FOUND");
    }

    [Fact]
    public async Task Mfa_locks_after_three_wrong_codes_and_expires_after_five_minutes()
    {
        var ana = await _f.Ana();
        var ch = await _f.Send(HttpMethod.Post, Ports.Mfa, "/api/v1/mfa/challenges", "{\"channel\":\"EMAIL\",\"destination\":\"mfa.test@correo.nordvik.test\"}", NordvikFixture.Bearer(ana));
        Assert.Equal("m***@correo.nordvik.test", ch.Str("maskedDestination"));
        var id = ch.Str("id");
        var w1 = await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{id}/verify", "{\"code\":\"000000\"}", NordvikFixture.Bearer(ana));
        // un 000000 podría ser el código bueno: probabilidad 1 entre un millón
        if (w1.Code == 200) return;
        w1.ShouldBeProblem(422, "INVALID_CODE");
        (await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{id}/verify", "{\"code\":\"000001\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(422, "INVALID_CODE");
        (await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{id}/verify", "{\"code\":\"000002\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(423, "CHALLENGE_LOCKED");

        var ch2 = await _f.Send(HttpMethod.Post, Ports.Mfa, "/api/v1/mfa/challenges", "{\"channel\":\"EMAIL\",\"destination\":\"mfa.test2@correo.nordvik.test\"}", NordvikFixture.Bearer(ana));
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":250}");
        await _f.Post(Ports.Clock, "/api/v1/clock/advance", "{\"minutes\":1}");
        var fresh = await _f.Ana();
        (await _f.Send(HttpMethod.Post, Ports.Mfa, $"/api/v1/mfa/challenges/{ch2.Str("id")}/verify", "{\"code\":\"123456\"}", NordvikFixture.Bearer(fresh))).ShouldBeProblem(410, "CHALLENGE_EXPIRED");
    }

    [Fact]
    public async Task Customer_step_up_with_nordid_returns_a_high_assurance_token()
    {
        var ana = await _f.Ana();
        var s = await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", "{\"reason\":\"retirada grande\"}", NordvikFixture.Bearer(ana));
        Assert.Equal(201, s.Code);
        Assert.Equal("EID", s.Str("method"));
        var done = await Eventually.Until(() => _f.Get(Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}", NordvikFixture.Bearer(ana)), r => r.Str("status") != "PENDING", 8000, "step-up");
        Assert.Equal("COMPLETED", done.Str("status"));
        var payload = TestData.JwtPayload(done.Str("accessToken"));
        Assert.Equal("high", payload.GetProperty("acr").GetString());
        Assert.Equal(300, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        Assert.Equal("cliente.ana", payload.GetProperty("sub").GetString());
        Assert.Equal(300, done.Int("expiresIn"));
    }

    [Fact]
    public async Task Employees_step_up_with_mfa()
    {
        var erik = await _f.Erik();
        (await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", "{\"method\":\"EID\"}", NordvikFixture.Bearer(erik))).ShouldBeProblem(422, "EID_NOT_AVAILABLE_FOR_ROLE");
        var phone = "+46 73 " + Random.Shared.Next(100, 999) + " 11 22";
        var s = await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", $"{{\"method\":\"MFA\",\"channel\":\"SMS\",\"destination\":\"{phone}\"}}", NordvikFixture.Bearer(erik));
        Assert.Equal(201, s.Code);
        var body = (await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={Uri.EscapeDataString(phone)}")).Json.GetProperty("items")[0].GetProperty("body").GetString()!;
        var code = System.Text.RegularExpressions.Regex.Match(body, @"\d{6}").Value;
        var done = await _f.Send(HttpMethod.Post, Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}/verify", $"{{\"code\":\"{code}\"}}", NordvikFixture.Bearer(erik));
        Assert.Equal("COMPLETED", done.Str("status"));
        Assert.Equal("Advisor", TestData.JwtPayload(done.Str("accessToken")).GetProperty("role").GetString());
        (await _f.Get(Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(404, "STEP_UP_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class NotificationTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public NotificationTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Templates_render_with_fallback_versions_and_missing_variables()
    {
        var es = await _f.Post(Ports.Templates, "/api/v1/templates/welcome/render", "{\"language\":\"es\",\"vars\":{\"firstName\":\"Ana\",\"customerNumber\":\"CUS-900001\"}}");
        Assert.Equal("¡Hola, Ana! Tu número de cliente es CUS-900001.", es.Str("body"));
        var fr = await _f.Post(Ports.Templates, "/api/v1/templates/welcome/render", "{\"language\":\"fr\",\"vars\":{\"firstName\":\"Ana\",\"customerNumber\":\"CUS-1\"}}");
        Assert.True(fr.Json.GetProperty("fallbackLanguage").GetBoolean());
        Assert.Equal("en", fr.Str("language"));
        var missing = await _f.Post(Ports.Templates, "/api/v1/templates/order.executed/render", "{\"language\":\"es\",\"vars\":{\"firstName\":\"Ana\"}}");
        missing.ShouldBeProblem(422, "MISSING_VARIABLES");
        Assert.Equal(6, missing.Json.GetProperty("errors").GetArrayLength()); // v2: fundName (asunto y cuerpo, una vez), orderId, side, units, nav, settlementDate
        Assert.Equal(2, (await _f.Get(Ports.Templates, "/api/v1/templates/order.executed?lang=es")).Int("version"));
        Assert.Equal(1, (await _f.Get(Ports.Templates, "/api/v1/templates/order.executed?lang=en")).Int("version"));
        (await _f.Get(Ports.Templates, "/api/v1/templates/order.executed?lang=es&version=9")).ShouldBeProblem(404, "TEMPLATE_VERSION_NOT_FOUND");
        (await _f.Get(Ports.Templates, "/api/v1/templates/no.existe")).ShouldBeProblem(404, "TEMPLATE_NOT_FOUND");
    }

    [Fact]
    public async Task Direct_email_lands_in_the_mailbox_newest_first()
    {
        var to = "buzon." + Guid.NewGuid().ToString("N")[..6] + "@correo.nordvik.test";
        for (var i = 1; i <= 2; i++)
            Assert.Equal(201, (await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"to\":\"{to}\",\"templateKey\":\"welcome\",\"language\":\"en\",\"vars\":{{\"firstName\":\"N{i}\",\"customerNumber\":\"CUS-{i}\"}}}}")).Code);
        var box = await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={to}");
        Assert.Equal(2, box.Int("count"));
        Assert.Contains("N2", box.Json.GetProperty("items")[0].GetProperty("body").GetString());
        Assert.Equal("Welcome to Nordvik", box.Json.GetProperty("items")[0].GetProperty("subject").GetString());
        (await _f.Get(Ports.Notification, "/api/v1/mailbox")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Marketing_without_consent_is_suppressed()
    {
        var c = await _f.NewCustomer();
        var r = await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"customerId\":\"{c.GetProperty("id").GetString()}\",\"templateKey\":\"marketing.newsletter\",\"vars\":{{\"firstName\":\"X\"}}}}");
        Assert.Equal("SUPPRESSED", r.Str("status"));
        Assert.Equal("NO_MARKETING_CONSENT", r.Str("reason"));
        Assert.Equal(0, (await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={c.GetProperty("email").GetString()}")).Int("count"));
    }

    [Fact]
    public async Task Quiet_hours_delay_sms_until_morning_but_not_high_priority()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString();
        var erik = await _f.Erik();
        await _f.Send(HttpMethod.Patch, Ports.Customer, $"/api/v1/customers/{id}", "{\"phone\":\"+46 70 555 01 01\"}", NordvikFixture.Bearer(erik, "\"1\""));
        await _f.Freeze($"{TestData.Tuesday}T23:00:00");
        var r = await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"customerId\":\"{id}\",\"channel\":\"SMS\",\"templateKey\":\"deposit.received\",\"vars\":{{\"amount\":\"500.00\"}},\"reference\":\"q-{id}\"}}");
        Assert.Equal("SCHEDULED", r.Str("status"));
        Assert.StartsWith("2026-10-21T08:00:00", r.Str("scheduledFor"));
        var urgent = await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"customerId\":\"{id}\",\"channel\":\"SMS\",\"templateKey\":\"deposit.received\",\"priority\":\"HIGH\",\"vars\":{{\"amount\":\"1.00\"}}}}");
        Assert.Equal("SENT", urgent.Str("status"));
        await _f.Freeze("2026-10-21T08:00:30");
        await _f.Post(Ports.Notification, "/api/v1/notifications/dispatch");
        Assert.Equal("SENT", (await _f.Get(Ports.Notification, $"/api/v1/notifications/{r.Str("id")}")).Str("status"));
        Assert.Equal(2, (await _f.Get(Ports.Notification, "/api/v1/mailbox?to=%2B46%2070%20555%2001%2001")).Int("count"));
    }

    [Fact]
    public async Task Mailbox_messages_self_destruct_after_24_hours_and_errors()
    {
        var to = "efimero." + Guid.NewGuid().ToString("N")[..6] + "@correo.nordvik.test";
        await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"to\":\"{to}\",\"templateKey\":\"welcome\",\"vars\":{{\"firstName\":\"E\",\"customerNumber\":\"C\"}}}}");
        Assert.Equal(1, (await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={to}")).Int("count"));
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":86401}");
        Assert.Equal(0, (await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={to}")).Int("count"));
        await _f.ResetClock();
        (await _f.Post(Ports.Notification, "/api/v1/notifications", $"{{\"to\":\"{to}\",\"templateKey\":\"welcome\",\"vars\":{{}}}}")).ShouldBeProblem(422, "MISSING_VARIABLES");
        (await _f.Post(Ports.Notification, "/api/v1/notifications", "{\"channel\":\"FAX\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }
}
