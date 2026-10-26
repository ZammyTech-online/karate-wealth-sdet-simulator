using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// file-factory: ficheros de prueba para subidas multipart y procesos por lotes: PDF válidos, el fichero EICAR, un "PDF" que en realidad
/// es JSON (tipo falso), ficheros de más de 5 MB, anexos JSON y CSV de aportaciones de empresa con errores a propósito.
/// </summary>
public sealed class FileFactory : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "file-factory", "A-utilities", 6013, 2, "Plataforma de datos de prueba",
        "Genera ficheros de prueba: PDF, EICAR, tipo falso, ficheros grandes, anexos JSON y CSV de empleador con errores controlados (reproducibles con seed).");

    public const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/files").WithTags("Ficheros");

        g.MapGet("/pdf", (int? pages, string? title) =>
        {
            var n = Math.Clamp(pages ?? 1, 1, 50);
            var lines = Enumerable.Range(1, n * 52).Select(i => $"Línea {i} de un documento de pruebas de Nordvik. Åäö ñ é.").ToList();
            return Results.File(PdfWriter.Create(title ?? "Documento de pruebas", lines), "application/pdf", "prueba.pdf");
        }).WithSummary("Un PDF válido de N páginas");

        g.MapGet("/eicar", () => Results.File(Encoding.ASCII.GetBytes(Eicar), "text/plain", "eicar.com.txt"))
            .WithSummary("El fichero de prueba EICAR (todos los antivirus lo detectan; es inofensivo)");

        g.MapGet("/eicar-pdf", () =>
        {
            var pdf = PdfWriter.Create("Factura", new[] { "Este PDF lleva dentro la firma EICAR.", Eicar });
            return Results.File(pdf, "application/pdf", "factura.pdf");
        }).WithSummary("Un PDF con la firma EICAR dentro (el antivirus debe rechazarlo)");

        g.MapGet("/fake-pdf", () => Results.File(Encoding.UTF8.GetBytes("{\"esto\":\"no es un PDF\"}"), "application/pdf", "falso.pdf"))
            .WithSummary("JSON servido como application/pdf (tipo declarado falso)");

        g.MapGet("/oversized", (int? mb) =>
        {
            var size = Math.Clamp(mb ?? 6, 1, 20) * 1024 * 1024;
            var data = new byte[size];
            Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(data, 0);
            new Random(7).NextBytes(data.AsSpan(9));
            return Results.File(data, "application/pdf", $"grande-{mb ?? 6}mb.pdf");
        }).WithSummary("Un fichero de N MB (por defecto 6: supera el límite de 5)");

        g.MapGet("/annex-json", (string? kind) => Results.Json((kind ?? "FEE_SCHEDULE") switch
        {
            "FEE_SCHEDULE" => (object)new { managementFee = "0.45", entryFee = "0.00", exitFee = "0.00", currency = "SEK", validFrom = "2026-01-01" },
            "SUITABILITY" => new { riskProfile = 4, horizonYears = 10, knowledge = "MEDIUM", assessedAt = "2026-10-20" },
            _ => new { kind, note = "anexo genérico de pruebas" },
        })).WithSummary("Contenido JSON de un anexo (FEE_SCHEDULE, SUITABILITY...)");

        g.MapGet("/employer-csv", (int? rows, int? seed, int? errors) =>
        {
            var n = Math.Clamp(rows ?? 20, 1, 5000);
            var rnd = new Random(seed ?? 42);
            var bad = Math.Clamp(errors ?? 0, 0, n);
            var badRows = Enumerable.Range(0, n).OrderBy(_ => rnd.Next()).Take(bad).ToHashSet();
            var sb = new StringBuilder("employeeNationalId;periodMonth;salarySek;contributionPercent;contributionSek\n");
            for (var i = 0; i < n; i++)
            {
                var birth = new DateOnly(1965, 1, 1).AddDays(rnd.Next(0, 12000));
                var pn = NationalId.Create(birth, rnd.Next(100), rnd.Next(2) == 0);
                var salary = rnd.Next(25000, 90000);
                var pct = salary > 55000 ? 6.5m : 4.5m;
                var contribution = Math.Round(salary * pct / 100, 2, MidpointRounding.ToEven);
                if (badRows.Contains(i))
                    switch (rnd.Next(4))
                    {
                        case 0: pn = pn[..^1] + ((pn[^1] - '0' + 1) % 10); break;           // dígito de control incorrecto
                        case 1: contribution += 1; break;                                   // importe que no cuadra
                        case 2: sb.Append($"{pn};2026-13;{salary};{pct.ToString(CultureInfo.InvariantCulture)};{contribution.ToString("F2", CultureInfo.InvariantCulture)}\n"); continue; // mes imposible
                        default: sb.Append($"{pn};2026-10;{salary};;\n"); continue;        // campos vacíos
                    }
                sb.Append($"{pn};2026-10;{salary};{pct.ToString(CultureInfo.InvariantCulture)};{contribution.ToString("F2", CultureInfo.InvariantCulture)}\n");
            }
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "aportaciones-2026-10.csv");
        }).WithSummary("CSV de aportaciones de empresa (oleada 3: pensiones de empleo), con N filas erróneas a propósito");
    }
}
