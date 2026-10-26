using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PopulationPerson(string PersonalNumber, string GivenName, string Surname, string? Street, string? PostalCode, string? City,
    bool ProtectedIdentity, string? DeceasedDate, string Citizenship);

/// <summary>
/// population-sim · sistema externo E2 (simulado): el Registro de Población. Autenticación por certificado de cliente (mTLS simulado:
/// la huella va en la cabecera X-Client-Cert). Personas con identidad protegida (sin dirección), fallecidas y desconocidas (404).
/// Sin precarga, cualquier número nacional válido devuelve una persona coherente generada a partir del número.
/// </summary>
public sealed class PopulationSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "population-sim", "X-external", 7702, 3, "Sistema externo (simulado)",
        "E2 Registro de Población: persona por número nacional con mTLS simulado, identidad protegida, fallecidos y 404; precarga en /__admin/persons.",
        DependsOn: new[] { "clock-service" });

    public const string ClientCert = "SHA256:nordvik-client-cert-2026";
    private static readonly string[] Given = { "Anna", "Erik", "Maria", "Lars", "Karin", "Johan", "Elin", "Nils", "Sara", "Oskar" };
    private static readonly string[] Surnames = { "Andersson", "Johansson", "Karlsson", "Nilsson", "Eriksson", "Larsson", "Olsson", "Persson", "Svensson", "Gustafsson" };
    private static readonly (string Street, string Postal, string City)[] Places =
        { ("Drottninggatan 12", "111 51", "Stockholm"), ("Kungsportsavenyen 3", "411 36", "Göteborg"), ("Stortorget 5", "211 22", "Malmö"), ("Storgatan 40", "753 31", "Uppsala") };
    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, PopulationPerson> _persons = new();
    private readonly ConcurrentDictionary<string, bool> _unknown = new();
    private readonly ConcurrentQueue<string> _seen = new();

    public static PopulationPerson Generate(string nid)
    {
        var h = Canonical.Sha256Hex(nid);
        int N(int i) => Convert.ToInt32(h.Substring(i * 2, 2), 16);
        var place = Places[N(2) % Places.Length];
        return new PopulationPerson(nid, Given[N(0) % Given.Length], Surnames[N(1) % Surnames.Length], place.Street, place.Postal, place.City, false, null, "SE");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);

        app.MapGet("/v1/persons/{personalNumber}", async (HttpContext http, string personalNumber) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var cert = http.Request.Headers["X-Client-Cert"].FirstOrDefault();
            if (cert is null) return Results.Json(new { error = "CLIENT_CERT_REQUIRED", message = "mutual TLS: no client certificate presented" }, statusCode: 401);
            if (cert != ClientCert) return Results.Json(new { error = "CLIENT_CERT_REJECTED", message = "certificate not trusted for this API" }, statusCode: 403);
            _seen.Enqueue(DateTimeOffset.UtcNow.ToString("O"));
            var check = NationalId.Check(personalNumber);
            if (!check.Valid) return Results.Json(new { error = "INVALID_PERSONAL_NUMBER", message = check.Reason }, statusCode: 400);
            var nid = check.Normalized!;
            if (_unknown.ContainsKey(nid)) return Results.Json(new { error = "NOT_FOUND", message = "no person registered with that number" }, statusCode: 404);
            var p = _persons.TryGetValue(nid, out var pre) ? pre : Generate(nid);
            if (p.ProtectedIdentity) p = p with { Street = null, PostalCode = null, City = null };
            return Results.Ok(p);
        }).WithTags("Registro de Población v1").WithSummary("Persona por número nacional (cabecera X-Client-Cert obligatoria)");

        app.MapPost("/__admin/persons", (PopulationPerson p) =>
        {
            var check = NationalId.Check(p.PersonalNumber);
            if (!check.Valid) return Results.Json(new { error = "INVALID_PERSONAL_NUMBER", message = check.Reason }, statusCode: 400);
            _persons[check.Normalized!] = p with { PersonalNumber = check.Normalized!, Citizenship = p.Citizenship ?? "SE" };
            _unknown.TryRemove(check.Normalized!, out _);
            return Results.Created($"/v1/persons/{check.Normalized}", _persons[check.Normalized!]);
        }).WithTags("Administración del simulador").WithSummary("Precargar una persona (identidad protegida, fallecida, otro nombre...)");
        app.MapPost("/__admin/unknown/{personalNumber}", (string personalNumber) => { _unknown[personalNumber] = true; return Results.Ok(new { personalNumber, registered = false }); })
            .WithTags("Administración del simulador").WithSummary("Hacer que un número no exista en el registro (404)");
        app.MapGet("/__admin/stats", () => Results.Ok(new { lookups = _seen.Count, preloaded = _persons.Count, unknown = _unknown.Count })).WithTags("Administración del simulador");
    }
}
