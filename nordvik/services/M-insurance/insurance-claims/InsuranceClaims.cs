using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record InsuranceClaimRequest(string? PolicyId, string? Type, string? EventDate);

/// <summary>
/// insurance-claims: siniestros de las pólizas. Operaciones los abre (DEATH en pólizas LIFE, DISABILITY en DISABILITY), se mandan a la
/// aseguradora y quedan SUBMITTED. Al consultarlos se sincronizan: APPROVED → se cobra el capital (asiento en ledger) y queda PAID, y la
/// póliza pasa a CLAIMED; REJECTED → REJECTED. Un siniestro abierto por póliza.
/// </summary>
public sealed class InsuranceClaims : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "insurance-claims", "M-insurance", 7203, 4, "Seguros",
        "Siniestros de vida e invalidez: apertura por Operaciones, envío a la aseguradora, sincronización del estado y cobro contable del capital aprobado.",
        DependsOn: new[] { "insurance-policy", "insurer-adapter", "ledger-service", "clock-service" }, Emits: new[] { "insurance-claim.submitted", "insurance-claim.paid", "insurance-claim.rejected" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS claims (id TEXT PRIMARY KEY, policy_id TEXT, customer_id TEXT, type TEXT, event_date TEXT, insurer_claim TEXT, status TEXT, amount TEXT, ledger_entry TEXT, created_at TEXT, updated_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Pol, string Cust, string Type, string Ev, string Ins, string Status, string? Amt, string? Led, string At, string Up)>(
            "SELECT id, policy_id, customer_id, type, event_date, insurer_claim, status, amount, ledger_entry, created_at, updated_at FROM claims WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, policyId = r.Pol, customerId = r.Cust, type = r.Type, eventDate = r.Ev, insurerClaimId = r.Ins, status = r.Status,
            paidAmount = r.Amt is null ? null : new { amount = r.Amt, currency = "SEK" }, ledgerEntryId = r.Led, createdAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Admin", "Compliance" };

        async Task Sync(HttpContext http, string id)
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(string Status, string Ins, string Pol)>("SELECT status, insurer_claim, policy_id FROM claims WHERE id = @id", new { id });
            if (r.Status != "SUBMITTED") return;
            var s = await Downstream.Get(http, "insurer-adapter", $"/api/v1/internal/claims/{r.Ins}", forwardAuth: false);
            if (!s.Ok) return;
            var status = s.Str("status");
            if (status == "REJECTED")
            {
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE claims SET status = 'REJECTED', updated_at = @at WHERE id = @id", new { id, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "insurance-claim.rejected", id, new { claimId = id }, tx);
                tx.Commit();
            }
            else if (status == "APPROVED")
            {
                var amount = s.Str("approvedAmount")!;
                var entry = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
                {
                    reference = "INSCLAIM-" + id, description = $"Capital asegurado del siniestro {id}",
                    lines = new object[] { new { account = "bank:insurance-receivable", debit = amount }, new { account = "bank:payouts-outgoing", credit = amount } },
                }, forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE claims SET status = 'PAID', amount = @amount, ledger_entry = @e, updated_at = @at WHERE id = @id", new { id, amount, e = entry.Str("id"), at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "insurance-claim.paid", id, new { claimId = id, amount }, tx);
                tx.Commit();
                await Downstream.Send(http, "insurance-policy", HttpMethod.Post, $"/api/v1/internal/insurance-policies/{r.Pol}/claimed", null, forwardAuth: false);
            }
        }

        app.MapPost("/api/v1/insurance-claims", async (HttpContext http, InsuranceClaimRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            var type = (b.Type ?? "").ToUpperInvariant();
            if (b.PolicyId is null || type is not ("DEATH" or "DISABILITY") || !Money.TryParseDate(b.EventDate, out var ev))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "policyId, type (DEATH o DISABILITY) y eventDate (AAAA-MM-DD) son obligatorios.", null);
            if (ev > clock.Today) return Problems.Unprocessable(http, "DATE_IN_FUTURE", "El siniestro no puede ser futuro.", null);
            var pol = await Downstream.Get(http, "insurance-policy", $"/api/v1/internal/insurance-policies/{b.PolicyId}", forwardAuth: false);
            if (!pol.Ok) return Problems.NotFound(http, "POLICY_NOT_FOUND", $"No existe la póliza {b.PolicyId}.", null);
            if (pol.Str("status") != "ACTIVE") return Problems.Conflict(http, "POLICY_NOT_ACTIVE", $"La póliza está {pol.Str("status")}.", null);
            if ((type == "DEATH") != (pol.Str("coverage") == "LIFE")) return Problems.Unprocessable(http, "COVERAGE_MISMATCH", $"Un siniestro {type} no lo cubre una póliza {pol.Str("coverage")}.", null);
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM claims WHERE policy_id = @PolicyId AND status = 'SUBMITTED'", new { b.PolicyId }) is { } open)
                    return Problems.Conflict(http, "CLAIM_ALREADY_OPEN", $"Ya hay un siniestro abierto: {open}.", null);
            var ins = await Downstream.Send(http, "insurer-adapter", HttpMethod.Post, "/api/v1/internal/claims",
                new { policyNumber = pol.Str("insurerPolicyNumber"), type, eventDate = Money.Date(ev), amount = pol.Json.GetProperty("capital").GetProperty("amount").GetString() }, forwardAuth: false);
            if (!ins.Ok) return Results.Content(ins.Raw, "application/problem+json", System.Text.Encoding.UTF8, ins.Status);
            var id = "cl_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO claims VALUES (@id, @PolicyId, @cust, @type, @ev, @ins, 'SUBMITTED', NULL, NULL, @at, @at)",
                    new { id, b.PolicyId, cust = pol.Str("customerId"), type, ev = Money.Date(ev), ins = ins.Str("claimId"), at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "insurance-claim.submitted", id, new { claimId = id, policyId = b.PolicyId, type }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/insurance-claims/{id}", Load(db, id));
        }).WithTags("Siniestros").WithSummary("Abrir un siniestro (Operaciones)");

        app.MapGet("/api/v1/insurance-claims/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<long>("SELECT COUNT(*) FROM claims WHERE id = @id", new { id }) == 0) return Problems.NotFound(http, "CLAIM_NOT_FOUND", $"No existe el siniestro {id}.", null);
            await Sync(http, id);
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Siniestros").WithSummary("Estado del siniestro (se sincroniza con la aseguradora)");
    }
}
