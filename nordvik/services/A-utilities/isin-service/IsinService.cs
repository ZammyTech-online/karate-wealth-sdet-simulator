using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateIsinRequest(string? Country, int? Seed);
public sealed record IsinCode(string Isin, string Country, string Nsin, int CheckDigit);
public sealed record ValidateIsinRequest(string? Isin);
public sealed record IsinValidation(string? Isin, bool FormatOk, bool Valid, int? ExpectedCheckDigit, string? Reason);

/// <summary>
/// isin-service: genera ISIN con su dígito de control (Luhn sobre letras convertidas a números) y valida ISIN.
/// Ojo: los ISIN históricos del catálogo (SE00000000NN) son códigos de prueba y no todos tienen un dígito de control correcto.
/// </summary>
public sealed class IsinService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "isin-service", "A-utilities", 6008, 1, "Plataforma de datos de prueba",
        "Genera ISIN válidos y valida el dígito de control de cualquier ISIN.");

    private static readonly Regex Format = new("^[A-Z]{2}[A-Z0-9]{9}[0-9]$");

    public static int CheckDigit(string first11)
    {
        var digits = new StringBuilder();
        foreach (var c in first11) digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString());
        var s = digits.ToString();
        var sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var d = s[s.Length - 1 - i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/isins", (HttpContext http, CreateIsinRequest? body) =>
        {
            var country = (body?.Country ?? "SE").ToUpperInvariant();
            if (!Regex.IsMatch(country, "^[A-Z]{2}$"))
                return Problems.BadRequest(http, "INVALID_COUNTRY", "El país son 2 letras ISO 3166.", "Ejemplo: { \"country\": \"SE\" }");
            var rnd = body?.Seed is { } s ? new Random(s) : Random.Shared;
            var nsin = "N" + string.Concat(Enumerable.Range(0, 8).Select(_ => rnd.Next(10)));
            var check = CheckDigit(country + nsin);
            return Results.Created($"/api/v1/isins/{country}{nsin}{check}", new IsinCode($"{country}{nsin}{check}", country, nsin, check));
        }).WithTags("ISIN").WithSummary("Generar un ISIN válido").Produces<IsinCode>(201);

        app.MapPost("/api/v1/isins/validate", (HttpContext http, ValidateIsinRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Isin))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta el campo isin.", "Ejemplo: { \"isin\": \"SE0000000001\" }", new[] { new FieldError("isin", "obligatorio") });
            var isin = body.Isin.Trim();
            if (!Format.IsMatch(isin))
                return Results.Ok(new IsinValidation(isin, false, false, null, "formato: 2 letras + 9 alfanuméricos + 1 dígito, en mayúsculas"));
            var expected = CheckDigit(isin[..11]);
            var ok = expected == isin[11] - '0';
            return Results.Ok(new IsinValidation(isin, true, ok, expected, ok ? null : $"el dígito de control debería ser {expected}"));
        }).WithTags("ISIN").WithSummary("Validar el formato y el dígito de control de un ISIN").Produces<IsinValidation>();
    }
}
