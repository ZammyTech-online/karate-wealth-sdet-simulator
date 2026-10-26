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
        var all = ServiceCatalog.Create();
        ServiceDirectory.Offset = Offset;
        ServiceDirectory.Register(all.Select(s => s.Info));
        Nordvik.Services.ApiGateway.UseCatalog(all.Select(s => s.Info));
        foreach (var svc in all.OrderBy(s => s.Info.Name == "clock-service" ? 0 : 1))
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

    /// <summary>Pide un token con el flujo password de OAuth2 (form-urlencoded, como haría Karate).</summary>
    public async Task<string> Token(string user, string password)
    {
        var res = await Http.PostAsync(Url(Ports.Iam, "/api/v1/oauth/token"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = user, ["password"] = password,
        }));
        var r = await Reply.From(res);
        Assert.True(r.Code == 200, "login de " + user + " falló: " + r.Body);
        return r.Str("access_token");
    }

    public Task<string> Ana() => Token("cliente.ana", "Ana2026!");
    public Task<string> Lars() => Token("cliente.lars", "Lars2026!");
    public Task<string> Erik() => Token("asesor.erik", "Erik2026!");
    public Task<string> Maja() => Token("asesora.maja", "Maja2026!");
    public Task<string> Olof() => Token("operaciones.olof", "Olof2026!");
    public Task<string> Karin() => Token("compliance.karin", "Karin2026!");
    public Task<string> Admin() => Token("admin", "Admin2026!");

    public static Action<HttpRequestMessage> Bearer(string token, string? ifMatch = null) => req =>
    {
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
    };

    /// <summary>Una persona sintética nueva con número nacional válido.</summary>
    public async Task<JsonElement> NewPerson(int? age = null)
    {
        var r = await Post(Ports.Person, "/api/v1/persons", age is null ? "{\"minAge\":25,\"maxAge\":60}" : $"{{\"age\":{age}}}");
        Assert.Equal(200, r.Code);
        return r.Json.GetProperty("persons")[0];
    }

    /// <summary>Crea un cliente nuevo como asesor.erik y devuelve su JSON.</summary>
    public async Task<JsonElement> NewCustomer(string? token = null, string? externalRef = null)
    {
        var p = await NewPerson();
        var body = JsonSerializer.Serialize(new
        {
            firstName = p.GetProperty("firstName").GetString(), lastName = p.GetProperty("lastName").GetString(),
            nationalId = p.GetProperty("nationalId").GetString(), email = p.GetProperty("email").GetString(), externalRef,
        });
        var r = await Send(HttpMethod.Post, Ports.Customer, "/api/v1/customers", body, Bearer(token ?? await Erik()));
        Assert.True(r.Code == 201, r.Body);
        return r.Json;
    }

    /// <summary>Congela el reloj virtual en una hora de Estocolmo concreta.</summary>
    public Task<Reply> Freeze(string isoLocal) => Post(Ports.Clock, "/api/v1/clock/freeze", $"{{\"at\":\"{isoLocal}\"}}");
    public Task<Reply> ResetClock() => Post(Ports.Clock, "/api/v1/clock/reset");
}

[CollectionDefinition("nordvik")]
public sealed class NordvikCollection : ICollectionFixture<NordvikFixture> { }

public static class Ports
{
    public const int Uuid = 6001, Sequence = 6002, Clock = 6003, Random = 6004;
    public const int Person = 6005, Address = 6006, Iban = 6007, Isin = 6008, Reset = 6009, Fixture = 6010;
    public const int Gateway = 6100, RateLimiter = 6101, Config = 6102;
    public const int Iam = 6200, Session = 6201, Permission = 6202;
    public const int Customer = 6300, Contact = 6301, CustomerAddress = 6302, Preferences = 6303, Search = 6304;
    public const int Catalog = 6801, Market = 6802, Fx = 6803, Calendar = 6804, Factsheet = 6805, Fee = 6806, Benchmark = 6807;
    public const int Portfolio = 6900, Allocation = 6901;
    // Oleada 2
    public const int Hash = 6011, Idempotency = 6103, Tracer = 6108;
    public const int EventBus = 7400, Relay = 7401, Audit = 7402;
    public const int Eid = 6203, Mfa = 6204, StepUp = 6205, Notification = 6600, Templates = 6601;
    public const int CustodyAdapter = 7500, PriceFeedAdapter = 7501, NordPayAdapter = 7502;
    public const int NordIdSim = 7701, CustodianSim = 7704, PriceFeedSim = 7705, NordPaySim = 7706;
    public const int Payments = 7000, Instructions = 7001, BankAccounts = 7002, Ledger = 7003, Cash = 7004, Reconciliation = 7005, FeeBilling = 7006, Refunds = 7007, GlExport = 7008;
    public const int Contracts = 6700, ContractTemplates = 6701, Clauses = 6702, Signing = 6703, Vault = 6704, Render = 6705, Antivirus = 6706;
    public const int TextFactory = 6012, FileFactory = 6013, Composition = 6107, BffWeb = 6104, BffMobile = 6105, BffAdvisor = 6106, Onboarding = 6305;
    public const int Orders = 6902, Router = 6903, Executions = 6904, Positions = 6905, Valuation = 6906, Performance = 6907, Rebalancing = 6908, CorporateActions = 6909;
    // Oleada 3
    public const int PiiVault = 6500, Keys = 6501, Consents = 6502, Passports = 6503, Lineage = 6504, Redaction = 6505, Retention = 6506;
    public const int Chaos = 7600, MockControl = 7601, EventSpy = 7602, JwtForge = 6014, Secrets = 6109, TokenExchange = 6206;
    public const int Kyc = 6400, Sanctions = 6401, Pep = 6402, Risk = 6403, Monitoring = 6404, Suitability = 6405, Appropriateness = 6406;
    public const int Household = 6306, Employment = 6307, TaxResidency = 6308, Cases = 7403, Tasks = 7404, Approvals = 7405, PopulationAdapter = 7503;
    public const int PopulationSim = 7702, SanctionScreenSim = 7703;
    public static readonly int[] All =
    {
        Uuid, Sequence, Clock, Random, Person, Address, Iban, Isin, Reset, Fixture, Gateway, RateLimiter, Config, Iam, Session, Permission,
        Customer, Contact, CustomerAddress, Preferences, Search, Catalog, Market, Fx, Calendar, Factsheet, Fee, Benchmark, Portfolio, Allocation,
        Hash, Idempotency, Tracer, EventBus, Relay, Audit, Eid, Mfa, StepUp, Notification, Templates, CustodyAdapter, PriceFeedAdapter, NordPayAdapter,
        NordIdSim, CustodianSim, PriceFeedSim, NordPaySim,
        Payments, Instructions, BankAccounts, Ledger, Cash, Reconciliation, FeeBilling, Refunds, GlExport,
        Orders, Router, Executions, Positions, Valuation, Performance, Rebalancing, CorporateActions,
        Contracts, ContractTemplates, Clauses, Signing, Vault, Render, Antivirus, TextFactory, FileFactory, Composition, BffWeb, BffMobile, BffAdvisor, Onboarding,
        PiiVault, Keys, Consents, Passports, Lineage, Redaction, Retention, Chaos, MockControl, EventSpy, JwtForge, Secrets, TokenExchange,
        Kyc, Sanctions, Pep, Risk, Monitoring, Suitability, Appropriateness, Household, Employment, TaxResidency, Cases, Tasks, Approvals, PopulationAdapter, PopulationSim, SanctionScreenSim,
    };
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
