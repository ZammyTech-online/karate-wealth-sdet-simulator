using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ProviderValuationRequest(string? ProviderCode, string? PolicyNumber, string? PersonalNumber);
public sealed record ProviderTransferRequest(string? ExternalId);

/// <summary>
/// provider-network-adapter: habla con la red de gestoras (E10). Firma cada petición (HMAC del cuerpo exacto, o de "GET {ruta}") y
/// VERIFICA la firma de cada respuesta: si no cuadra, la respuesta no se cree (502 INVALID_PROVIDER_SIGNATURE). Nunca devuelve el número
/// nacional: lo saca de customer-registry quien lo llama.
/// </summary>
public sealed class ProviderNetworkAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "provider-network-adapter", "P-integrations", 7506, 3, "Integraciones",
        "Red de gestoras (E10) con firma HMAC mutua: valoraciones asíncronas de pólizas y liberación del dinero en traspasos; rechaza respuestas mal firmadas.",
        DependsOn: new[] { "provider-network-sim", "secrets-service", "clock-service" });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string Base() => ServiceDirectory.UrlOf("provider-network-sim");

        async Task<string> Secret(HttpContext http) =>
            (await Downstream.Send(http, "secrets-service", HttpMethod.Get, "/api/v1/secrets/provider-network-hmac", null, forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value") ?? "";

        async Task<IResult> Call(HttpContext http, HttpMethod method, string path, object? body)
        {
            var secret = await Secret(http);
            var raw = body is null ? null : JsonSerializer.Serialize(body);
            var sig = Canonical.HmacHex(secret, raw ?? $"GET {path}");
            var r = await Downstream.SendUrl(method, Base() + path, raw, new Dictionary<string, string> { ["X-Signature"] = sig }, TimeSpan.FromSeconds(10));
            if (r.Body is not { ValueKind: JsonValueKind.Object } j) return Problems.Of(http, 502, "PROVIDER_ERROR", "Respuesta no válida", $"La gestora respondió {r.Status} sin JSON.", null);
            if (!r.Ok)
            {
                var code = j.TryGetProperty("error", out var e) ? e.GetString() : "PROVIDER_ERROR";
                return r.Status switch
                {
                    404 => Problems.NotFound(http, code!, j.TryGetProperty("message", out var m) ? m.GetString()! : "No encontrado en la gestora.", null),
                    409 or 422 => Problems.Of(http, r.Status, code!, "Rechazado por la gestora", j.GetProperty("message").GetString()!, null),
                    401 => Problems.Of(http, 502, "PROVIDER_REJECTED_SIGNATURE", "La gestora rechazó nuestra firma", "¿Secreto provider-network-hmac correcto?", null),
                    _ => Problems.Of(http, 502, "PROVIDER_ERROR", "Error de la gestora", $"La gestora respondió {r.Status}.", null),
                };
            }
            var data = j.GetProperty("data");
            if (!j.TryGetProperty("signature", out var s) || !string.Equals(s.GetString(), Canonical.HmacHex(secret, Canonical.Of(data)), StringComparison.OrdinalIgnoreCase))
                return Problems.Of(http, 502, "INVALID_PROVIDER_SIGNATURE", "Firma de la gestora no válida", "La firma de la respuesta no corresponde a su contenido: no se acepta.", "Firma mutua: se verifica también lo que llega.");
            return Results.Ok(data);
        }

        app.MapGet("/api/v1/providers", async (HttpContext http) =>
        {
            var r = await Downstream.SendUrl(HttpMethod.Get, Base() + "/v1/providers", null, null, TimeSpan.FromSeconds(5));
            return r.Ok ? Results.Ok(r.Json) : Problems.Of(http, 502, "PROVIDER_ERROR", "Red no disponible", $"La red respondió {r.Status}.", null);
        }).WithTags("Gestoras").WithSummary("Gestoras de la red E10");

        app.MapPost("/api/v1/internal/valuations", (HttpContext http, ProviderValuationRequest b) =>
            Call(http, HttpMethod.Post, "/v1/valuations", new { providerCode = b.ProviderCode, policyNumber = b.PolicyNumber, personalNumber = b.PersonalNumber }))
            .WithTags("Interno").WithSummary("(interno) Pedir una valoración (firmado y verificado)");

        app.MapGet("/api/v1/internal/valuations/{externalId}", (HttpContext http, string externalId) => Call(http, HttpMethod.Get, $"/v1/valuations/{externalId}", null))
            .WithTags("Interno").WithSummary("(interno) Estado de una valoración (firma verificada)");

        app.MapPost("/api/v1/internal/transfers", (HttpContext http, ProviderTransferRequest b) => Call(http, HttpMethod.Post, "/v1/transfers", new { valuationId = b.ExternalId }))
            .WithTags("Interno").WithSummary("(interno) Liberar el dinero de una póliza valorada");
    }
}
