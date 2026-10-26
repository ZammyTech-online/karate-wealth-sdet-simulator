using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DeathClaimRequest(string? CustomerId, string? DateOfDeath, string? ReportedBy);
public sealed record VerifyRequest(string? CertificateRef);

/// <summary>
/// death-claims: el fallecimiento de un cliente. Al abrir la reclamación se CONGELAN todos sus planes (FROZEN: ni aportaciones ni cobros) y se
/// suspenden sus planes de cobro. Al verificarla (certificado) se calcula el reparto de cada plan entre sus beneficiarios por porcentaje
/// (sin beneficiarios, todo al caudal hereditario: ESTATE). Al liquidarla los planes se cierran. Si tenía seguros de vida activos
/// (insurance-policy), la reclamación lo dice en insurancePart para que Operaciones abra el siniestro.
/// OPEN → VERIFIED → SETTLED; OPEN → REJECTED. Fuera de orden: 409 INVALID_STATE_TRANSITION.
/// </summary>
public sealed class DeathClaims : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "death-claims", "L-pensions", 7113, 3, "Pensiones",
        "Reclamaciones por fallecimiento: congelación de planes, suspensión de cobros, verificación, reparto por beneficiarios (o ESTATE) y liquidación.",
        DependsOn: new[] { "pension-plan-service", "payout-planner", "beneficiary-service", "valuation-service", "customer-registry", "clock-service" },
        OptionalDependsOn: new[] { "insurance-policy" },
        Emits: new[] { "death-claim.opened", "death-claim.verified", "death-claim.settled" });

    /// <summary>Reparte un importe por porcentajes al céntimo; el resto del redondeo va a la primera línea.</summary>
    public static List<decimal> Distribute(decimal amount, IReadOnlyList<decimal> percents)
    {
        var parts = percents.Select(p => Math.Round(amount * p / 100, 2, MidpointRounding.ToEven)).ToList();
        if (parts.Count > 0) parts[0] += amount - parts.Sum();
        return parts;
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS claims (id TEXT PRIMARY KEY, customer_id TEXT, date_of_death TEXT, reported_by TEXT, status TEXT, plans TEXT, distribution TEXT, certificate TEXT, reason TEXT, opened_at TEXT, updated_at TEXT)");
        db.Execute("CREATE TABLE IF NOT EXISTS claim_insurance (claim_id TEXT PRIMARY KEY, policies TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Dod, string? By, string Status, string Plans, string? Dist, string? Cert, string? Reason, string At, string Up)>(
            "SELECT id, customer_id, date_of_death, reported_by, status, plans, distribution, certificate, reason, opened_at, updated_at FROM claims WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var ins = JsonDocument.Parse(db.ExecuteScalar<string?>("SELECT policies FROM claim_insurance WHERE claim_id = @id", new { id }) ?? "[]").RootElement;
        return new
        {
            id = r.Id, customerId = r.Cust, dateOfDeath = r.Dod, reportedBy = r.By, status = r.Status, frozenPlans = JsonDocument.Parse(r.Plans).RootElement,
            distribution = r.Dist is null ? (JsonElement?)null : JsonDocument.Parse(r.Dist).RootElement, certificateRef = r.Cert, rejectionReason = r.Reason,
            insurancePart = ins.ValueKind == JsonValueKind.Null
                ? new { status = "NOT_CHECKED", policies = ins, note = "insurance-policy estaba apagado (es del nivel 4): revisa los seguros de vida antes de liquidar." }
                : ins.GetArrayLength() == 0
                ? new { status = "NO_POLICIES", policies = ins, note = "El cliente no tenía seguros de vida activos." }
                : new { status = "CLAIM_REQUIRED", policies = ins, note = "Operaciones abre un siniestro DEATH por cada póliza LIFE en insurance-claims." },
            openedAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] ops = { "Operations", "Admin" };

        IResult? Guard(HttpContext http, Microsoft.Data.Sqlite.SqliteConnection db, string id, string from)
        {
            var cur = db.ExecuteScalar<string?>("SELECT status FROM claims WHERE id = @id", new { id });
            if (cur is null) return Problems.NotFound(http, "CLAIM_NOT_FOUND", $"No existe la reclamación {id}.", null);
            return cur != from ? Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"La reclamación está {cur}; este paso necesita {from}.", null) : null;
        }

        app.MapPost("/api/v1/death-claims", async (HttpContext http, DeathClaimRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: ops) is { } denied) return denied;
            if (b.CustomerId is null || !Money.TryParseDate(b.DateOfDeath, out var dod)) return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId y dateOfDeath (AAAA-MM-DD) son obligatorios.", null);
            if (dod > clock.Today) return Problems.Unprocessable(http, "DATE_IN_FUTURE", "La fecha de fallecimiento no puede ser futura.", null);
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            if (!cust.Ok) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}.", null);
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM claims WHERE customer_id = @CustomerId AND status IN ('OPEN','VERIFIED','SETTLED')", new { b.CustomerId }) is { } open)
                    return Problems.Conflict(http, "CLAIM_ALREADY_OPEN", $"Ya hay una reclamación para ese cliente: {open}.", null);
            var plans = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans?customerId={b.CustomerId}", forwardAuth: false);
            var frozen = new List<object>();
            foreach (var pl in plans.Json.GetProperty("items").EnumerateArray())
            {
                var plId = pl.GetProperty("id").GetString()!;
                var before = pl.GetProperty("status").GetString();
                if (before is "CLOSED" or "TRANSFERRED_OUT") continue;
                await Downstream.Send(http, "pension-plan-service", HttpMethod.Post, $"/api/v1/internal/pension-plans/{plId}/status/FROZEN", new { reason = "DEATH_CLAIM" }, forwardAuth: false);
                var payouts = await Downstream.Get(http, "payout-planner", $"/api/v1/internal/payout-plans?pensionPlanId={plId}&status=ACTIVE", forwardAuth: false);
                var suspended = new List<string>();
                foreach (var po in payouts.Json.GetProperty("items").EnumerateArray())
                {
                    var poId = po.GetProperty("id").GetString()!;
                    await Downstream.Send(http, "payout-planner", HttpMethod.Post, $"/api/v1/internal/payout-plans/{poId}/status/SUSPENDED", null, forwardAuth: false);
                    suspended.Add(poId);
                }
                frozen.Add(new { planId = plId, statusBefore = before, portfolioId = pl.GetProperty("portfolioId").GetString(), suspendedPayoutPlans = suspended });
            }
            var id = "dc_" + Guid.NewGuid().ToString("N")[..12];
            var pols = await Downstream.GetOptional(http, "insurance-policy", $"/api/v1/internal/insurance-policies?customerId={b.CustomerId}&status=ACTIVE", forwardAuth: false);
            // insurance-policy es del nivel 4: si está apagado la reclamación sigue, pero dice que los seguros no se han revisado.
            var insurance = pols is { Ok: true } ? JsonSerializer.Serialize(pols.Json.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("coverage").GetString() == "LIFE")
                .Select(x => new { policyId = x.GetProperty("id").GetString(), coverage = "LIFE", capital = x.GetProperty("capital").GetProperty("amount").GetString() }).ToList(), Canonical.Web) : "null";
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO claim_insurance VALUES (@id, @p)", new { id, p = insurance }, tx);
                db.Execute("INSERT INTO claims VALUES (@id, @CustomerId, @dod, @ReportedBy, 'OPEN', @plans, NULL, NULL, NULL, @at, @at)",
                    new { id, b.CustomerId, dod = Money.Date(dod), b.ReportedBy, plans = JsonSerializer.Serialize(frozen, Canonical.Web), at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "death-claim.opened", id, new { claimId = id, customerId = b.CustomerId, frozenPlans = frozen.Count }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/death-claims/{id}", Load(db, id));
        }).WithTags("Fallecimientos").WithSummary("Abrir una reclamación: congela los planes y suspende los cobros");

        app.MapGet("/api/v1/death-claims/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin", "Compliance" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } c ? Results.Ok(c) : Problems.NotFound(http, "CLAIM_NOT_FOUND", $"No existe la reclamación {id}.", null);
        }).WithTags("Fallecimientos").WithSummary("Una reclamación con planes congelados y reparto");

        app.MapPost("/api/v1/death-claims/{id}/verify", async (HttpContext http, string id, VerifyRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.CertificateRef)) return Problems.BadRequest(http, "VALIDATION_FAILED", "certificateRef (el certificado de defunción) es obligatorio.", null);
            string plansJson;
            using (var db0 = ctx.OpenDb())
            {
                if (Guard(http, db0, id, "OPEN") is { } bad) return bad;
                plansJson = db0.ExecuteScalar<string>("SELECT plans FROM claims WHERE id = @id", new { id });
            }
            var dist = new List<object>();
            foreach (var pl in JsonDocument.Parse(plansJson).RootElement.EnumerateArray())
            {
                var plId = pl.GetProperty("planId").GetString()!;
                var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{pl.GetProperty("portfolioId").GetString()}/valuation", forwardAuth: false);
                var value = val.Ok && Money.TryParse(val.Str("totalValueSek"), out var v) ? v : 0;
                var ben = await Downstream.Get(http, "beneficiary-service", $"/api/v1/internal/pension-plans/{plId}/beneficiaries", forwardAuth: false);
                var list = ben.Json.GetProperty("beneficiaries").EnumerateArray().ToList();
                var shares = new List<object>();
                if (list.Count == 0) shares.Add(new { name = "Caudal hereditario", relation = "ESTATE", percent = "100.00", amount = Money.Format(value, 2) });
                else
                {
                    var amounts = Distribute(value, list.Select(x => decimal.Parse(x.GetProperty("percent").GetString()!, Money.Inv)).ToList());
                    shares.AddRange(list.Select((x, i) => (object)new { name = x.GetProperty("name").GetString(), relation = x.GetProperty("relation").GetString(), percent = x.GetProperty("percent").GetString(),
                        nationalIdMasked = x.TryGetProperty("nationalIdMasked", out var m) ? m.GetString() : null, amount = Money.Format(amounts[i], 2) }));
                }
                dist.Add(new { planId = plId, value = Money.Format(value, 2), currency = "SEK", shares });
            }
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("UPDATE claims SET status = 'VERIFIED', distribution = @d, certificate = @c, updated_at = @at WHERE id = @id",
                    new { id, d = JsonSerializer.Serialize(dist, Canonical.Web), c = b.CertificateRef, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "death-claim.verified", id, new { claimId = id, plans = dist.Count }, tx);
                tx.Commit();
            }
            return Results.Ok(Load(db, id));
        }).WithTags("Fallecimientos").WithSummary("Verificar con el certificado: calcula el reparto por beneficiarios");

        app.MapPost("/api/v1/death-claims/{id}/settle", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            string plansJson;
            using (var db0 = ctx.OpenDb())
            {
                if (Guard(http, db0, id, "VERIFIED") is { } bad) return bad;
                plansJson = db0.ExecuteScalar<string>("SELECT plans FROM claims WHERE id = @id", new { id });
            }
            foreach (var pl in JsonDocument.Parse(plansJson).RootElement.EnumerateArray())
            {
                var plId = pl.GetProperty("planId").GetString()!;
                await Downstream.Send(http, "pension-plan-service", HttpMethod.Post, $"/api/v1/internal/pension-plans/{plId}/status/CLOSED", new { reason = "DEATH_CLAIM_SETTLED" }, forwardAuth: false);
                foreach (var po in pl.GetProperty("suspendedPayoutPlans").EnumerateArray())
                    await Downstream.Send(http, "payout-planner", HttpMethod.Post, $"/api/v1/internal/payout-plans/{po.GetString()}/status/FINISHED", null, forwardAuth: false);
            }
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("UPDATE claims SET status = 'SETTLED', updated_at = @at WHERE id = @id", new { id, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "death-claim.settled", id, new { claimId = id }, tx);
                tx.Commit();
            }
            return Results.Ok(Load(db, id));
        }).WithTags("Fallecimientos").WithSummary("Liquidar: cierra los planes y termina los cobros");

        app.MapPost("/api/v1/death-claims/{id}/reject", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            string reason;
            using (var reader = new StreamReader(http.Request.Body)) { var raw = await reader.ReadToEndAsync(); reason = raw.Length > 0 && JsonDocument.Parse(raw).RootElement.TryGetProperty("reason", out var x) ? x.GetString() ?? "" : ""; }
            if (string.IsNullOrWhiteSpace(reason)) return Problems.BadRequest(http, "VALIDATION_FAILED", "reason es obligatorio.", null);
            using var db = ctx.OpenDb();
            if (Guard(http, db, id, "OPEN") is { } bad) return bad;
            db.Execute("UPDATE claims SET status = 'REJECTED', reason = @reason, updated_at = @at WHERE id = @id", new { id, reason, at = Nordic.Iso(clock.LocalNow) });
            return Results.Ok(new { claim = Load(db, id), note = "Los planes siguen FROZEN: un plan congelado solo puede cerrarse. Revisión manual en case-management." });
        }).WithTags("Fallecimientos").WithSummary("Rechazar una reclamación abierta (error de aviso)");
    }
}
