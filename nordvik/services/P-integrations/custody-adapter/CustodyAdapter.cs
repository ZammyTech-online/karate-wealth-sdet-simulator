using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AdapterOrder(string? ClientOrderId, string? AccountId, string? Isin, string? Side, string? Amount, string? Units);
public sealed record SendBatchRequest(string? BatchId, string? TradeDate, List<AdapterOrder>? Orders);
public sealed record SentBatch(string BatchId, string TradeDate, int Orders, string Status, string SentAt, string? ExecutedAt, int Executions, int Rejections);

/// <summary>
/// custody-adapter: la única puerta hacia el custodio (E4). Traduce nuestros lotes a su API, protege la API key,
/// verifica la firma HMAC de sus webhooks y convierte cada ejecución en un evento custody.execution.received.
/// </summary>
public sealed class CustodyAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "custody-adapter", "P-integrations", 7500, 2, "Integraciones",
        "Adaptador del custodio E4: envía lotes, recibe webhooks firmados (HMAC), deduplica y publica ejecuciones y rechazos como eventos.",
        DependsOn: new[] { "custodian-sim", "clock-service" },
        Emits: new[] { "custody.execution.received", "custody.order.rejected" });

    private const string ApiKey = "custody-key-2026";
    private const string WebhookSecret = "custody-webhook-secret-2026";
    private static readonly Dictionary<string, string> Key = new() { ["X-Api-Key"] = ApiKey };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS batches (batch_id TEXT PRIMARY KEY, trade_date TEXT, orders INTEGER, status TEXT, sent_at TEXT, executed_at TEXT, executions INTEGER, rejections INTEGER);
                     CREATE TABLE IF NOT EXISTS webhooks (id INTEGER PRIMARY KEY AUTOINCREMENT, batch_id TEXT, received_at TEXT, valid INTEGER, duplicate INTEGER)");
    }

    private static string Url(string path) => ServiceDirectory.UrlOf("custodian-sim") + path;

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/batches", async (HttpContext http, IClock clock, SendBatchRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.BatchId)) errors.Add(new FieldError("batchId", "obligatorio"));
            if (!Money.TryParseDate(b.TradeDate, out _)) errors.Add(new FieldError("tradeDate", "AAAA-MM-DD"));
            if (b.Orders is not { Count: > 0 }) errors.Add(new FieldError("orders", "al menos una orden"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Lote no válido.", "Normalmente lo envía order-router, no hace falta llamarlo a mano.", errors);
            DownstreamReply r;
            try
            {
                r = await Downstream.SendUrl(HttpMethod.Post, Url("/custody/v2/batches"), new
                {
                    b.BatchId, b.TradeDate, callbackUrl = ServiceDirectory.UrlOf("custody-adapter") + "/api/v1/webhooks/custody", orders = b.Orders,
                }, Key, TimeSpan.FromSeconds(5), http.Items[NordvikMiddleware.CorrelationKey] as string);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                return Problems.Of(http, 503, "CUSTODIAN_UNAVAILABLE", "Custodio no disponible", "El custodio no respondió a tiempo.", "Es reintentable: order-router lo vuelve a intentar en la siguiente vuelta.");
            }
            if (r.Status == 409) return Problems.Conflict(http, "BATCH_ALREADY_SENT", $"El custodio ya tiene el lote {b.BatchId}.", null);
            if (r.Status >= 500) return Problems.Of(http, 503, "CUSTODIAN_UNAVAILABLE", "Custodio no disponible", $"El custodio respondió {r.Status}.", "Mira /__admin/behaviour de custodian-sim: ¿está en modo down?");
            if (!r.Ok) return Problems.Of(http, 502, "CUSTODIAN_REJECTED_BATCH", "El custodio rechazó el lote", $"{r.Str("error")}: {r.Str("message")}", null);
            using var db = ctx.OpenDb();
            db.Execute("INSERT OR REPLACE INTO batches VALUES (@BatchId, @TradeDate, @n, 'SENT', @at, NULL, 0, 0)", new { b.BatchId, b.TradeDate, n = b.Orders!.Count, at = Nordic.Iso(clock.LocalNow) });
            return Results.Json(Load(db, b.BatchId!), statusCode: 202);
        }).WithTags("Lotes").WithSummary("Enviar un lote al custodio (lo usa order-router)").Produces<SentBatch>(202);

        app.MapGet("/api/v1/batches/{batchId}", (HttpContext http, string batchId) =>
        {
            using var db = ctx.OpenDb();
            var b = Load(db, batchId);
            return b is null ? Problems.NotFound(http, "BATCH_NOT_FOUND", $"No existe el lote {batchId}.", null) : Results.Ok(b);
        }).WithTags("Lotes").WithSummary("Estado de un lote enviado").Produces<SentBatch>();

        app.MapPost("/api/v1/webhooks/custody", async (HttpContext http, IClock clock) =>
        {
            var raw = await new StreamReader(http.Request.Body).ReadToEndAsync();
            var sig = http.Request.Headers["X-Custodian-Signature"].ToString();
            var expected = "sha256=" + Canonical.HmacHex(WebhookSecret, raw);
            using var db = ctx.OpenDb();
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(sig), System.Text.Encoding.UTF8.GetBytes(expected)))
            {
                db.Execute("INSERT INTO webhooks (batch_id, received_at, valid, duplicate) VALUES (NULL, @at, 0, 0)", new { at = Nordic.Iso(clock.LocalNow) });
                return Problems.Of(http, 401, "INVALID_SIGNATURE", "Firma no válida", "X-Custodian-Signature no corresponde al cuerpo.",
                    "La firma es sha256=HMAC-SHA256(secreto, cuerpo exacto). Si tu mock re-serializa el JSON, la firma cambia.");
            }
            JsonElement batch;
            try { batch = JsonDocument.Parse(raw).RootElement.GetProperty("batch"); }
            catch (Exception) { return Problems.BadRequest(http, "INVALID_PAYLOAD", "El webhook no tiene el formato del custodio.", null); }
            var batchId = batch.GetProperty("batchId").GetString()!;
            var status = db.ExecuteScalar<string?>("SELECT status FROM batches WHERE batch_id = @batchId", new { batchId });
            if (status == "EXECUTED")
            {
                db.Execute("INSERT INTO webhooks (batch_id, received_at, valid, duplicate) VALUES (@batchId, @at, 1, 1)", new { batchId, at = Nordic.Iso(clock.LocalNow) });
                return Results.Ok(new { batchId, duplicate = true });
            }
            using var tx = db.BeginTransaction();
            var execs = batch.GetProperty("executions").EnumerateArray().ToList();
            var rejs = batch.GetProperty("rejections").EnumerateArray().ToList();
            foreach (var e in execs) Outbox.Add(db, http, clock, "custody.execution.received", e.GetProperty("clientOrderId").GetString()!, new { batchId, execution = e }, tx);
            foreach (var r in rejs) Outbox.Add(db, http, clock, "custody.order.rejected", r.GetProperty("clientOrderId").GetString()!, new { batchId, rejection = r }, tx);
            db.Execute(@"INSERT INTO batches VALUES (@batchId, @td, @n, 'EXECUTED', @at, @at, @e, @r)
                         ON CONFLICT(batch_id) DO UPDATE SET status = 'EXECUTED', executed_at = excluded.executed_at, executions = excluded.executions, rejections = excluded.rejections",
                new { batchId, td = batch.GetProperty("tradeDate").GetString(), n = execs.Count + rejs.Count, at = Nordic.Iso(clock.LocalNow), e = execs.Count, r = rejs.Count }, tx);
            db.Execute("INSERT INTO webhooks (batch_id, received_at, valid, duplicate) VALUES (@batchId, @at, 1, 0)", new { batchId, at = Nordic.Iso(clock.LocalNow) }, tx);
            tx.Commit();
            return Results.Ok(new { batchId, duplicate = false, executions = execs.Count, rejections = rejs.Count });
        }).WithTags("Webhooks").WithSummary("Webhook del custodio (exige X-Custodian-Signature)");

        app.MapGet("/api/v1/webhooks/stats", () =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(long Total, long? Invalid, long? Dup)>("SELECT COUNT(*), SUM(CASE WHEN valid = 0 THEN 1 ELSE 0 END), SUM(duplicate) FROM webhooks");
            return Results.Ok(new { received = r.Total, invalidSignature = r.Invalid ?? 0, duplicates = r.Dup ?? 0 });
        }).WithTags("Webhooks").WithSummary("Cuántos webhooks llegaron, con firma mala o repetidos");

        app.MapGet("/api/v1/holdings", async (HttpContext http, string? accountId) =>
        {
            DownstreamReply r;
            try { r = await Downstream.SendUrl(HttpMethod.Get, Url(accountId is null ? "/custody/v2/holdings" : $"/custody/v2/accounts/{accountId}/holdings"), null, Key, TimeSpan.FromSeconds(5)); }
            catch (Exception) { return Problems.Of(http, 503, "CUSTODIAN_UNAVAILABLE", "Custodio no disponible", "El custodio no respondió.", null); }
            if (!r.Ok) return Problems.Of(http, 503, "CUSTODIAN_UNAVAILABLE", "Custodio no disponible", $"El custodio respondió {r.Status}.", null);
            return Results.Content(r.Raw, "application/json");
        }).WithTags("Posiciones").WithSummary("Posiciones según el custodio (para la conciliación)");
    }

    private static SentBatch? Load(Microsoft.Data.Sqlite.SqliteConnection db, string batchId)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Td, long N, string Status, string SentAt, string? ExecutedAt, long E, long R)>(
            "SELECT batch_id, trade_date, orders, status, sent_at, executed_at, executions, rejections FROM batches WHERE batch_id = @batchId", new { batchId });
        return r.Id is null ? null : new SentBatch(r.Id, r.Td, (int)r.N, r.Status, r.SentAt, r.ExecutedAt, (int)r.E, (int)r.R);
    }
}
