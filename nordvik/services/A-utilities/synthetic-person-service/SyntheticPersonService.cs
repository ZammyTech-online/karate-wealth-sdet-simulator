using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PersonRequest(string? Country, int? Age, int? MinAge, int? MaxAge, string? Gender, int? Count, int? Seed);
public sealed record PersonAddress(string Street, string Number, string PostalCode, string City, string Country);
public sealed record Person(string FirstName, string LastName, string NationalId, string BirthDate, int Age, string Gender,
    string Email, string Phone, string Language, PersonAddress Address);
public sealed record PersonBatch(int Seed, int Count, IReadOnlyList<Person> Persons);

/// <summary>synthetic-person-service: personas sintéticas completas y coherentes. La dirección la pide a address-service.</summary>
public sealed class SyntheticPersonService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "synthetic-person-service", "A-utilities", 6005, 1, "Plataforma de datos de prueba",
        "Personas sintéticas nórdicas: nombre, número nacional válido (Luhn), edad según el reloj virtual, email, teléfono y dirección.",
        DependsOn: new[] { "clock-service", "address-service" });

    private static readonly string[] Female = { "Ana", "Sofia", "Maja", "Elsa", "Astrid", "Ingrid", "Karin", "Linnea", "Freja", "Saga", "Ebba", "Klara" };
    private static readonly string[] Male = { "Erik", "Lars", "Olof", "Nils", "Gustav", "Oskar", "Axel", "Henrik", "Johan", "Anders", "Magnus", "Leo" };
    private static readonly string[] Last = { "Lindqvist", "Berg", "Nyström", "Johansson", "Andersson", "Karlsson", "Nilsson", "Eriksson", "Larsson", "Holm", "Sandberg", "Lund", "Ekström", "Wallin" };

    private static string Ascii(string s) =>
        new(s.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/persons", async (HttpContext http, IClock clock, PersonRequest? body) =>
        {
            var req = body ?? new PersonRequest(null, null, null, null, null, null, null);
            var country = (req.Country ?? "SE").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (country != "SE") errors.Add(new FieldError("country", "de momento solo SE (personnummer sueco)"));
            var minAge = req.Age ?? req.MinAge ?? 18;
            var maxAge = req.Age ?? req.MaxAge ?? 90;
            if (minAge is < 0 or > 110) errors.Add(new FieldError("minAge", "entre 0 y 110"));
            if (maxAge is < 0 or > 110) errors.Add(new FieldError("maxAge", "entre 0 y 110"));
            if (minAge > maxAge) errors.Add(new FieldError("minAge", "no puede ser mayor que maxAge"));
            if (req.Gender is not (null or "F" or "M")) errors.Add(new FieldError("gender", "F o M"));
            var n = req.Count ?? 1;
            if (n is < 1 or > 50) errors.Add(new FieldError("count", "entre 1 y 50"));
            if (errors.Count > 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "La petición de personas no es válida.",
                    "Ejemplo: { \"age\": 67, \"gender\": \"F\" } o { \"minAge\": 30, \"maxAge\": 40, \"count\": 5, \"seed\": 42 }", errors);

            var seed = req.Seed ?? Random.Shared.Next(1, int.MaxValue);
            var rnd = new Random(seed);
            var today = clock.Today;
            var addrReply = await Downstream.Get(http, "address-service", $"/api/v1/addresses?country={country}&count={n}&seed={seed}");
            if (!addrReply.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "address-service respondió " + addrReply.Status, "Revisa logs/address-service.log");
            var addresses = addrReply.Json.GetProperty("addresses").EnumerateArray().ToList();
            var people = new List<Person>();
            for (var i = 0; i < n; i++)
            {
                var female = req.Gender switch { "F" => true, "M" => false, _ => rnd.Next(2) == 0 };
                var age = rnd.Next(minAge, maxAge + 1);
                var birth = today.AddYears(-age).AddDays(-rnd.Next(1, 360));
                if (birth.AddYears(age) > today) birth = birth.AddYears(-1);
                var realAge = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
                var first = female ? Female[rnd.Next(Female.Length)] : Male[rnd.Next(Male.Length)];
                var last = Last[rnd.Next(Last.Length)];
                var a = addresses[i];
                people.Add(new Person(first, last, NationalId.Create(birth, rnd.Next(100), female), birth.ToString("yyyy-MM-dd"), realAge, female ? "F" : "M",
                    $"{Ascii(first).ToLowerInvariant()}.{Ascii(last).ToLowerInvariant()}.{rnd.Next(100, 999)}@correo.nordvik.test",
                    $"+46 7{rnd.Next(0, 10)} {rnd.Next(100, 999)} {rnd.Next(10, 99)} {rnd.Next(10, 99)}", "sv",
                    new PersonAddress(a.GetProperty("street").GetString()!, a.GetProperty("number").GetString()!, a.GetProperty("postalCode").GetString()!,
                        a.GetProperty("city").GetString()!, a.GetProperty("country").GetString()!)));
            }
            return Results.Ok(new PersonBatch(seed, n, people));
        }).WithTags("Personas").WithSummary("Generar personas sintéticas (reproducibles con seed)").Produces<PersonBatch>();

        app.MapGet("/api/v1/national-ids/validate", (string? value) => Results.Ok(NationalId.Check(value)))
            .WithTags("Personas").WithSummary("Validar un número nacional (formato, fecha y dígito de control)").Produces<NationalIdCheck>();
    }
}
