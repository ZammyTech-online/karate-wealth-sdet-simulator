using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DsarRequest(string? CustomerId, string? Type);

/// <summary>
/// dsar-service: el derecho de acceso (art. 15) y de portabilidad (art. 20). Reúne lo que Nordvik sabe del cliente en varios servicios
/// (ficha, carteras, planes de pensión, pólizas, consentimientos) en un paquete. ACCESS lo explica todo; PORTABILITY solo lo que el cliente
/// aportó, en un formato máquina estable (nordvik-portability-v1). Plazo legal: 30 días. El paquete se descarga como mucho 3 veces y caduca a
/// los 7 días: luego 410.
/// </summary>
public sealed class DsarService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "dsar-service", "F-privacy", 6508, 4, "Privacidad",
        "Derechos de acceso y portabilidad: paquete con los datos del cliente reunidos de varios servicios, plazo de 30 días, 3 descargas y caducidad a los 7 días.",
        DependsOn: new[] { "customer-registry", "portfolio-service", "pension-plan-service", "insurance-policy", "consent-ledger", "permission-service", "clock-service" },
        Emits: new[] { "dsar.completed" });

    public const int MaxDownloads = 3, DueDays = 30, ExpiryDays = 7;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS requests (id TEXT PRIMARY KEY, customer_id TEXT, type TEXT, status TEXT, package TEXT, downloads INTEGER, due_on TEXT, expires_at TEXT, created_at TEXT, requested_by TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Type, string Status, long Dl, string Due, string Exp, string At)>(
            "SELECT id, customer_id, type, status, downloads, due_on, expires_at, created_at FROM requests WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var expired = DateTimeOffset.Parse(r.Exp) < clock.UtcNow || r.Dl >= MaxDownloads;
        return new { id = r.Id, customerId = r.Cust, type = r.Type, status = expired ? "EXPIRED" : r.Status, dueOn = r.Due, expiresAt = r.Exp, downloadsLeft = Math.Max(0, MaxDownloads - r.Dl), createdAt = r.At };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<(IResult? Fail, string Customer)> Owner(HttpContext http, Principal p, string id)
        {
            string? c;
            using (var db = ctx.OpenDb()) c = db.ExecuteScalar<string?>("SELECT customer_id FROM requests WHERE id = @id", new { id });
            if (c is null) return (Problems.NotFound(http, "DSAR_NOT_FOUND", $"No existe la solicitud {id}.", null), "");
            return (await CustomerGuard.Check(http, p, "customer:read", c), c);
        }

        app.MapPost("/api/v1/dsar", async (HttpContext http, DsarRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Compliance", "Admin" }) is { } denied) return denied;
            var type = (b.Type ?? "ACCESS").ToUpperInvariant();
            var customerId = b.CustomerId ?? p.CustomerId;
            if (customerId is null || type is not ("ACCESS" or "PORTABILITY")) return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId y type (ACCESS o PORTABILITY) son obligatorios.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios?customerId={customerId}", forwardAuth: false);
            var plans = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans?customerId={customerId}", forwardAuth: false);
            var pols = await Downstream.Get(http, "insurance-policy", $"/api/v1/internal/insurance-policies?customerId={customerId}", forwardAuth: false);
            var consents = new List<object>();
            foreach (var purpose in new[] { "MARKETING", "ANALYTICS", "PROFILING", "THIRD_PARTY_SHARING", "VOICE_RECORDING", "ESG_PROFILING" })
            {
                var c = await Downstream.Get(http, "consent-ledger", $"/api/v1/internal/consents/check?customerId={customerId}&purpose={purpose}", forwardAuth: false);
                if (c.Ok) consents.Add(new { purpose, granted = c.Json.GetProperty("granted").GetBoolean(), source = c.Str("source") });
            }
            var profile = new
            {
                firstName = cust.Str("firstName"), lastName = cust.Str("lastName"), nationalId = cust.Str("nationalId"), birthDate = cust.Str("birthDate"),
                email = cust.Str("email"), phone = cust.Str("phone"), language = cust.Str("language"),
            };
            object package = type == "PORTABILITY"
                ? new { format = "nordvik-portability-v1", exportedAt = Nordic.Iso(clock.LocalNow), subject = profile, consents }
                : new
                {
                    format = "nordvik-access-v1", generatedAt = Nordic.Iso(clock.LocalNow),
                    sections = new
                    {
                        profile = new { source = "customer-registry", purpose = "Identificación y obligaciones legales (KYC)", data = profile, customerNumber = cust.Str("customerNumber"), status = cust.Str("status") },
                        portfolios = new { source = "portfolio-service", purpose = "Ejecución del contrato de inversión", data = pfs.Ok ? pfs.Json : default },
                        pensionPlans = new { source = "pension-plan-service", purpose = "Ejecución del contrato de pensión", data = plans.Ok ? plans.Json.GetProperty("items") : default },
                        insurance = new { source = "insurance-policy", purpose = "Ejecución del contrato de seguro", data = pols.Ok ? pols.Json.GetProperty("items") : default },
                        consents = new { source = "consent-ledger", purpose = "Lo que el cliente ha autorizado", data = consents },
                    },
                    recipients = new[] { "Custodio (ejecución de órdenes)", "Agencia Tributaria (obligación legal)", "Aseguradora (si hay pólizas)" },
                    retention = "Contabilidad 7 años; informes fiscales y archivo legal 10 años; el resto, mientras dure la relación.",
                };
            var id = "dsar_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO requests VALUES (@id, @customerId, @type, 'COMPLETED', @pkg, 0, @due, @exp, @at, @by)",
                    new { id, customerId, type, pkg = JsonSerializer.Serialize(package, Canonical.Web), due = Money.Date(clock.Today.AddDays(DueDays)), exp = clock.UtcNow.AddDays(ExpiryDays).ToString("O"), at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
                Outbox.Add(db, http, clock, "dsar.completed", id, new { requestId = id, type }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/dsar/{id}", Load(db, id, clock));
        }).WithTags("Derechos del interesado").WithSummary("Pedir acceso (ACCESS) o portabilidad (PORTABILITY)");

        app.MapGet("/api/v1/dsar/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if ((await Owner(http, p, id)).Fail is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Derechos del interesado").WithSummary("Estado de la solicitud y descargas que quedan");

        app.MapGet("/api/v1/dsar/{id}/package", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if ((await Owner(http, p, id)).Fail is { } g) return g;
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(string Pkg, long Dl, string Exp)>("SELECT package, downloads, expires_at FROM requests WHERE id = @id", new { id });
            if (DateTimeOffset.Parse(r.Exp) < clock.UtcNow) return Problems.Of(http, 410, "PACKAGE_EXPIRED", "Paquete caducado", $"El paquete caducó a los {ExpiryDays} días.", "Pide una solicitud nueva.");
            if (r.Dl >= MaxDownloads) return Problems.Of(http, 410, "DOWNLOAD_LIMIT_REACHED", "Sin descargas", $"El paquete ya se descargó {MaxDownloads} veces.", "Pide una solicitud nueva.");
            db.Execute("UPDATE requests SET downloads = downloads + 1 WHERE id = @id", new { id });
            http.Response.Headers["X-Downloads-Left"] = (MaxDownloads - r.Dl - 1).ToString();
            return Results.Content(r.Pkg, "application/json", System.Text.Encoding.UTF8);
        }).WithTags("Derechos del interesado").WithSummary("Descargar el paquete (3 veces como mucho, 7 días)");
    }
}
