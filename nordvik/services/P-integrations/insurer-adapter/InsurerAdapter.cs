using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// insurer-adapter: cliente OAuth2 (client credentials) de la aseguradora (E11). Guarda el token y lo reutiliza hasta 30 s antes de que caduque;
/// si la aseguradora responde 401 lo renueva UNA vez y repite la llamada (un segundo 401 ya es un error). Reenvía el cuerpo tal cual.
/// </summary>
public sealed class InsurerAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "insurer-adapter", "P-integrations", 7508, 4, "Integraciones",
        "Aseguradora (E11) con OAuth2 client credentials: token cacheado, renovación ante 401 con un solo reintento; suscripción, pólizas y siniestros.",
        DependsOn: new[] { "insurer-sim", "secrets-service", "clock-service" });

    private string? _token; private DateTimeOffset _expires;
    private long _fetched, _renewedAfter401;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string Base() => ServiceDirectory.UrlOf("insurer-sim");

        async Task<string?> Token(HttpContext http, bool force)
        {
            await _gate.WaitAsync();
            try
            {
                if (!force && _token is not null && _expires > clock.UtcNow.AddSeconds(30)) return _token;
                var secret = (await Downstream.Send(http, "secrets-service", HttpMethod.Get, "/api/v1/secrets/insurer-client-secret", null, forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value") ?? "";
                var r = await Downstream.SendUrl(HttpMethod.Post, Base() + "/oauth/token",
                    new FormBody("grant_type=client_credentials&client_id=nordvik&client_secret=" + Uri.EscapeDataString(secret)), null, TimeSpan.FromSeconds(8));
                if (!r.Ok) { _token = null; return null; }
                _token = r.Str("access_token");
                _expires = clock.UtcNow.AddSeconds(r.Json.GetProperty("expires_in").GetInt32());
                Interlocked.Increment(ref _fetched);
                return _token;
            }
            finally { _gate.Release(); }
        }

        async Task<IResult> Call(HttpContext http, HttpMethod method, string path)
        {
            string? body = null;
            if (method == HttpMethod.Post) { using var rd = new StreamReader(http.Request.Body); body = await rd.ReadToEndAsync(); }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await Token(http, force: attempt > 0);
                if (token is null) return Problems.Of(http, 502, "INSURER_AUTH_FAILED", "La aseguradora no nos da token", "¿Rotó insurer-client-secret?", null);
                var r = await Downstream.SendUrl(method, Base() + path, body, new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }, TimeSpan.FromSeconds(10));
                if (r.Status == 401 && attempt == 0) { Interlocked.Increment(ref _renewedAfter401); continue; }
                if (r.Status == 401) return Problems.Of(http, 502, "INSURER_AUTH_FAILED", "Token rechazado dos veces", "La aseguradora rechazó también el token nuevo.", null);
                if (r.Ok) return Results.Content(r.Raw, "application/json", System.Text.Encoding.UTF8, r.Status);
                var code = r.Body is { ValueKind: System.Text.Json.JsonValueKind.Object } j && j.TryGetProperty("error", out var e) ? e.GetString() : "INSURER_ERROR";
                return r.Status is 404 or 422 or 400
                    ? Problems.Of(http, r.Status == 400 ? 422 : r.Status, code!, "Rechazado por la aseguradora", r.Raw, null)
                    : Problems.Of(http, 502, "INSURER_ERROR", "Error de la aseguradora", $"Respondió {r.Status}.", null);
            }
            return Problems.Of(http, 502, "INSURER_ERROR", "Error de la aseguradora", "Sin respuesta válida.", null);
        }

        // Ojo: (HttpContext http) => Call(...) encajaría con RequestDelegate y la respuesta se perdería (200 vacío). Con bloque y return, no.
        app.MapPost("/api/v1/internal/underwriting", async (HttpContext http) => { return await Call(http, HttpMethod.Post, "/v1/underwriting"); }).WithTags("Interno").WithSummary("(interno) Suscripción médica");
        app.MapPost("/api/v1/internal/policies", async (HttpContext http) => { return await Call(http, HttpMethod.Post, "/v1/policies"); }).WithTags("Interno").WithSummary("(interno) Emitir póliza");
        app.MapPost("/api/v1/internal/claims", async (HttpContext http) => { return await Call(http, HttpMethod.Post, "/v1/claims"); }).WithTags("Interno").WithSummary("(interno) Abrir siniestro");
        app.MapGet("/api/v1/internal/claims/{id}", (HttpContext http, string id) => Call(http, HttpMethod.Get, $"/v1/claims/{id}")).WithTags("Interno").WithSummary("(interno) Estado de un siniestro");

        app.MapGet("/api/v1/token-status", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            return Results.Ok(new { hasToken = _token is not null, expiresAt = _token is null ? null : _expires.ToString("O"), tokensFetched = Interlocked.Read(ref _fetched), renewedAfter401 = Interlocked.Read(ref _renewedAfter401) });
        }).WithTags("Operación").WithSummary("Estado del token OAuth2 (sin mostrarlo)");
    }
}
