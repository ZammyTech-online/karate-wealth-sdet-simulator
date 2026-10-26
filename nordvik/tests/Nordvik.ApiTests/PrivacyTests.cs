using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Oleada 3 · privacidad: claves y crypto-shredding, tokenización, consentimientos, pasaportes, linaje, redacción y retención.</summary>
[Collection("nordvik")]
public class KeyManagementTests
{
    private readonly NordvikFixture _f;
    public KeyManagementTests(NordvikFixture f) => _f = f;

    private async Task<Reply> NewKey(string? subject = null) =>
        await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/keys", JsonSerializer.Serialize(new { subject = subject ?? "test:" + Guid.NewGuid().ToString("N"), purpose = "PII" }), NordvikFixture.Bearer(await _f.Karin()));

    [Fact]
    public async Task Keys_are_created_once_per_subject_and_only_by_governance_roles()
    {
        var subject = "test:" + Guid.NewGuid().ToString("N");
        var k = await NewKey(subject);
        Assert.Equal(201, k.Code);
        Assert.StartsWith("key_", k.Str("id"));
        Assert.Equal(1, k.Int("currentVersion"));
        Assert.Equal("ACTIVE", k.Str("status"));
        Assert.False(k.Has("material"));
        (await NewKey(subject)).ShouldBeProblem(409, "KEY_ALREADY_EXISTS");
        (await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/keys", "{\"subject\":\"x:1\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/keys", "{}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Rotation_keeps_old_ciphertexts_readable()
    {
        var karin = await _f.Karin();
        var id = (await NewKey()).Str("id");
        var c1 = await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/encrypt", "{\"plaintext\":\"SE45 5000 0000 0583 9825 7466\"}", NordvikFixture.Bearer(karin));
        Assert.Equal(1, c1.Int("version"));
        Assert.StartsWith($"nvk1.{id}.1.", c1.Str("ciphertext"));
        var rot = await _f.Post(Ports.Keys, $"/api/v1/keys/{id}/rotate");
        Assert.Equal(401, rot.Code);
        rot = await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/rotate", null, NordvikFixture.Bearer(karin));
        Assert.Equal(2, rot.Int("currentVersion"));
        Assert.Equal(2, rot.Int("versions"));
        var c2 = await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/encrypt", "{\"plaintext\":\"hola\"}", NordvikFixture.Bearer(karin));
        Assert.StartsWith($"nvk1.{id}.2.", c2.Str("ciphertext"));
        var d1 = await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/decrypt", JsonSerializer.Serialize(new { ciphertext = c1.Str("ciphertext") }), NordvikFixture.Bearer(karin));
        Assert.Equal("SE45 5000 0000 0583 9825 7466", d1.Str("plaintext"));
        Assert.Equal(1, d1.Int("version"));
    }

    [Fact]
    public async Task Shredding_makes_every_version_unreadable_forever()
    {
        var karin = await _f.Karin();
        var id = (await NewKey()).Str("id");
        var c = (await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/encrypt", "{\"plaintext\":\"secreto\"}", NordvikFixture.Bearer(karin))).Str("ciphertext");
        var s = await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/shred", "{\"reason\":\"GDPR art. 17\"}", NordvikFixture.Bearer(karin));
        Assert.Equal("SHREDDED", s.Str("status"));
        Assert.Equal("GDPR art. 17", s.Str("shredReason"));
        (await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/decrypt", JsonSerializer.Serialize(new { ciphertext = c }), NordvikFixture.Bearer(karin))).ShouldBeProblem(410, "KEY_SHREDDED");
        (await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/rotate", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(410, "KEY_SHREDDED");
        (await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/shred", null, NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "KEY_ALREADY_SHREDDED");
        (await _f.Get(Ports.Keys, $"/api/v1/internal/keys/{id}/versions/1")).ShouldBeProblem(410, "KEY_SHREDDED");
    }

    [Fact]
    public async Task Tampered_or_malformed_ciphertext_is_rejected()
    {
        var karin = await _f.Karin();
        var id = (await NewKey()).Str("id");
        var c = (await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{id}/encrypt", "{\"plaintext\":\"importe 100\"}", NordvikFixture.Bearer(karin))).Str("ciphertext");
        var raw = Convert.FromBase64String(c.Split('.')[3]);
        raw[^1] ^= 0x01;
        var tampered = string.Join('.', c.Split('.')[..3]) + "." + Convert.ToBase64String(raw);
        (await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/decrypt", JsonSerializer.Serialize(new { ciphertext = tampered }), NordvikFixture.Bearer(karin))).ShouldBeProblem(422, "DECRYPTION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Keys, "/api/v1/decrypt", "{\"ciphertext\":\"no-es-esto\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "INVALID_CIPHERTEXT");
    }
}

[Collection("nordvik")]
public class PiiVaultTests
{
    private readonly NordvikFixture _f;
    public PiiVaultTests(NordvikFixture f) => _f = f;

    public static async Task<Reply> Tokenize(NordvikFixture f, string type, string value, string? customerId, string? token = null) =>
        await f.Send(HttpMethod.Post, Ports.PiiVault, "/api/v1/tokens", JsonSerializer.Serialize(new { type, value, customerId }), NordvikFixture.Bearer(token ?? await f.Karin()));

    [Fact]
    public async Task Tokenizing_is_validated_masked_and_deterministic()
    {
        var nid = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var cust = "cust-" + Guid.NewGuid().ToString("N")[..8];
        var t = await Tokenize(_f, "NATIONAL_ID", nid, cust, await _f.Erik());
        Assert.Equal(201, t.Code);
        Assert.StartsWith("tok_nid_", t.Str("token"));
        Assert.Contains("**-****", t.Str("masked"));
        Assert.DoesNotContain(nid, t.Body);
        Assert.StartsWith("tok_cu_", t.Str("customerRef"));
        var again = await Tokenize(_f, "NATIONAL_ID", nid, cust, await _f.Erik());
        Assert.Equal(200, again.Code);
        Assert.Equal(t.Str("token"), again.Str("token"));
        var other = await Tokenize(_f, "NATIONAL_ID", nid, "otro-" + cust);
        Assert.NotEqual(t.Str("token"), other.Str("token"));
        (await Tokenize(_f, "NATIONAL_ID", "19900101-0000", cust)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Tokenize(_f, "IBAN", "SE00 1234", cust)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Tokenize(_f, "SHOE_SIZE", "42", cust)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Tokenize(_f, "EMAIL", "ana@example.com", cust, await _f.Ana())).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var mail = await Tokenize(_f, "EMAIL", "Ana.Berg@Example.com", cust);
        Assert.Equal("a***@example.com", mail.Str("masked"));
        var phone = await Tokenize(_f, "PHONE", "+46 70 123 45 67", cust);
        Assert.Equal("+46*******67", phone.Str("masked"));
    }

    [Fact]
    public async Task Reading_in_clear_needs_role_and_purpose_and_customers_only_see_their_own()
    {
        var iban = TestData.Iban("4242");
        var token = (await Tokenize(_f, "IBAN", iban, SeedIds.CustomerAna)).Str("token");
        string Path() => $"/api/v1/tokens/{token}/detokenize";
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"KYC_REVIEW\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "DETOKENIZE_NOT_ALLOWED");
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "PURPOSE_REQUIRED");
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"CURIOSITY\"}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "PURPOSE_REQUIRED");
        var r = await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"KYC_REVIEW\"}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal(200, r.Code);
        Assert.Equal(iban, r.Str("value"));
        var own = await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"CUSTOMER_REQUEST\"}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(iban, own.Str("value"));
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"KYC_REVIEW\"}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "TOKEN_NOT_FOUND");
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, Path(), "{\"purpose\":\"CUSTOMER_REQUEST\"}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "TOKEN_NOT_FOUND");
        (await _f.Get(Ports.PiiVault, "/api/v1/tokens/tok_iban_noexiste", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(404, "TOKEN_NOT_FOUND");
    }

    [Fact]
    public async Task Shredding_the_customer_key_erases_every_token_of_that_customer()
    {
        var cust = "gdpr-" + Guid.NewGuid().ToString("N")[..8];
        var a = (await Tokenize(_f, "EMAIL", $"{cust}@example.com", cust)).Str("token");
        var b = (await Tokenize(_f, "IBAN", TestData.Iban("0001"), cust)).Str("token");
        var karin = await _f.Karin();
        var inventory = await _f.Get(Ports.PiiVault, $"/api/v1/customers/{cust}/tokens", NordvikFixture.Bearer(karin));
        Assert.Equal(2, inventory.Int("count"));
        var keys = await _f.Get(Ports.Keys, $"/api/v1/keys?subject=customer:{cust}", NordvikFixture.Bearer(karin));
        var keyId = keys.Json.GetProperty("items")[0].GetProperty("id").GetString();
        await _f.Send(HttpMethod.Post, Ports.Keys, $"/api/v1/keys/{keyId}/shred", "{\"reason\":\"derecho al olvido\"}", NordvikFixture.Bearer(karin));
        (await _f.Send(HttpMethod.Post, Ports.PiiVault, $"/api/v1/tokens/{a}/detokenize", "{\"purpose\":\"LEGAL_HOLD\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(410, "PII_ERASED");
        await Eventually.Until(async () => await _f.Get(Ports.PiiVault, $"/api/v1/tokens/{b}", NordvikFixture.Bearer(karin)), r => r.Str("status") == "ERASED", 8000, "token b borrado por evento");
        (await Tokenize(_f, "PHONE", "+46701112233", cust)).ShouldBeProblem(409, "CUSTOMER_ERASED");
    }
}

[Collection("nordvik")]
public class ConsentLedgerTests
{
    private readonly NordvikFixture _f;
    public ConsentLedgerTests(NordvikFixture f) => _f = f;

    private async Task<Reply> Set(string token, string customerId, string purpose, bool granted, string? channel = null) =>
        await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", JsonSerializer.Serialize(new { customerId, purpose, granted, channel }), NordvikFixture.Bearer(token));

    [Fact]
    public async Task Everything_is_denied_by_default_and_changes_are_chained()
    {
        var erik = await _f.Erik();
        var id = (await _f.NewCustomer()).GetProperty("id").GetString()!;
        var state = await _f.Get(Ports.Consents, $"/api/v1/customers/{id}/consents", NordvikFixture.Bearer(erik));
        Assert.All(state.Json.GetProperty("consents").EnumerateArray(), c => { Assert.False(c.GetProperty("granted").GetBoolean()); Assert.Equal("DEFAULT", c.GetProperty("source").GetString()); });
        Assert.Equal(6, state.Json.GetProperty("consents").GetArrayLength());
        var g = await Set(erik, id, "MARKETING", true);
        Assert.Equal(201, g.Code);
        Assert.True(g.Json.GetProperty("changed").GetBoolean());
        Assert.Equal("ADVISOR", g.Json.GetProperty("entry").GetProperty("channel").GetString());
        var same = await Set(erik, id, "MARKETING", true);
        Assert.Equal(200, same.Code);
        Assert.False(same.Json.GetProperty("changed").GetBoolean());
        var w = await Set(erik, id, "marketing", false, "PAPER");
        Assert.Equal(201, w.Code);
        var hist = (await _f.Get(Ports.Consents, $"/api/v1/customers/{id}/consents/history", NordvikFixture.Bearer(erik))).Json.GetProperty("items");
        Assert.Equal(2, hist.GetArrayLength());
        Assert.Equal(new string('0', 64), hist[0].GetProperty("prevHash").GetString());
        Assert.Equal(hist[0].GetProperty("hash").GetString(), hist[1].GetProperty("prevHash").GetString());
        var v = await _f.Get(Ports.Consents, $"/api/v1/customers/{id}/consents/verify", NordvikFixture.Bearer(erik));
        Assert.True(v.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(hist[1].GetProperty("hash").GetString(), v.Str("head"));
        var check = await _f.Get(Ports.Consents, $"/api/v1/internal/consents/check?customerId={id}&purpose=MARKETING");
        Assert.False(check.Json.GetProperty("granted").GetBoolean());
        await TestData.Events(_f, "consent.withdrawn", id);
    }

    [Fact]
    public async Task Rewriting_the_past_is_detected_by_verify()
    {
        var erik = await _f.Erik();
        var id = (await _f.NewCustomer()).GetProperty("id").GetString()!;
        var first = await Set(erik, id, "ANALYTICS", true);
        await Set(erik, id, "PROFILING", true);
        await Set(erik, id, "ANALYTICS", false);
        (await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/admin/tamper", JsonSerializer.Serialize(new { entryId = first.Json.GetProperty("entry").GetProperty("id").GetString() }), NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/admin/tamper", JsonSerializer.Serialize(new { entryId = first.Json.GetProperty("entry").GetProperty("id").GetString() }), NordvikFixture.Bearer(await _f.Admin()));
        var v = await _f.Get(Ports.Consents, $"/api/v1/customers/{id}/consents/verify", NordvikFixture.Bearer(erik));
        Assert.False(v.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(1, v.Int("brokenAt"));
        Assert.Contains("hash", v.Str("reason"));
    }

    [Fact]
    public async Task Customers_manage_their_own_consents_only()
    {
        var ana = await _f.Ana();
        var r = await Set(ana, SeedIds.CustomerAna, "ESG_PROFILING", true);
        Assert.True(r.Code is 200 or 201, r.Body);
        Assert.Equal("WEB", r.Json.GetProperty("entry").GetProperty("channel").GetString());
        (await Set(ana, SeedIds.CustomerLars, "MARKETING", true)).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Get(Ports.Consents, $"/api/v1/customers/{SeedIds.CustomerAna}/consents", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await Set(ana, SeedIds.CustomerAna, "SPAM", true)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", "{\"purpose\":\"MARKETING\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Consents, $"/api/v1/customers/{SeedIds.CustomerAna}/consents")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
    }
}

[Collection("nordvik")]
public class DataPassportTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public DataPassportTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Issue(string token, string audience = "payments-gateway", int? maxReads = null, int? ttlSeconds = null) =>
        await _f.Send(HttpMethod.Post, Ports.Passports, "/api/v1/passports",
            JsonSerializer.Serialize(new { token, purpose = "PAYMENT_EXECUTION", audience, maxReads, ttlSeconds }), NordvikFixture.Bearer(await _f.Olof()));

    private async Task<Reply> Read(string id, string? caller)
    {
        var olof = await _f.Olof();
        return await _f.Send(HttpMethod.Post, Ports.Passports, $"/api/v1/passports/{id}/read", null, r =>
        {
            NordvikFixture.Bearer(olof)(r);
            if (caller is not null) r.Headers.TryAddWithoutValidation("X-Caller-Service", caller);
        });
    }

    [Fact]
    public async Task A_single_use_passport_reads_once_for_its_audience_only()
    {
        var iban = TestData.Iban("7777");
        var token = (await PiiVaultTests.Tokenize(_f, "IBAN", iban, SeedIds.CustomerAna)).Str("token");
        var p = await Issue(token);
        Assert.Equal(201, p.Code);
        Assert.Equal("ACTIVE", p.Str("status"));
        Assert.Equal(1, p.Int("readsLeft"));
        Assert.DoesNotContain(iban, p.Body);
        var id = p.Str("id");
        (await Read(id, null)).ShouldBeProblem(400, "CALLER_REQUIRED");
        (await Read(id, "bff-mobile")).ShouldBeProblem(403, "AUDIENCE_MISMATCH");
        var ok = await Read(id, "payments-gateway");
        Assert.Equal(200, ok.Code);
        Assert.Equal(iban, ok.Str("value"));
        Assert.Equal(1, ok.Int("readNo"));
        Assert.Equal(0, ok.Int("readsLeft"));
        (await Read(id, "payments-gateway")).ShouldBeProblem(410, "PASSPORT_EXHAUSTED");
        Assert.Equal("EXHAUSTED", (await _f.Get(Ports.Passports, $"/api/v1/passports/{id}", NordvikFixture.Bearer(await _f.Olof()))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Passports, $"/api/v1/passports/{id}/revoke", null, NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(409, "PASSPORT_NOT_ACTIVE");
    }

    [Fact]
    public async Task Passports_expire_with_the_virtual_clock_and_can_be_revoked()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var token = (await PiiVaultTests.Tokenize(_f, "EMAIL", $"p{Guid.NewGuid().ToString("N")[..6]}@example.com", SeedIds.CustomerAna)).Str("token");
        var p = await Issue(token, maxReads: 3, ttlSeconds: 60);
        Assert.Equal(3, p.Int("maxReads"));
        Assert.Equal(200, (await Read(p.Str("id"), "payments-gateway")).Code);
        await _f.Freeze($"{TestData.Tuesday}T10:01:01");
        (await Read(p.Str("id"), "payments-gateway")).ShouldBeProblem(410, "PASSPORT_EXPIRED");
        var q = await Issue(token);
        var rv = await _f.Send(HttpMethod.Post, Ports.Passports, $"/api/v1/passports/{q.Str("id")}/revoke", null, NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal("REVOKED", rv.Str("status"));
        (await Read(q.Str("id"), "payments-gateway")).ShouldBeProblem(410, "PASSPORT_REVOKED");
    }

    [Fact]
    public async Task Issuing_is_validated()
    {
        var token = (await PiiVaultTests.Tokenize(_f, "EMAIL", $"v{Guid.NewGuid().ToString("N")[..6]}@example.com", SeedIds.CustomerAna)).Str("token");
        (await Issue(token, audience: "no-existe")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Issue(token, maxReads: 11)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Issue(token, ttlSeconds: 5)).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Issue("tok_mail_noexiste")).ShouldBeProblem(422, "TOKEN_NOT_FOUND");
        (await _f.Send(HttpMethod.Post, Ports.Passports, "/api/v1/passports", JsonSerializer.Serialize(new { token, purpose = "X", audience = "payments-gateway" }), NordvikFixture.Bearer(await _f.Erik())))
            .ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Get(Ports.Passports, "/api/v1/passports/pas_noexiste", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(404, "PASSPORT_NOT_FOUND");
    }
}

[Collection("nordvik")]
public class LineageTests
{
    private readonly NordvikFixture _f;
    public LineageTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Every_hop_of_a_personal_datum_is_recorded()
    {
        var karin = await _f.Karin();
        var token = (await PiiVaultTests.Tokenize(_f, "IBAN", TestData.Iban("3131"), SeedIds.CustomerAna)).Str("token");
        await _f.Send(HttpMethod.Post, Ports.PiiVault, $"/api/v1/tokens/{token}/detokenize", "{\"purpose\":\"KYC_REVIEW\"}", NordvikFixture.Bearer(karin));
        var olof = await _f.Olof();
        var p = await _f.Send(HttpMethod.Post, Ports.Passports, "/api/v1/passports", JsonSerializer.Serialize(new { token, purpose = "PAYMENT_EXECUTION", audience = "payments-gateway" }), NordvikFixture.Bearer(olof));
        await _f.Send(HttpMethod.Post, Ports.Passports, $"/api/v1/passports/{p.Str("id")}/read", null, r => { NordvikFixture.Bearer(olof)(r); r.Headers.TryAddWithoutValidation("X-Caller-Service", "payments-gateway"); });
        var l = await Eventually.Until(async () => await _f.Get(Ports.Lineage, $"/api/v1/lineage/{token}", NordvikFixture.Bearer(karin)),
            r => r.Code == 200 && r.Int("count") >= 5, 10000, "linaje completo");
        var actions = l.Json.GetProperty("hops").EnumerateArray().Select(h => h.GetProperty("action").GetString()).ToList();
        Assert.Equal("TOKENIZED", actions[0]);
        Assert.Contains("PASSPORT_ISSUED", actions);
        Assert.Contains("PASSPORT_READ", actions);
        Assert.Equal(2, l.Int("clearReads"));
        Assert.Contains("payments-gateway", l.Json.GetProperty("readers").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("compliance.karin", l.Json.GetProperty("readers").EnumerateArray().Select(x => x.GetString()));
        (await _f.Get(Ports.Lineage, $"/api/v1/lineage/{token}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await _f.Get(Ports.Lineage, "/api/v1/lineage/tok_nada", NordvikFixture.Bearer(karin))).ShouldBeProblem(404, "LINEAGE_NOT_FOUND");
        (await _f.Get(Ports.Lineage, "/api/v1/lineage", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "FILTER_REQUIRED");
        var byActor = await _f.Get(Ports.Lineage, "/api/v1/lineage?actor=payments-gateway&action=PASSPORT_READ", NordvikFixture.Bearer(karin));
        Assert.True(byActor.Int("count") >= 1);
    }
}

[Collection("nordvik")]
public class RedactionTests
{
    private readonly NordvikFixture _f;
    public RedactionTests(NordvikFixture f) => _f = f;

    private async Task<Reply> Redact(string text, string mode, string? token = null, string[]? types = null) =>
        await _f.Send(HttpMethod.Post, Ports.Redaction, "/api/v1/redact", JsonSerializer.Serialize(new { text, mode, types, customerId = SeedIds.CustomerAna }), NordvikFixture.Bearer(token ?? await _f.Erik()));

    [Fact]
    public async Task Only_valid_identifiers_are_redacted()
    {
        var nid = (await _f.NewPerson()).GetProperty("nationalId").GetString()!;
        var iban = IbanMath.Print(TestData.Iban("5555"));
        var text = $"Hola, soy {nid}, mi cuenta es {iban} KR y mi correo ana.berg@example.com. Llamadme al +46 70 123 45 67. Tarjeta 4111 1111 1111 1111. Referencia 19900101-0000 y pedido 12345.";
        var r = await Redact(text, "MASK");
        Assert.Equal(200, r.Code);
        var types = r.Json.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { "NATIONAL_ID", "IBAN", "EMAIL", "PHONE", "CARD" }, types);
        var red = r.Str("redacted");
        Assert.DoesNotContain(nid, red);
        Assert.DoesNotContain(iban, red);
        Assert.DoesNotContain("ana.berg@", red);
        Assert.Contains("19900101-0000", red);
        Assert.Contains("12345", red);
        Assert.Contains(" KR y mi correo", red);
        Assert.Contains("**** **** **** 1111", red);
        Assert.Equal(5, r.Int("total"));
    }

    [Fact]
    public async Task Remove_and_tokenize_modes()
    {
        var iban = TestData.Iban("8888");
        var rm = await Redact($"Cuenta {iban}.", "REMOVE");
        Assert.Equal("Cuenta [IBAN].", rm.Str("redacted"));
        var tk = await Redact($"Cuenta {iban}.", "TOKENIZE", await _f.Karin());
        Assert.Matches("^Cuenta tok_iban_[0-9a-f]{12}\\.$", tk.Str("redacted"));
        (await Redact($"Cuenta {iban}.", "TOKENIZE", await _f.Ana())).ShouldBeProblem(403, "TOKENIZE_NOT_ALLOWED");
        var onlyEmail = await Redact($"a@b.se y {iban}", "REMOVE", types: new[] { "EMAIL" });
        Assert.Equal($"[EMAIL] y {iban}", onlyEmail.Str("redacted"));
        (await Redact("x", "SHRED")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Redact("x", "MASK", types: new[] { "DNA" })).ShouldBeProblem(400, "VALIDATION_FAILED");
        var clean = await _f.Send(HttpMethod.Post, Ports.Redaction, "/api/v1/scan", "{\"text\":\"Quiero comprar fondos verdes\"}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.False(clean.Json.GetProperty("containsPii").GetBoolean());
        var dirty = await _f.Send(HttpMethod.Post, Ports.Redaction, "/api/v1/scan", JsonSerializer.Serialize(new { text = "escribe a x@y.se" }), NordvikFixture.Bearer(await _f.Ana()));
        Assert.True(dirty.Json.GetProperty("containsPii").GetBoolean());
        Assert.DoesNotContain("x@y.se", dirty.Body);
    }
}

[Collection("nordvik")]
public class RetentionTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public RetentionTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Register(string dataClass, string? subject = null) =>
        await _f.Send(HttpMethod.Post, Ports.Retention, "/api/v1/items",
            JsonSerializer.Serialize(new { dataClass, ownerService = "voice-service", @ref = "call_" + Guid.NewGuid().ToString("N")[..8], subject }), NordvikFixture.Bearer(await _f.Olof()));

    private async Task<Reply> Run() => await _f.Send(HttpMethod.Post, Ports.Retention, "/api/v1/runs", null, NordvikFixture.Bearer(await _f.Olof()));

    [Fact]
    public async Task Expired_items_are_purged_and_their_keys_shredded()
    {
        await _f.Freeze("2026-11-03T09:00:00");
        var subject = "call:" + Guid.NewGuid().ToString("N")[..8];
        var key = await _f.Post(Ports.Keys, "/api/v1/internal/keys/resolve", JsonSerializer.Serialize(new { subject, purpose = "VOICE" }));
        var item = await Register("voice_audio", subject);
        Assert.Equal(201, item.Code);
        Assert.Equal("SCHEDULED", item.Str("status"));
        Assert.StartsWith("2026-11-03T09:10:00", item.Str("dueAt"));
        var early = await Run();
        Assert.DoesNotContain(item.Str("id"), early.Body);
        await _f.Freeze("2026-11-03T09:10:01");
        var run = await Run();
        Assert.Contains(item.Str("id"), run.Body);
        var after = await _f.Get(Ports.Retention, $"/api/v1/items/{item.Str("id")}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal("PURGED", after.Str("status"));
        Assert.Equal(run.Str("id"), after.Str("runId"));
        (await _f.Get(Ports.Keys, $"/api/v1/internal/keys/{key.Str("keyId")}/versions/1")).ShouldBeProblem(410, "KEY_SHREDDED");
        await TestData.Events(_f, "retention.expired", item.Str("ref"));
        var report = await _f.Get(Ports.Retention, $"/api/v1/runs/{run.Str("id")}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.True(report.Int("purged") >= 1);
    }

    [Fact]
    public async Task Legal_hold_beats_retention()
    {
        await _f.Freeze("2026-11-03T12:00:00");
        var item = await Register("MFA_RESULT");
        var karin = await _f.Karin();
        (await _f.Send(HttpMethod.Post, Ports.Retention, $"/api/v1/items/{item.Str("id")}/legal-hold", "{\"hold\":true}", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.Retention, $"/api/v1/items/{item.Str("id")}/legal-hold", "{\"hold\":true,\"reason\":\"x\"}", NordvikFixture.Bearer(await _f.Olof()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var held = await _f.Send(HttpMethod.Post, Ports.Retention, $"/api/v1/items/{item.Str("id")}/legal-hold", "{\"hold\":true,\"reason\":\"Investigación de fraude 2026-77\"}", NordvikFixture.Bearer(karin));
        Assert.True(held.Json.GetProperty("legalHold").GetBoolean());
        await _f.Freeze("2026-11-03T13:00:00");
        karin = await _f.Karin(); // el token anterior caducó al adelantar el reloj una hora
        var run = await Run();
        Assert.Contains(item.Str("id"), run.Json.GetProperty("heldItems").GetRawText());
        Assert.Equal("SCHEDULED", (await _f.Get(Ports.Retention, $"/api/v1/items/{item.Str("id")}", NordvikFixture.Bearer(karin))).Str("status"));
        await _f.Send(HttpMethod.Post, Ports.Retention, $"/api/v1/items/{item.Str("id")}/legal-hold", "{\"hold\":false}", NordvikFixture.Bearer(karin));
        await Run();
        (await _f.Send(HttpMethod.Post, Ports.Retention, $"/api/v1/items/{item.Str("id")}/legal-hold", "{\"hold\":true,\"reason\":\"tarde\"}", NordvikFixture.Bearer(karin))).ShouldBeProblem(409, "ITEM_ALREADY_PURGED");
    }

    [Fact]
    public async Task Policies_are_seeded_and_governed()
    {
        var p = await _f.Get(Ports.Retention, "/api/v1/policies");
        var voice = p.Json.GetProperty("items").EnumerateArray().First(x => x.GetProperty("dataClass").GetString() == "VOICE_AUDIO");
        Assert.Equal(600, voice.GetProperty("retentionSeconds").GetInt64());
        Assert.Equal("SHRED", voice.GetProperty("action").GetString());
        Assert.Equal("10 min", voice.GetProperty("human").GetString());
        (await _f.Send(HttpMethod.Put, Ports.Retention, "/api/v1/policies/CHAT_LOG", "{\"retentionSeconds\":3600}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        var put = await _f.Send(HttpMethod.Put, Ports.Retention, "/api/v1/policies/chat_log", "{\"retentionSeconds\":7200,\"action\":\"ANONYMIZE\"}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.Equal("CHAT_LOG", put.Str("dataClass"));
        Assert.Equal("2 h", put.Str("human"));
        (await _f.Send(HttpMethod.Put, Ports.Retention, "/api/v1/policies/X", "{\"retentionSeconds\":0}", NordvikFixture.Bearer(await _f.Karin()))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Register("NO_EXISTE")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }
}
