using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Nordvik.Host;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Arranca los servicios de verdad (con sus bases SQLite en una carpeta temporal) en puertos +10000.</summary>
public sealed class NordvikFixture : IAsyncLifetime
{
    public const int Offset = 10000;
    private readonly List<WebApplication> _apps = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nordvik-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task InitializeAsync()
    {
        var data = Path.Combine(Root, "data");
        var logs = Path.Combine(Root, "logs");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);
        var settings = new HostSettings(Root, data, logs, LearningMode: true, ClockUrl: $"http://localhost:{6003 + Offset}",
            ProfileName: "tests", PortOffset: Offset, ClockCacheMs: 0);
        foreach (var svc in ServiceCatalog.Create().OrderBy(s => s.Info.Name == "clock-service" ? 0 : 1))
        {
            var app = ServiceRunner.Build(svc, settings);
            await app.StartAsync();
            _apps.Add(app);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var app in _apps) await app.DisposeAsync();
        try { Directory.Delete(Root, recursive: true); } catch { /* ficheros en uso en Windows: no pasa nada */ }
    }

    public static string Url(int port, string path) => $"http://localhost:{port + Offset}{path}";

    public async Task<Reply> Get(int port, string path, Action<HttpRequestMessage>? configure = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Url(port, path));
        configure?.Invoke(req);
        return await Reply.From(await Http.SendAsync(req));
    }

    public async Task<Reply> Send(HttpMethod method, int port, string path, string? json = null, Action<HttpRequestMessage>? configure = null)
    {
        var req = new HttpRequestMessage(method, Url(port, path));
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        configure?.Invoke(req);
        return await Reply.From(await Http.SendAsync(req));
    }

    public Task<Reply> Post(int port, string path, string? json = null) => Send(HttpMethod.Post, port, path, json);
    public Task<Reply> Delete(int port, string path) => Send(HttpMethod.Delete, port, path);

    /// <summary>Congela el reloj virtual en una hora de Estocolmo concreta.</summary>
    public Task<Reply> Freeze(string isoLocal) => Post(Ports.Clock, "/api/v1/clock/freeze", $"{{\"at\":\"{isoLocal}\"}}");
    public Task<Reply> ResetClock() => Post(Ports.Clock, "/api/v1/clock/reset");
}

[CollectionDefinition("nordvik")]
public sealed class NordvikCollection : ICollectionFixture<NordvikFixture> { }

public static class Ports
{
    public const int Uuid = 6001, Sequence = 6002, Clock = 6003, Random = 6004;
    public const int Catalog = 6801, Market = 6802, Fx = 6803, Calendar = 6804;
    public static readonly int[] All = { Uuid, Sequence, Clock, Random, Catalog, Market, Fx, Calendar };
}

/// <summary>Respuesta HTTP ya leída, con el JSON parseado.</summary>
public sealed class Reply
{
    public required HttpStatusCode Status { get; init; }
    public required string Body { get; init; }
    public required HttpResponseHeaders Headers { get; init; }
    public required MediaTypeHeaderValue? ContentType { get; init; }
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
    public int Code => (int)Status;

    public static async Task<Reply> From(HttpResponseMessage r) => new()
    {
        Status = r.StatusCode,
        Body = await r.Content.ReadAsStringAsync(),
        Headers = r.Headers,
        ContentType = r.Content.Headers.ContentType,
    };

    public string Str(string prop) => Json.GetProperty(prop).GetString()!;
    public int Int(string prop) => Json.GetProperty(prop).GetInt32();
    public bool Has(string prop) => Json.TryGetProperty(prop, out _);

    /// <summary>Comprueba que es un error RFC 9457 con el código esperado.</summary>
    public Reply ShouldBeProblem(int status, string code)
    {
        Assert.Equal(status, Code);
        Assert.Equal("application/problem+json", ContentType?.MediaType);
        Assert.Equal(code, Str("code"));
        Assert.Equal(status, Int("status"));
        Assert.StartsWith("https://errors.nordvik.local/", Str("type"));
        Assert.False(string.IsNullOrWhiteSpace(Str("title")));
        Assert.False(string.IsNullOrWhiteSpace(Str("detail")));
        Assert.StartsWith("corr-", Str("correlationId"));
        return this;
    }
}
