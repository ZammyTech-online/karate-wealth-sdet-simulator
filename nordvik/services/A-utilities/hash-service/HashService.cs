using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CanonicalRequest(JsonElement? Json);
public sealed record CanonicalResult(string Canonical, string Sha256, int Bytes);
public sealed record TextHashRequest(string? Text, string? Encoding);
public sealed record HmacRequest(string? Key, string? Message, string? Encoding);
public sealed record HmacVerifyRequest(string? Key, string? Message, string? Signature);
public sealed record HmacResult(string Algorithm, string Signature, string Encoding);
public sealed record VerifyResult(bool Valid, string Expected, string? Reason);

/// <summary>hash-service: SHA-256 de JSON canónico, HMAC-SHA256 y verificación de firmas (para comprobar contentHash y webhooks).</summary>
public sealed class HashService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "hash-service", "A-utilities", 6011, 2, "Plataforma de datos de prueba",
        "Calcula el JSON canónico y su SHA-256 (como contentHash), SHA-256 de texto y HMAC-SHA256 de webhooks.");

    private static string Encode(byte[] b, string? enc) => enc?.ToLowerInvariant() == "base64" ? Convert.ToBase64String(b) : Convert.ToHexString(b).ToLowerInvariant();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/hash/canonical", (HttpContext http, CanonicalRequest body) =>
        {
            if (body.Json is null || body.Json.Value.ValueKind == JsonValueKind.Undefined)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta json.", "Ejemplo: { \"json\": { \"b\": 1, \"a\": [2, 1] } } → canónico {\"a\":[2,1],\"b\":1}", new[] { new FieldError("json", "obligatorio") });
            var c = Canonical.Of(body.Json.Value);
            return Results.Ok(new CanonicalResult(c, Canonical.Sha256Hex(c), Encoding.UTF8.GetByteCount(c)));
        }).WithTags("Hash").WithSummary("JSON canónico (claves ordenadas, sin espacios) y su SHA-256").Produces<CanonicalResult>();

        app.MapPost("/api/v1/hash/sha256", (HttpContext http, TextHashRequest body) =>
        {
            if (body.Text is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta text.", null, new[] { new FieldError("text", "obligatorio") });
            return Results.Ok(new { algorithm = "SHA-256", hash = Encode(SHA256.HashData(Encoding.UTF8.GetBytes(body.Text)), body.Encoding), encoding = body.Encoding ?? "hex" });
        }).WithTags("Hash").WithSummary("SHA-256 de un texto (UTF-8)");

        app.MapPost("/api/v1/hmac", (HttpContext http, HmacRequest body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrEmpty(body.Key)) errors.Add(new FieldError("key", "obligatorio"));
            if (body.Message is null) errors.Add(new FieldError("message", "obligatorio"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Faltan datos para el HMAC.", "Ejemplo: { \"key\": \"secreto\", \"message\": \"{...cuerpo exacto...}\" }", errors);
            return Results.Ok(new HmacResult("HMAC-SHA256", Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(body.Key!), Encoding.UTF8.GetBytes(body.Message!)), body.Encoding), body.Encoding ?? "hex"));
        }).WithTags("HMAC").WithSummary("Firmar un mensaje con HMAC-SHA256").Produces<HmacResult>();

        app.MapPost("/api/v1/hmac/verify", (HttpContext http, HmacVerifyRequest body) =>
        {
            if (string.IsNullOrEmpty(body.Key) || body.Message is null || string.IsNullOrEmpty(body.Signature))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta key, message y signature.", null);
            var sig = body.Signature.StartsWith("sha256=") ? body.Signature[7..] : body.Signature;
            var raw = HMACSHA256.HashData(Encoding.UTF8.GetBytes(body.Key), Encoding.UTF8.GetBytes(body.Message));
            var hex = Convert.ToHexString(raw).ToLowerInvariant();
            var ok = string.Equals(sig, hex, StringComparison.OrdinalIgnoreCase) || sig == Convert.ToBase64String(raw);
            return Results.Ok(new VerifyResult(ok, hex, ok ? null : "la firma no coincide: ¿el mensaje es exactamente el cuerpo recibido, sin re-serializar?"));
        }).WithTags("HMAC").WithSummary("Verificar una firma HMAC (hex, base64 o sha256=hex)").Produces<VerifyResult>();
    }
}
