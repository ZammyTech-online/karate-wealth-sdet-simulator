using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record LookupRequest(string? NationalId, string? Token);

/// <summary>
/// population-adapter: Nordvik ↔ Registro de Población (E2). Se autentica con el certificado de cliente que guarda secrets-service (mTLS simulado),
/// acepta el número nacional en el cuerpo (nunca en la URL: acabaría en los logs) o un token de pii-vault, respeta la identidad protegida
/// (la dirección no sale) y avisa de los fallecidos con un evento.
/// </summary>
public sealed class PopulationAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "population-adapter", "P-integrations", 7503, 3, "Integraciones",
        "Consulta al Registro de Población (E2) con mTLS simulado: identidad protegida sin dirección, fallecidos (evento) y comparación con la ficha del cliente.",
        DependsOn: new[] { "population-sim", "secrets-service", "pii-vault", "customer-registry", "clock-service" }, Emits: new[] { "population.deceased-detected" });

    private static string? _cert;

    private static async Task<DownstreamReply> Ask(IClock clock, string nid)
    {
        _cert ??= (await Downstream.Send(null, "secrets-service", HttpMethod.Get, "/api/v1/secrets/population-client-cert", forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value");
        return await Downstream.SendUrl(HttpMethod.Get, ServiceDirectory.UrlOf("population-sim") + "/v1/persons/" + Uri.EscapeDataString(nid), null,
            new Dictionary<string, string> { ["X-Client-Cert"] = _cert ?? "" }, TimeSpan.FromSeconds(5));
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<IResult> Lookup(HttpContext http, Principal p, string nid, string? customerId)
        {
            DownstreamReply r;
            try { r = await Ask(clock, nid); }
            catch (DependencyUnavailableException) { return Problems.Of(http, 503, "POPULATION_REGISTER_UNAVAILABLE", "Registro no disponible", "El Registro de Población no respondió.", "Reintenta más tarde; no inventes la dirección."); }
            if (r.Status is 500 or 503) return Problems.Of(http, 503, "POPULATION_REGISTER_UNAVAILABLE", "Registro no disponible", $"El Registro respondió {r.Status}.", null);
            if (r.Status == 404) return Problems.NotFound(http, "PERSON_NOT_REGISTERED", "El número nacional no está en el Registro de Población.", null);
            if (r.Status is 401 or 403) return Problems.Of(http, 502, "POPULATION_AUTH_FAILED", "Error de autenticación con el Registro", "El Registro rechazó nuestro certificado: " + r.Raw, "Revisa el secreto population-client-cert.");
            if (!r.Ok) return Problems.Of(http, 502, "POPULATION_REGISTER_ERROR", "Error del Registro", $"El Registro respondió {r.Status}.", null);
            var j = r.Json;
            var isProtected = j.GetProperty("protectedIdentity").GetBoolean();
            var deceased = j.GetProperty("deceasedDate").ValueKind == System.Text.Json.JsonValueKind.String ? j.GetProperty("deceasedDate").GetString() : null;
            if (deceased is not null)
            {
                using var db = ctx.OpenDb();
                Outbox.Add(db, http, clock, "population.deceased-detected", customerId ?? NationalId.Mask(nid), new { customerId, deceasedDate = deceased });
            }
            return Results.Ok(new
            {
                nationalIdMasked = NationalId.Mask(nid),
                name = new { given = j.GetProperty("givenName").GetString(), surname = j.GetProperty("surname").GetString() },
                address = isProtected ? null : new { street = j.GetProperty("street").GetString(), postalCode = j.GetProperty("postalCode").GetString(), city = j.GetProperty("city").GetString() },
                protectedIdentity = isProtected, deceased = deceased is not null, deceasedDate = deceased, citizenship = j.GetProperty("citizenship").GetString(), source = "Registro de Población (E2)",
            });
        }

        app.MapPost("/api/v1/population/lookups", async (HttpContext http, LookupRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            string? nid = b.NationalId;
            if (nid is null && b.Token is not null)
            {
                var t = await Downstream.Send(http, "pii-vault", HttpMethod.Post, $"/api/v1/internal/tokens/{b.Token}/detokenize", new { purpose = "KYC_REVIEW" }, forwardAuth: false,
                    headers: new Dictionary<string, string> { ["X-Caller"] = "population-adapter" });
                if (t.Status == 404) return Problems.Unprocessable(http, "TOKEN_NOT_FOUND", $"pii-vault no conoce el token {b.Token}.", null);
                if (t.Status == 410) return Problems.Of(http, 410, "PII_ERASED", "Dato borrado", "El número nacional de ese token se borró.", null);
                nid = t.Str("value");
            }
            if (nid is null || !NationalId.Check(nid).Valid)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Envía nationalId válido o token de pii-vault.", "El número va en el cuerpo, nunca en la URL: { \"nationalId\": \"19850312-1234\" }");
            return await Lookup(http, p, NationalId.Check(nid).Normalized!, null);
        }).WithTags("Registro de Población").WithSummary("Buscar una persona por número nacional (en el cuerpo) o por token de pii-vault");

        app.MapGet("/api/v1/customers/{customerId}/population-check", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var res = await Lookup(http, p, c.Str("nationalId")!, customerId);
            if (res is not IValueHttpResult { Value: { } v }) return res;
            var reg = System.Text.Json.JsonSerializer.SerializeToElement(v, Canonical.Web);
            var differences = new List<string>();
            if (!string.Equals(reg.GetProperty("name").GetProperty("given").GetString(), c.Str("firstName"), StringComparison.OrdinalIgnoreCase)) differences.Add("firstName");
            if (!string.Equals(reg.GetProperty("name").GetProperty("surname").GetString(), c.Str("lastName"), StringComparison.OrdinalIgnoreCase)) differences.Add("lastName");
            return Results.Ok(new { customerId, matches = differences.Count == 0, differences, register = reg });
        }).WithTags("Registro de Población").WithSummary("Comparar la ficha del cliente con el Registro (nombre; fallecido; identidad protegida)");
    }
}
