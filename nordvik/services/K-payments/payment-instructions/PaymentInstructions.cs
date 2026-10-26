using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record InstructionRequest(string? PaymentId, string? CustomerId, string? Amount, string? CreditorIban, string? CreditorName, string? Remittance);
public sealed record Instruction(string Id, string PaymentId, string CustomerId, string MsgId, string EndToEndId, string Amount, string MaskedCreditorIban, string Status,
    string? ReasonCode, int Attempts, string? LastError, string CreatedAt, string? SentAt, string? FinishedAt);

/// <summary>
/// payment-instructions: convierte una retirada en un mensaje de pago para el banco (msgId y endToEndId únicos),
/// lo reintenta si el banco no está disponible (QUEUED) y sigue su estado con los eventos payment.bank-status.changed.
/// </summary>
public sealed class PaymentInstructions : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "payment-instructions", "K-payments", 7001, 2, "Pagos y contabilidad",
        "Instrucciones de pago hacia NordPay: identificadores ISO 20022, cola de reintentos si el banco cae y estados SENT → SETTLED / REJECTED.",
        DependsOn: new[] { "nordpay-adapter", "event-bus", "sequence-service", "clock-service" },
        Emits: new[] { "payment.instruction.settled", "payment.instruction.rejected" }, Consumes: new[] { "payment.bank-status.changed" });

    private Timer? _timer;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS instructions (id TEXT PRIMARY KEY, payment_id TEXT UNIQUE, customer_id TEXT, msg_id TEXT UNIQUE, end_to_end_id TEXT UNIQUE, amount TEXT,
                     creditor_iban TEXT, creditor_name TEXT, remittance TEXT, status TEXT, reason TEXT, attempts INTEGER, last_error TEXT, next_ticks INTEGER,
                     created_at TEXT, sent_at TEXT, finished_at TEXT)");
    }

    private static Instruction? Load(Microsoft.Data.Sqlite.SqliteConnection db, string where, object args)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Pay, string Cust, string Msg, string E2E, string Amount, string Iban, string Status, string? Reason, long Attempts, string? Err,
            string Created, string? Sent, string? Finished)>(
            "SELECT id, payment_id, customer_id, msg_id, end_to_end_id, amount, creditor_iban, status, reason, attempts, last_error, created_at, sent_at, finished_at FROM instructions " + where, args);
        return r.Id is null ? null : new Instruction(r.Id, r.Pay, r.Cust, r.Msg, r.E2E, r.Amount, IbanMath.Mask(r.Iban), r.Status, r.Reason, (int)r.Attempts, r.Err, r.Created, r.Sent, r.Finished);
    }


    private static async Task Send(Microsoft.Data.Sqlite.SqliteConnection db, HttpContext? http, IClock clock, string id)
    {
        var i = db.QuerySingle<(string Msg, string E2E, string Amount, string Iban, string? Name, string? Rmt, long Attempts)>(
            "SELECT msg_id, end_to_end_id, amount, creditor_iban, creditor_name, remittance, attempts FROM instructions WHERE id = @id", new { id });
        DownstreamReply r;
        try
        {
            r = await Downstream.Post(http, "nordpay-adapter", "/api/v1/payments", new
            {
                msgId = i.Msg, endToEndId = i.E2E, amount = i.Amount, creditorIban = i.Iban, creditorName = i.Name, remittance = i.Rmt, requestedDate = Money.Date(clock.Today),
            }, forwardAuth: false);
        }
        catch (DependencyUnavailableException ex) { r = new DownstreamReply(503, null, ex.Message); }
        var attempts = i.Attempts + 1;
        if (r.Ok || r.Status == 409)
            db.Execute("UPDATE instructions SET status = 'SENT', attempts = @attempts, sent_at = @at, last_error = NULL WHERE id = @id", new { attempts, at = Nordic.Iso(clock.LocalNow), id });
        else if (r.Status >= 500 && attempts < 10)
            db.Execute("UPDATE instructions SET status = 'QUEUED', attempts = @attempts, last_error = @err, next_ticks = @n WHERE id = @id",
                new { attempts, err = $"HTTP {r.Status}", n = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, attempts - 1))).UtcTicks, id });
        else
        {
            db.Execute("UPDATE instructions SET status = 'REJECTED', reason = @reason, attempts = @attempts, last_error = @err, finished_at = @at WHERE id = @id",
                new { reason = r.Status >= 500 ? "BANK_UNAVAILABLE" : "BANK_REFUSED", attempts, err = $"HTTP {r.Status}", at = Nordic.Iso(clock.LocalNow), id });
            var pay = db.QuerySingle<(string PaymentId, string CustomerId, string Amount)>("SELECT payment_id, customer_id, amount FROM instructions WHERE id = @id", new { id });
            Outbox.Add(db, http, clock, "payment.instruction.rejected", pay.PaymentId, new { instructionId = id, paymentId = pay.PaymentId, customerId = pay.CustomerId, amount = pay.Amount, reasonCode = r.Status >= 500 ? "BANK_UNAVAILABLE" : "BANK_REFUSED" });
        }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/instructions", async (HttpContext http, InstructionRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.PaymentId)) errors.Add(new FieldError("paymentId", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (!Money.TryParse(b.Amount, out var amount) || amount <= 0) errors.Add(new FieldError("amount", "importe positivo"));
            if (!IbanMath.IsValid(b.CreditorIban)) errors.Add(new FieldError("creditorIban", "IBAN válido"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Instrucción no válida.", "La crea payments-gateway al aprobar una retirada.", errors);
            using var db = ctx.OpenDb();
            var existing = Load(db, "WHERE payment_id = @p", new { p = b.PaymentId });
            if (existing is not null) return Results.Ok(existing);
            var n = await Sequences.Next(http, "payment-msg", "PAY");
            var id = "pi_" + Guid.NewGuid().ToString("N")[..16];
            var msgId = $"NVK-{clock.Today:yyyyMMdd}-{n}";
            db.Execute(@"INSERT INTO instructions VALUES (@id, @PaymentId, @CustomerId, @msgId, @e2e, @amount, @iban, @CreditorName, @Remittance, 'PENDING', NULL, 0, NULL, 0, @at, NULL, NULL)",
                new { id, b.PaymentId, b.CustomerId, msgId, e2e = "E2E-" + b.PaymentId, amount = Money.Format(amount, 2), iban = IbanMath.Normalize(b.CreditorIban), b.CreditorName, b.Remittance, at = Nordic.Iso(clock.LocalNow) });
            await Send(db, http, clock, id);
            return Results.Created($"/api/v1/instructions/{id}", Load(db, "WHERE id = @id", new { id }));
        }).WithTags("Instrucciones").WithSummary("Crear y enviar una instrucción de pago (idempotente por paymentId)").Produces<Instruction>(201);

        app.MapGet("/api/v1/instructions/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var i = Load(db, "WHERE id = @id", new { id });
            return i is null ? Problems.NotFound(http, "INSTRUCTION_NOT_FOUND", $"No existe la instrucción {id}.", null) : Results.Ok(i);
        }).WithTags("Instrucciones").WithSummary("Una instrucción").Produces<Instruction>();

        app.MapGet("/api/v1/instructions", (HttpContext http, string? paymentId, string? status) =>
        {
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM instructions WHERE (@paymentId IS NULL OR payment_id = @paymentId) AND (@status IS NULL OR status = @status) ORDER BY created_at", new { paymentId, status });
            return Results.Ok(ids.Select(i => Load(db, "WHERE id = @i", new { i })!).ToList());
        }).WithTags("Instrucciones").WithSummary("Buscar por paymentId o estado").Produces<List<Instruction>>();

        EventConsumer.Start(app, ctx, new[] { "payment.bank-status.changed" }, ev =>
        {
            using var db = ctx.OpenDb();
            var e2e = ev.Key;
            var row = db.QuerySingleOrDefault<(string Id, string PaymentId, string CustomerId, string Amount, string Status)>(
                "SELECT id, payment_id, customer_id, amount, status FROM instructions WHERE end_to_end_id = @e2e", new { e2e });
            if (row.Id is null || row.Status is "SETTLED" or "REJECTED") return Task.CompletedTask;
            var status = ev.Payload.GetProperty("status").GetString();
            var reason = ev.Payload.TryGetProperty("reasonCode", out var rc) && rc.ValueKind == System.Text.Json.JsonValueKind.String ? rc.GetString() : null;
            using var tx = db.BeginTransaction();
            if (status == "ACSC")
            {
                db.Execute("UPDATE instructions SET status = 'SETTLED', finished_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id = row.Id }, tx);
                Outbox.Add(db, null, clock, "payment.instruction.settled", row.PaymentId, new { instructionId = row.Id, paymentId = row.PaymentId, customerId = row.CustomerId, amount = row.Amount, endToEndId = e2e }, tx, correlationId: ev.CorrelationId);
            }
            else if (status == "RJCT")
            {
                db.Execute("UPDATE instructions SET status = 'REJECTED', reason = @reason, finished_at = @at WHERE id = @id", new { reason, at = Nordic.Iso(clock.LocalNow), id = row.Id }, tx);
                Outbox.Add(db, null, clock, "payment.instruction.rejected", row.PaymentId, new { instructionId = row.Id, paymentId = row.PaymentId, customerId = row.CustomerId, amount = row.Amount, reasonCode = reason }, tx, correlationId: ev.CorrelationId);
            }
            tx.Commit();
            return Task.CompletedTask;
        });

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Retry(ctx, clock), null, 1000, 1000));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private int _busy;
    private async Task Retry(ServiceContext ctx, IClock clock)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            using var db = ctx.OpenDb();
            foreach (var id in db.Query<string>("SELECT id FROM instructions WHERE status = 'QUEUED' AND next_ticks <= @t", new { t = DateTimeOffset.UtcNow.UtcTicks }).ToList())
                await Send(db, null, clock, id);
        }
        catch { }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
