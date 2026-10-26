using System.Security.Cryptography;
using System.Text;

namespace Nordvik.Shared;

/// <summary>
/// Firma JWS "separada" (detached, RFC 7515 apéndice F) con HS256, como la que usa NordPay:
/// cabecera X-JWS-Signature = base64url(protected) + ".." + base64url(HMAC(protected + "." + base64url(cuerpo))).
/// El cuerpo viaja tal cual en la petición; si cambia un solo byte, la firma deja de valer.
/// </summary>
public static class Jws
{
    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string SignDetached(string body, string key, string kid)
    {
        var protectedHeader = B64(Encoding.UTF8.GetBytes($"{{\"alg\":\"HS256\",\"kid\":\"{kid}\"}}"));
        var input = protectedHeader + "." + B64(Encoding.UTF8.GetBytes(body));
        return protectedHeader + ".." + B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(input)));
    }

    public static bool VerifyDetached(string body, string? signature, string key)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var parts = signature.Split('.');
        if (parts.Length != 3 || parts[1].Length != 0) return false;
        var input = parts[0] + "." + B64(Encoding.UTF8.GetBytes(body));
        var expected = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(input)));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]));
    }
}
