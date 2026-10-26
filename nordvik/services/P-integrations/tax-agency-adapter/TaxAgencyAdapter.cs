using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TaxFilingRequest(int? Year, string? Kind, JsonElement? Lines);

/// <summary>
/// tax-agency-adapter: habla con la Agencia Tributaria (E8) con la API key de secrets-service. Cachea la tabla de retención por persona y año
/// (la Agencia la publica una vez al año) y entrega los ficheros anuales de control. Traduce los errores de la Agencia a problemas de Nordvik.
/// </summary>
public sealed class TaxAgencyAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "tax-agency-adapter", "P-integrations", 7507, 4, "Integraciones",
        "Agencia Tributaria (E8) con API key: tablas de retención cacheadas por persona y año y entrega de ficheros anuales con su acuse.",
        DependsOn: new[] { "tax-agency-sim", "secrets-service", "clock-service" });

    public const string Reporter = "556677-8899";
    private readonly ConcurrentDictionary<string, JsonElement> _cache = new();
    private long _hits, _misses;

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string Base() => ServiceDirectory.UrlOf("tax-agency-sim");
        async Task<Dictionary<string, string>> Key(HttpContext http) => new()
        {
            ["X-Api-Key"] = (await Downstream.Send(http, "secrets-service", HttpMethod.Get, "/api/v1/secrets/tax-agency-api-key", null, forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value") ?? "",
        };

        IResult Translate(HttpContext http, DownstreamReply r)
        {
            var code = r.Body is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("error", out var e) ? e.GetString() : null;
            var msg = r.Body is { ValueKind: JsonValueKind.Object } j2 && j2.TryGetProperty("message", out var m) ? m.GetString() : r.Raw;
            return r.Status switch
            {
                401 => Problems.Of(http, 502, "TAX_AGENCY_AUTH_FAILED", "La Agencia rechazó la API key", "¿Rotó tax-agency-api-key?", null),
                409 => Problems.Conflict(http, code ?? "DUPLICATE_FILING", msg ?? "Fichero duplicado.", "Para cambiar un año ya presentado se envía una corrección."),
                400 or 422 => Problems.Unprocessable(http, code ?? "FILING_REJECTED", "La Agencia rechazó el fichero: " + msg, null),
                _ => Problems.Of(http, 502, "TAX_AGENCY_ERROR", "Error de la Agencia", $"Respondió {r.Status}.", null),
            };
        }

        app.MapGet("/api/v1/internal/tax-tables/{personalNumber}", async (HttpContext http, string personalNumber, int? year) =>
        {
            var y = year ?? clock.Today.Year;
            var k = $"{personalNumber}/{y}";
            if (_cache.TryGetValue(k, out var hit)) { Interlocked.Increment(ref _hits); return Results.Ok(new { table = hit, cached = true }); }
            Interlocked.Increment(ref _misses);
            var r = await Downstream.SendUrl(HttpMethod.Get, Base() + $"/v1/tax-tables/{personalNumber}?year={y}", null, await Key(http), TimeSpan.FromSeconds(8));
            if (!r.Ok) return Translate(http, r);
            _cache[k] = r.Json;
            return Results.Ok(new { table = r.Json, cached = false });
        }).WithTags("Interno").WithSummary("(interno) Tabla de retención de una persona (cacheada por año)");

        app.MapPost("/api/v1/internal/filings", async (HttpContext http, TaxFilingRequest b) =>
        {
            var r = await Downstream.SendUrl(HttpMethod.Post, Base() + "/v1/filings",
                new { reporter = Reporter, year = b.Year, kind = b.Kind ?? "ORIGINAL", lines = b.Lines }, await Key(http), TimeSpan.FromSeconds(20));
            return r.Ok ? Results.Ok(r.Json) : Translate(http, r);
        }).WithTags("Interno").WithSummary("(interno) Entregar un fichero anual de control");

        app.MapGet("/api/v1/cache", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            return Results.Ok(new { entries = _cache.Count, hits = Interlocked.Read(ref _hits), misses = Interlocked.Read(ref _misses) });
        }).WithTags("Operación").WithSummary("Estado de la caché de tablas");

        app.MapDelete("/api/v1/cache", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            var n = _cache.Count; _cache.Clear();
            return Results.Ok(new { removed = n });
        }).WithTags("Operación").WithSummary("Vaciar la caché de tablas");
    }
}
