using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PpEnvelope(PpHeader? Header, JsonElement? Body);
public sealed record PpHeader(string? Sender, string? Timestamp, string? Signature);
public sealed record MaintenanceRequest(bool? On, int? RetryAfterSeconds);

/// <summary>
/// public-pension-sim · sistema externo E7 (simulado): la Autoridad de Pensiones Públicas. Habla "SOAP pasado a JSON": un sobre con cabecera
/// y cuerpo, firmado con HMAC-SHA256 sobre el JSON canónico del cuerpo (secreto compartido). Responde con otro sobre firmado.
/// Tiene ventanas de mantenimiento: 503 con la cabecera Retry-After.
/// </summary>
public sealed class PublicPensionSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "public-pension-sim", "X-external", 7707, 3, "Sistema externo (simulado)",
        "E7 Autoridad de Pensiones Públicas: sobre firmado (HMAC del cuerpo canónico), edad mínima de cobro por año de nacimiento, pensión pública estimada y mantenimiento con Retry-After.",
        DependsOn: new[] { "clock-service" });

    public const string SharedSecret = "e7-pensionsmyndigheten-shared-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private (bool On, int RetryAfter) _maintenance;

    public static int EarliestAge(int birthYear) => birthYear switch { <= 1957 => 61, <= 1960 => 62, <= 1975 => 63, <= 1990 => 64, _ => 65 };

    private static IResult Envelope(object body)
    {
        var canonical = Canonical.Of(body);
        return Results.Ok(new { header = new { sender = "PENSIONSMYNDIGHETEN-SIM", timestamp = DateTimeOffset.UtcNow.ToString("O"), signature = Canonical.HmacHex(SharedSecret, canonical) }, body });
    }

    private static IResult Fault(int status, string code, string text) =>
        Results.Json(new { fault = new { faultcode = code, faultstring = text } }, statusCode: status);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/v1/envelope", async (HttpContext http, PpEnvelope e) =>
        {
            if (_maintenance.On)
            {
                http.Response.Headers.RetryAfter = _maintenance.RetryAfter.ToString();
                return Results.Json(new { fault = new { faultcode = "Server.Maintenance", faultstring = "Scheduled maintenance, try again later", retryAfterSeconds = _maintenance.RetryAfter } }, statusCode: 503);
            }
            if (await _behaviour.Apply() is { } fail) return fail;
            if (e.Header?.Signature is null || e.Body is null) return Fault(400, "Client.BadEnvelope", "header.signature and body are required");
            if (Canonical.HmacHex(SharedSecret, Canonical.Of(e.Body.Value)) != e.Header.Signature) return Fault(401, "Client.InvalidSignature", "envelope signature does not match the body");
            var b = e.Body.Value;
            var op = b.TryGetProperty("operation", out var o) ? o.GetString() : null;
            var pnr = b.TryGetProperty("personalNumber", out var p) ? p.GetString() : null;
            var check = NationalId.Check(pnr);
            if (op != "GetPensionRights") return Fault(400, "Client.UnknownOperation", "operation must be GetPensionRights");
            if (!check.Valid) return Fault(400, "Client.InvalidPersonalNumber", check.Reason ?? "invalid");
            var birthYear = int.Parse(check.Normalized![..4]);
            var h = Canonical.Sha256Hex(check.Normalized);
            var estimated = 9000 + Convert.ToInt32(h[..4], 16) % 9000;
            return Envelope(new
            {
                operation = "GetPensionRightsResponse", personalNumber = check.Normalized, birthYear,
                earliestWithdrawalAge = EarliestAge(birthYear), guaranteePensionAge = 66,
                estimatedMonthlyPensionSek = Money.Format(estimated, 2), statementYear = clock.Today.Year,
            });
        }).WithTags("Pensiones públicas v1").WithSummary("Sobre firmado: GetPensionRights por número nacional");

        app.MapPost("/__admin/maintenance", (MaintenanceRequest b) =>
        {
            _maintenance = (b.On ?? true, Math.Clamp(b.RetryAfterSeconds ?? 120, 1, 86400));
            return Results.Ok(new { maintenance = _maintenance.On, retryAfterSeconds = _maintenance.RetryAfter });
        }).WithTags("Administración del simulador").WithSummary("Ventana de mantenimiento: 503 con Retry-After");
        app.MapPost("/__admin/sign", (JsonElement body) => Results.Ok(new { canonical = Canonical.Of(body), signature = Canonical.HmacHex(SharedSecret, Canonical.Of(body)) }))
            .WithTags("Administración del simulador").WithSummary("Ayuda: firma un cuerpo como lo haría un cliente correcto");
    }
}
