using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CustodyOrder(string? ClientOrderId, string? AccountId, string? Isin, string? Side, string? Amount, string? Units);
public sealed record CustodyBatchRequest(string? BatchId, string? TradeDate, string? CallbackUrl, List<CustodyOrder>? Orders);
public sealed record CustodyExecution(string ExecutionId, string ClientOrderId, string AccountId, string Isin, string Side, string Units, string Nav, string FundCurrency,
    string FxRate, string GrossAmountSek, string FeeSek, string NetAmountSek, string TradeDate, string SettlementDate);
public sealed record CustodyRejection(string ClientOrderId, string Reason);
public sealed record CustodyHolding(string AccountId, string Isin, string Units);
public sealed record BreakRequest(string? AccountId, string? Isin, string? DeltaUnits);

/// <summary>
/// custodian-sim · sistema externo E4 (simulado): el banco custodio que ejecuta las órdenes de fondos.
/// Recibe lotes (X-Api-Key), espera al NAV del día de negociación (18:00) y avisa por webhook firmado
/// con HMAC (X-Custodian-Signature). Lleva su propio libro de participaciones para la conciliación.
/// </summary>
public sealed class CustodianSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "custodian-sim", "X-external", 7704, 2, "Sistema externo (simulado)",
        "E4 Custodio: lotes de órdenes, ejecución al NAV del día (con comisiones del folleto y cambio de divisa), webhooks firmados y extracto de posiciones.",
        DependsOn: new[] { "clock-service", "fx-rates" });

    public const string ApiKey = "custody-key-2026";
    public const string WebhookSecret = "custody-webhook-secret-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private Timer? _timer;
    private int _busy;

    private static IResult Error(int status, string error, string message) => Results.Json(new { error, message }, statusCode: status);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS batches (batch_id TEXT PRIMARY KEY, trade_date TEXT, callback_url TEXT, status TEXT, received_at TEXT, executed_at TEXT, orders TEXT, result TEXT,
                        webhook_attempts INTEGER, webhook_status INTEGER, next_webhook_ticks INTEGER);
                     CREATE TABLE IF NOT EXISTS holdings (account_id TEXT, isin TEXT, units TEXT, PRIMARY KEY (account_id, isin))");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM batches WHERE batch_id LIKE @p", new { p = prefix + "%" });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();
        var g = app.MapGroup("/custody/v2").WithTags("Custodio API v2");

        g.MapPost("/batches", async (HttpContext http, CustodyBatchRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            if (string.IsNullOrWhiteSpace(b.BatchId)) return Error(400, "INVALID_BATCH", "batchId is required");
            if (!Money.TryParseDate(b.TradeDate, out var td) || !NordicCalendar.IsTradingDay("XSTO", td)) return Error(400, "INVALID_TRADE_DATE", "tradeDate must be an XSTO trading day (yyyy-MM-dd)");
            if (b.Orders is not { Count: > 0 }) return Error(400, "INVALID_BATCH", "orders must not be empty");
            if (b.Orders.Count > 500) return Error(400, "INVALID_BATCH", "max 500 orders per batch");
            if (!Uri.TryCreate(b.CallbackUrl, UriKind.Absolute, out _)) return Error(400, "INVALID_BATCH", "callbackUrl is required");
            foreach (var o in b.Orders)
                if (string.IsNullOrWhiteSpace(o.ClientOrderId) || string.IsNullOrWhiteSpace(o.AccountId) || o.Side is not ("BUY" or "SELL"))
                    return Error(400, "INVALID_ORDER", "each order needs clientOrderId, accountId and side BUY|SELL");
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM batches WHERE batch_id = @BatchId", b) > 0) return Error(409, "DUPLICATE_BATCH", "batchId already received");
            db.Execute("INSERT INTO batches VALUES (@BatchId, @TradeDate, @CallbackUrl, 'ACCEPTED', @at, NULL, @orders, NULL, 0, NULL, 0)",
                new { b.BatchId, b.TradeDate, b.CallbackUrl, at = Nordic.Iso(clock.LocalNow), orders = JsonSerializer.Serialize(b.Orders, Canonical.Web) });
            return Results.Json(new { batchId = b.BatchId, status = "ACCEPTED", orders = b.Orders.Count }, statusCode: 202);
        }).WithSummary("Enviar un lote de órdenes (202; la ejecución llega por webhook)");

        g.MapGet("/batches/{batchId}", async (HttpContext http, string batchId) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string TradeDate, string Status, string ReceivedAt, string? ExecutedAt, string? Result, long Attempts, long? WebhookStatus)>(
                "SELECT batch_id, trade_date, status, received_at, executed_at, result, webhook_attempts, webhook_status FROM batches WHERE batch_id = @batchId", new { batchId });
            if (r.Id is null) return Error(404, "BATCH_NOT_FOUND", "unknown batchId");
            return Results.Ok(new { batchId = r.Id, tradeDate = r.TradeDate, status = r.Status, receivedAt = r.ReceivedAt, executedAt = r.ExecutedAt,
                result = r.Result is null ? (JsonElement?)null : JsonDocument.Parse(r.Result).RootElement, webhookAttempts = r.Attempts, webhookStatus = r.WebhookStatus });
        }).WithSummary("Estado de un lote");

        g.MapGet("/accounts/{accountId}/holdings", async (HttpContext http, string accountId) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            using var db = ctx.OpenDb();
            return Results.Ok(new { accountId, asOf = Nordic.Iso(clock.LocalNow),
                holdings = db.Query<CustodyHolding>("SELECT account_id AS AccountId, isin, units FROM holdings WHERE account_id = @accountId AND units <> '0.0000' ORDER BY isin", new { accountId }).ToList() });
        }).WithSummary("Participaciones de una cuenta según el custodio");

        g.MapGet("/holdings", async (HttpContext http) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (http.Request.Headers["X-Api-Key"] != ApiKey) return Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key");
            using var db = ctx.OpenDb();
            return Results.Ok(new { asOf = Nordic.Iso(clock.LocalNow), holdings = db.Query<CustodyHolding>("SELECT account_id AS AccountId, isin, units FROM holdings WHERE units <> '0.0000' ORDER BY account_id, isin").ToList() });
        }).WithSummary("Extracto completo de posiciones (para la conciliación)");

        app.MapPost("/__admin/break-holding", (BreakRequest b) =>
        {
            if (b.AccountId is null || b.Isin is null || !Money.TryParse(b.DeltaUnits, out var delta)) return Error(400, "INVALID", "accountId, isin and deltaUnits are required");
            using var db = ctx.OpenDb();
            AddUnits(db, b.AccountId, b.Isin, delta);
            return Results.Ok(new { b.AccountId, b.Isin, b.DeltaUnits, note = "El custodio ahora no cuadra con position-keeping: la conciliación debe detectarlo." });
        }).WithTags("Administración del simulador").WithSummary("Descuadrar a propósito una posición del custodio");

        app.MapPost("/__admin/process", async () => { await Process(ctx, clock); return Results.Ok(new { processed = true }); })
            .WithTags("Administración del simulador").WithSummary("Procesar ya los lotes pendientes (sin esperar al temporizador)");

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Process(ctx, clock), null, 300, 300));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private static void AddUnits(Microsoft.Data.Sqlite.SqliteConnection db, string account, string isin, decimal delta)
    {
        var current = db.ExecuteScalar<string?>("SELECT units FROM holdings WHERE account_id = @account AND isin = @isin", new { account, isin });
        var units = (current is null ? 0m : decimal.Parse(current, CultureInfo.InvariantCulture)) + delta;
        db.Execute("INSERT INTO holdings VALUES (@account, @isin, @u) ON CONFLICT(account_id, isin) DO UPDATE SET units = excluded.units",
            new { account, isin, u = Money.Format(units, 4) });
    }

    private static decimal Floor4(decimal v) => Math.Floor(v * 10000m) / 10000m;

    private async Task Process(ServiceContext ctx, IClock clock)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            DateTimeOffset localNow;
            try { localNow = clock.LocalNow; } catch { return; }
            using var db = ctx.OpenDb();
            // 1) Ejecutar los lotes cuyo NAV ya se publicó
            foreach (var b in db.Query<(string Id, string TradeDate, string Orders)>("SELECT batch_id, trade_date, orders FROM batches WHERE status = 'ACCEPTED'").ToList())
            {
                var td = DateOnly.Parse(b.TradeDate, CultureInfo.InvariantCulture);
                if (td > NavMath.LastPublishedDate(localNow)) continue;
                var settle = NordicCalendar.AddTradingDays("XSTO", td, 2);
                var executions = new List<CustodyExecution>();
                var rejections = new List<CustodyRejection>();
                foreach (var o in JsonSerializer.Deserialize<List<CustodyOrder>>(b.Orders, Canonical.Web)!)
                {
                    var fund = FundSeed.All.FirstOrDefault(f => f.Isin == o.Isin);
                    if (fund is null) { rejections.Add(new(o.ClientOrderId!, "UNKNOWN_INSTRUMENT")); continue; }
                    if (o.Side == "BUY" && fund.Status == "CLOSED") { rejections.Add(new(o.ClientOrderId!, "FUND_CLOSED")); continue; }
                    var nav = decimal.Parse(NavMath.Series(fund.Isin)[td], CultureInfo.InvariantCulture);
                    var rate = 1m;
                    if (fund.Currency != "SEK")
                    {
                        var fx = await Downstream.Get(null, "fx-rates", $"/api/v1/rates/{fund.Currency}/SEK?date={b.TradeDate}");
                        if (!fx.Ok) return; // se reintenta en la siguiente vuelta
                        rate = decimal.Parse(fx.Str("rate")!, CultureInfo.InvariantCulture);
                    }
                    var execId = "EX-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                    if (o.Side == "BUY")
                    {
                        if (!Money.TryParse(o.Amount, out var amount) || amount <= 0) { rejections.Add(new(o.ClientOrderId!, "INVALID_AMOUNT")); continue; }
                        var fee = Math.Round(amount * decimal.Parse(fund.EntryFee, CultureInfo.InvariantCulture) / 100, 2, MidpointRounding.ToEven);
                        var units = Floor4((amount - fee) / rate / nav);
                        if (units <= 0) { rejections.Add(new(o.ClientOrderId!, "AMOUNT_TOO_SMALL")); continue; }
                        AddUnits(db, o.AccountId!, fund.Isin, units);
                        executions.Add(new(execId, o.ClientOrderId!, o.AccountId!, fund.Isin, "BUY", Money.Format(units, 4), Money.Format(nav, 4), fund.Currency, Money.Format(rate, 6),
                            Money.Format(amount, 2), Money.Format(fee, 2), Money.Format(amount - fee, 2), b.TradeDate, Money.Date(settle)));
                    }
                    else
                    {
                        if (!Money.TryParse(o.Units, out var units) || units <= 0) { rejections.Add(new(o.ClientOrderId!, "INVALID_UNITS")); continue; }
                        var held = db.ExecuteScalar<string?>("SELECT units FROM holdings WHERE account_id = @a AND isin = @i", new { a = o.AccountId, i = fund.Isin });
                        if (held is null || decimal.Parse(held, CultureInfo.InvariantCulture) < units) { rejections.Add(new(o.ClientOrderId!, "INSUFFICIENT_HOLDINGS")); continue; }
                        var gross = Math.Round(units * nav * rate, 2, MidpointRounding.ToEven);
                        var fee = Math.Round(gross * decimal.Parse(fund.ExitFee, CultureInfo.InvariantCulture) / 100, 2, MidpointRounding.ToEven);
                        AddUnits(db, o.AccountId!, fund.Isin, -units);
                        executions.Add(new(execId, o.ClientOrderId!, o.AccountId!, fund.Isin, "SELL", Money.Format(units, 4), Money.Format(nav, 4), fund.Currency, Money.Format(rate, 6),
                            Money.Format(gross, 2), Money.Format(fee, 2), Money.Format(gross - fee, 2), b.TradeDate, Money.Date(settle)));
                    }
                }
                var result = JsonSerializer.Serialize(new { batchId = b.Id, tradeDate = b.TradeDate, executions, rejections }, Canonical.Web);
                db.Execute("UPDATE batches SET status = 'EXECUTED', executed_at = @at, result = @result, next_webhook_ticks = 0 WHERE batch_id = @id",
                    new { at = Nordic.Iso(localNow), result, id = b.Id });
            }
            // 2) Avisar por webhook (con reintentos: hasta 6 intentos, cada 1 s)
            foreach (var w in db.Query<(string Id, string Url, string Result, long Attempts, long Next)>(
                         "SELECT batch_id, callback_url, result, webhook_attempts, next_webhook_ticks FROM batches WHERE status = 'EXECUTED' AND (webhook_status IS NULL OR webhook_status >= 300) AND webhook_attempts < 6").ToList())
            {
                if (w.Next > DateTimeOffset.UtcNow.UtcTicks) continue;
                var body = JsonSerializer.Serialize(new { eventType = "BATCH_EXECUTED", sentAt = Nordic.Iso(localNow), batch = JsonDocument.Parse(w.Result).RootElement }, Canonical.Web);
                int status;
                try
                {
                    var r = await Downstream.SendUrl(HttpMethod.Post, w.Url, body, new Dictionary<string, string> { ["X-Custodian-Signature"] = "sha256=" + Canonical.HmacHex(WebhookSecret, body) }, TimeSpan.FromSeconds(3));
                    status = r.Status;
                }
                catch { status = 599; }
                db.Execute("UPDATE batches SET webhook_attempts = webhook_attempts + 1, webhook_status = @status, next_webhook_ticks = @next WHERE batch_id = @id",
                    new { status, next = DateTimeOffset.UtcNow.AddSeconds(1).UtcTicks, id = w.Id });
            }
        }
        catch { /* el simulador nunca debe caerse */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
