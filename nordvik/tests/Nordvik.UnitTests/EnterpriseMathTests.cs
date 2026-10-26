using System.Text.Json;
using Nordvik.Services;
using Xunit;

namespace Nordvik.UnitTests;

public class EnterpriseMathTests
{
    private static SchemaDef S(string[] req, params (string, string)[] props) => new(req, props.ToDictionary(p => p.Item1, p => p.Item2));

    [Fact]
    public void Backward_compatibility_rules()
    {
        var v1 = S(new[] { "id" }, ("id", "string"), ("amount", "string"));
        Assert.Empty(SchemaRegistry.Incompatibilities(v1, S(new[] { "id" }, ("id", "string"), ("amount", "string"), ("note", "string")), "BACKWARD"));
        Assert.Contains("campo obligatorio nuevo: note", SchemaRegistry.Incompatibilities(v1, S(new[] { "id", "note" }, ("id", "string"), ("amount", "string"), ("note", "string")), "BACKWARD"));
        Assert.Contains("campo eliminado: amount", SchemaRegistry.Incompatibilities(v1, S(new[] { "id" }, ("id", "string")), "BACKWARD"));
        Assert.Contains("un campo obligatorio deja de serlo: id", SchemaRegistry.Incompatibilities(v1, S(Array.Empty<string>(), ("id", "string"), ("amount", "string")), "FORWARD"));
        Assert.Empty(SchemaRegistry.Incompatibilities(v1, S(new[] { "x" }, ("x", "integer")), "NONE"));
    }

    [Fact]
    public void Payload_validation()
    {
        var s = S(new[] { "id", "n" }, ("id", "string"), ("n", "integer"), ("ok", "boolean"));
        Assert.Empty(SchemaRegistry.Validate(s, JsonDocument.Parse("{\"id\":\"a\",\"n\":3,\"ok\":true}").RootElement));
        Assert.Equal(new[] { "falta n", "id debería ser string", "ok debería ser boolean" }, SchemaRegistry.Validate(s, JsonDocument.Parse("{\"id\":1,\"ok\":\"yes\"}").RootElement));
        Assert.Contains("n debería ser integer", SchemaRegistry.Validate(s, JsonDocument.Parse("{\"id\":\"a\",\"n\":1.5}").RootElement));
    }

    [Fact]
    public void Flags_evaluate_in_order()
    {
        Assert.Equal((false, "OFF"), FeatureFlags.Evaluate(false, 100, new[] { "a" }, Array.Empty<string>(), "a", "f"));
        Assert.Equal((false, "DENYLIST"), FeatureFlags.Evaluate(true, 100, Array.Empty<string>(), new[] { "a" }, "a", "f"));
        Assert.Equal((true, "ALLOWLIST"), FeatureFlags.Evaluate(true, 0, new[] { "a" }, Array.Empty<string>(), "a", "f"));
        Assert.Equal((true, "ALL"), FeatureFlags.Evaluate(true, 100, Array.Empty<string>(), Array.Empty<string>(), null, "f"));
        Assert.Equal(FeatureFlags.Bucket("f", "c1"), FeatureFlags.Bucket("f", "c1"));
        Assert.InRange(FeatureFlags.Bucket("f", "c1"), 0, 99);
    }

    [Theory]
    [InlineData("2026-10-20", 2, "2026-10-22")]
    [InlineData("2026-10-23", 2, "2026-10-27")]
    [InlineData("2026-12-23", 2, "2026-12-29")]
    [InlineData("2026-12-30", 1, "2027-01-04")]
    public void Complaint_deadlines_skip_weekends_and_holidays(string from, int days, string due) =>
        Assert.Equal(DateOnly.Parse(due), ComplaintsService.Due(DateOnly.Parse(from), days));

    [Fact]
    public void Fixed_width_lines()
    {
        var line = LegacyMainframeBridge.BalanceLine("pp_1", 1234.5m, new DateOnly(2014, 12, 31));
        Assert.Equal(80, line.Length);
        Assert.Equal("BLpp_1                000000000123450" + "20141231", line[..45]);
        var p = LegacyMainframeBridge.ParseBalance(line);
        Assert.Equal(("0000", "pp_1", 1234.50m, new DateOnly(2014, 12, 31)), (p.Rc, p.PlanId, p.Balance!.Value, p.Date!.Value));
        Assert.Equal("0008", LegacyMainframeBridge.ParseBalance("BL").Rc);
        Assert.Equal("0012", LegacyMainframeBridge.ParseBalance("ZZ".PadRight(80)).Rc);
        Assert.Equal("0004", LegacyMainframeBridge.ParseBalance(LegacyMainframeBridge.BalanceLine("pp_1", 0m, new DateOnly(2014, 1, 1))).Rc);
    }

    [Fact]
    public void Legacy_plans_only_for_people_born_before_1975_and_deterministic()
    {
        var a = LegacyPensionsSoap.PlansFor("19600505-1234");
        Assert.InRange(a.Count, 1, 2);
        Assert.Equal(a, LegacyPensionsSoap.PlansFor("19600505-1234"));
        Assert.All(a, p => Assert.InRange(p.StartYear, 1995, 2014));
        Assert.Empty(LegacyPensionsSoap.PlansFor("19800505-1234"));
    }

    [Theory]
    [InlineData(new[] { "passed", "failed", "passed", "failed" }, 1.0)]
    [InlineData(new[] { "passed", "passed", "failed", "failed" }, 0.333)]
    [InlineData(new[] { "passed", "skipped", "passed" }, 0.0)]
    [InlineData(new[] { "failed" }, 0.0)]
    public void Flip_rate(string[] statuses, double expected) => Assert.Equal(expected, TestInsights.FlipRate(statuses));

    [Theory]
    [InlineData("/api/v1/funds/{isin}", "/api/v1/funds/SE0000000001", true)]
    [InlineData("/api/v1/funds/{isin}", "/api/v1/funds/SE0000000001/fees", false)]
    [InlineData("/api/v1/funds", "/api/v1/FUNDS?x=1", true)]
    public void Path_templates(string template, string path, bool ok) => Assert.Equal(ok, ContractCheck.PathMatches(template, path));

    [Fact]
    public void Schema_drift_is_reported_with_paths()
    {
        var doc = JsonDocument.Parse("{\"components\":{\"schemas\":{\"F\":{\"type\":\"object\",\"properties\":{\"isin\":{\"type\":\"string\"},\"sri\":{\"type\":\"integer\"}}}}}}").RootElement;
        var schema = JsonDocument.Parse("{\"$ref\":\"#/components/schemas/F\"}").RootElement;
        var diffs = new List<object>();
        ContractCheck.Compare(schema, JsonDocument.Parse("{\"instrumentId\":\"x\",\"sri\":\"4\"}").RootElement, doc, "$", diffs);
        var text = JsonSerializer.Serialize(diffs);
        Assert.Contains("MISSING_FIELD", text);
        Assert.Contains("$.instrumentId", text);
        Assert.Contains("TYPE_MISMATCH", text);
    }

    [Theory]
    [InlineData("4999999.99", 30, "PREMIUM")]
    [InlineData("0", 18, "YOUNG_SAVER")]
    [InlineData("5000000", 18, "PRIVATE_BANKING")]
    public void Segments(string assets, int age, string segment) =>
        Assert.Equal(segment, SegmentationService.Classify(decimal.Parse(assets, System.Globalization.CultureInfo.InvariantCulture), age).Segment);

    [Theory]
    [InlineData("ACTIVE", "2026-01-01", "2026-12-31", "2026-06-01", "ACTIVE")]
    [InlineData("ACTIVE", "2026-01-01", "2026-12-31", "2027-01-01", "EXPIRED")]
    [InlineData("ACTIVE", "2027-01-01", "2027-12-31", "2026-06-01", "NOT_YET_VALID")]
    [InlineData("REVOKED", "2026-01-01", "2026-12-31", "2026-06-01", "REVOKED")]
    public void Delegation_effective_status(string stored, string from, string to, string today, string expected) =>
        Assert.Equal(expected, DelegationService.Effective(stored, DateOnly.Parse(from), DateOnly.Parse(to), DateOnly.Parse(today)));

    [Fact]
    public void Archive_chain_hash_depends_on_everything()
    {
        var h = ArchiveService.ChainHash(1, "a", "0", "t");
        Assert.NotEqual(h, ArchiveService.ChainHash(2, "a", "0", "t"));
        Assert.NotEqual(h, ArchiveService.ChainHash(1, "b", "0", "t"));
        Assert.Equal(h, ArchiveService.ChainHash(1, "a", "0", "t"));
    }
}
