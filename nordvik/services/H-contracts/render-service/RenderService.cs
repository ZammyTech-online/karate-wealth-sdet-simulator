using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RenderRequest2(string? Title, List<string>? Lines, string? Footer, string? Format);
public sealed record RenderedDocument(string Format, int Pages, long Size, string Sha256, string ContentBase64);

/// <summary>render-service: convierte contenido estructurado en un PDF legible (contratos, extractos). Devuelve el PDF o, con format=json, su base64 y su hash.</summary>
public sealed class RenderService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "render-service", "H-contracts", 6705, 2, "Contratos y documentos",
        "Genera PDF reales (A4, varias páginas) a partir de un título y líneas de texto. format=json devuelve base64 + SHA-256.");

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/render", (HttpContext http, RenderRequest2 b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Title) || b.Lines is not { Count: > 0 })
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta title y lines.", "Ejemplo: { \"title\": \"Contrato\", \"lines\": [\"Cláusula 1...\", \"Cláusula 2...\"] }");
            if (b.Lines.Count > 5000) return Problems.Unprocessable(http, "TOO_MANY_LINES", "Como mucho 5000 líneas.", null);
            var pdf = PdfWriter.Create(b.Title, b.Lines, b.Footer);
            if (b.Format == "json" || http.Request.Headers.Accept.ToString().Contains("application/json"))
                return Results.Ok(new RenderedDocument("pdf", PdfWriter.PageCount(pdf), pdf.LongLength, Canonical.Sha256Hex(pdf), Convert.ToBase64String(pdf)));
            http.Response.Headers["X-Pdf-Pages"] = PdfWriter.PageCount(pdf).ToString();
            return Results.File(pdf, "application/pdf", "documento.pdf");
        }).WithTags("Render").WithSummary("Generar un PDF (Accept: application/json o format=json para recibir base64)").Produces<RenderedDocument>();
    }
}
