using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ForgeRequest(string? Sub, string? Role, string? Scope, string? CustomerId, string? AdvisorId, int? ExpiresInSeconds, string? Issuer, string? Acr, string? Tamper);
public sealed record DecodeRequest(string? Token);

/// <summary>
/// jwt-forge: fabrica tokens a medida para probar la seguridad sin pasar por el login: caducados, con scopes raros,
/// de otro emisor, con la firma rota o con el payload manipulado. Solo existe en local y ci (en un banco real, nunca en producción).
/// </summary>
public sealed class JwtForge : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "jwt-forge", "A-utilities", 6014, 3, "Plataforma de datos de prueba",
        "Tokens de prueba firmados con claims a medida: caducados, sin scope, de otro emisor, con firma rota o payload manipulado. Y un decodificador.",
        DependsOn: new[] { "clock-service" });

    public static readonly string[] Roles = { "Customer", "Advisor", "Operations", "Compliance", "Admin", "Service" };
    public static readonly string[] Tampers = { "none", "signature", "payload", "alg-none", "truncate" };

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/tokens/forge", (HttpContext http, ForgeRequest b) =>
        {
            var tamper = (b.Tamper ?? "none").ToLowerInvariant();
            var errors = new List<FieldError>();
            if (b.Role is null || !Roles.Contains(b.Role)) errors.Add(new FieldError("role", "uno de " + string.Join(", ", Roles)));
            if (b.Role == "Customer" && b.CustomerId is null) errors.Add(new FieldError("customerId", "un token de cliente necesita customerId"));
            if (b.ExpiresInSeconds is < -86400 or > 86400) errors.Add(new FieldError("expiresInSeconds", "entre -86400 (caducado hace un día) y 86400"));
            if (!Tampers.Contains(tamper)) errors.Add(new FieldError("tamper", "uno de " + string.Join(", ", Tampers)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Token no válido.",
                "Ejemplo: { \"role\": \"Customer\", \"customerId\": \"c0000000-0000-4000-8000-000000000001\", \"scope\": \"profile:read\", \"expiresInSeconds\": -60 }", errors);
            var now = clock.UtcNow.ToUnixTimeSeconds();
            var exp = b.ExpiresInSeconds ?? 300;
            var p = new Principal(b.Sub ?? (b.Role == "Customer" ? "forged.customer" : "forged." + b.Role!.ToLowerInvariant()), b.Role!, b.Scope ?? "", b.CustomerId, b.AdvisorId, null,
                "jti_forged_" + Guid.NewGuid().ToString("N")[..10], exp < 0 ? now + exp - 300 : now, now + exp, b.Issuer ?? Auth.Issuer, b.Acr);
            var token = Auth.Issue(p);
            var parts = token.Split('.');
            token = tamper switch
            {
                "signature" => $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}",
                "payload" => $"{parts[0]}.{B64(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(FromB64(parts[1])).Replace($"\"role\":\"{p.Role}\"", "\"role\":\"Admin\"")))}.{parts[2]}",
                "alg-none" => $"{B64(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"))}.{parts[1]}.",
                "truncate" => token[..(token.Length / 2)],
                _ => token,
            };
            var expected = tamper switch
            {
                "signature" or "payload" or "alg-none" => "401 INVALID_TOKEN (firma)",
                "truncate" => "401 INVALID_TOKEN (formato)",
                _ when exp <= 0 => "401 TOKEN_EXPIRED",
                _ when (b.Issuer ?? Auth.Issuer) != Auth.Issuer => "401 INVALID_TOKEN (emisor)",
                _ => "válido para los servicios de Nordvik",
            };
            http.Response.Headers["X-Forged-Token"] = "true";
            return Results.Ok(new { token, claims = p, tamper, expected, warning = "Token de laboratorio: jwt-forge solo existe en local y ci." });
        }).WithTags("Tokens").WithSummary("Fabricar un token a medida (caducado, sin scope, otro emisor, firma o payload manipulados)");

        app.MapPost("/api/v1/tokens/decode", (HttpContext http, DecodeRequest b) =>
        {
            var token = (b.Token ?? "").Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..];
            var parts = token.Split('.');
            if (parts.Length != 3) return Problems.BadRequest(http, "MALFORMED_TOKEN", "Un JWT tiene tres partes separadas por puntos.", null);
            JsonElement header, payload;
            try { header = JsonDocument.Parse(FromB64(parts[0])).RootElement; payload = JsonDocument.Parse(FromB64(parts[1])).RootElement; }
            catch { return Problems.BadRequest(http, "MALFORMED_TOKEN", "La cabecera o el payload no son JSON en base64url.", null); }
            var reason = Auth.Validate(token, clock.UtcNow, out _);
            long? secondsLeft = payload.TryGetProperty("exp", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() - clock.UtcNow.ToUnixTimeSeconds() : null;
            return Results.Ok(new { header, payload, valid = reason is null, reason, secondsLeft });
        }).WithTags("Tokens").WithSummary("Decodificar y validar un token (firma, emisor y caducidad con el reloj virtual)");

        app.MapGet("/api/v1/tokens/recipes", () => Results.Ok(new
        {
            recipes = new object[]
            {
                new { name = "caducado", body = new { role = "Advisor", advisorId = SeedIds.AdvisorErik, scope = "customers:read", expiresInSeconds = -60 }, expect = "401 TOKEN_EXPIRED" },
                new { name = "sin scope", body = new { role = "Advisor", advisorId = SeedIds.AdvisorErik, scope = "" }, expect = "403 SCOPE_MISSING" },
                new { name = "otro emisor", body = new { role = "Admin", scope = "*", issuer = "evil-iam" }, expect = "401 INVALID_TOKEN" },
                new { name = "firma rota", body = new { role = "Admin", scope = "*", tamper = "signature" }, expect = "401 INVALID_TOKEN" },
                new { name = "escalada de rol en el payload", body = new { role = "Customer", customerId = SeedIds.CustomerAna, tamper = "payload" }, expect = "401 INVALID_TOKEN" },
                new { name = "alg none", body = new { role = "Admin", scope = "*", tamper = "alg-none" }, expect = "401 INVALID_TOKEN" },
                new { name = "cliente de otro", body = new { role = "Customer", customerId = SeedIds.CustomerLars, scope = "profile:read" }, expect = "404 al pedir datos de Ana" },
            },
        })).WithTags("Tokens").WithSummary("Recetas de tokens para las pruebas de seguridad típicas");
    }
}
