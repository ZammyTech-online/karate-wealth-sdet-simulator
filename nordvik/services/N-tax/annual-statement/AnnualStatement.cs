using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// annual-statement: el certificado fiscal anual del cliente (lo que Nordvik declaró de él a la Agencia Tributaria): pensión bruta,
/// retención y neto, en JSON y en PDF. Solo existe cuando el año está presentado; si hubo corrección, manda la última.
/// </summary>
public sealed class AnnualStatement : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "annual-statement", "N-tax", 7302, 4, "Fiscalidad",
        "Certificado fiscal anual del cliente en JSON y PDF a partir del informe presentado (el último, si hubo corrección).",
        DependsOn: new[] { "tax-reporting", "customer-registry", "permission-service", "clock-service" });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<(IResult? Fail, DownstreamReply Line)> Get(HttpContext http, string customerId, int year)
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return (denied, default!);
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return (g, default!);
            var line = await Downstream.Get(http, "tax-reporting", $"/api/v1/internal/tax-reports/{year}/customers/{customerId}", forwardAuth: false);
            if (!line.Ok) return (Problems.NotFound(http, "STATEMENT_NOT_AVAILABLE", $"No hay certificado de {year}: el año no está presentado o no hubo pagos.", "Se publica cuando Operaciones presenta el informe anual."), line);
            return (null, line);
        }

        app.MapGet("/api/v1/customers/{customerId}/annual-statements/{year:int}", async (HttpContext http, string customerId, int year) =>
        {
            var (fail, l) = await Get(http, customerId, year);
            if (fail is not null) return fail;
            var gross = decimal.Parse(l.Str("gross")!, Money.Inv); var tax = decimal.Parse(l.Str("taxWithheld")!, Money.Inv);
            return Results.Ok(new
            {
                customerId, year, personalNumberMasked = l.Str("personalNumberMasked"), pensionGross = Money.Format(gross, 2), taxWithheld = Money.Format(tax, 2), net = Money.Format(gross - tax, 2),
                currency = "SEK", payments = l.Json.GetProperty("payments").GetInt32(), reportId = l.Str("reportId"), reportKind = l.Str("kind"), reportedAt = l.Str("filedAt"),
            });
        }).WithTags("Certificado anual").WithSummary("Certificado fiscal del año (JSON)");

        app.MapGet("/api/v1/customers/{customerId}/annual-statements/{year:int}/pdf", async (HttpContext http, string customerId, int year) =>
        {
            var (fail, l) = await Get(http, customerId, year);
            if (fail is not null) return fail;
            var pdf = PdfWriter.Create($"Nordvik · Certificado fiscal {year}", new[]
            {
                $"Titular: {l.Str("personalNumberMasked")}", $"Ejercicio: {year}",
                $"Pensión bruta pagada: {l.Str("gross")} SEK", $"Retención practicada: {l.Str("taxWithheld")} SEK", $"Pagos: {l.Json.GetProperty("payments").GetInt32()}",
                $"Declarado a la Agencia Tributaria en el informe {l.Str("reportId")} ({l.Str("kind")}) el {l.Str("filedAt")}.",
            }, "Documento generado por Nordvik Wealth & Pension (simulador). Sin validez fiscal.");
            return Results.File(pdf, "application/pdf", $"certificado-{year}.pdf");
        }).WithTags("Certificado anual").WithSummary("Certificado fiscal del año (PDF)");
    }
}
