using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record NpAmount(string? Amount, string? Ccy);
public sealed record NpAccount(string? Iban);
public sealed record NpPaymentRequest(string? MsgId, string? EndToEndId, NpAmount? InstdAmt, NpAccount? DbtrAcct, NpAccount? CdtrAcct, string? CdtrNm, string? RmtInf, string? ReqdExctnDt, string? CallbackUrl);
public sealed record NpVerificationRequest(string? Iban, string? NationalId, string? Name);
public sealed record NpOwnerRequest(string? Iban, string? NationalId, string? Name, string? Status);
public sealed record NpCreditRequest(string? DebtorIban, string? DebtorName, string? Amount, string? Reference, string? BookingDate);
public sealed record NpNotifyUrl(string? Url);

/// <summary>
/// nordpay-sim · sistema externo E6 (simulado): el banco de pagos. Mensajes tipo ISO 20022 (pain.001) en JSON,
/// firma JWS separada (X-JWS-Signature), estados ACCP → ACSC / RJCT con código de motivo, avisos de abono (camt.054),
/// verificación de titularidad de cuentas y extracto diario (camt.053) de la cuenta de clientes de Nordvik.
/// Reglas: IBAN acabado en 0004 → cuenta cerrada (AC04); 0006 → bloqueada (AC06); 0009 → titular distinto; 0008 → parecido.
/// </summary>
public sealed class NordPaySim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "nordpay-sim", "X-external", 7706, 2, "Sistema externo (simulado)",
        "E6 NordPay: transferencias firmadas con JWS, estados ACCP/ACSC/RJCT por webhook, verificación de titularidad, abonos entrantes y extracto camt.053.",
        DependsOn: new[] { "clock-service" });

    public const string ApiKey = "nordpay-key-2026";
    public const string JwsKey = "nordpay-jws-secret-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private Timer? _timer;
    private int _busy;
    private string? _notifyUrl;

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS payments (payment_id TEXT PRIMARY KEY, msg_id TEXT UNIQUE, end_to_end_id TEXT, amount TEXT, ccy TEXT, debtor TEXT, creditor TEXT, creditor_name TEXT,
                        remittance TEXT, requested_date TEXT, callback_url TEXT, status TEXT, reason TEXT, received_ticks INTEGER, booking_date TEXT,
                        webhook_attempts INTEGER, webhook_status INTEGER, next_webhook_ticks INTEGER);
                     CREATE TABLE IF NOT EXISTS entries (entry_ref TEXT PRIMARY KEY, iban TEXT, type TEXT, amount TEXT, reference TEXT, end_to_end_id TEXT, counterparty TEXT, booking_date TEXT, seq INTEGER);
                     CREATE TABLE IF NOT EXISTS credits (credit_id TEXT PRIMARY KEY, body TEXT, webhook_attempts INTEGER, webhook_status INTEGER, next_webhook_ticks INTEGER);
                     CREATE TABLE IF NOT EXISTS owners (iban TEXT PRIMARY KEY, national_id TEXT, name TEXT, status TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();
        var g = app.MapGroup("/nordpay/v1").WithTags("NordPay API v1");

        g.MapPost("/payments", async (HttpContext http) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            var raw = await new StreamReader(http.Request.Body).ReadToEndAsync();
            if (!Jws.VerifyDetached(raw, http.Request.Headers["X-JWS-Signature"], JwsKey)) return Error(401, "SIGNATURE_INVALID", "X-JWS-Signature does not match the body");
            NpPaymentRequest? p;
            try { p = JsonSerializer.Deserialize<NpPaymentRequest>(raw, Canonical.Web); } catch (JsonException) { return Error(400, "FORMAT_ERROR", "body is not valid JSON"); }
            if (p?.MsgId is null || p.EndToEndId is null || p.InstdAmt is null || p.CdtrAcct?.Iban is null || p.DbtrAcct?.Iban is null)
                return Error(400, "FORMAT_ERROR", "msgId, endToEndId, instdAmt, dbtrAcct and cdtrAcct are mandatory");
            if (!Money.TryParse(p.InstdAmt.Amount, out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount) return Error(400, "FORMAT_ERROR", "instdAmt.amount must be a positive decimal with max 2 decimals");
            if (p.InstdAmt.Ccy != "SEK") return Error(422, "CURRENCY_NOT_SUPPORTED", "only SEK");
            if (IbanMath.Normalize(p.DbtrAcct.Iban) != IbanMath.ClientMoneyAccount) return Error(422, "DEBTOR_ACCOUNT_UNKNOWN", "debtor account is not held at NordPay by this client");
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM payments WHERE msg_id = @MsgId", p) > 0) return Error(409, "DUPLICATE_MESSAGE", "msgId already received");
            var id = "NP" + Guid.NewGuid().ToString("N")[..14].ToUpperInvariant();
            db.Execute(@"INSERT INTO payments VALUES (@id, @MsgId, @EndToEndId, @amount, 'SEK', @debtor, @creditor, @CdtrNm, @RmtInf, @date, @CallbackUrl, 'ACCP', NULL, @t, NULL, 0, NULL, 0)",
                new { id, p.MsgId, p.EndToEndId, amount = Money.Format(amount, 2), debtor = IbanMath.ClientMoneyAccount, creditor = IbanMath.Normalize(p.CdtrAcct.Iban), p.CdtrNm, p.RmtInf,
                      date = p.ReqdExctnDt ?? Money.Date(clock.Today), p.CallbackUrl, t = DateTimeOffset.UtcNow.UtcTicks });
            return Results.Json(new { paymentId = id, msgId = p.MsgId, endToEndId = p.EndToEndId, status = "ACCP" }, statusCode: 202);
        }).WithSummary("Enviar una transferencia (pain.001 en JSON, firmada con X-JWS-Signature)");

        g.MapGet("/payments/{paymentId}", async (HttpContext http, string paymentId) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string MsgId, string E2E, string Amount, string Status, string? Reason, string? Booking)>(
                "SELECT payment_id, msg_id, end_to_end_id, amount, status, reason, booking_date FROM payments WHERE payment_id = @paymentId", new { paymentId });
            return r.Id is null ? Error(404, "NOT_FOUND", "unknown paymentId")
                : Results.Ok(new { paymentId = r.Id, msgId = r.MsgId, endToEndId = r.E2E, amount = r.Amount, status = r.Status, reasonCode = r.Reason, bookingDate = r.Booking });
        }).WithSummary("Estado de una transferencia");

        g.MapPost("/account-verifications", async (HttpContext http, NpVerificationRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            var iban = IbanMath.Normalize(b.Iban);
            if (!IbanMath.IsValid(iban)) return Error(400, "INVALID_IBAN", "iban is not valid");
            if (string.IsNullOrWhiteSpace(b.NationalId)) return Error(400, "FORMAT_ERROR", "nationalId is mandatory");
            using var db = ctx.OpenDb();
            var owner = db.QuerySingleOrDefault<(string Iban, string NationalId, string? Name, string Status)>("SELECT iban, national_id, name, status FROM owners WHERE iban = @iban", new { iban });
            string result;
            if (owner.Iban is not null) result = owner.Status == "CLOSED" ? "ACCOUNT_CLOSED" : owner.NationalId == b.NationalId ? "MATCH" : "NO_MATCH";
            else result = iban.EndsWith("0004") ? "ACCOUNT_CLOSED" : iban.EndsWith("0009") ? "NO_MATCH" : iban.EndsWith("0008") ? "CLOSE_MATCH" : "MATCH";
            return Results.Ok(new { iban, result, checkedAt = Nordic.Iso(clock.LocalNow) });
        }).WithSummary("¿Es esta persona titular de este IBAN? MATCH · NO_MATCH · CLOSE_MATCH · ACCOUNT_CLOSED");

        g.MapGet("/accounts/{iban}/statement", async (HttpContext http, string iban, string? date) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            var acct = IbanMath.Normalize(iban);
            if (acct != IbanMath.ClientMoneyAccount) return Error(404, "ACCOUNT_NOT_FOUND", "only the client money account has statements");
            if (!Money.TryParseDate(date ?? Money.Date(clock.Today), out var d)) return Error(400, "FORMAT_ERROR", "date must be yyyy-MM-dd");
            using var db = ctx.OpenDb();
            decimal Sum(string where, object args) => db.Query<(string Type, string Amount)>("SELECT type, amount FROM entries WHERE iban = @acct AND " + where, args)
                .Sum(e => (e.Type == "CRDT" ? 1 : -1) * decimal.Parse(e.Amount, CultureInfo.InvariantCulture));
            var opening = Sum("booking_date < @d", new { acct, d = Money.Date(d) });
            var entries = db.Query<(string Ref, string Type, string Amount, string? Reference, string? E2E, string? Counterparty, string Booking)>(
                "SELECT entry_ref, type, amount, reference, end_to_end_id, counterparty, booking_date FROM entries WHERE iban = @acct AND booking_date = @d ORDER BY seq", new { acct, d = Money.Date(d) })
                .Select(e => new { entryRef = e.Ref, type = e.Type, amount = e.Amount, reference = e.Reference, endToEndId = e.E2E, counterparty = e.Counterparty, bookingDate = e.Booking }).ToList();
            var closing = opening + entries.Sum(e => (e.type == "CRDT" ? 1 : -1) * decimal.Parse(e.amount, CultureInfo.InvariantCulture));
            return Results.Ok(new { iban = acct, date = Money.Date(d), openingBalance = Money.Format(opening, 2), closingBalance = Money.Format(closing, 2), currency = "SEK", entries });
        }).WithSummary("Extracto diario (camt.053) de la cuenta de clientes de Nordvik");

        g.MapPost("/notification-url", (NpNotifyUrl b) =>
        {
            if (!Uri.TryCreate(b.Url, UriKind.Absolute, out _)) return Error(400, "FORMAT_ERROR", "url is mandatory");
            _notifyUrl = b.Url;
            return Results.Ok(new { url = _notifyUrl });
        }).WithSummary("Dónde enviar los avisos de abono (camt.054)");

        app.MapPost("/__admin/accounts", (NpOwnerRequest b) =>
        {
            var iban = IbanMath.Normalize(b.Iban);
            if (!IbanMath.IsValid(iban) || string.IsNullOrWhiteSpace(b.NationalId)) return Error(400, "FORMAT_ERROR", "valid iban and nationalId are mandatory");
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO owners VALUES (@iban, @NationalId, @Name, @status) ON CONFLICT(iban) DO UPDATE SET national_id = excluded.national_id, name = excluded.name, status = excluded.status",
                new { iban, b.NationalId, b.Name, status = b.Status ?? "OPEN" });
            return Results.Ok(new { iban, b.NationalId, status = b.Status ?? "OPEN" });
        }).WithTags("Administración del simulador").WithSummary("Registrar el titular de un IBAN (o cerrarlo con status CLOSED)");

        app.MapPost("/__admin/incoming-credits", (NpCreditRequest b) =>
        {
            if (!Money.TryParse(b.Amount, out var amount) || amount <= 0) return Error(400, "FORMAT_ERROR", "amount must be positive");
            if (string.IsNullOrWhiteSpace(b.Reference)) return Error(400, "FORMAT_ERROR", "reference (OCR) is mandatory");
            var booking = b.BookingDate ?? Money.Date(clock.Today);
            var id = "CR" + Guid.NewGuid().ToString("N")[..14].ToUpperInvariant();
            using var db = ctx.OpenDb();
            AddEntry(db, IbanMath.ClientMoneyAccount, "CRDT", Money.Format(amount, 2), b.Reference, null, IbanMath.Normalize(b.DebtorIban), booking);
            var body = JsonSerializer.Serialize(new
            {
                eventType = "CREDIT_NOTIFICATION", creditId = id, creditorIban = IbanMath.ClientMoneyAccount, debtorIban = IbanMath.Normalize(b.DebtorIban), debtorName = b.DebtorName,
                amount = Money.Format(amount, 2), ccy = "SEK", reference = b.Reference, bookingDate = booking,
            }, Canonical.Web);
            db.Execute("INSERT INTO credits VALUES (@id, @body, 0, NULL, 0)", new { id, body });
            return Results.Json(new { creditId = id, status = "BOOKED", note = "El aviso camt.054 sale por webhook hacia nordpay-adapter." }, statusCode: 201);
        }).WithTags("Administración del simulador").WithSummary("Simular que un cliente nos transfiere dinero (con su referencia OCR)");

        app.MapPost("/__admin/process", async () => { await Process(ctx, clock); return Results.Ok(new { processed = true }); })
            .WithTags("Administración del simulador").WithSummary("Procesar ya los pagos pendientes");

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Process(ctx, clock), null, 300, 300));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private static void AddEntry(Microsoft.Data.Sqlite.SqliteConnection db, string iban, string type, string amount, string? reference, string? e2e, string? counterparty, string booking)
    {
        var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM entries") ?? 0) + 1;
        db.Execute("INSERT INTO entries VALUES (@r, @iban, @type, @amount, @reference, @e2e, @counterparty, @booking, @seq)",
            new { r = "EN" + seq.ToString("D8"), iban, type, amount, reference, e2e, counterparty, booking, seq });
    }

    private static string? Reason(string creditor, decimal amount, string? ownerStatus) =>
        !IbanMath.IsValid(creditor) ? "AC01" : ownerStatus == "CLOSED" || creditor.EndsWith("0004") ? "AC04" : creditor.EndsWith("0006") ? "AC06" : amount > 1_000_000m ? "AM02" : null;

    private async Task Process(ServiceContext ctx, IClock clock)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            DateOnly today;
            try { today = clock.Today; } catch { return; }
            using var db = ctx.OpenDb();
            if (NordicCalendar.IsTradingDay("XSTO", today))
            {
                foreach (var p in db.Query<(string Id, string Amount, string Creditor, string Date, long Ticks, string? E2E, string? Rmt)>(
                             "SELECT payment_id, amount, creditor, requested_date, received_ticks, end_to_end_id, remittance FROM payments WHERE status = 'ACCP'").ToList())
                {
                    if (string.CompareOrdinal(p.Date, Money.Date(today)) > 0 || DateTimeOffset.UtcNow.UtcTicks - p.Ticks < TimeSpan.FromMilliseconds(300).Ticks) continue;
                    var amount = decimal.Parse(p.Amount, CultureInfo.InvariantCulture);
                    var ownerStatus = db.ExecuteScalar<string?>("SELECT status FROM owners WHERE iban = @c", new { c = p.Creditor });
                    var reason = Reason(p.Creditor, amount, ownerStatus);
                    if (reason is null) AddEntry(db, IbanMath.ClientMoneyAccount, "DBIT", p.Amount, p.Rmt, p.E2E, p.Creditor, Money.Date(today));
                    db.Execute("UPDATE payments SET status = @s, reason = @reason, booking_date = @d WHERE payment_id = @id",
                        new { s = reason is null ? "ACSC" : "RJCT", reason, d = Money.Date(today), id = p.Id });
                }
            }
            foreach (var w in db.Query<(string Id, string MsgId, string E2E, string Amount, string Status, string? Reason, string? Booking, string? Url, long Next)>(
                         @"SELECT payment_id, msg_id, end_to_end_id, amount, status, reason, booking_date, callback_url, next_webhook_ticks FROM payments
                           WHERE status IN ('ACSC','RJCT') AND callback_url IS NOT NULL AND (webhook_status IS NULL OR webhook_status >= 300) AND webhook_attempts < 6").ToList())
            {
                if (w.Next > DateTimeOffset.UtcNow.UtcTicks) continue;
                var body = JsonSerializer.Serialize(new { eventType = "PAYMENT_STATUS", paymentId = w.Id, msgId = w.MsgId, endToEndId = w.E2E, status = w.Status, reasonCode = w.Reason, amount = w.Amount, bookingDate = w.Booking }, Canonical.Web);
                var status = await Send(w.Url!, body);
                db.Execute("UPDATE payments SET webhook_attempts = webhook_attempts + 1, webhook_status = @status, next_webhook_ticks = @n WHERE payment_id = @id",
                    new { status, n = DateTimeOffset.UtcNow.AddSeconds(1).UtcTicks, id = w.Id });
            }
            var notify = _notifyUrl ?? (ServiceDirectory.Knows("nordpay-adapter") ? ServiceDirectory.UrlOf("nordpay-adapter") + "/api/v1/webhooks/nordpay" : null);
            if (notify is not null)
                foreach (var c in db.Query<(string Id, string Body, long Next)>("SELECT credit_id, body, next_webhook_ticks FROM credits WHERE (webhook_status IS NULL OR webhook_status >= 300) AND webhook_attempts < 6").ToList())
                {
                    if (c.Next > DateTimeOffset.UtcNow.UtcTicks) continue;
                    var status = await Send(notify, c.Body);
                    db.Execute("UPDATE credits SET webhook_attempts = webhook_attempts + 1, webhook_status = @status, next_webhook_ticks = @n WHERE credit_id = @id",
                        new { status, n = DateTimeOffset.UtcNow.AddSeconds(1).UtcTicks, id = c.Id });
                }
        }
        catch { /* el simulador nunca debe caerse */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static async Task<int> Send(string url, string body)
    {
        try
        {
            var r = await Downstream.SendUrl(HttpMethod.Post, url, body, new Dictionary<string, string> { ["X-JWS-Signature"] = Jws.SignDetached(body, JwsKey, "nordpay-2026") }, TimeSpan.FromSeconds(3));
            return r.Status;
        }
        catch { return 599; }
    }
}
