using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FixtureInfo(string Name, string Description, IReadOnlyList<string> Uses);
public sealed record FixtureRequest(string? ExternalRef, int? Seed);
public sealed record FixtureResult(string Fixture, string ExternalRef, IReadOnlyDictionary<string, object?> Created, IReadOnlyList<string> Steps);

/// <summary>
/// fixture-loader: monta escenarios completos con una sola llamada (persona → cliente → domicilio → cartera...).
/// Usa las APIs reales con el usuario asesor.erik, así que todo lo creado es tan real como si lo hicieras a mano.
/// </summary>
public sealed class FixtureLoader : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "fixture-loader", "A-utilities", 6010, 1, "Plataforma de datos de prueba",
        "Escenarios precargados: cliente básico, cliente con cartera, cliente jubilado. Llama a 4-6 servicios por ti.",
        DependsOn: new[] { "iam-service", "synthetic-person-service", "customer-registry", "customer-address-service", "portfolio-service" });

    private static readonly FixtureInfo[] Fixtures =
    {
        new("cliente-basico", "Un cliente nuevo de 30-60 años asignado a asesor.erik, con domicilio.", new[] { "synthetic-person-service", "customer-registry", "customer-address-service" }),
        new("cliente-con-cartera", "cliente-basico + una cartera ISK 60/40 (renta variable global / renta fija corta).", new[] { "synthetic-person-service", "customer-registry", "customer-address-service", "portfolio-service" }),
        new("cliente-jubilado", "Un cliente de 67 años con domicilio y una cartera prudente 20/80.", new[] { "synthetic-person-service", "customer-registry", "customer-address-service", "portfolio-service" }),
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/fixtures", () => Results.Ok(Fixtures)).WithTags("Escenarios").WithSummary("Escenarios disponibles").Produces<FixtureInfo[]>();

        app.MapPost("/api/v1/fixtures/{name}", async (HttpContext http, string name, FixtureRequest? body) =>
        {
            var fx = Fixtures.FirstOrDefault(f => f.Name == name);
            if (fx is null) return Problems.NotFound(http, "FIXTURE_NOT_FOUND", $"No existe el escenario '{name}'.", "Lista: " + string.Join(", ", Fixtures.Select(f => f.Name)));
            var steps = new List<string>();
            var created = new Dictionary<string, object?>();
            var externalRef = body?.ExternalRef ?? "FIX-" + Guid.NewGuid().ToString("N")[..8];

            var tokenReply = await Downstream.Send(http, "iam-service", HttpMethod.Post, "/api/v1/oauth/token",
                JsonSerializer.Serialize(new { grant_type = "password", username = "asesor.erik", password = "Erik2026!" }), forwardAuth: false);
            if (!tokenReply.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "iam-service no dio token.", null);
            var auth = new Dictionary<string, string> { ["Authorization"] = "Bearer " + tokenReply.Str("access_token") };
            steps.Add("Token de asesor.erik pedido a iam-service");

            var age = name == "cliente-jubilado" ? new { age = 67, seed = body?.Seed } as object : new { minAge = 30, maxAge = 60, seed = body?.Seed };
            var person = await Downstream.Post(http, "synthetic-person-service", "/api/v1/persons", age, forwardAuth: false);
            if (!person.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "synthetic-person-service respondió " + person.Status, null);
            var p = person.Json.GetProperty("persons")[0];
            steps.Add("Persona sintética generada");

            var customer = await Downstream.Send(http, "customer-registry", HttpMethod.Post, "/api/v1/customers", new
            {
                firstName = p.GetProperty("firstName").GetString(), lastName = p.GetProperty("lastName").GetString(), nationalId = p.GetProperty("nationalId").GetString(),
                email = p.GetProperty("email").GetString(), phone = p.GetProperty("phone").GetString(), language = "sv", externalRef,
            }, forwardAuth: false, headers: auth);
            if (!customer.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "customer-registry respondió " + customer.Status + ": " + customer.Str("code"), null);
            var customerId = customer.Str("id")!;
            created["customer"] = customer.Json;
            steps.Add($"Cliente {customer.Str("customerNumber")} creado y asignado a asesor.erik");

            var a = p.GetProperty("address");
            var addr = await Downstream.Send(http, "customer-address-service", HttpMethod.Put, $"/api/v1/customers/{customerId}/address", new
            {
                street = a.GetProperty("street").GetString(), number = a.GetProperty("number").GetString(), postalCode = a.GetProperty("postalCode").GetString(),
                city = a.GetProperty("city").GetString(), country = a.GetProperty("country").GetString(),
            }, forwardAuth: false, headers: auth);
            created["address"] = addr.Body;
            steps.Add("Domicilio registrado: " + addr.Status);

            if (name != "cliente-basico")
            {
                var allocation = name == "cliente-jubilado"
                    ? new object[] { new { isin = "SE0000000001", percent = 20 }, new { isin = "SE0000000101", percent = 80 } }
                    : new object[] { new { isin = "SE0000000001", percent = 60 }, new { isin = "SE0000000101", percent = 40 } };
                var pf = await Downstream.Send(http, "portfolio-service", HttpMethod.Post, "/api/v1/portfolios", new
                {
                    customerId, name = name == "cliente-jubilado" ? "Renta jubilación" : "Ahorro", type = "ISK", targetAllocation = allocation, externalRef,
                }, forwardAuth: false, headers: auth);
                if (!pf.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "portfolio-service respondió " + pf.Status, null);
                created["portfolio"] = pf.Json;
                steps.Add("Cartera creada");
            }
            created["login"] = new { advisor = "asesor.erik / Erik2026!", note = "Los clientes creados no tienen usuario propio: opera con el asesor." };
            return Results.Created($"/api/v1/fixtures/{name}", new FixtureResult(name, externalRef, created, steps));
        }).WithTags("Escenarios").WithSummary("Montar un escenario completo (borrable con data-reset-service por externalRef)").Produces<FixtureResult>(201);
    }
}
