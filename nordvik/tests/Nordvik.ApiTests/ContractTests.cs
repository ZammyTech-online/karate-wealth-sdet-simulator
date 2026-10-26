using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

public static class ContractFlows
{
    public static async Task<Reply> Create(NordvikFixture f, string customerId, string type = "MASTER_AGREEMENT", string accountType = "ISK", string? token = null) =>
        await f.Send(HttpMethod.Post, Ports.Contracts, "/api/v2/contracts", $"{{\"type\":\"{type}\",\"customerId\":\"{customerId}\",\"accountType\":\"{accountType}\"}}",
            NordvikFixture.Bearer(token ?? await f.Erik()));

    public static async Task<Reply> Send(NordvikFixture f, string id) =>
        await f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/send", null, NordvikFixture.Bearer(await f.Erik()));

    public static async Task<Reply> Get(NordvikFixture f, string id) =>
        await f.Get(Ports.Contracts, $"/api/v2/contracts/{id}", NordvikFixture.Bearer(await f.Olof()));

    /// <summary>Firma una parte con NordID y espera a que la sesión termine.</summary>
    public static async Task<Reply> Sign(NordvikFixture f, string id, string party, Func<Task<string>> token)
    {
        var t = await token();
        var s = await f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", $"{{\"party\":\"{party}\"}}", NordvikFixture.Bearer(t));
        Assert.True(s.Code == 201, s.Body);
        return await Eventually.Until(async () => await f.Get(Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions/{s.Str("id")}", NordvikFixture.Bearer(await token())),
            r => r.Str("status") != "PENDING", 10000, $"firma de {party}");
    }

    public static async Task<string> Active(NordvikFixture f, string customerId, Func<Task<string>> customerToken)
    {
        var c = await Create(f, customerId);
        await Send(f, c.Str("id"));
        Assert.Equal("COMPLETED", (await Sign(f, c.Str("id"), "CUSTOMER", customerToken)).Str("status"));
        return c.Str("id");
    }
}

[Collection("nordvik")]
public class TemplateAndClauseTests
{
    private readonly NordvikFixture _f;
    public TemplateAndClauseTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Templates_are_versioned_and_old_versions_stay_available()
    {
        var list = await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates");
        Assert.Equal(4, list.Json.GetArrayLength());
        var master = list.Json.EnumerateArray().Single(t => t.GetProperty("type").GetString() == "MASTER_AGREEMENT");
        Assert.Equal(3, master.GetProperty("currentVersion").GetInt32());
        Assert.Equal("RETIRED", (await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates/MASTER_AGREEMENT/versions/1")).Str("status"));
        Assert.Equal(3, (await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates/MASTER_AGREEMENT/current")).Int("version"));
        Assert.Equal(new[] { "CUSTOMER", "ADVISOR", "COMPLIANCE" }, (await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates/DISCRETIONARY_MANDATE/current")).Json.GetProperty("parties").EnumerateArray().Select(p => p.GetString()));
        (await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates/MASTER_AGREEMENT/versions/9")).ShouldBeProblem(404, "TEMPLATE_VERSION_NOT_FOUND");
        (await _f.Get(Ports.ContractTemplates, "/api/v1/contract-templates/LOAN/current")).ShouldBeProblem(404, "TEMPLATE_NOT_FOUND");
    }

    [Fact]
    public async Task Clauses_depend_on_the_customer_facts()
    {
        var r = await _f.Post(Ports.Clauses, "/api/v1/clauses/resolve", "{\"templateType\":\"MASTER_AGREEMENT\",\"facts\":{\"age\":67,\"taxResidency\":\"NO\",\"accountType\":\"KF\",\"isPep\":true}}");
        var inc = r.Json.GetProperty("included").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToHashSet();
        var exc = r.Json.GetProperty("excluded").EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("reason").GetString());
        Assert.Superset(new HashSet<string?> { "GEN-01", "GDPR-01", "TAX-FOREIGN", "KF-01", "SENIOR-01", "PEP-01" }, inc);
        Assert.Equal("no se cumple taxResidency == 'SE'", exc["TAX-SE"]);
        Assert.Contains("ISK-01", exc.Keys);
        var missing = await _f.Post(Ports.Clauses, "/api/v1/clauses/resolve", "{\"templateType\":\"MASTER_AGREEMENT\",\"version\":2,\"facts\":{}}");
        Assert.Equal(2, missing.Int("version"));
        Assert.Contains(missing.Json.GetProperty("excluded").EnumerateArray(), c => c.GetProperty("reason").GetString()!.StartsWith("falta un dato"));
        var ana = await _f.Post(Ports.Clauses, "/api/v1/clauses/resolve", $"{{\"templateType\":\"MASTER_AGREEMENT\",\"customerId\":\"{SeedIds.CustomerAna}\"}}");
        Assert.Equal("SE", ana.Json.GetProperty("facts").GetProperty("taxResidency").GetString());
        Assert.Contains(ana.Json.GetProperty("included").EnumerateArray(), c => c.GetProperty("code").GetString() == "TAX-SE");
    }

    [Theory]
    [InlineData("age >= 65 and accountType == 'ISK'", "{\"age\":70,\"accountType\":\"ISK\"}", true)]
    [InlineData("age >= 65 and accountType == 'ISK'", "{\"age\":64,\"accountType\":\"ISK\"}", false)]
    [InlineData("isPep == true or age < 18", "{\"isPep\":false,\"age\":17}", true)]
    [InlineData("taxResidency != 'SE'", "{\"taxResidency\":\"SE\"}", false)]
    [InlineData("always", "{}", true)]
    public async Task Conditions_evaluate_like_a_truth_table(string condition, string facts, bool expected)
    {
        var r = await _f.Post(Ports.Clauses, "/api/v1/conditions/evaluate", JsonSerializer.Serialize(new { condition, facts = JsonDocument.Parse(facts).RootElement }));
        Assert.Equal(expected, r.Json.GetProperty("result").GetBoolean());
    }

    [Fact]
    public async Task Bad_conditions_are_rejected() =>
        (await _f.Post(Ports.Clauses, "/api/v1/conditions/evaluate", "{\"condition\":\"age >>= 3\",\"facts\":{}}")).ShouldBeProblem(422, "INVALID_CONDITION");
}

[Collection("nordvik")]
public class ContractLifecycleTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public ContractLifecycleTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Multipart(string id, byte[] data, string mime, string fileName, string kind = "FEE_SCHEDULE")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        content.Add(file, "file", fileName);
        content.Add(new StringContent(kind), "kind");
        var req = new HttpRequestMessage(HttpMethod.Post, NordvikFixture.Url(Ports.Contracts, $"/api/v2/contracts/{id}/annexes")) { Content = content };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + await _f.Erik());
        return await Reply.From(await _f.Http.SendAsync(req));
    }

    private async Task<byte[]> File(string path) => await (await _f.Http.GetAsync(NordvikFixture.Url(Ports.FileFactory, path))).Content.ReadAsByteArrayAsync();

    [Fact]
    public async Task Contract_is_built_from_template_without_personal_data()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna);
        Assert.Equal(201, c.Code);
        Assert.Equal("DRAFT", c.Str("status"));
        Assert.Equal(3, c.Int("templateVersion"));
        Assert.Equal("\"1\"", c.Headers.ETag?.Tag);
        var codes = c.Json.GetProperty("content").GetProperty("clauses").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToList();
        Assert.Contains("ISK-01", codes);
        Assert.Contains("TAX-SE", codes);
        Assert.DoesNotContain("KF-01", codes);
        Assert.Equal(codes.Count, c.Json.GetProperty("content").GetProperty("clauses").EnumerateArray().Select(x => x.GetProperty("clauseNo").GetInt32()).Distinct().Count());
        Assert.Equal(Canonical.Hash(c.Json.GetProperty("content")), c.Str("contentHash"));
        Assert.StartsWith("tok_cu_", c.Str("customerRef"));
        var nid = await TestData.NationalIdOf(_f, SeedIds.CustomerAna);
        Assert.DoesNotContain(nid, c.Body);
        Assert.DoesNotContain(SeedIds.CustomerAna, c.Body);
        (await ContractFlows.Create(_f, SeedIds.CustomerSofia)).ShouldBeProblem(403, "ACCESS_DENIED");
        var bad = await _f.Send(HttpMethod.Post, Ports.Contracts, "/api/v2/contracts", "{\"type\":\"LOAN\",\"accountType\":\"X\"}", NordvikFixture.Bearer(await _f.Erik()));
        bad.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(3, bad.Json.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Annexes_are_scanned_typed_and_change_the_hash()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna);
        var id = c.Str("id");
        var json = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/annexes", "{\"kind\":\"FEE_SCHEDULE\",\"content\":{\"managementFee\":\"0.45\",\"currency\":\"SEK\"}}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, json.Code);
        Assert.Equal(2, json.Int("version"));
        Assert.NotEqual(c.Str("contentHash"), json.Str("contentHash"));
        var annex = json.Json.GetProperty("annexes")[0];
        Assert.Equal(Canonical.Hash(JsonDocument.Parse("{\"managementFee\":\"0.45\",\"currency\":\"SEK\"}").RootElement), annex.GetProperty("contentHash").GetString());
        Assert.Equal(annex.GetProperty("documentId").GetString(), json.Json.GetProperty("content").GetProperty("annexes")[0].GetProperty("documentId").GetString());

        var pdf = await Multipart(id, await File("/api/v1/files/pdf?pages=2"), "application/pdf", "tarifa.pdf");
        Assert.Equal(201, pdf.Code);
        Assert.Equal(3, pdf.Int("version"));
        (await Multipart(id, await File("/api/v1/files/eicar-pdf"), "application/pdf", "factura.pdf")).ShouldBeProblem(422, "ANNEX_INFECTED");
        (await Multipart(id, await File("/api/v1/files/fake-pdf"), "application/pdf", "falso.pdf")).ShouldBeProblem(422, "ANNEX_MIME_MISMATCH");
        (await Multipart(id, await File("/api/v1/files/oversized?mb=6"), "application/pdf", "grande.pdf")).ShouldBeProblem(413, "FILE_TOO_LARGE");
        (await Multipart(id, Encoding.UTF8.GetBytes("hola"), "text/plain", "nota.txt")).ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        await ContractFlows.Send(_f, id);
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/annexes", "{\"kind\":\"X\",\"content\":{}}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(409, "CONTRACT_NOT_EDITABLE");
    }

    [Fact]
    public async Task Customer_signs_with_nordid_and_the_contract_activates()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna);
        var id = c.Str("id");
        var sent = await ContractFlows.Send(_f, id);
        Assert.Equal("PENDING_SIGNATURE", sent.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"CUSTOMER\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "WRONG_SIGNER");
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"CUSTOMER\"}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CONTRACT_NOT_FOUND");
        var session = await ContractFlows.Sign(_f, id, "CUSTOMER", _f.Ana);
        Assert.Equal("COMPLETED", session.Str("status"));
        Assert.Equal(c.Str("contentHash"), session.Str("signedHash"));
        var active = await ContractFlows.Get(_f, id);
        Assert.Equal("ACTIVE", active.Str("status"));
        Assert.Matches(@"T23:59:59\.999[+-]\d{2}:\d{2}$", active.Str("withdrawalDeadline"));
        var sigs = await _f.Get(Ports.Contracts, $"/api/v2/contracts/{id}/signatures", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Empty(sigs.Json.GetProperty("missing").EnumerateArray());
        var verify = await _f.Post(Ports.Contracts, $"/api/v2/contracts/{id}/verify");
        Assert.Equal(401, verify.Code);
        verify = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/verify", null, NordvikFixture.Bearer(await _f.Karin()));
        Assert.True(verify.Json.GetProperty("valid").GetBoolean());
        await _f.Post(Ports.Contracts, $"/_test/contracts/{id}/tamper");
        var broken = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/verify", null, NordvikFixture.Bearer(await _f.Karin()));
        Assert.False(broken.Json.GetProperty("contentHashValid").GetBoolean());
        Assert.False(broken.Json.GetProperty("signatures")[0].GetProperty("valid").GetBoolean());
        var mail = await Eventually.Until(async () => await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={(await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{SeedIds.CustomerAna}")).Str("email")}"),
            r => r.Body.Contains("NordID"), 8000, "aviso de contrato pendiente de firma");
        Assert.Equal(200, mail.Code);
    }

    [Fact]
    public async Task Advisory_mandate_needs_both_parties()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerLars, "ADVISORY_MANDATE");
        var id = c.Str("id");
        await ContractFlows.Send(_f, id);
        Assert.Equal("COMPLETED", (await ContractFlows.Sign(_f, id, "CUSTOMER", _f.Lars)).Str("status"));
        Assert.Equal("PARTIALLY_SIGNED", (await ContractFlows.Get(_f, id)).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"CUSTOMER\"}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(409, "PARTY_ALREADY_SIGNED");
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"NOTARY\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(400, "UNKNOWN_PARTY");
        Assert.Equal("COMPLETED", (await ContractFlows.Sign(_f, id, "ADVISOR", _f.Erik)).Str("status"));
        var active = await ContractFlows.Get(_f, id);
        Assert.Equal("ACTIVE", active.Str("status"));
        Assert.Equal(2, active.Json.GetProperty("signatures").GetArrayLength());
    }

    [Fact]
    public async Task Second_signature_with_another_template_version_invalidates_the_contract()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerLars, "ADVISORY_MANDATE");
        var id = c.Str("id");
        await ContractFlows.Send(_f, id);
        await ContractFlows.Sign(_f, id, "CUSTOMER", _f.Lars);
        // El asesor abre su firma y, mientras firma, alguien publica otra versión de la plantilla
        var start = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"ADVISOR\"}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, start.Code);
        await _f.Post(Ports.Contracts, $"/_test/contracts/{id}/bump-template");
        var s = await Eventually.Until(async () => await _f.Get(Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions/{start.Str("id")}", NordvikFixture.Bearer(await _f.Erik())),
            r => r.Str("status") != "PENDING", 10000, "firma del asesor");
        Assert.Equal("REJECTED", s.Str("status"));
        var invalid = await ContractFlows.Get(_f, id);
        Assert.Equal("INVALID_SIGNATURE", invalid.Str("status"));
        Assert.Contains("plantilla", invalid.Str("invalidReason"));
    }

    [Fact]
    public async Task Unsigned_contracts_expire_after_14_days()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerLars);
        var id = c.Str("id");
        await ContractFlows.Send(_f, id);
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":1209601}");
        Assert.Equal("EXPIRED", (await ContractFlows.Get(_f, id)).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/signing-sessions", "{\"party\":\"CUSTOMER\"}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(409, "CONTRACT_NOT_SIGNABLE");
    }

    [Fact]
    public async Task Withdrawal_deadline_is_the_end_of_day_14_in_stockholm()
    {
        await _f.Freeze("2026-10-20T10:00:00");
        var a = await ContractFlows.Active(_f, SeedIds.CustomerLars, _f.Lars);
        var b = await ContractFlows.Active(_f, SeedIds.CustomerLars, _f.Lars);
        Assert.StartsWith("2026-11-03T23:59:59.999+01:00", (await ContractFlows.Get(_f, a)).Str("withdrawalDeadline"));
        await _f.Freeze("2026-11-03T23:59:00");
        var ok = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{a}/withdraw", "{\"reason\":\"me lo he pensado\"}", NordvikFixture.Bearer(await _f.Lars()));
        Assert.Equal("WITHDRAWN", ok.Str("status"));
        await _f.Freeze("2026-11-04T00:00:01");
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{b}/withdraw", "{}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(422, "WITHDRAWAL_PERIOD_EXPIRED");
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{b}/withdraw", "{}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ACCESS_DENIED");
    }

    [Fact]
    public async Task Amendments_never_edit_the_original()
    {
        var id = await ContractFlows.Active(_f, SeedIds.CustomerAna, _f.Ana);
        var original = await ContractFlows.Get(_f, id);
        var am = await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/amendments", "{\"accountType\":\"KF\",\"reason\":\"cambio a KF\"}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(201, am.Code);
        Assert.Equal(id, am.Str("amends"));
        Assert.Contains(am.Json.GetProperty("content").GetProperty("clauses").EnumerateArray(), x => x.GetProperty("code").GetString() == "KF-01");
        var stillOriginal = await ContractFlows.Get(_f, id);
        Assert.Equal("ACTIVE", stillOriginal.Str("status"));
        Assert.Equal(original.Str("contentHash"), stillOriginal.Str("contentHash"));
        Assert.Equal(am.Str("id"), stillOriginal.Str("amendedBy"));
        (await _f.Send(HttpMethod.Post, Ports.Contracts, $"/api/v2/contracts/{id}/amendments", "{\"reason\":\"otra\"}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(409, "AMENDMENT_IN_PROGRESS");
        await ContractFlows.Send(_f, am.Str("id"));
        await ContractFlows.Sign(_f, am.Str("id"), "CUSTOMER", _f.Ana);
        Assert.Equal("ACTIVE", (await ContractFlows.Get(_f, am.Str("id"))).Str("status"));
        Assert.Equal("SUPERSEDED", (await ContractFlows.Get(_f, id)).Str("status"));
    }

    [Fact]
    public async Task Rendered_pdf_is_a_single_use_link()
    {
        var c = await ContractFlows.Create(_f, SeedIds.CustomerAna);
        var r = await _f.Get(Ports.Contracts, $"/api/v2/contracts/{c.Str("id")}/render?format=pdf", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(201, r.Code);
        Assert.True(r.Json.GetProperty("singleUse").GetBoolean());
        var first = await _f.Http.GetAsync(r.Str("url"));
        var bytes = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal("application/pdf", first.Content.Headers.ContentType?.MediaType);
        Assert.True(PdfWriter.LooksLikePdf(bytes));
        Assert.Equal(r.Str("sha256"), Canonical.Sha256Hex(bytes));
        var second = await Reply.From(await _f.Http.GetAsync(r.Str("url")));
        second.ShouldBeProblem(410, "LINK_ALREADY_USED");
    }

    [Fact]
    public async Task Contracts_are_listed_with_cursor_pagination()
    {
        for (var i = 0; i < 3; i++) await ContractFlows.Create(_f, SeedIds.CustomerLars, "RECORDING_CONSENT");
        var erik = await _f.Erik();
        var p1 = await _f.Get(Ports.Contracts, $"/api/v2/contracts?customerId={SeedIds.CustomerLars}&status=DRAFT&limit=2", NordvikFixture.Bearer(erik));
        Assert.Equal(2, p1.Json.GetProperty("items").GetArrayLength());
        var cursor = p1.Str("nextCursor");
        var p2 = await _f.Get(Ports.Contracts, $"/api/v2/contracts?customerId={SeedIds.CustomerLars}&status=DRAFT&limit=2&cursor={Uri.EscapeDataString(cursor)}", NordvikFixture.Bearer(erik));
        Assert.True(p2.Json.GetProperty("items").GetArrayLength() >= 1);
        Assert.Empty(p1.Json.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).Intersect(p2.Json.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString())));
        var own = await _f.Get(Ports.Contracts, $"/api/v2/contracts?customerId={SeedIds.CustomerAna}", NordvikFixture.Bearer(await _f.Lars()));
        Assert.All(own.Json.GetProperty("items").EnumerateArray(), x => Assert.Equal(DocumentCustomerRef(SeedIds.CustomerLars), x.GetProperty("customerRef").GetString()));
        (await _f.Get(Ports.Contracts, "/api/v2/contracts", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "CUSTOMER_REQUIRED");
        (await _f.Get(Ports.Contracts, $"/api/v2/contracts?customerId={SeedIds.CustomerLars}", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(403, "ACCESS_DENIED");
        (await _f.Get(Ports.Contracts, $"/api/v2/contracts?customerId={SeedIds.CustomerLars}&cursor=xyz", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "INVALID_CURSOR");
    }

    private static string DocumentCustomerRef(string id) => "tok_cu_" + Canonical.Sha256Hex(id)[..6];
}

[Collection("nordvik")]
public class VaultAndScanTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public VaultAndScanTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Documents_self_destruct_and_links_expire()
    {
        var d = await _f.Post(Ports.Vault, "/api/v1/documents", "{\"kind\":\"EXTERNAL_STATEMENT\",\"content\":{\"balance\":\"1000.00\"},\"expiresInSeconds\":60}");
        Assert.Equal(201, d.Code);
        Assert.Equal("1000.00", d.Json.GetProperty("content").GetProperty("balance").GetString());
        var link = await _f.Post(Ports.Vault, $"/api/v1/documents/{d.Str("id")}/links", "{\"ttlSeconds\":5}");
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":6}");
        (await Reply.From(await _f.Http.GetAsync(link.Str("url")))).ShouldBeProblem(410, "LINK_EXPIRED");
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":61}");
        var gone = await _f.Get(Ports.Vault, $"/api/v1/documents/{d.Str("id")}", NordvikFixture.Bearer(await _f.Olof()));
        gone.ShouldBeProblem(410, "DOCUMENT_DESTROYED");
        var meta = await _f.Get(Ports.Vault, "/api/v1/documents");
        var tomb = meta.Json.EnumerateArray().Single(x => x.GetProperty("id").GetString() == d.Str("id"));
        Assert.Equal("DESTROYED", tomb.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, tomb.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task Documents_respect_customer_ownership_and_can_be_shredded()
    {
        var d = await _f.Post(Ports.Vault, "/api/v1/documents", $"{{\"kind\":\"KYC_SUMMARY\",\"content\":{{\"risk\":\"LOW\"}},\"customerId\":\"{SeedIds.CustomerLars}\"}}");
        (await _f.Get(Ports.Vault, $"/api/v1/documents/{d.Str("id")}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "DOCUMENT_NOT_FOUND");
        Assert.Equal(200, (await _f.Get(Ports.Vault, $"/api/v1/documents/{d.Str("id")}", NordvikFixture.Bearer(await _f.Lars()))).Code);
        (await _f.Get(Ports.Vault, $"/api/v1/documents/{d.Str("id")}")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
        (await _f.Send(HttpMethod.Delete, Ports.Vault, $"/api/v1/documents/{d.Str("id")}", null, NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        Assert.Equal("DESTROYED", (await _f.Send(HttpMethod.Delete, Ports.Vault, $"/api/v1/documents/{d.Str("id")}", null, NordvikFixture.Bearer(await _f.Karin()))).Str("status"));
        (await _f.Get(Ports.Vault, $"/api/v1/documents/{d.Str("id")}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(410, "DOCUMENT_DESTROYED");
        (await _f.Post(Ports.Vault, "/api/v1/documents", "{\"kind\":\"X\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Antivirus_detects_eicar_and_real_types()
    {
        async Task<Reply> Scan(string path, string mime)
        {
            var bytes = await (await _f.Http.GetAsync(NordvikFixture.Url(Ports.FileFactory, path))).Content.ReadAsByteArrayAsync();
            var req = new HttpRequestMessage(HttpMethod.Post, NordvikFixture.Url(Ports.Antivirus, "/api/v1/scans")) { Content = new ByteArrayContent(bytes) };
            req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
            return await Reply.From(await _f.Http.SendAsync(req));
        }
        var eicar = await Scan("/api/v1/files/eicar", "text/plain");
        Assert.Equal("INFECTED", eicar.Str("verdict"));
        Assert.Equal("EICAR-Test-File", eicar.Str("signature"));
        var pdf = await Scan("/api/v1/files/pdf", "application/pdf");
        Assert.Equal("CLEAN", pdf.Str("verdict"));
        Assert.Equal("application/pdf", pdf.Str("detectedMime"));
        var fake = await Scan("/api/v1/files/fake-pdf", "application/pdf");
        Assert.Equal("SUSPICIOUS", fake.Str("verdict"));
        Assert.Equal("application/json", fake.Str("detectedMime"));
        (await Scan("/api/v1/files/oversized?mb=6", "application/pdf")).ShouldBeProblem(413, "FILE_TOO_LARGE");
    }

    [Fact]
    public async Task File_and_text_factories()
    {
        var csv = await (await _f.Http.GetAsync(NordvikFixture.Url(Ports.FileFactory, "/api/v1/files/employer-csv?rows=50&errors=5&seed=9"))).Content.ReadAsStringAsync();
        var lines = csv.Trim().Split('\n');
        Assert.Equal(51, lines.Length);
        var invalid = lines.Skip(1).Count(l => { var f = l.Split(';'); return !NationalId.Check(f[0]).Valid || f[1] != "2026-10" || f[3] == "" ||
            Math.Round(decimal.Parse(f[2]) * decimal.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture) / 100, 2) != decimal.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture); });
        Assert.Equal(5, invalid);
        var pdf = await (await _f.Http.GetAsync(NordvikFixture.Url(Ports.FileFactory, "/api/v1/files/pdf?pages=3"))).Content.ReadAsByteArrayAsync();
        Assert.Equal(3, PdfWriter.PageCount(pdf));
        var msg = await _f.Post(Ports.TextFactory, "/api/v1/texts/customer-message", "{\"scenario\":\"complaint\",\"seed\":3}");
        Assert.Equal(new[] { "NATIONAL_ID", "IBAN" }, msg.Json.GetProperty("piiPresent").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(msg.Json.GetProperty("expected").GetProperty("nationalId").GetString()!, msg.Str("text"));
        var clean = await _f.Post(Ports.TextFactory, "/api/v1/texts/customer-message", "{\"scenario\":\"complaint\",\"includePii\":false}");
        Assert.Empty(clean.Json.GetProperty("piiPresent").EnumerateArray());
        var script = await _f.Post(Ports.TextFactory, "/api/v1/texts/call-script?scenario=V3");
        var offsets = script.Json.GetProperty("turns").EnumerateArray().Select(t => t.GetProperty("offsetMs").GetInt32()).ToList();
        Assert.Equal(offsets.OrderBy(x => x), offsets);
        (await _f.Post(Ports.TextFactory, "/api/v1/texts/call-script?scenario=V9")).ShouldBeProblem(400, "UNKNOWN_SCENARIO");
    }
}
