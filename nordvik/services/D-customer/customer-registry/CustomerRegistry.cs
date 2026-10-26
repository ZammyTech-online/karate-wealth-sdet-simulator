using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateCustomerRequest(string? FirstName, string? LastName, string? NationalId, string? Email, string? Phone, string? Language, string? ExternalRef);
public sealed record UpdateCustomerRequest(string? FirstName, string? LastName, string? Email, string? Phone, string? Language, string? NationalId);
public sealed record Customer(string Id, string CustomerNumber, string FirstName, string LastName, string NationalId, string BirthDate, long Age,
    string? Email, string? Phone, string Language, string Status, long Version, string CreatedAt, string UpdatedAt, string? ExternalRef);
public sealed record CustomerPage(IReadOnlyList<Customer> Items, int Page, int PageSize, int Total);
public sealed record CustomerChange(long Seq, string CustomerId, string Kind, string At);
public sealed record ChangeFeed(IReadOnlyList<CustomerChange> Items, long LastSeq);

/// <summary>
/// customer-registry: la ficha maestra del cliente. Equipo Clientes (camelCase).
/// Pide el número de cliente a sequence-service, decide permisos con permission-service y publica un registro de cambios.
/// </summary>
public sealed class CustomerRegistry : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "customer-registry", "D-customer", 6300, 1, "Clientes",
        "Alta, consulta, modificación (con If-Match) y cierre de clientes. Enmascara el número nacional según el rol y publica un registro de cambios.",
        DependsOn: new[] { "clock-service", "sequence-service", "permission-service" });

    private static readonly Regex NameRule = new(@"^[\p{L}][\p{L} '\-]{0,59}$");
    private static readonly Regex EmailRule = new(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase);
    private static readonly Regex PhoneRule = new(@"^\+\d[\d ]{7,18}$");
    private static readonly string[] Languages = { "sv", "en", "es" };
    private const string Sel = "SELECT id, customer_number AS CustomerNumber, first_name AS FirstName, last_name AS LastName, national_id AS NationalId, birth_date AS BirthDate, 0 AS Age, email, phone, language, status, version, created_at AS CreatedAt, updated_at AS UpdatedAt, external_ref AS ExternalRef FROM customers";
    private static readonly object Gate = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS customers (id TEXT PRIMARY KEY, customer_number TEXT UNIQUE, first_name TEXT, last_name TEXT, national_id TEXT UNIQUE,
                        birth_date TEXT, email TEXT, phone TEXT, language TEXT, status TEXT, version INTEGER, created_at TEXT, updated_at TEXT, external_ref TEXT);
                     CREATE TABLE IF NOT EXISTS changes (seq INTEGER PRIMARY KEY AUTOINCREMENT, customer_id TEXT, kind TEXT, at TEXT)");
        var seeds = new[]
        {
            (SeedIds.CustomerAna, "CUS-900001", "Ana", "Lindqvist", new DateOnly(1985, 3, 14), true, "ana.lindqvist@correo.nordvik.test", "+46 70 123 45 67", "sv"),
            (SeedIds.CustomerLars, "CUS-900002", "Lars", "Berg", new DateOnly(1958, 11, 2), false, "lars.berg@correo.nordvik.test", "+46 73 555 12 34", "sv"),
            (SeedIds.CustomerSofia, "CUS-900003", "Sofia", "Nyström", new DateOnly(1995, 7, 21), true, "sofia.nystrom@correo.nordvik.test", "+46 76 987 65 43", "es"),
        };
        foreach (var (id, num, fn, ln, birth, f, email, phone, lang) in seeds)
        {
            if (db.Execute(@"INSERT OR IGNORE INTO customers VALUES (@id, @num, @fn, @ln, @nid, @birth, @email, @phone, @lang, 'ACTIVE', 1, '2026-01-02T09:00:00.000+01:00', '2026-01-02T09:00:00.000+01:00', NULL)",
                new { id, num, fn, ln, nid = NationalId.Create(birth, 12, f), birth = birth.ToString("yyyy-MM-dd"), email, phone, lang }) > 0)
                db.Execute("INSERT INTO changes (customer_id, kind, at) VALUES (@id, 'CREATED', '2026-01-02T09:00:00.000+01:00')", new { id });
        }
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        var ids = db.Query<string>("SELECT id FROM customers WHERE external_ref LIKE @p", new { p = prefix + "%" }).ToList();
        foreach (var id in ids) db.Execute("INSERT INTO changes (customer_id, kind, at) VALUES (@id, 'DELETED', @at)", new { id, at = DateTimeOffset.Now.ToString("O") });
        return db.Execute("DELETE FROM customers WHERE external_ref LIKE @p", new { p = prefix + "%" });
    }

    private static Customer WithAge(Customer c, IClock clock)
    {
        var birth = DateOnly.Parse(c.BirthDate);
        var today = clock.Today;
        var age = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
        return c with { Age = age };
    }

    /// <summary>Lo que ve cada rol: el asesor no ve el número nacional completo.</summary>
    private static Customer View(Customer c, Principal p) => p.Role == "Advisor" ? c with { NationalId = NationalId.Mask(c.NationalId) } : c;

    private static Customer? Find(ServiceContext ctx, string id)
    {
        using var db = ctx.OpenDb();
        return db.QuerySingleOrDefault<Customer>(Sel + " WHERE id = @id", new { id });
    }

    private static void Change(ServiceContext ctx, IClock clock, string id, string kind)
    {
        using var db = ctx.OpenDb();
        db.Execute("INSERT INTO changes (customer_id, kind, at) VALUES (@id, @kind, @at)", new { id, kind, at = Nordic.Iso(clock.LocalNow) });
    }

    private static IResult NotFound(HttpContext http, string id) =>
        Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {id}.", "Los id de cliente son UUID. Los tres clientes semilla empiezan por c0000000-.");

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/customers").WithTags("Clientes");

        g.MapPost("", async (HttpContext http, IClock clock, CreateCustomerRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "customers:write");
            if (denied is not null) return denied;
            var perm = await Permissions.Check(http, p, "customer:create", null);
            if (!perm.Allowed) return Problems.Of(http, 403, "ACCESS_DENIED", "Prohibido", perm.Reason, "Dan de alta clientes los asesores, Operaciones y Admin.");

            var errors = new List<FieldError>();
            if (body.FirstName is null || !NameRule.IsMatch(body.FirstName)) errors.Add(new FieldError("firstName", "obligatorio, solo letras, máximo 60"));
            if (body.LastName is null || !NameRule.IsMatch(body.LastName)) errors.Add(new FieldError("lastName", "obligatorio, solo letras, máximo 60"));
            var nid = NationalId.Check(body.NationalId);
            if (!nid.Valid) errors.Add(new FieldError("nationalId", "no válido: " + nid.Reason));
            if (body.Email is not null && !EmailRule.IsMatch(body.Email)) errors.Add(new FieldError("email", "formato de email incorrecto"));
            if (body.Phone is not null && !PhoneRule.IsMatch(body.Phone)) errors.Add(new FieldError("phone", "formato internacional: +46 70 123 45 67"));
            var lang = body.Language ?? "sv";
            if (!Languages.Contains(lang)) errors.Add(new FieldError("language", "sv, en o es"));
            if (errors.Count > 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Los datos del cliente no son válidos.",
                    "¿Necesitas un número nacional válido? Pídelo a synthetic-person-service (POST /api/v1/persons, puerto 6005).", errors);

            var birth = DateOnly.Parse(nid.BirthDate!);
            var today = clock.Today;
            var age = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
            if (age < 18)
                return Problems.Unprocessable(http, "CUSTOMER_UNDERAGE", $"El cliente tiene {age} años: hace falta ser mayor de edad.", "Los menores se dan de alta con un tutor (lo verás en la oleada 4 con delegation-service).");

            using (var db = ctx.OpenDb())
                if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM customers WHERE national_id = @n", new { n = nid.Normalized }) > 0)
                    return Problems.Conflict(http, "CUSTOMER_ALREADY_EXISTS",
                        Chaos.TrapOn("T15") ? $"Ya existe un cliente con el número nacional {nid.Normalized}." : "Ya existe un cliente con ese número nacional.", // Trampa T15
                        "Genera una persona nueva para cada test.");

            await Downstream.Post(http, "sequence-service", "/api/v1/sequences", new { name = "customer-number", prefix = "CUS", padding = 6 }, forwardAuth: false);
            var next = await Downstream.Post(http, "sequence-service", "/api/v1/sequences/customer-number/next", null, forwardAuth: false);
            if (!next.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "sequence-service no dio número de cliente.", null);
            var number = next.Json.GetProperty("values")[0].GetString()!;

            var now = Nordic.Iso(clock.LocalNow);
            var c = new Customer(Guid.NewGuid().ToString(), number, body.FirstName!.Trim(), body.LastName!.Trim(), nid.Normalized!, nid.BirthDate!, age,
                body.Email, body.Phone, lang, "ACTIVE", 1, now, now, body.ExternalRef);
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                db.Execute(@"INSERT INTO customers VALUES (@Id, @CustomerNumber, @FirstName, @LastName, @NationalId, @BirthDate, @Email, @Phone, @Language, @Status, @Version, @CreatedAt, @UpdatedAt, @ExternalRef)", c);
            }
            Change(ctx, clock, c.Id, "CREATED");
            if (p.Role == "Advisor" && p.AdvisorId is not null)
                await Downstream.Post(http, "permission-service", "/api/v1/assignments", new { advisorId = p.AdvisorId, customerId = c.Id });
            http.Response.Headers.ETag = $"\"{c.Version}\"";
            return Results.Created($"/api/v1/customers/{c.Id}", View(c, p));
        }).WithSummary("Dar de alta un cliente (Advisor, Operations, Admin)").Produces<Customer>(201);

        g.MapGet("/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "customers:read");
            if (denied is not null) return denied;
            var c = Find(ctx, id);
            if (c is null) return NotFound(http, id);
            var perm = await Permissions.Check(http, p, "customer:read", id);
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "CUSTOMER_NOT_FOUND", $"el cliente {id}");
            http.Response.Headers.ETag = $"\"{c.Version}\"";
            return Results.Ok(View(WithAge(c, clock), p));
        }).WithSummary("Ver un cliente (el asesor ve el número nacional enmascarado)").Produces<Customer>();

        g.MapGet("", async (HttpContext http, IClock clock, string? q, string? status, int? page, int? pageSize) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Compliance", "Admin" }, scope: "customers:read");
            if (denied is not null) return denied;
            var pg = page ?? 1; var size = pageSize ?? 20;
            if (pg < 1 || size is < 1 or > 100)
                return Problems.BadRequest(http, "INVALID_QUERY", "page empieza en 1 y pageSize va de 1 a 100.", "Este equipo usa camelCase: pageSize.");
            using var db = ctx.OpenDb();
            IEnumerable<Customer> rows = db.Query<Customer>(Sel + " ORDER BY customer_number");
            if (p.Role == "Advisor")
            {
                var mine = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{p.AdvisorId}/customers", forwardAuth: false);
                var ids = mine.Json.EnumerateArray().Select(a => a.GetProperty("customerId").GetString()).ToHashSet();
                rows = rows.Where(r => ids.Contains(r.Id));
            }
            if (status is not null) rows = rows.Where(r => r.Status == status);
            if (q is not null) rows = rows.Where(r => $"{r.FirstName} {r.LastName} {r.CustomerNumber} {r.Email}".Contains(q, StringComparison.OrdinalIgnoreCase));
            var list = rows.ToList();
            return Results.Ok(new CustomerPage(list.Skip((pg - 1) * size).Take(size).Select(c => View(WithAge(c, clock), p)).ToList(), pg, size, list.Count));
        }).WithSummary("Buscar clientes (un asesor solo ve los suyos)").Produces<CustomerPage>();

        g.MapPatch("/{id}", async (HttpContext http, IClock clock, string id, UpdateCustomerRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "customers:write");
            if (denied is not null) return denied;
            var c = Find(ctx, id);
            if (c is null) return NotFound(http, id);
            var perm = await Permissions.Check(http, p, "customer:update", id);
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "CUSTOMER_NOT_FOUND", $"el cliente {id}");
            var ifMatch = http.Request.Headers.IfMatch.ToString();
            if (string.IsNullOrEmpty(ifMatch))
                return Problems.Of(http, 428, "PRECONDITION_REQUIRED", "Falta If-Match", "Para modificar hay que enviar la versión que leíste.",
                    "Lee el cliente, copia su cabecera ETag y envíala como If-Match. Así nadie pisa los cambios de otro.");
            if (ifMatch.Trim('"', ' ', 'W', '/') != c.Version.ToString())
                return Problems.Of(http, 412, "VERSION_CONFLICT", "La versión no coincide", $"Enviaste la versión {ifMatch} pero la actual es \"{c.Version}\".",
                    "Alguien lo cambió después de que lo leyeras. Vuelve a leer el cliente y repite el cambio.");
            if (body.NationalId is not null)
                return Problems.Unprocessable(http, "FIELD_NOT_UPDATABLE", "El número nacional no se puede cambiar.", "Un error en el número nacional se corrige con un proceso de Operaciones, no con PATCH.");
            if (c.Status == "CLOSED") return Problems.Conflict(http, "CUSTOMER_CLOSED", "El cliente está cerrado.", null);
            var errors = new List<FieldError>();
            if (body.FirstName is not null && !NameRule.IsMatch(body.FirstName)) errors.Add(new FieldError("firstName", "solo letras, máximo 60"));
            if (body.LastName is not null && !NameRule.IsMatch(body.LastName)) errors.Add(new FieldError("lastName", "solo letras, máximo 60"));
            if (body.Email is not null && !EmailRule.IsMatch(body.Email)) errors.Add(new FieldError("email", "formato de email incorrecto"));
            if (body.Phone is not null && !PhoneRule.IsMatch(body.Phone)) errors.Add(new FieldError("phone", "formato internacional: +46 70 123 45 67"));
            if (body.Language is not null && !Languages.Contains(body.Language)) errors.Add(new FieldError("language", "sv, en o es"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Algún campo no es válido.", null, errors);
            var updated = c with
            {
                FirstName = body.FirstName ?? c.FirstName, LastName = body.LastName ?? c.LastName, Email = body.Email ?? c.Email,
                Phone = body.Phone ?? c.Phone, Language = body.Language ?? c.Language, Version = c.Version + 1, UpdatedAt = Nordic.Iso(clock.LocalNow),
            };
            using (var db = ctx.OpenDb())
                db.Execute("UPDATE customers SET first_name=@FirstName, last_name=@LastName, email=@Email, phone=@Phone, language=@Language, version=@Version, updated_at=@UpdatedAt WHERE id=@Id", updated);
            Change(ctx, clock, id, "UPDATED");
            http.Response.Headers.ETag = $"\"{updated.Version}\"";
            return Results.Ok(View(WithAge(updated, clock), p));
        }).WithSummary("Modificar un cliente (exige If-Match con la versión)").Produces<Customer>();

        g.MapDelete("/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var c = Find(ctx, id);
            if (c is null) return NotFound(http, id);
            var perm = await Permissions.Check(http, p, "customer:close", id);
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "CUSTOMER_NOT_FOUND", $"el cliente {id}");
            if (c.Status == "CLOSED") return Problems.Conflict(http, "CUSTOMER_ALREADY_CLOSED", "El cliente ya estaba cerrado.", null);
            var closed = c with { Status = "CLOSED", Version = c.Version + 1, UpdatedAt = Nordic.Iso(clock.LocalNow) };
            using (var db = ctx.OpenDb()) db.Execute("UPDATE customers SET status='CLOSED', version=@Version, updated_at=@UpdatedAt WHERE id=@Id", closed);
            Change(ctx, clock, id, "CLOSED");
            return Results.Ok(WithAge(closed, clock));
        }).WithSummary("Cerrar un cliente (solo Admin): no se borra, pasa a CLOSED").Produces<Customer>();

        g.MapGet("/by-number/{customerNumber}", async (HttpContext http, IClock clock, string customerNumber) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "customers:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<Customer>(Sel + " WHERE customer_number = @customerNumber", new { customerNumber });
            if (c is null) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {customerNumber}.", "Formato: CUS-NNNNNN.");
            var perm = await Permissions.Check(http, p, "customer:read", c.Id);
            if (!perm.Allowed) return Permissions.Denied(http, p, perm, "CUSTOMER_NOT_FOUND", $"el cliente {customerNumber}");
            return Results.Ok(View(WithAge(c, clock), p));
        }).WithSummary("Buscar por número de cliente").Produces<Customer>();

        app.MapGet("/api/v1/internal/customers/changes", (long? since, int? limit) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<CustomerChange>("SELECT seq, customer_id AS CustomerId, kind, at FROM changes WHERE seq > @s ORDER BY seq LIMIT @l",
                new { s = since ?? 0, l = Math.Clamp(limit ?? 100, 1, 500) }).ToList();
            return Results.Ok(new ChangeFeed(items, items.Count > 0 ? items[^1].Seq : since ?? 0));
        }).WithTags("Interno").WithSummary("Registro de cambios (red interna, sin autenticación)").Produces<ChangeFeed>();

        app.MapGet("/api/v1/internal/customers/by-national-id/{nationalId}", (HttpContext http, IClock clock, string nationalId) =>
        {
            var n = NationalId.Check(nationalId);
            if (!n.Valid) return Problems.BadRequest(http, "INVALID_NATIONAL_ID", "Número nacional no válido: " + n.Reason, null);
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<Customer>(Sel + " WHERE national_id = @nid", new { nid = n.Normalized });
            return c is null ? Problems.NotFound(http, "CUSTOMER_NOT_FOUND", "Ningún cliente tiene ese número nacional.", null) : Results.Ok(WithAge(c, clock));
        }).WithTags("Interno").WithSummary("(interno) Cliente por número nacional (lo usan los ficheros de empleadores)").Produces<Customer>();

        app.MapGet("/api/v1/internal/customers/{id}", (HttpContext http, IClock clock, string id) =>
        {
            var c = Find(ctx, id);
            return c is null ? NotFound(http, id) : Results.Ok(WithAge(c, clock));
        }).WithTags("Interno").WithSummary("Cliente completo para otros servicios (red interna, sin autenticación)").Produces<Customer>();
    }
}
