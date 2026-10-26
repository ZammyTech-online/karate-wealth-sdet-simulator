using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record UuidBatch(int Version, int Count, IReadOnlyList<string> Uuids, string GeneratedAt);
public sealed record SingleUuid(int Version, string Uuid);
public sealed record ValidateUuidRequest(string? Value);
public sealed record UuidValidation(string? Value, bool Valid, int? Version, string? Variant, string? Reason);

/// <summary>uuid-service: genera UUID v4 (aleatorios) y v7 (ordenables por tiempo, con la hora del reloj virtual).</summary>
public sealed class UuidService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "uuid-service", "A-utilities", 6001, 0, "Plataforma de datos de prueba",
        "Genera identificadores únicos UUID v4 y v7, solos o en lote, y valida UUID.",
        DependsOn: new[] { "clock-service" });

    private static readonly Regex Pattern = new("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/uuids").WithTags("UUID");

        g.MapGet("", (HttpContext http, IClock clock, int? version, int? count) =>
        {
            var v = version ?? 4;
            var n = count ?? 1;
            if (v is not (4 or 7))
                return Problems.BadRequest(http, "UNSUPPORTED_UUID_VERSION", $"La versión {v} no está soportada.",
                    "Solo existen version=4 (aleatorio) y version=7 (ordenable por tiempo). Prueba ?version=7");
            if (n is < 1 or > 1000)
                return Problems.BadRequest(http, "COUNT_OUT_OF_RANGE", $"count={n} está fuera del rango 1-1000.",
                    "Pide entre 1 y 1000 UUID por llamada. Si necesitas más, haz varias llamadas.");
            var now = clock.UtcNow;
            var list = Enumerable.Range(0, n).Select(i => (v == 7 ? NewV7(now.AddTicks(i)) : Guid.NewGuid()).ToString("D")).ToList();
            return Results.Ok(new UuidBatch(v, n, list, Nordic.Iso(Nordic.ToStockholm(now))));
        }).WithSummary("Generar un lote de UUID").Produces<UuidBatch>();

        g.MapGet("/one", (HttpContext http, IClock clock, int? version) =>
        {
            var v = version ?? 4;
            if (v is not (4 or 7))
                return Problems.BadRequest(http, "UNSUPPORTED_UUID_VERSION", $"La versión {v} no está soportada.",
                    "Solo existen version=4 y version=7.");
            return Results.Ok(new SingleUuid(v, (v == 7 ? NewV7(clock.UtcNow) : Guid.NewGuid()).ToString("D")));
        }).WithSummary("Generar un solo UUID").Produces<SingleUuid>();

        g.MapPost("/validate", (HttpContext http, ValidateUuidRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Value))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta el campo value.",
                    "Envía un JSON como { \"value\": \"0192d3c4-...\" }",
                    new[] { new FieldError("value", "obligatorio") });
            var value = body.Value.Trim();
            if (!Pattern.IsMatch(value))
                return Results.Ok(new UuidValidation(value, false, null, null, "No tiene el formato 8-4-4-4-12 en hexadecimal"));
            var version = Convert.ToInt32(value[14].ToString(), 16);
            var variantChar = char.ToLowerInvariant(value[19]);
            var variant = variantChar is '8' or '9' or 'a' or 'b' ? "RFC_9562" : "OTHER";
            return Results.Ok(new UuidValidation(value, true, version, variant, null));
        }).WithSummary("Validar un UUID y decir su versión").Produces<UuidValidation>();
    }

    /// <summary>UUID v7: 48 bits de milisegundos Unix + aleatorio (RFC 9562).</summary>
    public static Guid NewV7(DateTimeOffset now)
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        var ms = now.ToUnixTimeMilliseconds();
        b[0] = (byte)(ms >> 40); b[1] = (byte)(ms >> 32); b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16); b[4] = (byte)(ms >> 8); b[5] = (byte)ms;
        b[6] = (byte)((b[6] & 0x0F) | 0x70);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }
}
