using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RefundRequest(string? EntryId, string? Reason);
public sealed record ReturnRecord(string Id, string PaymentId, string? CustomerId, string Amount, string ReasonCode, string ReasonText, string RecordedAt);
public sealed record Refund(string Id, string EntryId, string ReversalEntryId, string Reason, string RequestedBy, string CreatedAt);

/// <summary>
/// refunds-returns: devoluciones. Registra cada retirada rechazada por el banco (con el significado del código ISO) y permite
/// a Operaciones devolver un cargo (por ejemplo una comisión) revirtiendo su asiento en el ledger.
/// </summary>
public sealed class RefundsReturns : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "refunds-returns", "K-payments", 7007, 2, "Pagos y contabilidad",
        "Pagos devueltos por el banco (AC04, AC06, AM02...) y devoluciones de cargos mediante asientos de reversión.",
        DependsOn: new[] { "ledger-service", "event-bus", "clock-service" }, Emits: new[] { "refund.issued" }, Consumes: new[] { "payment.instruction.rejected" });

    public static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>
    {
        ["AC01"] = "Número de cuenta incorrecto", ["AC04"] = "Cuenta cerrada", ["AC06"] = "Cuenta bloqueada", ["AM02"] = "Importe no permitido",
        ["BANK_UNAVAILABLE"] = "El banco no respondió tras varios reintentos", ["BANK_REFUSED"] = "El banco rechazó el mensaje",
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS returns (id TEXT PRIMARY KEY, payment_id TEXT UNIQUE, customer_id TEXT, amount TEXT, reason_code TEXT, reason_text TEXT, recorded_at TEXT);
                     CREATE TABLE IF NOT EXISTS refunds (id TEXT PRIMARY KEY, entry_id TEXT UNIQUE, reversal_entry_id TEXT, reason TEXT, requested_by TEXT, created_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/returns", (string? customerId) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<ReturnRecord>(@"SELECT id, payment_id AS PaymentId, customer_id AS CustomerId, amount, reason_code AS ReasonCode, reason_text AS ReasonText, recorded_at AS RecordedAt
                                                       FROM returns WHERE (@customerId IS NULL OR customer_id = @customerId) ORDER BY recorded_at", new { customerId }).ToList());
        }).WithTags("Devoluciones del banco").WithSummary("Pagos devueltos por el banco").Produces<List<ReturnRecord>>();

        app.MapGet("/api/v1/reason-codes", () => Results.Ok(Reasons)).WithTags("Devoluciones del banco").WithSummary("Significado de los códigos de motivo ISO 20022");

        app.MapPost("/api/v1/refunds", async (HttpContext http, RefundRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(b.EntryId) || string.IsNullOrWhiteSpace(b.Reason))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta entryId y reason.", "Ejemplo: { \"entryId\": \"je_...\", \"reason\": \"Comisión cobrada por error\" }");
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM refunds WHERE entry_id = @EntryId", b) > 0)
                return Problems.Conflict(http, "ALREADY_REFUNDED", "Ese cargo ya se devolvió.", null);
            var e = await Downstream.Get(http, "ledger-service", $"/api/v1/journal-entries/{b.EntryId}", forwardAuth: false);
            if (e.Status == 404) return Problems.NotFound(http, "ENTRY_NOT_FOUND", $"No existe el asiento {b.EntryId}.", null);
            var touchesCustomer = e.Json.GetProperty("lines").EnumerateArray().Any(l => l.GetProperty("account").GetString()!.StartsWith("customer:") && l.GetProperty("debit").GetString() != "0.00");
            if (!touchesCustomer) return Problems.Unprocessable(http, "NOT_A_CUSTOMER_CHARGE", "Solo se devuelven cargos al cliente (asientos con debe en customer:*:cash).", null);
            var rev = await Downstream.Post(http, "ledger-service", $"/api/v1/journal-entries/{b.EntryId}/reverse", new { reason = "Devolución: " + b.Reason }, forwardAuth: false);
            if (rev.Status == 409) return Problems.Conflict(http, "ALREADY_REVERSED", "El asiento ya estaba revertido en el ledger.", null);
            if (!rev.Ok) return Problems.Unprocessable(http, "CANNOT_REFUND", rev.Str("detail") ?? "El ledger no permitió la reversión.", null);
            var id = "ref_" + Guid.NewGuid().ToString("N")[..16];
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO refunds VALUES (@id, @EntryId, @rev, @Reason, @by, @at)", new { id, b.EntryId, rev = rev.Str("id"), b.Reason, by = p.Sub, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "refund.issued", id, new { refundId = id, entryId = b.EntryId, reversalEntryId = rev.Str("id") }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/refunds/{id}", db.QuerySingle<Refund>(
                "SELECT id, entry_id AS EntryId, reversal_entry_id AS ReversalEntryId, reason, requested_by AS RequestedBy, created_at AS CreatedAt FROM refunds WHERE id = @id", new { id }));
        }).WithTags("Devoluciones de cargos").WithSummary("Devolver un cargo al cliente (Operaciones)").Produces<Refund>(201);

        app.MapGet("/api/v1/refunds", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Refund>("SELECT id, entry_id AS EntryId, reversal_entry_id AS ReversalEntryId, reason, requested_by AS RequestedBy, created_at AS CreatedAt FROM refunds ORDER BY created_at").ToList());
        }).WithTags("Devoluciones de cargos").WithSummary("Devoluciones hechas").Produces<List<Refund>>();

        EventConsumer.Start(app, ctx, new[] { "payment.instruction.rejected" }, ev =>
        {
            var pl = ev.Payload;
            var code = pl.TryGetProperty("reasonCode", out var rc) && rc.ValueKind == JsonValueKind.String ? rc.GetString()! : "UNKNOWN";
            using var db = ctx.OpenDb();
            db.Execute("INSERT OR IGNORE INTO returns VALUES (@id, @pay, @cust, @amount, @code, @text, @at)",
                new { id = "ret_" + Guid.NewGuid().ToString("N")[..16], pay = pl.GetProperty("paymentId").GetString(), cust = pl.TryGetProperty("customerId", out var c) ? c.GetString() : null,
                      amount = pl.GetProperty("amount").GetString(), code, text = Reasons.TryGetValue(code, out var t) ? t : "Motivo desconocido", at = Nordic.Iso(clock.LocalNow) });
            return Task.CompletedTask;
        });
    }
}
