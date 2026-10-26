using System.Net;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class PlatformTests
{
    private readonly NordvikFixture _f;
    public PlatformTests(NordvikFixture f) => _f = f;

    public static IEnumerable<object[]> AllPorts => Ports.All.Select(p => new object[] { p });

    [Theory, MemberData(nameof(AllPorts))]
    public async Task Every_service_reports_health_up(int port)
    {
        var r = await _f.Get(port, "/health");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("UP", r.Str("status"));
    }

    [Theory, MemberData(nameof(AllPorts))]
    public async Task Every_service_publishes_its_openapi(int port)
    {
        var r = await _f.Get(port, "/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.StartsWith("3.", r.Json.GetProperty("openapi").GetString());
        Assert.True(r.Json.GetProperty("paths").EnumerateObject().Count() >= 3);
    }

    [Theory, MemberData(nameof(AllPorts))]
    public async Task Every_service_describes_itself_in_info(int port)
    {
        var r = await _f.Get(port, "/info");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(port + NordvikFixture.Offset, r.Int("port"));
        Assert.InRange(r.Int("wave"), 0, 2);
        Assert.False(string.IsNullOrWhiteSpace(r.Str("team")));
    }

    [Theory, MemberData(nameof(AllPorts))]
    public async Task Unknown_routes_return_a_problem_with_hint(int port)
    {
        var r = await _f.Get(port, "/no-existe");
        if (port == Ports.Gateway) { r.ShouldBeProblem(404, "SERVICE_NOT_FOUND"); return; }
        r.ShouldBeProblem(404, "ROUTE_NOT_FOUND");
        Assert.Contains("/swagger", r.Str("hint"));
    }

    [Theory, MemberData(nameof(AllPorts))]
    public async Task Every_response_names_the_service_and_its_version(int port)
    {
        var r = await _f.Get(port, "/health");
        Assert.True(r.Headers.Contains("X-Service"));
        Assert.Equal(r.Str("version"), r.Headers.GetValues("X-Service-Version").Single());
        Assert.Matches(@"^\d+\.\d+\.\d+$", r.Str("version"));
    }

    [Fact]
    public async Task Correlation_id_sent_by_the_client_is_echoed()
    {
        var r = await _f.Get(Ports.Catalog, "/health", req => req.Headers.Add("X-Correlation-Id", "mi-test-123"));
        Assert.Equal("mi-test-123", r.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_missing()
    {
        var r = await _f.Get(Ports.Catalog, "/health");
        Assert.StartsWith("corr-", r.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Correlation_id_appears_inside_problems()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds/xx", req => req.Headers.Add("X-Correlation-Id", "trazable-1"));
        Assert.Equal("trazable-1", r.Str("correlationId"));
    }

    [Fact]
    public async Task Learning_mode_can_be_switched_off_per_request()
    {
        var on = await _f.Get(Ports.Catalog, "/api/v1/funds/xx");
        var off = await _f.Get(Ports.Catalog, "/api/v1/funds/xx", req => req.Headers.Add("X-Learning-Mode", "false"));
        Assert.True(on.Has("hint"));
        Assert.False(off.Has("hint"));
    }

    [Fact]
    public async Task Problems_are_served_as_utf8()
    {
        var r = await _f.Get(Ports.Catalog, "/api/v1/funds/xx");
        Assert.Equal("utf-8", r.ContentType?.CharSet);
        Assert.Contains("Petición no válida", r.Body);
    }

    [Fact]
    public async Task Malformed_json_body_is_a_400_problem()
    {
        var r = await _f.Post(Ports.Sequence, "/api/v1/sequences", "{ esto no es json");
        r.ShouldBeProblem(400, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Each_request_is_logged_with_its_correlation_id()
    {
        await _f.Get(Ports.Fx, "/api/v1/currencies", req => req.Headers.Add("X-Correlation-Id", "log-check-42"));
        var file = Path.Combine(_f.Root, "logs", "fx-rates.log");
        var found = false;
        for (var i = 0; i < 20 && !found; i++)
        {
            found = File.Exists(file) && (await File.ReadAllTextAsync(file)).Contains("log-check-42");
            if (!found) await Task.Delay(100);
        }
        Assert.True(found);
    }
}
