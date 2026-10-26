using System.Numerics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateIbanRequest(string? Country, string? BankCode, int? Seed);
public sealed record Bank(string Country, string Code, string Name, string Bic);
public sealed record Iban(string Electronic, string Print, string Country, string CheckDigits, string BankCode, string BankName, string Bic);
public sealed record ValidateIbanRequest(string? Iban);
public sealed record IbanValidation(string? Input, bool Valid, string? Electronic, string? Country, string? BankCode, string? BankName, string? Bic, string? Reason);

/// <summary>iban-service: IBAN de prueba válidos (mod 97) de bancos ficticios nórdicos, y validación de IBAN.</summary>
public sealed class IbanService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "iban-service", "A-utilities", 6007, 1, "Plataforma de datos de prueba",
        "Genera IBAN de prueba válidos de bancos ficticios y valida IBAN (longitud por país y dígitos de control mod 97).");

    private static readonly Dictionary<string, int> Lengths = new() { ["SE"] = 24, ["NO"] = 15, ["DK"] = 18, ["FI"] = 18 };

    public static readonly Bank[] Banks =
    {
        new("SE", "300", "Fjordbanken", "FJRDSESS"), new("SE", "500", "Älvbanken", "ALVBSESS"), new("SE", "800", "Sparbanken Nord", "SPNOSESS"),
        new("NO", "1503", "Kystbanken", "KYSTNOKK"), new("NO", "6011", "Fjellbanken", "FJELNOKK"),
        new("DK", "2000", "Øresundsbanken", "ORESDKKK"), new("DK", "5301", "Jyllandsbanken", "JYLBDKKK"),
        new("FI", "1234", "Järvipankki", "JARVFIHH"), new("FI", "4000", "Metsäpankki", "METSFIHH"),
    };

    public static string CheckDigits(string country, string bban)
    {
        var rearranged = bban + country + "00";
        var digits = new StringBuilder();
        foreach (var c in rearranged) digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString());
        var mod = (int)(BigInteger.Parse(digits.ToString()) % 97);
        return (98 - mod).ToString("00");
    }

    public static bool Mod97Ok(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var digits = new StringBuilder();
        foreach (var c in rearranged) digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString());
        return BigInteger.Parse(digits.ToString()) % 97 == 1;
    }

    private static string Print(string iban) => string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));

    private static (string Bank, string Account) Split(string country, string bban) => country switch
    {
        "SE" => (bban[..3], bban[3..]),
        _ => (bban[..4], bban[4..]),
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/banks", (string? country) => Results.Ok(Banks.Where(b => country is null || b.Country.Equals(country, StringComparison.OrdinalIgnoreCase)).ToList()))
            .WithTags("IBAN").WithSummary("Bancos ficticios disponibles").Produces<List<Bank>>();

        app.MapPost("/api/v1/ibans", (HttpContext http, CreateIbanRequest? body) =>
        {
            var country = (body?.Country ?? "SE").ToUpperInvariant();
            if (!Lengths.ContainsKey(country))
                return Problems.BadRequest(http, "UNSUPPORTED_COUNTRY", $"País '{body?.Country}' no soportado.", "Países: SE, NO, DK, FI.");
            var banks = Banks.Where(b => b.Country == country && (body?.BankCode is null || b.Code == body.BankCode)).ToArray();
            if (banks.Length == 0)
                return Problems.NotFound(http, "BANK_NOT_FOUND", $"No hay ningún banco {body?.BankCode} en {country}.", "Lista los bancos con GET /api/v1/banks?country=" + country);
            var rnd = body?.Seed is { } s ? new Random(s) : Random.Shared;
            var bank = banks[rnd.Next(banks.Length)];
            var accountLength = Lengths[country] - 4 - bank.Code.Length;
            var account = string.Concat(Enumerable.Range(0, accountLength).Select(_ => rnd.Next(10)));
            var bban = bank.Code + account;
            var iban = country + CheckDigits(country, bban) + bban;
            return Results.Created($"/api/v1/ibans/{iban}", new Iban(iban, Print(iban), country, iban[2..4], bank.Code, bank.Name, bank.Bic));
        }).WithTags("IBAN").WithSummary("Generar un IBAN de prueba válido").Produces<Iban>(201);

        app.MapPost("/api/v1/ibans/validate", (HttpContext http, ValidateIbanRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Iban))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta el campo iban.", "Ejemplo: { \"iban\": \"SE45 3000 0000 0123 4567 8901\" } (los espacios se ignoran)",
                    new[] { new FieldError("iban", "obligatorio") });
            var iban = body.Iban.Replace(" ", "").ToUpperInvariant();
            IResult Invalid(string reason) => Results.Ok(new IbanValidation(body.Iban, false, null, null, null, null, null, reason));
            if (iban.Length < 5 || !char.IsLetter(iban[0]) || !char.IsLetter(iban[1])) return Invalid("debe empezar por el código de país de 2 letras");
            var country = iban[..2];
            if (!Lengths.TryGetValue(country, out var len)) return Invalid($"país {country} no soportado");
            if (iban.Length != len) return Invalid($"un IBAN de {country} tiene {len} caracteres y este tiene {iban.Length}");
            if (!iban[2..].All(char.IsLetterOrDigit)) return Invalid("solo admite letras y números");
            if (!Mod97Ok(iban)) return Invalid("los dígitos de control no cuadran (mod 97)");
            var (code, _) = Split(country, iban[4..]);
            var bank = Banks.FirstOrDefault(b => b.Country == country && b.Code == code);
            return Results.Ok(new IbanValidation(body.Iban, true, iban, country, code, bank?.Name, bank?.Bic, bank is null ? "IBAN válido, banco desconocido en el entorno de pruebas" : null));
        }).WithTags("IBAN").WithSummary("Validar un IBAN").Produces<IbanValidation>();
    }
}
