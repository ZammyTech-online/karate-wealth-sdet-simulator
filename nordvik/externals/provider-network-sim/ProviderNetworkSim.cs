using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PolicySeed(string? ProviderCode, string? PolicyNumber, string? PersonalNumber, string? Value, int? DelayDays, decimal? DriftPct);
public sealed record TamperRequest(bool? On);

/// <summary>
/// provider-network-sim · sistema externo E10 (simulado): la red de otras gestoras de pensiones. Nordvik pide la valoración de una póliza
/// para traspasarla; la gestora tarda de 1 a 3 días (VIRTUALES) en contestar. Firma MUTUA: Nordvik firma lo que envía (X-Signature, HMAC del
/// cuerpo exacto o de "GET {ruta}") y la gestora devuelve { data, signature } con el HMAC del JSON canónico de data.
/// Las pólizas P + 6 cifras existen siempre (valor determinista); con /__admin/policies se fijan valor, demora y desviación del traspaso.
/// </summary>
public sealed class ProviderNetworkSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "provider-network-sim", "X-external", 7710, 3, "Sistema externo (simulado)",
        "E10 Red de gestoras: valoraciones de pólizas que tardan 1–3 días virtuales, traspaso del dinero y firma HMAC mutua en las dos direcciones.",
        DependsOn: new[] { "clock-service" });

    public const string SharedSecret = "e10-provider-network-2026";
    public static readonly (string Code, string Name)[] Providers = { ("FJORDLIV", "Fjordliv Pension AB"), ("SUNDPENSION", "Sundpension Försäkring"), ("NORRBO", "Norrbo Livförsäkring") };

    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, PolicySeed> _policies = new();
    private readonly ConcurrentDictionary<string, (string Provider, string Policy, string Pnr, decimal Value, decimal Drift, DateTimeOffset ReadyAt, DateTimeOffset At)> _valuations = new();
    private readonly ConcurrentDictionary<string, object> _transfers = new();
    private readonly ConcurrentQueue<object> _log = new();
    private bool _tamper;

    private IResult Signed(object data, int status = 200)
    {
        var canonical = Canonical.Of(data);
        var sig = Canonical.HmacHex(SharedSecret, canonical);
        if (_tamper) sig = new string(sig.Reverse().ToArray());
        return Results.Json(new { data, signature = sig }, statusCode: status);
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new { error = code, message }, statusCode: status);

    private PolicySeed? Policy(string provider, string policy)
    {
        if (_policies.TryGetValue($"{provider}/{policy}", out var p)) return p;
        if (!System.Text.RegularExpressions.Regex.IsMatch(policy, @"^P\d{6}$")) return null;
        var h = Canonical.Sha256Hex(provider + policy);
        return new PolicySeed(provider, policy, null, Money.Format(50000 + Convert.ToInt32(h[..5], 16) % 500000, 2), 1 + Convert.ToInt32(h[5..6], 16) % 3, 0);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<(string? Raw, IResult? Fail)> Verified(HttpContext http, string what)
        {
            string raw = what;
            if (http.Request.Method == "POST") { using var r = new StreamReader(http.Request.Body, Encoding.UTF8); raw = await r.ReadToEndAsync(); }
            var sig = http.Request.Headers["X-Signature"].FirstOrDefault();
            _log.Enqueue(new { method = http.Request.Method, path = http.Request.Path.Value, signed = sig is not null, at = clock.UtcNow.ToString("O") });
            if (sig is null || !string.Equals(sig, Canonical.HmacHex(SharedSecret, raw), StringComparison.OrdinalIgnoreCase))
                return (null, Error(401, "INVALID_SIGNATURE", "X-Signature must be the HMAC-SHA256 of the exact body (or of 'GET {path}')"));
            return (raw, null);
        }

        app.MapGet("/v1/providers", () => Results.Ok(new { items = Providers.Select(p => new { code = p.Code, name = p.Name }) })).WithTags("Gestoras v1").WithSummary("Gestoras de la red");

        app.MapPost("/v1/valuations", async (HttpContext http) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var (raw, bad) = await Verified(http, "");
            if (bad is not null) return bad;
            var b = JsonDocument.Parse(raw!).RootElement;
            string? S(string n) => b.TryGetProperty(n, out var v) ? v.GetString() : null;
            var provider = S("providerCode")?.ToUpperInvariant() ?? "";
            if (Providers.All(p => p.Code != provider)) return Error(404, "PROVIDER_NOT_FOUND", $"unknown provider {provider}");
            var pol = Policy(provider, S("policyNumber") ?? "");
            if (pol is null) return Error(404, "POLICY_NOT_FOUND", "no such policy at this provider");
            var check = NationalId.Check(S("personalNumber"));
            if (!check.Valid) return Error(400, "INVALID_PERSONAL_NUMBER", "personalNumber is not valid");
            if (pol.PersonalNumber is not null && NationalId.Check(pol.PersonalNumber).Normalized != check.Normalized) return Error(422, "POLICY_HOLDER_MISMATCH", "the policy belongs to someone else");
            var id = "pv_" + Guid.NewGuid().ToString("N")[..12];
            var ready = clock.UtcNow.AddDays(pol.DelayDays ?? 1);
            _valuations[id] = (provider, pol.PolicyNumber!, check.Normalized!, decimal.Parse(pol.Value!, Money.Inv), pol.DriftPct ?? 0, ready, clock.UtcNow);
            return Signed(new { valuationId = id, providerCode = provider, policyNumber = pol.PolicyNumber, status = "PENDING", readyAt = ready.ToString("O") }, 202);
        }).WithTags("Gestoras v1").WithSummary("Pedir la valoración de una póliza (firmado; tarda 1–3 días virtuales)");

        app.MapGet("/v1/valuations/{id}", async (HttpContext http, string id) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var (_, bad) = await Verified(http, $"GET /v1/valuations/{id}");
            if (bad is not null) return bad;
            if (!_valuations.TryGetValue(id, out var v)) return Error(404, "VALUATION_NOT_FOUND", id);
            var ready = clock.UtcNow >= v.ReadyAt;
            return Signed(new { valuationId = id, providerCode = v.Provider, policyNumber = v.Policy, status = ready ? "READY" : "PENDING", readyAt = v.ReadyAt.ToString("O"),
                                value = ready ? Money.Format(v.Value, 2) : null, currency = "SEK", valuedAt = ready ? v.ReadyAt.ToString("O") : null });
        }).WithTags("Gestoras v1").WithSummary("Estado de una valoración (firmado)");

        app.MapPost("/v1/transfers", async (HttpContext http) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var (raw, bad) = await Verified(http, "");
            if (bad is not null) return bad;
            var vid = JsonDocument.Parse(raw!).RootElement.TryGetProperty("valuationId", out var x) ? x.GetString() ?? "" : "";
            if (!_valuations.TryGetValue(vid, out var v)) return Error(404, "VALUATION_NOT_FOUND", vid);
            if (clock.UtcNow < v.ReadyAt) return Error(409, "VALUATION_NOT_READY", "the valuation is not ready yet");
            var t = _transfers.GetOrAdd(vid, _ => new { transferId = "pt_" + Guid.NewGuid().ToString("N")[..12], valuationId = vid, amount = Money.Format(Math.Round(v.Value * (1 + v.Drift / 100), 2), 2), currency = "SEK", status = "RELEASED" });
            return Signed(t);
        }).WithTags("Gestoras v1").WithSummary("Liberar el dinero de una póliza valorada (idempotente por valoración)");

        app.MapPost("/__admin/policies", (PolicySeed b) =>
        {
            if (b.ProviderCode is null || b.PolicyNumber is null || !Money.TryParse(b.Value, out _)) return Error(400, "BAD_REQUEST", "providerCode, policyNumber and value are required");
            var p = b with { ProviderCode = b.ProviderCode.ToUpperInvariant(), DelayDays = Math.Clamp(b.DelayDays ?? 1, 0, 10) };
            _policies[$"{p.ProviderCode}/{p.PolicyNumber}"] = p;
            return Results.Ok(p);
        }).WithTags("Administración del simulador").WithSummary("Fijar una póliza: valor, días de demora y desviación (%) del traspaso");
        app.MapPost("/__admin/tamper", (TamperRequest b) => { _tamper = b.On ?? true; return Results.Ok(new { tamper = _tamper }); })
            .WithTags("Administración del simulador").WithSummary("Firmar mal las respuestas (Nordvik debe rechazarlas)");
        app.MapGet("/__admin/requests", () => Results.Ok(new { items = _log.ToArray() })).WithTags("Administración del simulador").WithSummary("Peticiones recibidas");
    }
}
