using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// public-pension-adapter: Nordvik ↔ Autoridad de Pensiones Públicas (E7). Firma el sobre con el secreto de secrets-service, verifica
/// la firma de la respuesta (si no cuadra, no se fía) y traduce el mantenimiento a 503 PUBLIC_PENSION_MAINTENANCE con Retry-After.
/// </summary>
public sealed class PublicPensionAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "public-pension-adapter", "P-integrations", 7504, 3, "Integraciones",
        "Consulta a la Autoridad de Pensiones Públicas (E7): sobre firmado, verificación de la firma de la respuesta y mantenimiento con Retry-After.",
        DependsOn: new[] { "public-pension-sim", "secrets-service", "customer-registry", "permission-service", "clock-service" });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/customers/{customerId}/public-pension", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var secret = (await Downstream.Send(http, "secrets-service", HttpMethod.Get, "/api/v1/secrets/public-pension-hmac", null, forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value") ?? "";
            var body = new { operation = "GetPensionRights", personalNumber = c.Str("nationalId") };
            var canonical = Canonical.Of(body);
            DownstreamReply r;
            try
            {
                r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("public-pension-sim") + "/v1/envelope",
                    new { header = new { sender = "NORDVIK", timestamp = DateTimeOffset.UtcNow.ToString("O"), signature = Canonical.HmacHex(secret, canonical) }, body }, null, TimeSpan.FromSeconds(8));
            }
            catch (DependencyUnavailableException) { return Problems.Of(http, 504, "PUBLIC_PENSION_TIMEOUT", "Sin respuesta", "La Autoridad de Pensiones no respondió.", null); }
            if (r.Status == 503 && r.Body is { } fb && fb.TryGetProperty("fault", out _))
            {
                var retry = r.Json.GetProperty("fault").GetProperty("faultstring").GetString();
                var retryAfter = r.Json.GetProperty("fault").TryGetProperty("retryAfterSeconds", out var ra) ? ra.GetInt32() : 120;
                http.Response.Headers.RetryAfter = retryAfter.ToString();
                return Problems.Of(http, 503, "PUBLIC_PENSION_MAINTENANCE", "En mantenimiento", "La Autoridad de Pensiones está en mantenimiento: " + retry, "Respeta Retry-After antes de reintentar.");
            }
            if (r.Status == 401) return Problems.Of(http, 502, "PUBLIC_PENSION_SIGNATURE_REJECTED", "Firma rechazada", "La Autoridad rechazó nuestra firma: ¿rotó el secreto public-pension-hmac?", null);
            if (!r.Ok) return Problems.Of(http, 502, "PUBLIC_PENSION_ERROR", "Error de la Autoridad", $"Respondió {r.Status}: {r.Raw}", null);
            var resp = r.Json.GetProperty("body");
            if (Canonical.HmacHex(secret, Canonical.Of(resp)) != r.Json.GetProperty("header").GetProperty("signature").GetString())
                return Problems.Of(http, 502, "PUBLIC_PENSION_BAD_SIGNATURE", "Respuesta no fiable", "La firma de la respuesta no cuadra: se descarta.", null);
            var age = (int)c.Json.GetProperty("age").GetInt64();
            var earliest = resp.GetProperty("earliestWithdrawalAge").GetInt32();
            return Results.Ok(new
            {
                customerId, earliestWithdrawalAge = earliest, guaranteePensionAge = resp.GetProperty("guaranteePensionAge").GetInt32(), currentAge = age,
                eligibleForPublicPension = age >= earliest, estimatedMonthlyPension = new { amount = resp.GetProperty("estimatedMonthlyPensionSek").GetString(), currency = "SEK" },
                statementYear = resp.GetProperty("statementYear").GetInt32(), source = "Autoridad de Pensiones Públicas (E7)",
            });
        }).WithTags("Pensión pública").WithSummary("Derechos de pensión pública del cliente (edad mínima y estimación)");
    }
}
