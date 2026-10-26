using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record BankPaymentRequest(string? MsgId, string? EndToEndId, string? Amount, string? CreditorIban, string? CreditorName, string? Remittance, string? RequestedDate);
public sealed record BankPayment(string MsgId, string EndToEndId, string? BankPaymentId, string Status, string? ReasonCode, string Amount, string CreditorIban, string SentAt, string? UpdatedAt);
public sealed record OwnershipRequest(string? Iban, string? NationalId, string? Name);

/// <summary>
/// nordpay-adapter: la única puerta hacia el banco NordPay (E6). Construye el pain.001 en JSON, lo firma con JWS separado,
/// verifica la firma de los webhooks y publica payment.bank-status.changed y payment.credit.received.
/// </summary>
public sealed class NordPayAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "nordpay-adapter", "P-integrations", 7502, 2, "Integraciones",
        "Adaptador del banco E6: transferencias ISO 20022 firmadas (JWS), estados por webhook, avisos de abono, titularidad de cuentas y extracto.",
        DependsOn: new[] { "nordpay-sim", "clock-service" },
        Emits: new[] { "payment.bank-status.changed", "payment.credit.received" });

    private const string JwsKey = "nordpay-jws-secret-2026";
    private static readonly Dictionary<string, string> Key = new() { ["X-Api-Key"] = "nordpay-key-2026" };
    private static string Url(string path) => ServiceDirectory.UrlOf("nordpay-sim") + path;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS payments (msg_id TEXT PRIMARY KEY, end_to_end_id TEXT UNIQUE, bank_payment_id TEXT, status TEXT, reason TEXT, amount TEXT, creditor TEXT, sent_at TEXT, updated_at TEXT);
                     CREATE TABLE IF NOT EXISTS credits (credit_id TEXT PRIMARY KEY, reference TEXT, amount TEXT, received_at TEXT);
                     CREATE TABLE IF NOT EXISTS webhook_log (id INTEGER PRIMARY KEY AUTOINCREMENT, event_type TEXT, valid INTEGER, duplicate INTEGER, at TEXT)");
    }

    private static async Task<DownstreamReply?> Bank(HttpMethod m, string path, object? body, Dictionary<string, string>? extra = null)
    {
        var h = new Dictionary<string, string>(Key);
        if (extra is not null) foreach (var (k, v) in extra) h[k] = v;
        try { return await Downstream.SendUrl(m, Url(path), body, h, TimeSpan.FromSeconds(5)); }
        catch (Exception) { return null; }
    }

    private static BankPayment? Load(Microsoft.Data.Sqlite.SqliteConnection db, string where, object args)
    {
        var r = db.QuerySingleOrDefault<(string MsgId, string E2E, string? Bank, string Status, string? Reason, string Amount, string Creditor, string SentAt, string? Updated)>(
            "SELECT msg_id, end_to_end_id, bank_payment_id, status, reason, amount, creditor, sent_at, updated_at FROM payments " + where, args);
        return r.MsgId is null ? null : new BankPayment(r.MsgId, r.E2E, r.Bank, r.Status, r.Reason, r.Amount, r.Creditor, r.SentAt, r.Updated);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/payments", async (HttpContext http, IClock clock, BankPaymentRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.MsgId)) errors.Add(new FieldError("msgId", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.EndToEndId)) errors.Add(new FieldError("endToEndId", "obligatorio"));
            if (!Money.TryParse(b.Amount, out var amount) || amount <= 0) errors.Add(new FieldError("amount", "importe positivo como texto"));
            if (string.IsNullOrWhiteSpace(b.CreditorIban)) errors.Add(new FieldError("creditorIban", "obligatorio"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Pago no válido.", "Normalmente lo envía payment-instructions.", errors);
            var pain001 = JsonSerializer.Serialize(new
            {
                msgId = b.MsgId, endToEndId = b.EndToEndId, instdAmt = new { amount = Money.Format(amount, 2), ccy = "SEK" },
                dbtrAcct = new { iban = IbanMath.ClientMoneyAccount }, cdtrAcct = new { iban = IbanMath.Normalize(b.CreditorIban) }, cdtrNm = b.CreditorName,
                rmtInf = b.Remittance, reqdExctnDt = b.RequestedDate ?? Money.Date(clock.Today),
                callbackUrl = ServiceDirectory.UrlOf("nordpay-adapter") + "/api/v1/webhooks/nordpay",
            }, Canonical.Web);
            var r = await Bank(HttpMethod.Post, "/nordpay/v1/payments", pain001, new() { ["X-JWS-Signature"] = Jws.SignDetached(pain001, JwsKey, "nordvik-2026") });
            if (r is null || r.Status >= 500) return Problems.Of(http, 503, "BANK_UNAVAILABLE", "Banco no disponible", "NordPay no respondió o devolvió un error interno.", "Es reintentable con el mismo msgId.");
            if (r.Status == 409) return Problems.Conflict(http, "DUPLICATE_MESSAGE", $"El banco ya recibió el mensaje {b.MsgId}.", null);
            if (r.Status == 401) return Problems.Of(http, 502, "BANK_SIGNATURE_REJECTED", "El banco rechazó la firma", r.Str("message") ?? "", null);
            if (!r.Ok) return Problems.Unprocessable(http, "BANK_REJECTED", $"{r.Str("code")}: {r.Str("message")}", null);
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO payments VALUES (@MsgId, @EndToEndId, @bank, 'ACCP', NULL, @amount, @creditor, @at, NULL)",
                new { b.MsgId, b.EndToEndId, bank = r.Str("paymentId"), amount = Money.Format(amount, 2), creditor = IbanMath.Normalize(b.CreditorIban), at = Nordic.Iso(clock.LocalNow) });
            return Results.Json(Load(db, "WHERE msg_id = @m", new { m = b.MsgId }), statusCode: 202);
        }).WithTags("Pagos").WithSummary("Enviar una transferencia al banco (la usa payment-instructions)").Produces<BankPayment>(202);

        app.MapGet("/api/v1/payments/{endToEndId}", (HttpContext http, string endToEndId) =>
        {
            using var db = ctx.OpenDb();
            var p = Load(db, "WHERE end_to_end_id = @endToEndId", new { endToEndId });
            return p is null ? Problems.NotFound(http, "PAYMENT_NOT_FOUND", $"No hay ningún pago con endToEndId {endToEndId}.", null) : Results.Ok(p);
        }).WithTags("Pagos").WithSummary("Estado de una transferencia en el banco").Produces<BankPayment>();

        app.MapPost("/api/v1/webhooks/nordpay", async (HttpContext http, IClock clock) =>
        {
            var raw = await new StreamReader(http.Request.Body).ReadToEndAsync();
            using var db = ctx.OpenDb();
            if (!Jws.VerifyDetached(raw, http.Request.Headers["X-JWS-Signature"], JwsKey))
            {
                db.Execute("INSERT INTO webhook_log (event_type, valid, duplicate, at) VALUES (NULL, 0, 0, @at)", new { at = Nordic.Iso(clock.LocalNow) });
                return Problems.Of(http, 401, "INVALID_SIGNATURE", "Firma no válida", "X-JWS-Signature no corresponde al cuerpo.", "JWS separado HS256: base64url(cabecera)..base64url(HMAC(cabecera + '.' + base64url(cuerpo))).");
            }
            var j = JsonDocument.Parse(raw).RootElement;
            var type = j.GetProperty("eventType").GetString();
            var dup = false;
            if (type == "PAYMENT_STATUS")
            {
                var e2e = j.GetProperty("endToEndId").GetString()!;
                var status = j.GetProperty("status").GetString()!;
                var current = Load(db, "WHERE end_to_end_id = @e2e", new { e2e });
                dup = current?.Status == status;
                if (!dup)
                {
                    using var tx = db.BeginTransaction();
                    db.Execute("UPDATE payments SET status = @status, reason = @reason, updated_at = @at WHERE end_to_end_id = @e2e",
                        new { status, reason = j.GetProperty("reasonCode").ValueKind == JsonValueKind.String ? j.GetProperty("reasonCode").GetString() : null, at = Nordic.Iso(clock.LocalNow), e2e }, tx);
                    Outbox.Add(db, http, clock, "payment.bank-status.changed", e2e, j, tx);
                    tx.Commit();
                }
            }
            else if (type == "CREDIT_NOTIFICATION")
            {
                var id = j.GetProperty("creditId").GetString()!;
                using var tx = db.BeginTransaction();
                dup = db.Execute("INSERT OR IGNORE INTO credits VALUES (@id, @ref, @amount, @at)",
                    new { id, @ref = j.GetProperty("reference").GetString(), amount = j.GetProperty("amount").GetString(), at = Nordic.Iso(clock.LocalNow) }, tx) == 0;
                if (!dup) Outbox.Add(db, http, clock, "payment.credit.received", j.GetProperty("reference").GetString()!, j, tx);
                tx.Commit();
            }
            else return Problems.BadRequest(http, "UNKNOWN_EVENT_TYPE", $"eventType desconocido: {type}.", null);
            db.Execute("INSERT INTO webhook_log (event_type, valid, duplicate, at) VALUES (@type, 1, @dup, @at)", new { type, dup = dup ? 1 : 0, at = Nordic.Iso(clock.LocalNow) });
            return Results.Ok(new { eventType = type, duplicate = dup });
        }).WithTags("Webhooks").WithSummary("Webhook de NordPay (exige X-JWS-Signature)");

        app.MapGet("/api/v1/webhooks/stats", () =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(long Total, long? Invalid, long? Dup)>("SELECT COUNT(*), SUM(CASE WHEN valid = 0 THEN 1 ELSE 0 END), SUM(duplicate) FROM webhook_log");
            return Results.Ok(new { received = r.Total, invalidSignature = r.Invalid ?? 0, duplicates = r.Dup ?? 0 });
        }).WithTags("Webhooks").WithSummary("Webhooks recibidos, con firma mala o repetidos");

        app.MapPost("/api/v1/account-verifications", async (HttpContext http, OwnershipRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Iban) || string.IsNullOrWhiteSpace(b.NationalId))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta iban y nationalId.", null);
            var r = await Bank(HttpMethod.Post, "/nordpay/v1/account-verifications", new { iban = b.Iban, nationalId = b.NationalId, name = b.Name });
            if (r is null || r.Status >= 500) return Problems.Of(http, 503, "BANK_UNAVAILABLE", "Banco no disponible", "NordPay no respondió.", null);
            if (r.Status == 400) return Problems.Unprocessable(http, "INVALID_IBAN", r.Str("message") ?? "IBAN no válido", null);
            return Results.Ok(new { iban = r.Str("iban"), result = r.Str("result"), checkedAt = r.Str("checkedAt") });
        }).WithTags("Cuentas").WithSummary("Comprobar con el banco quién es el titular de un IBAN");

        app.MapGet("/api/v1/statements", async (HttpContext http, string? date) =>
        {
            var r = await Bank(HttpMethod.Get, $"/nordpay/v1/accounts/{IbanMath.ClientMoneyAccount}/statement" + (date is null ? "" : "?date=" + date), null);
            if (r is null || r.Status >= 500) return Problems.Of(http, 503, "BANK_UNAVAILABLE", "Banco no disponible", "NordPay no respondió.", null);
            return Results.Content(r.Raw, "application/json");
        }).WithTags("Cuentas").WithSummary("Extracto diario de la cuenta de clientes (camt.053)");
    }
}
