using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class UuidServiceTests
{
    private readonly NordvikFixture _f;
    public UuidServiceTests(NordvikFixture f) => _f = f;
    private static readonly Regex V4 = new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
    private static readonly Regex V7 = new("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

    [Fact]
    public async Task Default_is_one_v4_uuid()
    {
        var r = await _f.Get(Ports.Uuid, "/api/v1/uuids");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(4, r.Int("version"));
        Assert.Equal(1, r.Int("count"));
        Assert.Matches(V4, r.Json.GetProperty("uuids")[0].GetString());
    }

    [Fact]
    public async Task Batch_of_v7_is_sorted_and_unique()
    {
        var r = await _f.Get(Ports.Uuid, "/api/v1/uuids?version=7&count=500");
        var ids = r.Json.GetProperty("uuids").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(500, ids.Count);
        Assert.Equal(500, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(V7, id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(-5)]
    public async Task Count_out_of_range_is_rejected(int count) =>
        (await _f.Get(Ports.Uuid, $"/api/v1/uuids?count={count}")).ShouldBeProblem(400, "COUNT_OUT_OF_RANGE");

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task Unsupported_versions_are_rejected(int version) =>
        (await _f.Get(Ports.Uuid, $"/api/v1/uuids?version={version}")).ShouldBeProblem(400, "UNSUPPORTED_UUID_VERSION");

    [Fact]
    public async Task Non_numeric_count_is_a_bad_request()
    {
        var r = await _f.Get(Ports.Uuid, "/api/v1/uuids?count=muchos");
        Assert.Equal(400, r.Code);
    }

    [Fact]
    public async Task One_returns_a_single_uuid()
    {
        var r = await _f.Get(Ports.Uuid, "/api/v1/uuids/one?version=7");
        Assert.Matches(V7, r.Str("uuid"));
    }

    [Fact]
    public async Task Validate_recognises_a_v4()
    {
        var r = await _f.Post(Ports.Uuid, "/api/v1/uuids/validate", "{\"value\":\"3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90\"}");
        Assert.True(r.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(4, r.Int("version"));
        Assert.Equal("RFC_9562", r.Str("variant"));
    }

    [Fact]
    public async Task Validate_reports_bad_format_without_error()
    {
        var r = await _f.Post(Ports.Uuid, "/api/v1/uuids/validate", "{\"value\":\"no-soy-un-uuid\"}");
        Assert.Equal(200, r.Code);
        Assert.False(r.Json.GetProperty("valid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(r.Str("reason")));
    }

    [Fact]
    public async Task Validate_without_value_is_a_validation_problem()
    {
        var r = await _f.Post(Ports.Uuid, "/api/v1/uuids/validate", "{}");
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal("value", r.Json.GetProperty("errors")[0].GetProperty("field").GetString());
    }
}

[Collection("nordvik")]
public class SequenceServiceTests
{
    private readonly NordvikFixture _f;
    public SequenceServiceTests(NordvikFixture f) => _f = f;
    private static string NewName() => "seq-" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Create_returns_201_with_location()
    {
        var name = NewName();
        var r = await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\",\"prefix\":\"CLI\",\"padding\":5}}");
        Assert.Equal(201, r.Code);
        Assert.Equal($"/api/v1/sequences/{name}", r.Headers.Location?.OriginalString);
        Assert.Equal(0, r.Int("current"));
    }

    [Fact]
    public async Task Next_returns_padded_values_with_prefix()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\",\"prefix\":\"RUN\",\"padding\":4}}");
        var r = await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next?count=3");
        var values = r.Json.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Equal(new[] { "RUN-0001", "RUN-0002", "RUN-0003" }, values);
        Assert.Equal(3, r.Int("current"));
        var again = await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next");
        Assert.Equal("RUN-0004", again.Json.GetProperty("values")[0].GetString());
    }

    [Fact]
    public async Task Without_prefix_values_are_just_numbers()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        var r = await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next");
        Assert.Equal("0001", r.Json.GetProperty("values")[0].GetString());
    }

    [Fact]
    public async Task Duplicate_name_is_a_conflict()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        (await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}")).ShouldBeProblem(409, "SEQUENCE_ALREADY_EXISTS");
    }

    [Theory]
    [InlineData("{\"name\":\"Mayusculas\"}", "name")]
    [InlineData("{\"name\":\"a\"}", "name")]
    [InlineData("{}", "name")]
    [InlineData("{\"name\":\"valido\",\"padding\":0}", "padding")]
    [InlineData("{\"name\":\"valido\",\"padding\":13}", "padding")]
    [InlineData("{\"name\":\"valido\",\"prefix\":\"PREFIJO-DEMASIADO-LARGO-X\"}", "prefix")]
    public async Task Invalid_sequences_list_the_wrong_field(string json, string field)
    {
        var r = await _f.Post(Ports.Sequence, "/api/v1/sequences", json);
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Contains(r.Json.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
    }

    [Fact]
    public async Task Next_on_unknown_sequence_is_404_with_hint()
    {
        var r = await _f.Post(Ports.Sequence, "/api/v1/sequences/no-existe-nunca/next");
        r.ShouldBeProblem(404, "SEQUENCE_NOT_FOUND");
        Assert.Contains("POST /api/v1/sequences", r.Str("hint"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Next_count_out_of_range(int count)
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        (await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next?count={count}")).ShouldBeProblem(400, "COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Reset_and_delete_lifecycle()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next?count=10");
        Assert.Equal(204, (await _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/reset")).Code);
        Assert.Equal(0, (await _f.Get(Ports.Sequence, $"/api/v1/sequences/{name}")).Int("current"));
        Assert.Equal(204, (await _f.Delete(Ports.Sequence, $"/api/v1/sequences/{name}")).Code);
        (await _f.Get(Ports.Sequence, $"/api/v1/sequences/{name}")).ShouldBeProblem(404, "SEQUENCE_NOT_FOUND");
        (await _f.Delete(Ports.Sequence, $"/api/v1/sequences/{name}")).ShouldBeProblem(404, "SEQUENCE_NOT_FOUND");
    }

    [Fact]
    public async Task Concurrent_next_calls_never_repeat_a_value()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        var tasks = Enumerable.Range(0, 40).Select(_ => _f.Post(Ports.Sequence, $"/api/v1/sequences/{name}/next?count=5"));
        var replies = await Task.WhenAll(tasks);
        var all = replies.SelectMany(r => r.Json.GetProperty("values").EnumerateArray().Select(v => v.GetString())).ToList();
        Assert.Equal(200, all.Count);
        Assert.Equal(200, all.Distinct().Count());
    }

    [Fact]
    public async Task List_includes_created_sequences()
    {
        var name = NewName();
        await _f.Post(Ports.Sequence, "/api/v1/sequences", $"{{\"name\":\"{name}\"}}");
        var r = await _f.Get(Ports.Sequence, "/api/v1/sequences");
        Assert.Contains(r.Json.EnumerateArray(), s => s.GetProperty("name").GetString() == name);
    }
}

[Collection("nordvik")]
public class ClockServiceTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public ClockServiceTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Now_is_real_time_by_default()
    {
        var r = await _f.Get(Ports.Clock, "/api/v1/clock/now");
        Assert.Equal("REAL", r.Str("mode"));
        Assert.Equal("Europe/Stockholm", r.Str("timezone"));
        var utc = r.Json.GetProperty("utc").GetDateTimeOffset();
        Assert.True((utc - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromSeconds(5));
        Assert.Matches(@"[+-]\d{2}:\d{2}$", r.Str("now"));
    }

    [Fact]
    public async Task Travel_moves_the_clock_and_keeps_it_running()
    {
        var r = await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"to\":\"2026-12-24T09:00:00+01:00\"}");
        Assert.Equal("OFFSET", r.Str("mode"));
        Assert.Equal("2026-12-24", r.Str("date"));
        Assert.StartsWith("09:00", r.Str("time"));
    }

    [Fact]
    public async Task Travel_by_offset_seconds()
    {
        var r = await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":86400}");
        var utc = r.Json.GetProperty("utc").GetDateTimeOffset();
        Assert.True((utc - DateTimeOffset.UtcNow.AddDays(1)).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Freeze_stops_time()
    {
        await _f.Freeze("2026-10-26T14:59:00+01:00");
        var a = await _f.Get(Ports.Clock, "/api/v1/clock/now");
        await Task.Delay(1200);
        var b = await _f.Get(Ports.Clock, "/api/v1/clock/now");
        Assert.Equal("FROZEN", a.Str("mode"));
        Assert.Equal(a.Str("now"), b.Str("now"));
        Assert.Equal("2026-10-26T14:59:00.000+01:00", a.Str("now"));
    }

    [Fact]
    public async Task Advance_adds_time_to_a_frozen_clock()
    {
        await _f.Freeze("2026-10-26T14:59:00+01:00");
        var r = await _f.Post(Ports.Clock, "/api/v1/clock/advance", "{\"minutes\":2}");
        Assert.Equal("15:01:00", r.Str("time"));
        var back = await _f.Post(Ports.Clock, "/api/v1/clock/advance", "{\"days\":-1}");
        Assert.Equal("2026-10-25", back.Str("date"));
    }

    [Fact]
    public async Task Daylight_saving_is_handled_when_freezing_in_summer()
    {
        var r = await _f.Freeze("2026-07-01T12:00:00+02:00");
        Assert.Equal("2026-07-01T12:00:00.000+02:00", r.Str("now"));
    }

    [Fact]
    public async Task Reset_returns_to_real_time()
    {
        await _f.Freeze("2030-01-01T00:00:00+01:00");
        var r = await _f.ResetClock();
        Assert.Equal("REAL", r.Str("mode"));
        Assert.Equal(0, r.Int("offsetSeconds"));
    }

    [Fact]
    public async Task Travel_without_target_is_rejected() =>
        (await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Advance_by_zero_is_rejected() =>
        (await _f.Post(Ports.Clock, "/api/v1/clock/advance", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
}

[Collection("nordvik")]
public class RandomSeedServiceTests
{
    private readonly NordvikFixture _f;
    public RandomSeedServiceTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Same_seed_gives_same_values()
    {
        var a = await _f.Get(Ports.Random, "/api/v1/random/ints?seed=42&count=20&min=1&max=1000");
        var b = await _f.Get(Ports.Random, "/api/v1/random/ints?seed=42&count=20&min=1&max=1000");
        Assert.Equal(a.Json.GetProperty("values").GetRawText(), b.Json.GetProperty("values").GetRawText());
    }

    [Fact]
    public async Task Different_seeds_give_different_values()
    {
        var a = await _f.Get(Ports.Random, "/api/v1/random/ints?seed=1&count=20&min=1&max=1000000");
        var b = await _f.Get(Ports.Random, "/api/v1/random/ints?seed=2&count=20&min=1&max=1000000");
        Assert.NotEqual(a.Json.GetProperty("values").GetRawText(), b.Json.GetProperty("values").GetRawText());
    }

    [Fact]
    public async Task Ints_respect_min_and_max_inclusive()
    {
        var r = await _f.Get(Ports.Random, "/api/v1/random/ints?seed=7&count=1000&min=1&max=3");
        var values = r.Json.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()).ToList();
        Assert.All(values, v => Assert.InRange(v, 1, 3));
        Assert.Contains(3, values);
    }

    [Fact]
    public async Task Missing_seed_is_explained() =>
        (await _f.Get(Ports.Random, "/api/v1/random/ints")).ShouldBeProblem(400, "SEED_REQUIRED");

    [Fact]
    public async Task Min_must_be_lower_than_max() =>
        (await _f.Get(Ports.Random, "/api/v1/random/ints?seed=1&min=5&max=5")).ShouldBeProblem(400, "INVALID_RANGE");

    [Fact]
    public async Task Decimals_have_the_requested_scale()
    {
        var r = await _f.Get(Ports.Random, "/api/v1/random/decimals?seed=3&count=50&min=100.00&max=200.00&scale=2");
        Assert.All(r.Json.GetProperty("values").EnumerateArray(), v => Assert.Matches(@"^1\d{2}\.\d{2}$|^200\.00$", v.GetString()));
    }

    [Fact]
    public async Task Decimals_with_comma_are_rejected() =>
        (await _f.Get(Ports.Random, "/api/v1/random/decimals?seed=3&min=1,5&max=9")).ShouldBeProblem(400, "INVALID_RANGE");

    [Fact]
    public async Task Pick_unique_returns_distinct_items()
    {
        var r = await _f.Get(Ports.Random, "/api/v1/random/pick?seed=9&items=a,b,c,d&count=4&unique=true");
        var values = r.Json.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Equal(4, values.Distinct().Count());
    }

    [Fact]
    public async Task Pick_unique_with_too_few_items_is_unprocessable() =>
        (await _f.Get(Ports.Random, "/api/v1/random/pick?seed=9&items=a,b&count=3&unique=true")).ShouldBeProblem(422, "NOT_ENOUGH_ITEMS");

    [Fact]
    public async Task Pick_without_items_is_rejected() =>
        (await _f.Get(Ports.Random, "/api/v1/random/pick?seed=9")).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Create_seed_returns_201_and_appears_in_list()
    {
        var r = await _f.Post(Ports.Random, "/api/v1/seeds", "{\"label\":\"mi-fallo\"}");
        Assert.Equal(201, r.Code);
        var value = r.Int("value");
        var list = await _f.Get(Ports.Random, "/api/v1/seeds");
        Assert.Contains(list.Json.EnumerateArray(), s => s.GetProperty("value").GetInt32() == value && s.GetProperty("label").GetString() == "mi-fallo");
    }
}
