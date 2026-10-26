using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RouterBatch(string BatchId, string TradeDate, IReadOnlyList<string> OrderIds, string Status, string? Error, string CreatedAt, string Trigger);
public sealed record RunResult(int Batches, int Orders, IReadOnlyList<RouterBatch> Sent, IReadOnlyList<string> Errors);

/// <summary>
/// order-router: agrupa las órdenes RECEIVED en un lote por fecha de negociación y lo envía al custodio a través de custody-adapter.
/// Solo, cada segundo: envía las órdenes de días pasados y las de hoy en cuanto pasa la hora de corte (15:00).
/// A mano: POST /api/v1/batches/run envía ya todo lo que tenga fecha de negociación de hoy o anterior.
/// </summary>
public sealed class OrderRouter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "order-router", "J-investment", 6903, 2, "Inversiones",
        "Ventana de lotes: a la hora de corte agrupa las órdenes por fecha de negociación y las envía al custodio. Reintenta si el custodio no está.",
        DependsOn: new[] { "order-management", "custody-adapter", "config-service", "clock-service" });

    private Timer? _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS batches (batch_id TEXT PRIMARY KEY, trade_date TEXT, order_ids TEXT, status TEXT, error TEXT, created_at TEXT, trigger TEXT, seq INTEGER)");
    }

    private static RouterBatch Map((string Id, string Td, string Orders, string Status, string? Error, string At, string Trigger) r) =>
        new(r.Id, r.Td, JsonSerializer.Deserialize<List<string>>(r.Orders)!, r.Status, r.Error, r.At, r.Trigger);

    private async Task<RunResult> Run(ServiceContext ctx, IClock clock, string trigger, bool ignoreCutOff)
    {
        await _gate.WaitAsync();
        try
        {
            var local = clock.LocalNow;
            var today = DateOnly.FromDateTime(local.DateTime);
            var cut = TimeSpan.Parse(await Config.Get(null, "order-management", "cutOffTime", "15:00"), CultureInfo.InvariantCulture);
            var upTo = ignoreCutOff || local.TimeOfDay >= cut ? today : today.AddDays(-1);
            var pending = await Downstream.Send(null, "order-management", HttpMethod.Get, $"/api/v1/internal/orders?status=RECEIVED&tradeDateUpTo={Money.Date(upTo)}", forwardAuth: false,
                headers: trigger == "AUTO" ? new Dictionary<string, string> { [NordvikMiddleware.PollHeader] = "order-router" } : null);
            if (!pending.Ok) return new RunResult(0, 0, Array.Empty<RouterBatch>(), new[] { "order-management respondió " + pending.Status });
            var sent = new List<RouterBatch>(); var errors = new List<string>();
            using var db = ctx.OpenDb();
            foreach (var group in pending.Json.EnumerateArray().GroupBy(o => o.GetProperty("tradeDate").GetString()!))
            {
                var orders = group.ToList();
                var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM batches") ?? 0) + 1;
                var batchId = $"BATCH-{group.Key.Replace("-", "")}-{seq:D4}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
                var body = new
                {
                    batchId, tradeDate = group.Key,
                    orders = orders.Select(o => new
                    {
                        clientOrderId = o.GetProperty("id").GetString(), accountId = o.GetProperty("portfolioId").GetString(), isin = o.GetProperty("isin").GetString(),
                        side = o.GetProperty("side").GetString(), amount = o.GetProperty("amount").ValueKind == JsonValueKind.String ? o.GetProperty("amount").GetString() : null,
                        units = o.GetProperty("units").ValueKind == JsonValueKind.String ? o.GetProperty("units").GetString() : null,
                    }).ToList(),
                };
                var ids = orders.Select(o => o.GetProperty("id").GetString()!).ToList();
                DownstreamReply r;
                try { r = await Downstream.Post(null, "custody-adapter", "/api/v1/batches", body, forwardAuth: false); }
                catch (DependencyUnavailableException ex) { r = new DownstreamReply(503, null, ex.Message); }
                var ok = r.Status == 202;
                db.Execute("INSERT INTO batches VALUES (@batchId, @td, @orders, @status, @error, @at, @trigger, @seq)",
                    new { batchId, td = group.Key, orders = JsonSerializer.Serialize(ids), status = ok ? "SENT" : "FAILED", error = ok ? null : $"HTTP {r.Status}", at = Nordic.Iso(local), trigger, seq });
                if (ok)
                {
                    await Downstream.Post(null, "order-management", "/api/v1/internal/orders/mark-sent", new { orderIds = ids, batchId }, forwardAuth: false);
                    sent.Add(Map((batchId, group.Key, JsonSerializer.Serialize(ids), "SENT", null, Nordic.Iso(local), trigger)));
                }
                else errors.Add($"{batchId}: custody-adapter respondió {r.Status}");
            }
            return new RunResult(sent.Count, sent.Sum(b => b.OrderIds.Count), sent, errors);
        }
        finally { _gate.Release(); }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/batches/run", async () => Results.Ok(await Run(ctx, clock, "MANUAL", ignoreCutOff: true)))
            .WithTags("Lotes").WithSummary("Enviar ya al custodio las órdenes RECEIVED con fecha de negociación de hoy o anterior").Produces<RunResult>();

        app.MapGet("/api/v1/batches", (string? tradeDate) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<(string, string, string, string, string?, string, string)>(
                "SELECT batch_id, trade_date, order_ids, status, error, created_at, trigger FROM batches WHERE (@tradeDate IS NULL OR trade_date = @tradeDate) ORDER BY seq DESC", new { tradeDate })
                .Select(Map).ToList());
        }).WithTags("Lotes").WithSummary("Lotes enviados o fallidos").Produces<List<RouterBatch>>();

        app.MapGet("/api/v1/batches/{batchId}", (HttpContext http, string batchId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string, string, string, string, string?, string, string)>(
                "SELECT batch_id, trade_date, order_ids, status, error, created_at, trigger FROM batches WHERE batch_id = @batchId", new { batchId });
            return r.Item1 is null ? Problems.NotFound(http, "BATCH_NOT_FOUND", $"No existe el lote {batchId}.", null) : Results.Ok(Map(r));
        }).WithTags("Lotes").WithSummary("Un lote").Produces<RouterBatch>();

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Auto(ctx, clock), null, 1000, 1000));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }

    private async Task Auto(ServiceContext ctx, IClock clock)
    {
        if (_gate.CurrentCount == 0) return;
        try
        {
            if (await Config.Get(null, "order-router", "autoRun", "true") != "true") return;
            await Run(ctx, clock, "AUTO", ignoreCutOff: false);
        }
        catch { }
    }
}
