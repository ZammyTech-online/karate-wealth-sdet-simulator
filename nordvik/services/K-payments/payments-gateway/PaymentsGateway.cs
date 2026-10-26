using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DepositRequest(string? CustomerId, string? ExpectedAmount);
public sealed record WithdrawalRequest(string? CustomerId, string? Amount, string? BankAccountId);
public sealed record AssignRequest2(string? CustomerId);
public sealed record Payment(string Id, string Type, string CustomerId, string Status, string? Amount, string? ExpectedAmount, string? Reference, string? ToIban,
    string? MaskedIban, string? BankAccountId, string? InstructionId, string? HoldId, string? LedgerEntryId, string? Reason, bool AmountMismatch, string CreatedAt, string? CompletedAt, string? ExpiresAt);
public sealed record UnmatchedCredit(string Id, string Reference, string Amount, string? DebtorIban, string Status, string? AssignedTo, string ReceivedAt);

/// <summary>
/// payments-gateway: entrada de ingresos y retiradas del cliente.
/// Ingreso: el cliente pide una referencia OCR, transfiere desde su banco y, cuando llega el aviso del banco, se abona en el ledger.
/// Retirada: Idempotency-Key obligatoria, cuenta verificada, step-up por encima del umbral, límite diario, retención del dinero y
/// envío al banco; se completa o se rechaza de forma asíncrona.
/// </summary>
public sealed class PaymentsGateway : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "payments-gateway", "K-payments", 7000, 2, "Pagos y contabilidad",
        "Ingresos con referencia OCR y retiradas con Idempotency-Key, step-up (acr=high) por encima de 10 000 SEK, límite diario y saga asíncrona con el banco.",
        DependsOn: new[] { "ledger-service", "cash-management", "bank-account-verification", "payment-instructions", "idempotency-store", "customer-registry", "permission-service", "config-service", "event-bus", "clock-service" },
        Emits: new[] { "deposit.received", "withdrawal.requested", "payment.settled", "payment.rejected" },
        Consumes: new[] { "payment.credit.received", "payment.instruction.settled", "payment.instruction.rejected" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS payments (id TEXT PRIMARY KEY, type TEXT, customer_id TEXT, status TEXT, amount TEXT, expected_amount TEXT, reference TEXT UNIQUE, to_iban TEXT,
                        bank_account_id TEXT, iban TEXT, instruction_id TEXT, hold_id TEXT, ledger_entry_id TEXT, reason TEXT, mismatch INTEGER, created_at TEXT, created_date TEXT,
                        completed_at TEXT, expires_ticks INTEGER, expires_at TEXT);
                     CREATE TABLE IF NOT EXISTS unmatched (id TEXT PRIMARY KEY, reference TEXT, amount TEXT, debtor_iban TEXT, status TEXT, assigned_to TEXT, received_at TEXT)");
    }

    private static Payment? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, string Cust, string Status, string? Amount, string? Expected, string? Ref, string? ToIban, string? BaId, string? Iban,
            string? Instr, string? Hold, string? Entry, string? Reason, long Mismatch, string Created, string? Completed, string? Expires)>(
            @"SELECT id, type, customer_id, status, amount, expected_amount, reference, to_iban, bank_account_id, iban, instruction_id, hold_id, ledger_entry_id, reason, mismatch,
                     created_at, completed_at, expires_at FROM payments WHERE id = @id", new { id });
        return r.Id is null ? null : new Payment(r.Id, r.Type, r.Cust, r.Status, r.Amount, r.Expected, r.Ref, r.ToIban, r.Iban is null ? null : IbanMath.Mask(r.Iban),
            r.BaId, r.Instr, r.Hold, r.Entry, r.Reason, r.Mismatch == 1, r.Created, r.Completed, r.Expires);
    }

    /// <summary>Referencia OCR: 9 dígitos + dígito de control Luhn (como las referencias de pago suecas).</summary>
    private static string Ocr()
    {
        var body = "7" + Random.Shared.Next(10_000_000, 99_999_999);
        return body + NationalId.LuhnDigit(body);
    }

    private static async Task<string?> Ledger(HttpContext? http, string reference, string description, params (string Account, string? Debit, string? Credit)[] lines)
    {
        var r = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
        {
            reference, description, lines = lines.Select(l => new { account = l.Account, debit = l.Debit, credit = l.Credit }).ToList(),
        }, forwardAuth: false);
        if (r.Ok) return r.Str("id");
        if (r.Status == 409)
        {
            var list = await Downstream.Get(http, "ledger-service", $"/api/v1/journal-entries?reference={reference}", forwardAuth: false);
            return list.Ok && list.Json.GetArrayLength() > 0 ? list.Json[0].GetProperty("id").GetString() : null;
        }
        throw new InvalidOperationException("ledger-service respondió " + r.Status + ": " + r.Raw);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        // ---------- Ingresos ----------
        app.MapPost("/api/v1/deposits", async (HttpContext http, DepositRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "payments:write");
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(b.CustomerId)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta customerId.", null, new[] { new FieldError("customerId", "obligatorio") });
            if (b.ExpectedAmount is not null && (!Money.TryParse(b.ExpectedAmount, out var exp) || exp <= 0))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "expectedAmount debe ser un importe positivo.", null, new[] { new FieldError("expectedAmount", "importe positivo") });
            var guard = await CustomerGuard.Check(http, p, "payment:create", b.CustomerId);
            if (guard is not null) return guard;
            var id = "dep_" + Guid.NewGuid().ToString("N")[..16];
            var now = clock.UtcNow;
            using var db = ctx.OpenDb();
            db.Execute(@"INSERT INTO payments (id, type, customer_id, status, expected_amount, reference, to_iban, mismatch, created_at, created_date, expires_ticks, expires_at)
                         VALUES (@id, 'DEPOSIT', @CustomerId, 'AWAITING_FUNDS', @ExpectedAmount, @ocr, @iban, 0, @at, @d, @et, @ea)",
                new { id, b.CustomerId, b.ExpectedAmount, ocr = Ocr(), iban = IbanMath.Print(IbanMath.ClientMoneyAccount), at = Nordic.Iso(clock.LocalNow), d = Money.Date(clock.Today),
                      et = now.AddDays(7).UtcTicks, ea = Nordic.Iso(Nordic.ToStockholm(now.AddDays(7))) });
            return Results.Created($"/api/v1/payments/{id}", Load(db, id));
        }).WithTags("Ingresos").WithSummary("Pedir una referencia OCR para ingresar dinero (transfiere a toIban con esa referencia)").Produces<Payment>(201);

        // ---------- Retiradas ----------
        app.MapPost("/api/v1/withdrawals", async (HttpContext http, WithdrawalRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "payments:write");
            if (denied is not null) return denied;
            return await Idempotency.Run(http, "withdrawals", new { b.CustomerId, b.Amount, b.BankAccountId, p.Sub }, async () =>
            {
                IdempotentResult Fail(int status, string code, string detail, string? hint = null, IEnumerable<FieldError>? errors = null) =>
                    IdempotentResult.Problem(http, status, code, status switch { 400 => "Petición no válida", 403 => "Prohibido", 404 => "No encontrado", _ => "Regla de negocio incumplida" }, detail, hint, errors);
                var errors = new List<FieldError>();
                if (string.IsNullOrWhiteSpace(b.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
                if (!Money.TryParse(b.Amount, out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount) errors.Add(new FieldError("amount", "importe positivo, máximo 2 decimales, como texto"));
                if (string.IsNullOrWhiteSpace(b.BankAccountId)) errors.Add(new FieldError("bankAccountId", "obligatorio (una cuenta VERIFIED de bank-account-verification)"));
                if (errors.Count > 0) return Fail(400, "VALIDATION_FAILED", "Retirada no válida.", "Ejemplo: { \"customerId\": \"...\", \"amount\": \"500.00\", \"bankAccountId\": \"ba_...\" }", errors);
                var guard = await CustomerGuard.Check(http, p, "payment:create", b.CustomerId!);
                if (guard is not null)
                {
                    var status = guard is IStatusCodeHttpResult sc ? sc.StatusCode ?? 403 : 403;
                    return status == 404 ? Fail(404, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}.") : Fail(403, "ACCESS_DENIED", "No puedes retirar dinero de este cliente.");
                }
                var ba = await Downstream.Get(http, "bank-account-verification", $"/api/v1/internal/bank-accounts/{b.BankAccountId}", forwardAuth: false);
                if (!ba.Ok || ba.Str("customerId") != b.CustomerId) return Fail(404, "BANK_ACCOUNT_NOT_FOUND", $"El cliente no tiene la cuenta {b.BankAccountId}.");
                if (ba.Str("status") != "VERIFIED") return Fail(422, "BANK_ACCOUNT_NOT_VERIFIED", $"La cuenta está {ba.Str("status")}: solo se puede retirar a cuentas VERIFIED.");
                var threshold = await Config.GetDecimal(http, "payments-gateway", "stepUpThreshold", 10000m);
                if (amount > threshold && p.Role == "Customer" && !p.IsHighAssurance)
                    return Fail(403, "STEP_UP_REQUIRED", $"Las retiradas de más de {Money.Format(threshold, 2)} SEK exigen re-autenticación.",
                        "POST /api/v1/step-up (step-up-service, 6205), completa NordID y repite la retirada con el accessToken acr=high (y una Idempotency-Key nueva).");
                var limit = await Config.GetDecimal(http, "payments-gateway", "dailyWithdrawalLimit", 50000m);
                using var db = ctx.OpenDb();
                var today = db.Query<string>("SELECT amount FROM payments WHERE type = 'WITHDRAWAL' AND customer_id = @c AND created_date = @d AND status <> 'REJECTED'",
                    new { c = b.CustomerId, d = Money.Date(clock.Today) }).Sum(a => decimal.Parse(a, CultureInfo.InvariantCulture));
                if (today + amount > limit)
                    return Fail(422, "DAILY_LIMIT_EXCEEDED", $"Límite diario de retiradas: {Money.Format(limit, 2)} SEK. Hoy ya van {Money.Format(today, 2)}.", "El límite se reinicia a medianoche (hora de Estocolmo).");
                var id = "wdr_" + Guid.NewGuid().ToString("N")[..16];
                var hold = await Downstream.Post(http, "cash-management", "/api/v1/holds", new { customerId = b.CustomerId, amount = Money.Format(amount, 2), reference = id, reason = "Retirada" }, forwardAuth: false);
                if (hold.Status == 422) return Fail(422, "INSUFFICIENT_FUNDS", hold.Str("detail") ?? "Saldo insuficiente.", "Mira el disponible en cash-management: GET /api/v1/customers/{id}/cash");
                if (!hold.Ok) return new IdempotentResult(502, Problems.BodyOf(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "cash-management respondió " + hold.Status));
                var iban = ba.Str("iban")!;
                db.Execute(@"INSERT INTO payments (id, type, customer_id, status, amount, bank_account_id, iban, hold_id, mismatch, created_at, created_date)
                             VALUES (@id, 'WITHDRAWAL', @CustomerId, 'PROCESSING', @amount, @BankAccountId, @iban, @hold, 0, @at, @d)",
                    new { id, b.CustomerId, amount = Money.Format(amount, 2), b.BankAccountId, iban, hold = hold.Str("id"), at = Nordic.Iso(clock.LocalNow), d = Money.Date(clock.Today) });
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
                var instr = await Downstream.Post(http, "payment-instructions", "/api/v1/instructions", new
                {
                    paymentId = id, customerId = b.CustomerId, amount = Money.Format(amount, 2), creditorIban = iban, creditorName = c.Str("firstName") + " " + c.Str("lastName"), remittance = "Nordvik " + id,
                }, forwardAuth: false);
                if (!instr.Ok)
                {
                    await Downstream.Post(http, "cash-management", $"/api/v1/holds/{hold.Str("id")}/release", null, forwardAuth: false);
                    db.Execute("UPDATE payments SET status = 'REJECTED', reason = 'INSTRUCTION_FAILED' WHERE id = @id", new { id });
                    return new IdempotentResult(502, Problems.BodyOf(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "payment-instructions respondió " + instr.Status));
                }
                db.Execute("UPDATE payments SET instruction_id = @i WHERE id = @id", new { i = instr.Str("id"), id });
                Outbox.Add(db, http, clock, "withdrawal.requested", id, new { paymentId = id, customerId = b.CustomerId, amount = Money.Format(amount, 2) });
                return new IdempotentResult(201, Load(db, id)!, $"/api/v1/payments/{id}");
            });
        }).WithTags("Retiradas").WithSummary("Retirar dinero a una cuenta verificada (Idempotency-Key obligatoria)").Produces<Payment>(201);

        app.MapGet("/api/v1/payments/{id}", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "payments:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            ExpireDeposits(db);
            var pay = Load(db, id);
            if (pay is null) return Problems.NotFound(http, "PAYMENT_NOT_FOUND", $"No existe el pago {id}.", null);
            var d = await Permissions.Check(http, p, "payment:read", pay.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PAYMENT_NOT_FOUND", $"el pago {id}");
            return Results.Ok(pay);
        }).WithTags("Pagos").WithSummary("Un ingreso o una retirada").Produces<Payment>();

        app.MapGet("/api/v1/customers/{customerId}/payments", async (HttpContext http, string customerId, string? type, string? status) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "payments:read");
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "payment:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            ExpireDeposits(db);
            var ids = db.Query<string>("SELECT id FROM payments WHERE customer_id = @customerId AND (@type IS NULL OR type = @type) AND (@status IS NULL OR status = @status) ORDER BY created_at DESC",
                new { customerId, type, status });
            return Results.Ok(ids.Select(i => Load(db, i)!).ToList());
        }).WithTags("Pagos").WithSummary("Ingresos y retiradas de un cliente").Produces<List<Payment>>();

        app.MapGet("/api/v1/unmatched-credits", (HttpContext http) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<UnmatchedCredit>("SELECT id, reference, amount, debtor_iban AS DebtorIban, status, assigned_to AS AssignedTo, received_at AS ReceivedAt FROM unmatched ORDER BY received_at").ToList());
        }).WithTags("Ingresos").WithSummary("Abonos sin referencia conocida (en la cuenta transitoria suspense:unmatched)").Produces<List<UnmatchedCredit>>();

        app.MapPost("/api/v1/unmatched-credits/{id}/assign", async (HttpContext http, string id, AssignRequest2 b) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var u = db.QuerySingleOrDefault<UnmatchedCredit>("SELECT id, reference, amount, debtor_iban AS DebtorIban, status, assigned_to AS AssignedTo, received_at AS ReceivedAt FROM unmatched WHERE id = @id", new { id });
            if (u is null) return Problems.NotFound(http, "UNMATCHED_CREDIT_NOT_FOUND", $"No existe el abono {id}.", null);
            if (u.Status != "UNMATCHED") return Problems.Conflict(http, "ALREADY_ASSIGNED", $"Ya se asignó a {u.AssignedTo}.", null);
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            if (!c.Ok) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}.", null);
            await Ledger(http, "ASSIGN-" + id, "Asignación de abono sin referencia", ("suspense:unmatched", u.Amount, null), ($"customer:{b.CustomerId}:cash", null, u.Amount));
            db.Execute("UPDATE unmatched SET status = 'ASSIGNED', assigned_to = @c WHERE id = @id", new { c = b.CustomerId, id });
            return Results.Ok(db.QuerySingle<UnmatchedCredit>("SELECT id, reference, amount, debtor_iban AS DebtorIban, status, assigned_to AS AssignedTo, received_at AS ReceivedAt FROM unmatched WHERE id = @id", new { id }));
        }).WithTags("Ingresos").WithSummary("Asignar a un cliente un abono sin referencia").Produces<UnmatchedCredit>();

        // ---------- Saga asíncrona ----------
        EventConsumer.Start(app, ctx, new[] { "payment.credit.received", "payment.instruction.settled", "payment.instruction.rejected" }, async ev =>
        {
            using var db = ctx.OpenDb();
            ExpireDeposits(db);
            var pl = ev.Payload;
            if (ev.Topic == "payment.credit.received")
            {
                var reference = pl.GetProperty("reference").GetString()!;
                var amount = pl.GetProperty("amount").GetString()!;
                var dep = db.QuerySingleOrDefault<(string Id, string Cust, string? Expected)>("SELECT id, customer_id, expected_amount FROM payments WHERE reference = @reference AND status = 'AWAITING_FUNDS'", new { reference });
                if (dep.Id is null)
                {
                    var uid = "unm_" + pl.GetProperty("creditId").GetString();
                    if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM unmatched WHERE id = @uid", new { uid }) > 0) return;
                    await Ledger(null, "UNM-" + uid, "Abono sin referencia conocida", ("bank:client-money", amount, null), ("suspense:unmatched", null, amount));
                    db.Execute("INSERT INTO unmatched VALUES (@uid, @reference, @amount, @debtor, 'UNMATCHED', NULL, @at)",
                        new { uid, reference, amount, debtor = pl.TryGetProperty("debtorIban", out var di) ? di.GetString() : null, at = Nordic.Iso(clock.LocalNow) });
                    return;
                }
                var entry = await Ledger(null, "DEP-" + dep.Id, "Ingreso " + reference, ("bank:client-money", amount, null), ($"customer:{dep.Cust}:cash", null, amount));
                var mismatch = dep.Expected is not null && decimal.Parse(dep.Expected, CultureInfo.InvariantCulture) != decimal.Parse(amount, CultureInfo.InvariantCulture);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE payments SET status = 'RECEIVED', amount = @amount, ledger_entry_id = @entry, mismatch = @m, completed_at = @at WHERE id = @id",
                    new { amount, entry, m = mismatch ? 1 : 0, at = Nordic.Iso(clock.LocalNow), id = dep.Id }, tx);
                Outbox.Add(db, null, clock, "deposit.received", dep.Id, new { paymentId = dep.Id, customerId = dep.Cust, amount, amountMismatch = mismatch, notificationVars = new { amount } }, tx, correlationId: ev.CorrelationId);
                tx.Commit();
                return;
            }
            var id = ev.Key;
            var w = db.QuerySingleOrDefault<(string Id, string Cust, string Amount, string Status, string Hold, string Iban)>(
                "SELECT id, customer_id, amount, status, hold_id, iban FROM payments WHERE id = @id AND type = 'WITHDRAWAL'", new { id });
            if (w.Id is null || w.Status != "PROCESSING") return;
            var cust = await Downstream.Get(null, "customer-registry", $"/api/v1/internal/customers/{w.Cust}", forwardAuth: false);
            var firstName = cust.Ok ? cust.Str("firstName") : "";
            if (ev.Topic == "payment.instruction.settled")
            {
                var entry = await Ledger(null, "WDR-" + w.Id, "Retirada " + w.Id, ($"customer:{w.Cust}:cash", w.Amount, null), ("bank:client-money", null, w.Amount));
                await Downstream.Post(null, "cash-management", $"/api/v1/holds/{w.Hold}/capture", null, forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE payments SET status = 'COMPLETED', ledger_entry_id = @entry, completed_at = @at WHERE id = @id", new { entry, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, null, clock, "payment.settled", id, new { paymentId = id, customerId = w.Cust, amount = w.Amount,
                    notificationVars = new { firstName, amount = w.Amount, iban = IbanMath.Mask(w.Iban) } }, tx, correlationId: ev.CorrelationId);
                tx.Commit();
            }
            else
            {
                var reason = pl.TryGetProperty("reasonCode", out var rc) && rc.ValueKind == JsonValueKind.String ? rc.GetString() : "REJECTED";
                await Downstream.Post(null, "cash-management", $"/api/v1/holds/{w.Hold}/release", null, forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE payments SET status = 'REJECTED', reason = @reason, completed_at = @at WHERE id = @id", new { reason, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, null, clock, "payment.rejected", id, new { paymentId = id, customerId = w.Cust, amount = w.Amount, reasonCode = reason,
                    notificationVars = new { firstName, amount = w.Amount, reason } }, tx, correlationId: ev.CorrelationId);
                tx.Commit();
            }
        });

        void ExpireDeposits(Microsoft.Data.Sqlite.SqliteConnection db) =>
            db.Execute("UPDATE payments SET status = 'EXPIRED' WHERE type = 'DEPOSIT' AND status = 'AWAITING_FUNDS' AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
    }
}
