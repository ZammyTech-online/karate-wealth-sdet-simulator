using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

public static class Calls
{
    public static async Task GrantRecording(NordvikFixture f, string customerId, string? token = null) =>
        Assert.True((await f.Send(HttpMethod.Post, Ports.Consents, "/api/v1/consents", JsonSerializer.Serialize(new { customerId, purpose = "VOICE_RECORDING", granted = true }),
            NordvikFixture.Bearer(token ?? await f.Erik()))).Code is 200 or 201);

    public static async Task<Reply> Start(NordvikFixture f, string customerId, string? token = null) =>
        await f.Send(HttpMethod.Post, Ports.Voice, "/api/v2/calls", JsonSerializer.Serialize(new { customerId, channel = "PHONE" }), NordvikFixture.Bearer(token ?? await f.Erik()));

    public static async Task<string> Run(NordvikFixture f, string customerId, string script, object? variables = null)
    {
        var c = await Start(f, customerId);
        Assert.True(c.Code == 201, c.Body);
        var id = c.Str("id");
        var p = await f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/play-script", JsonSerializer.Serialize(new { scriptId = script, variables }), NordvikFixture.Bearer(await f.Erik()));
        Assert.True(p.Code == 202, p.Body);
        Assert.Equal(202, (await f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/end", null, NordvikFixture.Bearer(await f.Erik()))).Code);
        return id;
    }

    public static async Task<Reply> Until(NordvikFixture f, string id, string status) =>
        await Eventually.Until(async () => await f.Get(Ports.Voice, $"/api/v2/calls/{id}", NordvikFixture.Bearer(await f.Erik())), r => r.Str("status") == status, 15000, $"llamada {id} en {status}");

    public static async Task<Reply> Insights(NordvikFixture f, string id) => await f.Get(Ports.Voice, $"/api/v2/calls/{id}/insights", NordvikFixture.Bearer(await f.Erik()));

    public static JsonElement Field(Reply insights, string field) => insights.Json.GetProperty("items").EnumerateArray().First(i => i.GetProperty("field").GetString() == field);

    public static async Task<Reply> Complete(NordvikFixture f, string id) => await f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/review/complete", null, NordvikFixture.Bearer(await f.Erik()));
}

[Collection("nordvik")]
public class VoiceTests
{
    private readonly NordvikFixture _f;
    public VoiceTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task V1_no_recording_consent_no_call_and_nothing_is_created()
    {
        var id = await People.Clean(_f);
        (await Calls.Start(_f, id)).ShouldBeProblem(409, "RECORDING_CONSENT_MISSING");
        var list = await _f.Get(Ports.Voice, $"/api/v2/customers/{id}/calls", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(0, list.Int("count"));
        (await Calls.Start(_f, id, await _f.Ana())).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        (await Calls.Start(_f, SeedIds.CustomerSofia)).ShouldBeProblem(403, "ACCESS_DENIED");
    }

    [Fact]
    public async Task V2_a_short_call_is_summarised_and_shredded_with_a_verifiable_receipt()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v2-consulta-corta");
        var done = await Calls.Until(_f, id, "SHREDDED");
        Assert.Equal(0, done.Int("storedChunks"));
        Assert.Equal(6, done.Int("turns"));
        var erik = await _f.Erik();
        var summary = await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/summary", NordvikFixture.Bearer(erik));
        Assert.Equal(5, summary.Json.GetProperty("lines").GetArrayLength());
        Assert.Equal("CLEAN", summary.Str("piiCheck"));
        Assert.Contains("Sin datos para el perfil", summary.Body);
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript", NordvikFixture.Bearer(erik))).ShouldBeProblem(404, "TRANSCRIPT_NOT_AVAILABLE");
        var receipt = await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/destruction-receipt", NordvikFixture.Bearer(erik));
        var r = receipt.Str("receipt"); var sig = receipt.Str("signature");
        Assert.Contains(id, r);
        Assert.True((await _f.Post(Ports.Voice, "/api/v2/receipts/verify", JsonSerializer.Serialize(new { receipt = r, signature = sig }))).Json.GetProperty("valid").GetBoolean());
        Assert.False((await _f.Post(Ports.Voice, "/api/v2/receipts/verify", JsonSerializer.Serialize(new { receipt = r.Replace("\"audioChunks\":6", "\"audioChunks\":0"), signature = sig }))).Json.GetProperty("valid").GetBoolean());
        var keys = await _f.Get(Ports.Keys, $"/api/v1/keys?subject=call:{id}", NordvikFixture.Bearer(await _f.Karin()));
        Assert.All(keys.Json.GetProperty("items").EnumerateArray(), k => Assert.Equal("SHREDDED", k.GetProperty("status").GetString()));
        await TestData.Events(_f, "voice.call.shredded", id);
    }

    [Fact]
    public async Task V3_a_dictated_national_id_never_appears_and_the_original_is_read_once()
    {
        var cust = await People.Clean(_f);
        var pnr = await TestData.NationalIdOf(_f, cust);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v3-dicta-numero", new { PNR = pnr });
        await Calls.Until(_f, id, "AWAITING_REVIEW");
        var erik = await _f.Erik();
        var t = await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript", NordvikFixture.Bearer(erik));
        Assert.DoesNotContain(pnr, t.Body);
        Assert.Contains("[NATIONAL_ID]", t.Json.GetProperty("turns")[1].GetProperty("text").GetString());
        var red = t.Json.GetProperty("redactions")[0];
        Assert.Equal("NATIONAL_ID", red.GetProperty("type").GetString());
        Assert.StartsWith("tok_nid_", red.GetProperty("vaultRef").GetString());
        var karin = await _f.Karin();
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/redactions/0/reveal", NordvikFixture.Bearer(karin))).ShouldBeProblem(400, "PURPOSE_REQUIRED");
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/redactions/0/reveal?purpose=LEGAL_HOLD", NordvikFixture.Bearer(erik))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        Assert.Equal(pnr, (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/redactions/0/reveal?purpose=LEGAL_HOLD", NordvikFixture.Bearer(karin))).Str("value"));
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/redactions/0/reveal?purpose=LEGAL_HOLD", NordvikFixture.Bearer(karin))).ShouldBeProblem(410, "ALREADY_REVEALED");
        var raw = await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript/raw?purpose=QUALITY_REVIEW", NordvikFixture.Bearer(karin));
        Assert.Contains(pnr, raw.Body);
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript/raw?purpose=QUALITY_REVIEW", NordvikFixture.Bearer(karin))).ShouldBeProblem(410, "RAW_ALREADY_READ");
        var amount = Calls.Field(await Calls.Insights(_f, id), "monthlyAmount");
        Assert.Equal("2000.00", amount.GetProperty("value").GetProperty("amount").GetString());
        Assert.False(amount.GetProperty("needsHumanReview").GetBoolean());
        Assert.Equal(4, amount.GetProperty("evidence").GetProperty("turn").GetInt32());
        Assert.Equal(202, (await Calls.Complete(_f, id)).Code);
        await Calls.Until(_f, id, "SHREDDED");
        var file = await Eventually.Until(async () => await _f.Get(Ports.AdvisorDesk, $"/api/v1/customers/{cust}/advisory-file", NordvikFixture.Bearer(await _f.Erik())),
            r => r.Json.GetProperty("facts").GetArrayLength() >= 1, 10000, "hecho en el expediente");
        var fact = file.Json.GetProperty("facts")[0];
        Assert.Equal("monthlyAmount", fact.GetProperty("field").GetString());
        Assert.Equal(id, fact.GetProperty("source").GetProperty("callId").GetString());
        Assert.DoesNotContain(pnr, file.Body);
    }

    [Fact]
    public async Task V4_suitability_data_comes_with_evidence()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v4-idoneidad");
        await Calls.Until(_f, id, "AWAITING_REVIEW");
        var ins = await Calls.Insights(_f, id);
        Assert.Equal(4, ins.Int("count"));
        Assert.Equal("RETIREMENT", Calls.Field(ins, "goal").GetProperty("value").GetString());
        Assert.Equal(15, Calls.Field(ins, "horizonYears").GetProperty("value").GetInt32());
        Assert.Equal("MEDIUM", Calls.Field(ins, "riskTolerance").GetProperty("value").GetString());
        var m = Calls.Field(ins, "monthlyAmount");
        Assert.Equal("3000.00", m.GetProperty("value").GetProperty("amount").GetString());
        Assert.Equal("Podría aportar tres mil al mes.", m.GetProperty("evidence").GetProperty("quote").GetString());
        Assert.True(m.GetProperty("evidence").GetProperty("startMs").GetInt64() > 0);
        Assert.All(ins.Json.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("confidence").GetDouble() >= 0.75));
    }

    [Fact]
    public async Task V5_health_is_redacted_and_never_reaches_the_advisory_file()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v5-salud");
        var t = await Eventually.Until(async () => await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript", NordvikFixture.Bearer(await _f.Erik())), r => r.Code == 200, 15000, "transcripción");
        Assert.DoesNotContain("diabetes", t.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[HEALTH]", t.Body);
        Assert.True(t.Json.GetProperty("healthMentioned").GetBoolean());
        var call = await Calls.Until(_f, id, "SHREDDED");
        Assert.True(call.Json.GetProperty("healthMentioned").GetBoolean());
        Assert.Contains("mención de salud", (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/summary", NordvikFixture.Bearer(await _f.Erik()))).Body);
        Assert.Equal(0, (await _f.Get(Ports.AdvisorDesk, $"/api/v1/customers/{cust}/advisory-file", NordvikFixture.Bearer(await _f.Erik()))).Json.GetProperty("facts").GetArrayLength());
    }

    [Fact]
    public async Task V6_an_order_given_by_phone_needs_the_advisor_and_is_placed_once()
    {
        var (cust, _, _) = await Flows.FundedCustomer(_f, "20000.00");
        var pf = await Flows.Portfolio(_f, cust);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v6-orden");
        await Calls.Until(_f, id, "AWAITING_REVIEW");
        var order = Calls.Field(await Calls.Insights(_f, id), "orderIntent");
        Assert.True(order.GetProperty("needsHumanReview").GetBoolean());
        Assert.Equal("SE0000000001", order.GetProperty("value").GetProperty("isin").GetString());
        Assert.Equal("10000.00", order.GetProperty("value").GetProperty("amount").GetString());
        (await Calls.Complete(_f, id)).ShouldBeProblem(409, "REVIEW_PENDING");
        (await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/insights/orderIntent/confirm", "{\"accept\":true}", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(403, "NOT_YOUR_CALL");
        var conf = await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/insights/orderIntent/confirm", JsonSerializer.Serialize(new { accept = true, value = new { portfolioId = pf } }), NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("CONFIRMED", conf.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/insights/orderIntent/confirm", "{\"accept\":true}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(409, "INSIGHT_ALREADY_REVIEWED");
        var done = await Calls.Complete(_f, id);
        Assert.Equal(202, done.Code);
        var placed = done.Json.GetProperty("orders")[0];
        Assert.True(placed.GetProperty("placed").GetBoolean());
        Assert.Equal($"call-{id}-turn-2", placed.GetProperty("idempotencyKey").GetString());
        var o = await _f.Get(Ports.Orders, $"/api/v1/orders/{placed.GetProperty("orderId").GetString()}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal("10000.00", o.Str("amount"));
        (await Calls.Complete(_f, id)).ShouldBeProblem(409, "CALL_NOT_IN_REVIEW");
    }

    [Fact]
    public async Task V7_low_confidence_waits_for_the_advisor_and_can_be_discarded()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var id = await Calls.Run(_f, cust, "v7-ruido");
        await Calls.Until(_f, id, "AWAITING_REVIEW");
        var m = Calls.Field(await Calls.Insights(_f, id), "monthlyAmount");
        Assert.True(m.GetProperty("needsHumanReview").GetBoolean());
        Assert.True(m.GetProperty("confidence").GetDouble() < 0.75);
        (await Calls.Complete(_f, id)).ShouldBeProblem(409, "REVIEW_PENDING");
        Assert.Equal("REJECTED", (await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/insights/monthlyAmount/confirm", "{\"accept\":false}", NordvikFixture.Bearer(await _f.Erik()))).Str("status"));
        Assert.Equal(202, (await Calls.Complete(_f, id)).Code);
        await Calls.Until(_f, id, "SHREDDED");
        Assert.Equal(0, (await _f.Get(Ports.AdvisorDesk, $"/api/v1/customers/{cust}/advisory-file", NordvikFixture.Bearer(await _f.Erik()))).Json.GetProperty("facts").GetArrayLength());
    }

    [Fact]
    public async Task V8_revoking_the_recording_destroys_what_was_recorded_and_the_call_goes_on()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var erik = await _f.Erik();
        var id = (await Calls.Start(_f, cust)).Str("id");
        var turns = "[{\"turn\":1,\"speaker\":\"CUSTOMER\",\"startMs\":0,\"endMs\":2000,\"text\":\"Hola\",\"confidence\":0.9},{\"turn\":1,\"speaker\":\"CUSTOMER\",\"startMs\":0,\"endMs\":2000,\"text\":\"Hola\",\"confidence\":0.9},{\"turn\":2,\"speaker\":\"CUSTOMER\",\"startMs\":2500,\"endMs\":4000,\"text\":\"No quiero que se grabe\",\"confidence\":0.9}]";
        var sent = await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/chunks", turns, NordvikFixture.Bearer(erik));
        Assert.Equal(2, sent.Int("accepted"));
        Assert.Equal(1, sent.Int("duplicates"));
        var rv = await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/revoke-recording", null, NordvikFixture.Bearer(erik));
        Assert.False(rv.Json.GetProperty("recording").GetBoolean());
        Assert.Equal(0, rv.Int("storedChunks"));
        Assert.Equal("RECORDING", rv.Str("status"));
        var after = await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/chunks", "[{\"turn\":3,\"speaker\":\"ADVISOR\",\"text\":\"De acuerdo\",\"confidence\":0.9}]", NordvikFixture.Bearer(erik));
        Assert.False(after.Json.GetProperty("recorded").GetBoolean());
        Assert.Equal("ENDED_NOT_RECORDED", (await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/end", null, NordvikFixture.Bearer(erik))).Str("status"));
        Assert.Contains("revocó", (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/destruction-receipt", NordvikFixture.Bearer(erik))).Str("receipt"));
        (await _f.Send(HttpMethod.Post, Ports.Voice, $"/api/v2/calls/{id}/chunks", "[{\"turn\":1,\"speaker\":\"ROBOT\",\"text\":\"x\"}]", NordvikFixture.Bearer(erik))).ShouldBeProblem(409, "CALL_NOT_RECORDING");
    }

    [Fact]
    public async Task V9_a_cited_clause_is_joined_with_the_mandate_in_composition_c9()
    {
        var cust = await People.Clean(_f);
        await Calls.GrantRecording(_f, cust);
        var mandate = await ContractFlows.Create(_f, cust, "ADVISORY_MANDATE");
        var id = await Calls.Run(_f, cust, "v9-clausula");
        await Calls.Until(_f, id, "AWAITING_REVIEW");
        Assert.Equal(4, Calls.Field(await Calls.Insights(_f, id), "contractClause").GetProperty("value").GetProperty("clauseNo").GetInt32());
        await Calls.Complete(_f, id);
        await Calls.Until(_f, id, "SHREDDED");
        var c9 = await _f.Get(Ports.Composition, $"/api/v2/compositions/call-summary/{id}", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Equal(200, c9.Code);
        var clause = c9.Json.GetProperty("bundle").GetProperty("citedClause");
        Assert.Equal(4, clause.GetProperty("clauseNo").GetInt32());
        Assert.Equal(mandate.Str("id"), clause.GetProperty("contractId").GetString());
        Assert.Equal(1, clause.GetProperty("citedAt").GetProperty("turn").GetInt32());
        Assert.Equal(5, c9.Json.GetProperty("bundle").GetProperty("summary").GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task V10_the_customer_gets_summaries_metadata_and_receipts_never_audio()
    {
        var ana = await _f.Ana();
        await Calls.GrantRecording(_f, SeedIds.CustomerAna, ana);
        var id = await Calls.Run(_f, SeedIds.CustomerAna, "v2-consulta-corta");
        await Calls.Until(_f, id, "SHREDDED");
        var mine = await _f.Get(Ports.Voice, $"/api/v2/customers/{SeedIds.CustomerAna}/calls", NordvikFixture.Bearer(ana));
        var item = mine.Json.GetProperty("items").EnumerateArray().First(i => i.GetProperty("callId").GetString() == id);
        Assert.False(item.GetProperty("audioAvailable").GetBoolean());
        Assert.Equal(5, item.GetProperty("summary").GetArrayLength());
        Assert.EndsWith("/destruction-receipt", item.GetProperty("destructionReceipt").GetString());
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/transcript", NordvikFixture.Bearer(ana))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
        Assert.Equal(200, (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}/destruction-receipt", NordvikFixture.Bearer(ana))).Code);
        (await _f.Get(Ports.Voice, $"/api/v2/calls/{id}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CALL_NOT_FOUND");
        (await _f.Get(Ports.Voice, $"/api/v2/customers/{SeedIds.CustomerAna}/calls", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }

    [Theory]
    [InlineData("Quiero aportar dos mil al mes", "monthlyAmount", "2000.00")]
    [InlineData("Podría invertir 2.500 cada mes", "monthlyAmount", "2500.00")]
    [InlineData("Jag vill spara tretusen i månaden", "monthlyAmount", "3000.00")]
    [InlineData("I want to save 1500 a month", "monthlyAmount", "1500.00")]
    [InlineData("Vende cinco mil en el fondo tech", "orderIntent", "5000.00")]
    public async Task Extraction_rules_in_three_languages(string text, string field, string amount)
    {
        var r = await _f.Post(Ports.Insights, "/api/v1/extract/preview", JsonSerializer.Serialize(new { text }));
        var f = r.Json.GetProperty("items").EnumerateArray().First(i => i.GetProperty("field").GetString() == field);
        Assert.Equal(amount, f.GetProperty("value").GetProperty("amount").GetString());
    }
}
