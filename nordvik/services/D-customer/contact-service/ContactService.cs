using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AddContactRequest(string? Type, string? Value);
public sealed record VerifyRequest(string? Code);
public sealed record Contact(string Id, string CustomerId, string Type, string Value, string Status, long AttemptsLeft, string CreatedAt, string? VerifiedAt, string CodeExpiresAt);

/// <summary>
/// contact-service: emails y teléfonos verificados con un código de 6 cifras que caduca a los 10 minutos.
/// Hasta que exista notification-service (oleada 2), el código se lee en GET /_test/contacts/{id}/code.
/// </summary>
public sealed class ContactService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "contact-service", "D-customer", 6301, 1, "Clientes",
        "Medios de contacto del cliente con verificación por código: 3 intentos, caducidad de 10 minutos y bloqueo.",
        DependsOn: new[] { "clock-service", "customer-registry", "permission-service" });

    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase);
    private static readonly Regex Phone = new(@"^\+\d[\d ]{7,18}$");
    private const string Sel = "SELECT id, customer_id AS CustomerId, type, value, status, attempts_left AS AttemptsLeft, created_at AS CreatedAt, verified_at AS VerifiedAt, code_expires_at AS CodeExpiresAt FROM contacts";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS contacts (id TEXT PRIMARY KEY, customer_id TEXT, type TEXT, value TEXT, status TEXT, attempts_left INTEGER, created_at TEXT, verified_at TEXT, code TEXT, code_expires_at TEXT, code_expires_ticks INTEGER)");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM contacts WHERE value LIKE @p", new { p = prefix.ToLowerInvariant() + "%" });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/customers/{customerId}/contacts", async (HttpContext http, IClock clock, string customerId, AddContactRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var type = body.Type?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (type is not ("EMAIL" or "PHONE")) errors.Add(new FieldError("type", "EMAIL o PHONE"));
            else if (type == "EMAIL" && (body.Value is null || !Email.IsMatch(body.Value))) errors.Add(new FieldError("value", "email no válido"));
            else if (type == "PHONE" && (body.Value is null || !Phone.IsMatch(body.Value))) errors.Add(new FieldError("value", "teléfono internacional: +46 70 123 45 67"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "El contacto no es válido.", "Ejemplo: { \"type\": \"EMAIL\", \"value\": \"ana@correo.nordvik.test\" }", errors);
            var guard = await CustomerGuard.Check(http, p, "contact:write", customerId);
            if (guard is not null) return guard;
            var value = type == "EMAIL" ? body.Value!.Trim().ToLowerInvariant() : body.Value!.Trim();
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM contacts WHERE customer_id = @customerId AND value = @value AND status <> 'REMOVED'", new { customerId, value }) > 0)
                return Problems.Conflict(http, "CONTACT_ALREADY_EXISTS", "Ese contacto ya está registrado para el cliente.", null);
            var now = clock.UtcNow;
            var expires = now.AddMinutes(10);
            var c = new Contact("ctc_" + Guid.NewGuid().ToString("N")[..16], customerId, type!, value, "PENDING_VERIFICATION", 3, Nordic.Iso(Nordic.ToStockholm(now)), null, Nordic.Iso(Nordic.ToStockholm(expires)));
            var code = Random.Shared.Next(100000, 1000000).ToString();
            db.Execute("INSERT INTO contacts VALUES (@Id, @CustomerId, @Type, @Value, @Status, @AttemptsLeft, @CreatedAt, NULL, @code, @CodeExpiresAt, @ticks)",
                new { c.Id, c.CustomerId, c.Type, c.Value, c.Status, c.AttemptsLeft, c.CreatedAt, code, c.CodeExpiresAt, ticks = expires.UtcTicks });
            return Results.Created($"/api/v1/contacts/{c.Id}", c);
        }).WithTags("Contactos").WithSummary("Añadir un email o teléfono (queda pendiente de verificar)").Produces<Contact>(201);

        app.MapGet("/api/v1/customers/{customerId}/contacts", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "contact:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Contact>(Sel + " WHERE customer_id = @customerId AND status <> 'REMOVED' ORDER BY created_at", new { customerId }).ToList());
        }).WithTags("Contactos").WithSummary("Contactos de un cliente").Produces<List<Contact>>();

        app.MapPost("/api/v1/contacts/{id}/verify", async (HttpContext http, IClock clock, string id, VerifyRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<Contact>(Sel + " WHERE id = @id", new { id });
            if (c is null) return Problems.NotFound(http, "CONTACT_NOT_FOUND", $"No existe el contacto {id}.", null);
            var guard = await CustomerGuard.Check(http, p, "contact:write", c.CustomerId);
            if (guard is not null) return guard;
            if (c.Status == "VERIFIED") return Problems.Conflict(http, "ALREADY_VERIFIED", "El contacto ya estaba verificado.", null);
            if (c.Status == "LOCKED") return Problems.Of(http, 423, "CONTACT_LOCKED", "Bloqueado", "Demasiados intentos: el contacto está bloqueado.", "Bórralo y vuelve a añadirlo para recibir un código nuevo.");
            var row = db.QuerySingle<(string Code, long Ticks)>("SELECT code, code_expires_ticks FROM contacts WHERE id = @id", new { id });
            if (clock.UtcNow.UtcTicks > row.Ticks)
                return Problems.Of(http, 410, "CODE_EXPIRED", "Código caducado", "El código caducó a las " + c.CodeExpiresAt + ".", "Los códigos duran 10 minutos del reloj virtual.");
            if (body.Code != row.Code)
            {
                var left = c.AttemptsLeft - 1;
                db.Execute("UPDATE contacts SET attempts_left = @left, status = @st WHERE id = @id", new { left, st = left <= 0 ? "LOCKED" : c.Status, id });
                return left <= 0
                    ? Problems.Of(http, 423, "CONTACT_LOCKED", "Bloqueado", "Código incorrecto por tercera vez: el contacto queda bloqueado.", null)
                    : Problems.Unprocessable(http, "INVALID_CODE", $"Código incorrecto. Te quedan {left} intentos.", "En el entorno de pruebas lo lees con GET /_test/contacts/{id}/code.");
            }
            var verifiedAt = Nordic.Iso(clock.LocalNow);
            db.Execute("UPDATE contacts SET status = 'VERIFIED', verified_at = @verifiedAt WHERE id = @id", new { verifiedAt, id });
            return Results.Ok(c with { Status = "VERIFIED", VerifiedAt = verifiedAt });
        }).WithTags("Contactos").WithSummary("Verificar con el código recibido").Produces<Contact>();

        app.MapDelete("/api/v1/contacts/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<Contact>(Sel + " WHERE id = @id AND status <> 'REMOVED'", new { id });
            if (c is null) return Problems.NotFound(http, "CONTACT_NOT_FOUND", $"No existe el contacto {id}.", null);
            var guard = await CustomerGuard.Check(http, p, "contact:write", c.CustomerId);
            if (guard is not null) return guard;
            db.Execute("UPDATE contacts SET status = 'REMOVED' WHERE id = @id", new { id });
            return Results.NoContent();
        }).WithTags("Contactos").WithSummary("Quitar un contacto");

        app.MapGet("/_test/contacts/{id}/code", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var code = db.ExecuteScalar<string?>("SELECT code FROM contacts WHERE id = @id", new { id });
            return code is null ? Problems.NotFound(http, "CONTACT_NOT_FOUND", $"No existe el contacto {id}.", null) : Results.Ok(new { contactId = id, code });
        }).WithTags("Solo pruebas").WithSummary("Leer el código de verificación (solo en local y CI)");
    }
}
