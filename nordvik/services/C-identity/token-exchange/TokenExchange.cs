using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ExchangeRequest(string? SubjectToken, string? Audience, string? Scope, int? ExpiresInSeconds);

/// <summary>
/// token-exchange (RFC 8693): cambia el token de un usuario por uno más pequeño para llamar a UN servicio, con solo los scopes
/// que ese servicio necesita y que el usuario ya tenía, y una vida más corta (60 s por defecto). Nunca amplía permisos.
/// </summary>
public sealed class TokenExchange : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "token-exchange", "C-identity", 6206, 3, "Identidad",
        "Intercambio de tokens (RFC 8693): token del usuario → token de menos permisos para un servicio concreto, más corto y sin poder ampliar scopes.",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "token.exchanged" });

    /// <summary>Qué scopes tiene sentido pedir para cada destinatario.</summary>
    public static readonly Dictionary<string, string[]> AudienceScopes = new()
    {
        ["portfolio-service"] = new[] { "portfolios:read" },
        ["order-management"] = new[] { "orders:read", "orders:write" },
        ["payments-gateway"] = new[] { "payments:read", "payments:write" },
        ["customer-registry"] = new[] { "customers:read", "profile:read" },
        ["contract-service"] = new[] { "contracts:read" },
        ["document-vault"] = new[] { "contracts:read" },
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/token-exchange", (HttpContext http, ExchangeRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.SubjectToken)) errors.Add(new FieldError("subjectToken", "obligatorio: el token del usuario"));
            if (string.IsNullOrWhiteSpace(b.Audience) || !AudienceScopes.ContainsKey(b.Audience)) errors.Add(new FieldError("audience", "uno de " + string.Join(", ", AudienceScopes.Keys)));
            if (b.ExpiresInSeconds is < 10 or > 300) errors.Add(new FieldError("expiresInSeconds", "entre 10 y 300"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Intercambio no válido.",
                "Ejemplo: { \"subjectToken\": \"eyJ...\", \"audience\": \"portfolio-service\", \"scope\": \"portfolios:read\" }", errors);
            var reason = Auth.Validate(b.SubjectToken!, clock.UtcNow, out var subject);
            if (reason is not null)
                return Problems.Of(http, 401, reason == "expired" ? "TOKEN_EXPIRED" : "INVALID_TOKEN", "No autenticado", "El subjectToken no es válido: " + reason + ".", "Un token caducado no se puede cambiar por otro: vuelve a hacer login.");
            if (subject!.Role == "Service") return Problems.Of(http, 403, "SUBJECT_NOT_EXCHANGEABLE", "Prohibido", "Un token de servicio no se intercambia: ya es de máquina.", null);
            var allowedForAudience = AudienceScopes[b.Audience!];
            var requested = (b.Scope ?? string.Join(' ', allowedForAudience)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var notForAudience = requested.Where(s => !allowedForAudience.Contains(s)).ToList();
            if (notForAudience.Count > 0)
                return Problems.BadRequest(http, "INVALID_SCOPE", $"{b.Audience} no acepta: {string.Join(", ", notForAudience)}.", "Scopes para este destinatario: " + string.Join(" ", allowedForAudience));
            var notHeld = requested.Where(s => !subject.HasScope(s)).ToList();
            if (notHeld.Count > 0)
                return Problems.Of(http, 403, "SCOPE_ESCALATION", "Prohibido", $"No puedes obtener scopes que tu token no tiene: {string.Join(", ", notHeld)}.", "El intercambio solo reduce permisos, nunca los amplía.");
            var now = clock.UtcNow.ToUnixTimeSeconds();
            var ttl = Math.Min(b.ExpiresInSeconds ?? 60, (int)(subject.Exp - now));
            var issued = subject with { Scope = string.Join(' ', requested), Jti = "jti_x_" + Guid.NewGuid().ToString("N")[..12], Iat = now, Exp = now + ttl };
            return Results.Ok(new
            {
                access_token = Auth.Issue(issued), issued_token_type = "urn:ietf:params:oauth:token-type:access_token", token_type = "Bearer",
                expires_in = ttl, scope = issued.Scope, audience = b.Audience, sub = issued.Sub,
            });
        }).WithTags("Intercambio").WithSummary("Cambiar un token de usuario por uno reducido para un servicio (nunca más scopes ni más vida que el original)");

        app.MapGet("/api/v1/audiences", () => Results.Ok(new { items = AudienceScopes.Select(a => new { audience = a.Key, scopes = a.Value }) }))
            .WithTags("Intercambio").WithSummary("Destinatarios y los scopes que aceptan");
    }
}
