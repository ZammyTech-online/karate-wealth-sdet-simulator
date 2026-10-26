using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ErasureRequest(string? CustomerId, string? Reason);

/// <summary>
/// erasure-orchestrator: la saga del derecho al olvido (RGPD art. 17). Primero comprueba lo que lo impide (planes de pensión vivos,
/// carteras con dinero, pólizas activas): si hay algo, BLOCKED con los motivos. Si no: tritura las claves del cliente (crypto-shredding:
/// pii-vault deja sus tokens ERASED por evento), retira los consentimientos y cierra la ficha. Lo que la ley obliga a guardar (contabilidad,
/// informes fiscales, archivo legal) se conserva y se dice. Termina con un certificado con huella SHA-256. Un borrado por cliente.
/// </summary>
public sealed class ErasureOrchestrator : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "erasure-orchestrator", "F-privacy", 6507, 4, "Privacidad",
        "Saga del derecho al olvido: comprobación de bloqueos, crypto-shredding, retirada de consentimientos, cierre de la ficha, datos retenidos por ley y certificado.",
        DependsOn: new[] { "customer-registry", "pension-plan-service", "portfolio-service", "valuation-service", "insurance-policy", "key-management", "consent-ledger", "clock-service" },
        Emits: new[] { "erasure.completed", "erasure.blocked" });

    public static readonly string[] Retained = { "ledger-service (contabilidad, 7 años)", "tax-reporting (informes fiscales, 10 años)", "archive-service (archivo legal, 10 años)", "audit-service (auditoría)" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS requests (id TEXT PRIMARY KEY, customer_id TEXT, reason TEXT, status TEXT, blockers TEXT, steps TEXT, certificate TEXT, requested_by TEXT, created_at TEXT, completed_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string? Reason, string Status, string Blk, string Steps, string? Cert, string By, string At, string? Done)>(
            "SELECT id, customer_id, reason, status, blockers, steps, certificate, requested_by, created_at, completed_at FROM requests WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerRef = "tok_cu_" + Canonical.Sha256Hex(r.Cust)[..6], reason = r.Reason, status = r.Status, blockers = JsonDocument.Parse(r.Blk).RootElement,
            steps = JsonDocument.Parse(r.Steps).RootElement, retained = r.Status == "COMPLETED" ? Retained : Array.Empty<string>(),
            certificateSha256 = r.Cert, requestedBy = r.By, createdAt = r.At, completedAt = r.Done,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/erasure-requests", async (HttpContext http, ErasureRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Compliance", "Admin" }) is { } denied) return denied;
            var customerId = b.CustomerId ?? p.CustomerId;
            if (customerId is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId es obligatorio.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM requests WHERE customer_id = @customerId AND status = 'COMPLETED'", new { customerId }) is { } done)
                    return Problems.Conflict(http, "ALREADY_ERASED", $"Ese cliente ya se borró: {done}.", null);
            var blockers = new List<object>();
            var plans = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans?customerId={customerId}", forwardAuth: false);
            foreach (var pl in plans.Ok ? plans.Json.GetProperty("items").EnumerateArray() : Enumerable.Empty<JsonElement>())
                if (pl.GetProperty("status").GetString() is "ACTIVE" or "PAYOUT" or "FROZEN") blockers.Add(new { code = "PENSION_PLAN_ACTIVE", reference = pl.GetProperty("id").GetString(), detail = "Un plan de pensión vivo es un contrato en vigor." });
            var pfs = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios?customerId={customerId}", forwardAuth: false);
            foreach (var pf in pfs.Ok ? pfs.Json.EnumerateArray() : Enumerable.Empty<JsonElement>())
            {
                if (pf.GetProperty("type").GetString() == "PENSION" || pf.GetProperty("status").GetString() != "ACTIVE") continue;
                var v = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{pf.GetProperty("id").GetString()}/valuation", forwardAuth: false);
                if (v.Ok && Money.TryParse(v.Str("totalValueSek"), out var val) && val > 0) blockers.Add(new { code = "HOLDINGS", reference = pf.GetProperty("id").GetString(), detail = $"La cartera vale {Money.Format(val, 2)} SEK: hay que vender y retirar antes." });
            }
            var pols = await Downstream.Get(http, "insurance-policy", $"/api/v1/internal/insurance-policies?customerId={customerId}&status=ACTIVE", forwardAuth: false);
            foreach (var pol in pols.Ok ? pols.Json.GetProperty("items").EnumerateArray() : Enumerable.Empty<JsonElement>())
                blockers.Add(new { code = "INSURANCE_ACTIVE", reference = pol.GetProperty("id").GetString(), detail = "Hay un seguro en vigor." });

            var id = "era_" + Guid.NewGuid().ToString("N")[..12];
            var steps = new List<object>();
            string status = "BLOCKED"; string? cert = null;
            if (blockers.Count == 0)
            {
                var svc = await ServiceToken.Header(clock);
                var shred = await Downstream.Send(http, "key-management", HttpMethod.Post, "/api/v1/internal/subjects/shred", new { subject = $"customer:{customerId}", purpose = "RGPD art. 17 " + id }, forwardAuth: false);
                steps.Add(new { step = "CRYPTO_SHRED", ok = shred.Ok, detail = shred.Ok ? $"{shred.Json.GetProperty("shredded").GetArrayLength()} claves trituradas" : shred.Raw });
                var withdrawn = 0;
                foreach (var purpose in new[] { "MARKETING", "ANALYTICS", "PROFILING", "THIRD_PARTY_SHARING", "VOICE_RECORDING", "ESG_PROFILING" })
                {
                    var c = await Downstream.Get(http, "consent-ledger", $"/api/v1/internal/consents/check?customerId={customerId}&purpose={purpose}", forwardAuth: false);
                    if (c.Ok && c.Json.GetProperty("granted").GetBoolean())
                    {
                        var w = await Downstream.Send(http, "consent-ledger", HttpMethod.Post, "/api/v1/consents", new { customerId, purpose, granted = false, channel = "PAPER" }, forwardAuth: false, headers: svc);
                        if (w.Ok) withdrawn++;
                    }
                }
                steps.Add(new { step = "CONSENTS_WITHDRAWN", ok = true, detail = $"{withdrawn} consentimientos retirados" });
                var close = await Downstream.Send(http, "customer-registry", HttpMethod.Delete, $"/api/v1/customers/{customerId}", null, forwardAuth: false, headers: svc);
                steps.Add(new { step = "CUSTOMER_CLOSED", ok = close.Ok || close.Status == 409, detail = close.Ok ? "Ficha cerrada" : close.Status == 409 ? "Ya estaba cerrada" : close.Raw });
                status = "COMPLETED";
                cert = Canonical.Sha256Hex(Canonical.Of(new { id, customerRef = "tok_cu_" + Canonical.Sha256Hex(customerId)[..6], steps, retained = Retained, at = Nordic.Iso(clock.LocalNow) }));
            }
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO requests VALUES (@id, @customerId, @Reason, @status, @blk, @steps, @cert, @by, @at, @done)",
                    new { id, customerId, b.Reason, status, blk = JsonSerializer.Serialize(blockers, Canonical.Web), steps = JsonSerializer.Serialize(steps, Canonical.Web), cert, by = p.Sub,
                          at = Nordic.Iso(clock.LocalNow), done = status == "COMPLETED" ? Nordic.Iso(clock.LocalNow) : null }, tx);
                Outbox.Add(db, http, clock, status == "COMPLETED" ? "erasure.completed" : "erasure.blocked", id, new { requestId = id, blockers = blockers.Count }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/erasure-requests/{id}", Load(db, id));
        }).WithTags("Borrado").WithSummary("Pedir el borrado (BLOCKED con motivos, o COMPLETED con certificado)");

        app.MapGet("/api/v1/erasure-requests/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Compliance", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } x ? Results.Ok(x) : Problems.NotFound(http, "ERASURE_NOT_FOUND", $"No existe la solicitud {id}.", null);
        }).WithTags("Borrado").WithSummary("Una solicitud con sus pasos");
    }
}
