using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Address(string Street, string Number, string PostalCode, string City, string Municipality, string Country);
public sealed record AddressBatch(int Seed, int Count, IReadOnlyList<Address> Addresses);
public sealed record PostalCodeInfo(string Country, string PostalCode, bool Valid, string? City, string? Reason);

/// <summary>address-service: direcciones sintéticas coherentes (calle, código postal y municipio) de los cuatro países nórdicos.</summary>
public sealed class AddressService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "address-service", "A-utilities", 6006, 1, "Plataforma de datos de prueba",
        "Direcciones sintéticas nórdicas con código postal coherente y validación de códigos postales.");

    internal sealed record City(string Country, string Name, string Municipality, int From, int To);

    internal static readonly City[] Cities =
    {
        new("SE", "Stockholm", "Stockholms kommun", 11120, 11860), new("SE", "Göteborg", "Göteborgs kommun", 41101, 41879),
        new("SE", "Malmö", "Malmö kommun", 21110, 21779), new("SE", "Uppsala", "Uppsala kommun", 75220, 75697),
        new("SE", "Västerås", "Västerås kommun", 72210, 72592), new("SE", "Linköping", "Linköpings kommun", 58183, 58937),
        new("NO", "Oslo", "Oslo kommune", 150, 1295), new("NO", "Bergen", "Bergen kommune", 5003, 5099),
        new("DK", "København", "Københavns Kommune", 1050, 2450), new("DK", "Aarhus", "Aarhus Kommune", 8000, 8270),
        new("FI", "Helsinki", "Helsingin kaupunki", 100, 990), new("FI", "Tampere", "Tampereen kaupunki", 33100, 33960),
    };

    private static readonly Dictionary<string, string[]> Streets = new()
    {
        ["SE"] = new[] { "Storgatan", "Drottninggatan", "Kungsgatan", "Vasagatan", "Sveavägen", "Linnégatan", "Ringvägen", "Skolgatan", "Parkvägen", "Björkvägen" },
        ["NO"] = new[] { "Storgata", "Kirkegata", "Thorvald Meyers gate", "Bogstadveien", "Markveien", "Sognsveien" },
        ["DK"] = new[] { "Vesterbrogade", "Nørrebrogade", "Strøget", "Amagerbrogade", "Østerbrogade", "Jagtvej" },
        ["FI"] = new[] { "Mannerheimintie", "Aleksanterinkatu", "Hämeenkatu", "Esplanadi", "Fredrikinkatu", "Runeberginkatu" },
    };

    public static string FormatPostal(string country, int code) => country switch
    {
        "SE" => code.ToString("00000").Insert(3, " "),
        "NO" or "DK" => code.ToString("0000"),
        _ => code.ToString("00000"),
    };

    public static Address Generate(Random rnd, string country, string? city)
    {
        var options = Cities.Where(c => c.Country == country && (city is null || c.Name.Equals(city, StringComparison.OrdinalIgnoreCase))).ToArray();
        var c = options[rnd.Next(options.Length)];
        var street = Streets[country][rnd.Next(Streets[country].Length)];
        return new Address(street, rnd.Next(1, 140).ToString(), FormatPostal(country, rnd.Next(c.From, c.To + 1)), c.Name, c.Municipality, country);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/addresses", (HttpContext http, string? country, string? city, int? count, int? seed) =>
        {
            var ctry = (country ?? "SE").ToUpperInvariant();
            if (!Streets.ContainsKey(ctry))
                return Problems.BadRequest(http, "UNSUPPORTED_COUNTRY", $"País '{country}' no soportado.", "Países: SE, NO, DK, FI (código ISO de 2 letras).");
            if (city is not null && !Cities.Any(c => c.Country == ctry && c.Name.Equals(city, StringComparison.OrdinalIgnoreCase)))
                return Problems.NotFound(http, "CITY_NOT_FOUND", $"No hay direcciones de '{city}' en {ctry}.",
                    "Ciudades: " + string.Join(", ", Cities.Where(c => c.Country == ctry).Select(c => c.Name)));
            var n = count ?? 1;
            if (n is < 1 or > 100) return Problems.BadRequest(http, "COUNT_OUT_OF_RANGE", "count entre 1 y 100.", "Pide como mucho 100 direcciones por llamada.");
            var s = seed ?? Random.Shared.Next(1, int.MaxValue);
            var rnd = new Random(s);
            return Results.Ok(new AddressBatch(s, n, Enumerable.Range(0, n).Select(_ => Generate(rnd, ctry, city)).ToList()));
        }).WithTags("Direcciones").WithSummary("Generar direcciones sintéticas (reproducibles con seed)").Produces<AddressBatch>();

        app.MapGet("/api/v1/postal-codes/{country}/{code}", (HttpContext http, string country, string code) =>
        {
            var ctry = country.ToUpperInvariant();
            if (!Streets.ContainsKey(ctry))
                return Problems.BadRequest(http, "UNSUPPORTED_COUNTRY", $"País '{country}' no soportado.", "Países: SE, NO, DK, FI.");
            var pattern = ctry switch { "SE" => @"^\d{3} \d{2}$", "NO" or "DK" => @"^\d{4}$", _ => @"^\d{5}$" };
            if (!Regex.IsMatch(code, pattern))
                return Results.Ok(new PostalCodeInfo(ctry, code, false, null, ctry == "SE" ? "en Suecia el formato es NNN NN (con espacio)" : "formato incorrecto"));
            var number = int.Parse(code.Replace(" ", ""));
            var city = Cities.FirstOrDefault(c => c.Country == ctry && number >= c.From && number <= c.To);
            return Results.Ok(new PostalCodeInfo(ctry, code, city is not null, city?.Name, city is null ? "no corresponde a ninguna ciudad conocida" : null));
        }).WithTags("Direcciones").WithSummary("Validar un código postal y decir su ciudad").Produces<PostalCodeInfo>();

        app.MapGet("/api/v1/cities", (string? country) =>
            Results.Ok(Cities.Where(c => country is null || c.Country.Equals(country, StringComparison.OrdinalIgnoreCase))
                .Select(c => new { country = c.Country, name = c.Name, municipality = c.Municipality, postalFrom = FormatPostal(c.Country, c.From), postalTo = FormatPostal(c.Country, c.To) })))
            .WithTags("Direcciones").WithSummary("Ciudades disponibles y su rango postal");
    }
}
