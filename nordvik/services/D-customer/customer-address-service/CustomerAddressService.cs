using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SetAddressRequest(string? Street, string? Number, string? PostalCode, string? City, string? Country);
public sealed record CustomerAddress(string CustomerId, long Version, string Street, string Number, string PostalCode, string City, string Country, string ValidFrom, string? ValidTo);

/// <summary>customer-address-service: domicilio del cliente con historial. Valida el código postal con address-service.</summary>
public sealed class CustomerAddressService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "customer-address-service", "D-customer", 6302, 1, "Clientes",
        "Domicilio actual e historial de domicilios del cliente; el código postal debe corresponder a la ciudad.",
        DependsOn: new[] { "clock-service", "customer-registry", "permission-service", "address-service" });

    private const string Sel = "SELECT customer_id AS CustomerId, version, street, number, postal_code AS PostalCode, city, country, valid_from AS ValidFrom, valid_to AS ValidTo FROM addresses";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS addresses (customer_id TEXT, version INTEGER, street TEXT, number TEXT, postal_code TEXT, city TEXT, country TEXT, valid_from TEXT, valid_to TEXT, PRIMARY KEY (customer_id, version))");
        db.Execute(@"INSERT OR IGNORE INTO addresses VALUES (@a, 1, 'Drottninggatan', '12', '111 51', 'Stockholm', 'SE', '2026-01-02T09:00:00.000+01:00', NULL),
                     (@l, 1, 'Kungsgatan', '7', '411 19', 'Göteborg', 'SE', '2026-01-02T09:00:00.000+01:00', NULL),
                     (@s, 1, 'Storgatan', '3', '753 20', 'Uppsala', 'SE', '2026-01-02T09:00:00.000+01:00', NULL)",
            new { a = SeedIds.CustomerAna, l = SeedIds.CustomerLars, s = SeedIds.CustomerSofia });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/customers/{customerId}/address").WithTags("Domicilio");

        app.MapGet("/api/v1/internal/customers/{customerId}/address", (HttpContext http, string customerId) =>
        {
            using var db = ctx.OpenDb();
            var a = db.QuerySingleOrDefault<CustomerAddress>(Sel + " WHERE customer_id = @customerId AND valid_to IS NULL", new { customerId });
            return a is null ? Problems.NotFound(http, "ADDRESS_NOT_FOUND", "El cliente no tiene domicilio registrado.", null) : Results.Ok(a);
        }).WithTags("Interno").WithSummary("Domicilio actual para otros servicios (red interna)").Produces<CustomerAddress>();

        g.MapGet("", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "address:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            var a = db.QuerySingleOrDefault<CustomerAddress>(Sel + " WHERE customer_id = @customerId AND valid_to IS NULL", new { customerId });
            return a is null ? Problems.NotFound(http, "ADDRESS_NOT_FOUND", "El cliente no tiene domicilio registrado.", "Regístralo con PUT en esta misma ruta.") : Results.Ok(a);
        }).WithSummary("Domicilio actual").Produces<CustomerAddress>();

        g.MapGet("/history", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "address:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<CustomerAddress>(Sel + " WHERE customer_id = @customerId ORDER BY version", new { customerId }).ToList());
        }).WithSummary("Todos los domicilios que ha tenido").Produces<List<CustomerAddress>>();

        g.MapPut("", async (HttpContext http, IClock clock, string customerId, SetAddressRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Street)) errors.Add(new FieldError("street", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.Number)) errors.Add(new FieldError("number", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.PostalCode)) errors.Add(new FieldError("postalCode", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.City)) errors.Add(new FieldError("city", "obligatorio"));
            var country = (body.Country ?? "SE").ToUpperInvariant();
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "El domicilio está incompleto.", "Una dirección válida la tienes en address-service (GET /api/v1/addresses).", errors);
            var guard = await CustomerGuard.Check(http, p, "address:write", customerId);
            if (guard is not null) return guard;
            var pc = await Downstream.Get(http, "address-service", $"/api/v1/postal-codes/{country}/{Uri.EscapeDataString(body.PostalCode!)}", forwardAuth: false);
            if (pc.Status == 400) return Problems.BadRequest(http, "UNSUPPORTED_COUNTRY", $"País {country} no soportado.", "SE, NO, DK o FI.");
            if (!pc.Ok || !pc.Json.GetProperty("valid").GetBoolean())
                return Problems.Unprocessable(http, "INVALID_POSTAL_CODE", $"El código postal {body.PostalCode} no es válido en {country}: {pc.Str("reason")}.", "En Suecia el formato es NNN NN, con espacio.");
            if (!string.Equals(pc.Str("city"), body.City, StringComparison.OrdinalIgnoreCase))
                return Problems.Unprocessable(http, "POSTAL_CODE_CITY_MISMATCH", $"El código {body.PostalCode} es de {pc.Str("city")}, no de {body.City}.", "address-service te dice la ciudad de cada código postal.");
            var now = Nordic.Iso(clock.LocalNow);
            using var db = ctx.OpenDb();
            var version = db.ExecuteScalar<long>("SELECT COALESCE(MAX(version), 0) FROM addresses WHERE customer_id = @customerId", new { customerId }) + 1;
            db.Execute("UPDATE addresses SET valid_to = @now WHERE customer_id = @customerId AND valid_to IS NULL", new { now, customerId });
            var a = new CustomerAddress(customerId, version, body.Street!.Trim(), body.Number!.Trim(), body.PostalCode!, pc.Str("city")!, country, now, null);
            db.Execute("INSERT INTO addresses VALUES (@CustomerId, @Version, @Street, @Number, @PostalCode, @City, @Country, @ValidFrom, NULL)", a);
            return Results.Ok(a);
        }).WithSummary("Registrar un domicilio nuevo (el anterior pasa al historial)").Produces<CustomerAddress>();
    }
}
