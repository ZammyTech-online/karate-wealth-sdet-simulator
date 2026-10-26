using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Quién hace la petición, sacado del token JWT.</summary>
public sealed record Principal(
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("customerId")] string? CustomerId,
    [property: JsonPropertyName("advisorId")] string? AdvisorId,
    [property: JsonPropertyName("sid")] string? SessionId,
    [property: JsonPropertyName("jti")] string Jti,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("exp")] long Exp,
    [property: JsonPropertyName("iss")] string Iss = "nordvik-iam")
{
    public IReadOnlyCollection<string> Scopes => Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    public bool HasScope(string s) => Scopes.Contains(s) || Scopes.Contains("*");
}

/// <summary>
/// Tokens JWT HS256 de Nordvik. iam-service los emite y cada servicio los valida él mismo con la clave compartida
/// (como en muchas empresas: así no hay que preguntar a iam en cada petición, pero un token revocado sigue valiendo hasta que caduca).
/// La caducidad se mide con el reloj virtual.
/// </summary>
public static class Auth
{
    public const string Issuer = "nordvik-iam";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("nordvik-entorno-de-pruebas-clave-hs256-no-usar-en-produccion");
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public const string PrincipalKey = "nordvik.principal";

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    public static string Issue(Principal p)
    {
        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(p, Json));
        var sig = B64(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(header + "." + payload)));
        return $"{header}.{payload}.{sig}";
    }

    /// <summary>Valida firma y caducidad. Devuelve el motivo del rechazo, o null si es válido.</summary>
    public static string? Validate(string token, DateTimeOffset now, out Principal? principal)
    {
        principal = null;
        var parts = token.Split('.');
        if (parts.Length != 3) return "malformed";
        var expected = B64(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(parts[0] + "." + parts[1])));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]))) return "bad_signature";
        try { principal = JsonSerializer.Deserialize<Principal>(FromB64(parts[1])); }
        catch { return "malformed"; }
        if (principal is null) return "malformed";
        if (principal.Iss != Issuer) return "bad_issuer";
        if (now.ToUnixTimeSeconds() >= principal.Exp) return "expired";
        return null;
    }

    /// <summary>
    /// Exige un token válido y, opcionalmente, uno de los roles y un scope. Devuelve el problema a responder o null.
    /// </summary>
    public static IResult? Require(HttpContext http, IClock clock, out Principal principal, string[]? roles = null, string? scope = null)
    {
        principal = null!;
        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return Problems.Of(http, 401, "AUTHENTICATION_REQUIRED", "No autenticado", "Este endpoint necesita un token.",
                "Pide un token con POST /api/v1/oauth/token en iam-service (puerto 6200) y envíalo en la cabecera Authorization: Bearer <token>.");
        }
        var reason = Validate(header[7..].Trim(), clock.UtcNow, out var p);
        if (reason is not null)
        {
            http.Response.Headers.WWWAuthenticate = $"Bearer error=\"invalid_token\", error_description=\"{reason}\"";
            var (code, hint) = reason switch
            {
                "expired" => ("TOKEN_EXPIRED", "Los tokens duran 5 minutos del reloj virtual. ¿Has adelantado el reloj? Pide uno nuevo o usa el refresh_token."),
                "bad_signature" => ("INVALID_TOKEN", "La firma no cuadra: el token se ha modificado o no lo emitió iam-service."),
                _ => ("INVALID_TOKEN", "El token no tiene formato JWT (tres partes separadas por puntos)."),
            };
            return Problems.Of(http, 401, code, "No autenticado", "El token no es válido: " + reason + ".", hint);
        }
        principal = p!;
        http.Items[PrincipalKey] = principal;
        if (roles is { Length: > 0 } && !roles.Contains(principal.Role))
            return Problems.Of(http, 403, "ROLE_NOT_ALLOWED", "Prohibido", $"El rol {principal.Role} no puede hacer esto.",
                "Roles permitidos: " + string.Join(", ", roles) + ". Pide el token con el usuario adecuado.");
        if (scope is not null && !principal.HasScope(scope))
            return Problems.Of(http, 403, "SCOPE_MISSING", "Prohibido", $"Al token le falta el scope '{scope}'.",
                "Los scopes dependen del rol y del parámetro scope al pedir el token. Mira el claim 'scope' del token.");
        return null;
    }
}
