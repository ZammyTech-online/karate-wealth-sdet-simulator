using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class AdvisorDirectoryTests
{
    private readonly NordvikFixture _f;
    public AdvisorDirectoryTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Directory_books_and_who_is_my_advisor()
    {
        var ana = await _f.Ana();
        var esg = await _f.Get(Ports.AdvisorDirectory, "/api/v1/advisors?specialty=esg", NordvikFixture.Bearer(ana));
        Assert.Equal(new[] { "asesora.maja" }, esg.Json.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("username").GetString()));
        Assert.Equal(2, (await _f.Get(Ports.AdvisorDirectory, "/api/v1/advisors", NordvikFixture.Bearer(ana))).Int("count"));
        var mine = await _f.Get(Ports.AdvisorDirectory, $"/api/v1/customers/{SeedIds.CustomerAna}/advisor", NordvikFixture.Bearer(ana));
        Assert.Equal(SeedIds.AdvisorErik, mine.Str("id"));
        Assert.Equal("09:00", mine.Json.GetProperty("workingHours").GetProperty("start").GetString());
        (await _f.Get(Ports.AdvisorDirectory, $"/api/v1/customers/{SeedIds.CustomerLars}/advisor", NordvikFixture.Bearer(ana))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        var book = await _f.Get(Ports.AdvisorDirectory, $"/api/v1/advisors/{SeedIds.AdvisorErik}/customers", NordvikFixture.Bearer(await _f.Erik()));
        Assert.Contains(SeedIds.CustomerAna, book.Body);
        (await _f.Get(Ports.AdvisorDirectory, $"/api/v1/advisors/{SeedIds.AdvisorErik}/customers", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(403, "NOT_YOUR_BOOK");
        (await _f.Get(Ports.AdvisorDirectory, "/api/v1/advisors/nadie", NordvikFixture.Bearer(ana))).ShouldBeProblem(404, "ADVISOR_NOT_FOUND");
        Assert.Equal(2, (await _f.Get(Ports.AdvisorDirectory, "/api/v1/offices")).Json.GetProperty("items").GetArrayLength());
    }
}

[Collection("nordvik")]
public class MeetingTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public MeetingTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Book(string start, string? advisorId = null, string? token = null) =>
        await _f.Send(HttpMethod.Post, Ports.Meetings, "/api/v1/meetings", JsonSerializer.Serialize(new { advisorId = advisorId ?? SeedIds.AdvisorErik, customerId = SeedIds.CustomerAna, start, channel = "VIDEO", topic = "Revisión anual" }),
            NordvikFixture.Bearer(token ?? await _f.Ana()));

    [Fact]
    public async Task Slots_bookings_and_the_rules_of_the_agenda()
    {
        await _f.Freeze("2026-11-03T08:00:00");
        var slots = await _f.Get(Ports.Meetings, $"/api/v1/advisors/{SeedIds.AdvisorErik}/slots?date=2026-11-04", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal(8, slots.Int("freeCount"));
        Assert.Equal("2026-11-04T09:00", slots.Json.GetProperty("slots")[0].GetProperty("start").GetString());
        var m = await Book("2026-11-04T10:00");
        Assert.Equal(201, m.Code);
        Assert.Equal("BOOKED", m.Str("status"));
        Assert.StartsWith("2026-11-03T10:00", m.Str("reminderAt"));
        (await Book("2026-11-04T10:00", token: await _f.Erik())).ShouldBeProblem(409, "SLOT_TAKEN");
        Assert.Equal(7, (await _f.Get(Ports.Meetings, $"/api/v1/advisors/{SeedIds.AdvisorErik}/slots?date=2026-11-04", NordvikFixture.Bearer(await _f.Ana()))).Int("freeCount"));
        (await Book("2026-11-04T10:30")).ShouldBeProblem(422, "OUTSIDE_WORKING_HOURS");
        (await Book("2026-11-04T18:00")).ShouldBeProblem(422, "OUTSIDE_WORKING_HOURS");
        (await Book("2026-11-07T10:00")).ShouldBeProblem(422, "NOT_A_WORKING_DAY");
        (await Book("2026-11-02T10:00")).ShouldBeProblem(422, "MEETING_IN_THE_PAST");
        (await Book("2026-11-04T11:00", SeedIds.AdvisorMaja)).ShouldBeProblem(422, "NOT_YOUR_ADVISOR");
        (await Book("4 de noviembre")).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Get(Ports.Meetings, $"/api/v1/meetings/{m.Str("id")}", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "MEETING_NOT_FOUND");
        Assert.Equal(1, (await _f.Get(Ports.Meetings, $"/api/v1/customers/{SeedIds.CustomerAna}/meetings", NordvikFixture.Bearer(await _f.Erik()))).Json.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("status").GetString() == "BOOKED" && x.GetProperty("id").GetString() == m.Str("id")));
        var cancel = await _f.Send(HttpMethod.Post, Ports.Meetings, $"/api/v1/meetings/{m.Str("id")}/cancel", "{\"reason\":\"viaje\"}", NordvikFixture.Bearer(await _f.Ana()));
        Assert.Equal("CANCELLED", cancel.Str("status"));
        Assert.False(cancel.Json.GetProperty("lateCancellation").GetBoolean());
        Assert.Equal(8, (await _f.Get(Ports.Meetings, $"/api/v1/advisors/{SeedIds.AdvisorErik}/slots?date=2026-11-04", NordvikFixture.Bearer(await _f.Ana()))).Int("freeCount"));
    }

    [Fact]
    public async Task Late_cancellations_reminders_and_absences()
    {
        await _f.Freeze("2026-11-10T08:00:00");
        var soon = await Book("2026-11-10T15:00");
        Assert.True((await _f.Send(HttpMethod.Post, Ports.Meetings, $"/api/v1/meetings/{soon.Str("id")}/cancel", "{}", NordvikFixture.Bearer(await _f.Ana()))).Json.GetProperty("lateCancellation").GetBoolean());
        var next = await Book("2026-11-11T09:00");
        Assert.Equal(201, next.Code);
        await _f.Freeze("2026-11-10T09:00:01");
        var run = await _f.Send(HttpMethod.Post, Ports.Meetings, "/api/v1/reminders/run", null, NordvikFixture.Bearer(await _f.Olof()));
        Assert.Contains(next.Str("id"), run.Body);
        Assert.DoesNotContain(next.Str("id"), (await _f.Send(HttpMethod.Post, Ports.Meetings, "/api/v1/reminders/run", null, NordvikFixture.Bearer(await _f.Olof()))).Body);
        var email = (await _f.Get(Ports.Customer, $"/api/v1/internal/customers/{SeedIds.CustomerAna}")).Str("email");
        var box = await _f.Get(Ports.Notification, $"/api/v1/mailbox?to={Uri.EscapeDataString(email)}");
        Assert.Contains("2026-11-11 09:00", box.Body);
        var erik = await _f.Erik();
        await _f.Send(HttpMethod.Post, Ports.AdvisorDirectory, $"/api/v1/advisors/{SeedIds.AdvisorErik}/absences", "{\"from\":\"2026-11-12\",\"to\":\"2026-11-13\",\"reason\":\"formación\"}", NordvikFixture.Bearer(erik));
        var none = await _f.Get(Ports.Meetings, $"/api/v1/advisors/{SeedIds.AdvisorErik}/slots?date=2026-11-12", NordvikFixture.Bearer(erik));
        Assert.Equal(0, none.Int("freeCount"));
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDirectory, $"/api/v1/advisors/{SeedIds.AdvisorErik}/absences", "{\"from\":\"2026-11-12\",\"to\":\"2026-11-13\"}", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(403, "NOT_YOURSELF");
        (await Book("2026-11-12T10:00")).ShouldBeProblem(422, "OUTSIDE_WORKING_HOURS");
    }
}

[Collection("nordvik")]
public class SecureMessagingTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public SecureMessagingTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    private async Task<Reply> Send(string conv, object body, string token) =>
        await _f.Send(HttpMethod.Post, Ports.Messaging, $"/api/v1/conversations/{conv}/messages", JsonSerializer.Serialize(body), NordvikFixture.Bearer(token));

    [Fact]
    public async Task Read_once_messages_are_destroyed_when_opened()
    {
        var ana = await _f.Ana(); var erik = await _f.Erik();
        var c = await _f.Send(HttpMethod.Post, Ports.Messaging, "/api/v1/conversations", "{\"subject\":\"Cambio de cuenta\"}", NordvikFixture.Bearer(ana));
        Assert.Equal(201, c.Code);
        Assert.Equal(SeedIds.AdvisorErik, c.Str("advisorId"));
        var conv = c.Str("id");
        Assert.Equal(201, (await Send(conv, new { text = "Hola Ana, ¿en qué cuenta quieres recibir el dinero?" }, erik)).Code);
        var iban = TestData.Iban("6060");
        (await Send(conv, new { text = $"Es la {iban}" }, ana)).ShouldBeProblem(422, "PII_NOT_ALLOWED");
        var secret = await Send(conv, new { text = $"Es la {iban}", readOnce = true }, ana);
        var list = await _f.Get(Ports.Messaging, $"/api/v1/conversations/{conv}/messages", NordvikFixture.Bearer(erik));
        Assert.DoesNotContain(iban, list.Body);
        var hidden = list.Json.GetProperty("items")[1];
        Assert.True(hidden.GetProperty("hidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("text").ValueKind);
        Assert.Contains("en qué cuenta", list.Json.GetProperty("items")[0].GetProperty("text").GetString());
        var open = $"/api/v1/conversations/{conv}/messages/{secret.Str("id")}/open";
        (await _f.Send(HttpMethod.Post, Ports.Messaging, open, null, NordvikFixture.Bearer(ana))).ShouldBeProblem(403, "SENDER_CANNOT_OPEN");
        Assert.Equal($"Es la {iban}", (await _f.Send(HttpMethod.Post, Ports.Messaging, open, null, NordvikFixture.Bearer(erik))).Str("text"));
        (await _f.Send(HttpMethod.Post, Ports.Messaging, open, null, NordvikFixture.Bearer(erik))).ShouldBeProblem(410, "MESSAGE_DESTROYED");
        Assert.Equal("DESTROYED", (await _f.Get(Ports.Messaging, $"/api/v1/conversations/{conv}/messages", NordvikFixture.Bearer(ana))).Json.GetProperty("items")[1].GetProperty("status").GetString());
        await TestData.Events(_f, "message.destroyed", secret.Str("id"));
        (await _f.Get(Ports.Messaging, $"/api/v1/conversations/{conv}/messages", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CONVERSATION_NOT_FOUND");
        (await _f.Get(Ports.Messaging, $"/api/v1/conversations/{conv}/messages", NordvikFixture.Bearer(await _f.Maja()))).ShouldBeProblem(404, "CONVERSATION_NOT_FOUND");
    }

    [Fact]
    public async Task Messages_can_expire_unread()
    {
        await _f.Freeze($"{TestData.Tuesday}T10:00:00");
        var ana = await _f.Ana(); var erik = await _f.Erik();
        var conv = (await _f.Send(HttpMethod.Post, Ports.Messaging, "/api/v1/conversations", JsonSerializer.Serialize(new { customerId = SeedIds.CustomerAna, subject = "Código" }), NordvikFixture.Bearer(erik))).Str("id");
        var m = await Send(conv, new { text = "Tu código provisional es 4411", readOnce = true, expiresInSeconds = 60 }, erik);
        await _f.Freeze($"{TestData.Tuesday}T10:02:00");
        ana = await _f.Ana();
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Messaging, $"/api/v1/conversations/{conv}/messages", NordvikFixture.Bearer(ana))).Json.GetProperty("items")[0].GetProperty("status").GetString());
        (await _f.Send(HttpMethod.Post, Ports.Messaging, $"/api/v1/conversations/{conv}/messages/{m.Str("id")}/open", null, NordvikFixture.Bearer(ana))).ShouldBeProblem(410, "MESSAGE_EXPIRED");
        (await Send(conv, new { text = "x", expiresInSeconds = 5 }, erik = await _f.Erik())).ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.True((await _f.Get(Ports.Messaging, $"/api/v1/customers/{SeedIds.CustomerAna}/conversations", NordvikFixture.Bearer(ana))).Int("count") >= 1);
    }
}

[Collection("nordvik")]
public class AdvisorDeskTests
{
    private readonly NordvikFixture _f;
    public AdvisorDeskTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Only_suitable_products_can_be_recommended_and_the_customer_decides()
    {
        var erik = await _f.Erik();
        var noProfile = await People.Clean(_f);
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/customers/{noProfile}/recommendations", "{\"isin\":\"SE0000000101\",\"amount\":\"1000.00\",\"rationale\":\"x\"}", NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(409, "ASSESSMENT_REQUIRED");
        var file = await _f.Get(Ports.AdvisorDesk, $"/api/v1/customers/{noProfile}/advisory-file", NordvikFixture.Bearer(erik));
        Assert.Contains("SUITABILITY_ASSESSMENT", file.Json.GetProperty("gaps").GetRawText());
        await _f.Send(HttpMethod.Post, Ports.Suitability, $"/api/v1/customers/{SeedIds.CustomerAna}/assessments",
            "{\"answers\":{\"Q1\":\"C\",\"Q2\":\"B\",\"Q3\":\"B\",\"Q4\":\"B\",\"Q5\":\"C\",\"Q6\":\"B\",\"Q7\":\"A\"}}", NordvikFixture.Bearer(erik));
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/customers/{SeedIds.CustomerAna}/recommendations", "{\"isin\":\"SE0000000006\",\"amount\":\"1000.00\",\"rationale\":\"tech\"}", NordvikFixture.Bearer(erik)))
            .ShouldBeProblem(422, "RECOMMENDATION_NOT_SUITABLE");
        var rec = await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/customers/{SeedIds.CustomerAna}/recommendations", "{\"isin\":\"SE0000000101\",\"amount\":\"5000.00\",\"rationale\":\"Liquidez a corto plazo\"}", NordvikFixture.Bearer(erik));
        Assert.Equal(201, rec.Code);
        Assert.Equal("PROPOSED", rec.Str("status"));
        Assert.Equal("SUITABLE", rec.Json.GetProperty("suitability").GetProperty("result").GetString());
        var ana = await _f.Ana();
        var mine = await _f.Get(Ports.AdvisorDesk, $"/api/v1/customers/{SeedIds.CustomerAna}/recommendations", NordvikFixture.Bearer(ana));
        Assert.Contains(rec.Str("id"), mine.Body);
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/recommendations/{rec.Str("id")}/accept", null, NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "RECOMMENDATION_NOT_FOUND");
        var ok = await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/recommendations/{rec.Str("id")}/accept", null, NordvikFixture.Bearer(ana));
        Assert.Equal("ACCEPTED", ok.Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/recommendations/{rec.Str("id")}/decline", null, NordvikFixture.Bearer(ana))).ShouldBeProblem(409, "RECOMMENDATION_CLOSED");
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/customers/{SeedIds.CustomerAna}/recommendations", "{\"isin\":\"SE0000000101\"}", NordvikFixture.Bearer(erik))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await _f.Send(HttpMethod.Post, Ports.AdvisorDesk, $"/api/v1/customers/{SeedIds.CustomerAna}/recommendations", "{\"isin\":\"SE0000000101\",\"amount\":\"1.00\",\"rationale\":\"x\"}", NordvikFixture.Bearer(ana))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }
}
