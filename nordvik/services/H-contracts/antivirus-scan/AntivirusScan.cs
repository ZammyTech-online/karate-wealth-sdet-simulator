using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ScanResult(string ScanId, string? FileName, long Size, string Sha256, string? DeclaredMime, string DetectedMime, bool MimeMismatch, string Verdict, string? Signature, string ScannedAt);

/// <summary>
/// antivirus-scan: escaneo simulado de adjuntos. Detecta el fichero de prueba EICAR (estándar de la industria para probar antivirus),
/// reconoce el tipo real por sus primeros bytes (PDF, JSON, PNG, ZIP) y avisa si no coincide con el Content-Type declarado. Máximo 5 MB.
/// </summary>
public sealed class AntivirusScan : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "antivirus-scan", "H-contracts", 6706, 2, "Contratos y documentos",
        "Escaneo de adjuntos: EICAR = INFECTED, tipo real por magic bytes frente al declarado, límite de 5 MB. Acepta multipart o el fichero en el cuerpo.");

    public const long MaxBytes = 5 * 1024 * 1024;
    private const string Eicar = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";

    public static string Detect(byte[] d)
    {
        if (d.Length >= 4 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F') return "application/pdf";
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G') return "image/png";
        if (d.Length >= 4 && d[0] == 'P' && d[1] == 'K' && d[2] == 3 && d[3] == 4) return "application/zip";
        var head = Encoding.UTF8.GetString(d, 0, Math.Min(d.Length, 64)).TrimStart('﻿', ' ', '\n', '\r', '\t');
        if (head.StartsWith('{') || head.StartsWith('['))
        {
            try { System.Text.Json.JsonDocument.Parse(d); return "application/json"; } catch { }
        }
        return d.All(b => b is 9 or 10 or 13 || (b >= 32 && b < 127) || b >= 128) ? "text/plain" : "application/octet-stream";
    }

    public static ScanResult Scan(byte[] data, string? fileName, string? declared, IClock clock)
    {
        var text = Encoding.Latin1.GetString(data);
        var detected = Detect(data);
        var infected = text.Contains(Eicar, StringComparison.Ordinal);
        var declaredBase = declared?.Split(';')[0].Trim().ToLowerInvariant();
        var mismatch = declaredBase is not null && declaredBase != "application/octet-stream" && declaredBase != detected;
        return new ScanResult("scan_" + Guid.NewGuid().ToString("N")[..12], fileName, data.LongLength, Canonical.Sha256Hex(data), declaredBase, detected, mismatch,
            infected ? "INFECTED" : mismatch ? "SUSPICIOUS" : "CLEAN", infected ? "EICAR-Test-File" : null, Nordic.Iso(clock.LocalNow));
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/scans", async (HttpContext http, IClock clock) =>
        {
            if (http.Request.ContentLength > MaxBytes) return TooLarge(http);
            byte[] data; string? name; string? declared;
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file is null) return Problems.BadRequest(http, "FILE_REQUIRED", "Falta el fichero.", "En Karate: * multipart file file = { read: 'classpath:contrato.pdf', contentType: 'application/pdf' }");
                if (file.Length > MaxBytes) return TooLarge(http);
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                data = ms.ToArray(); name = file.FileName; declared = file.ContentType;
            }
            else
            {
                using var ms = new MemoryStream();
                await http.Request.Body.CopyToAsync(ms);
                data = ms.ToArray(); name = http.Request.Headers["X-File-Name"].FirstOrDefault(); declared = http.Request.ContentType;
                if (data.Length > MaxBytes) return TooLarge(http);
            }
            if (data.Length == 0) return Problems.BadRequest(http, "EMPTY_FILE", "El fichero está vacío.", null);
            await Task.Delay(150); // un antivirus de verdad tarda
            return Results.Ok(Scan(data, name, declared, clock));
        }).WithTags("Escaneo").WithSummary("Escanear un fichero (multipart 'file' o bytes en el cuerpo)").Produces<ScanResult>();

        app.MapGet("/api/v1/signatures", () => Results.Ok(new[] { new { name = "EICAR-Test-File", description = "Fichero de prueba estándar EICAR: no es un virus, pero todos los antivirus lo detectan." } }))
            .WithTags("Escaneo").WithSummary("Firmas que detecta el escáner");
    }

    private static IResult TooLarge(HttpContext http) =>
        Problems.Of(http, 413, "FILE_TOO_LARGE", "Fichero demasiado grande", "El máximo es 5 MB.", "file-factory (puerto 6013) genera ficheros grandes para probar este límite.");
}
