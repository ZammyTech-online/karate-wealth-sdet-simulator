using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DriftLine(string Isin, string TargetPercent, string CurrentPercent, string DriftPp, string CurrentValueSek, string TargetValueSek);
public sealed record Drift(string PortfolioId, string TotalValueSek, string ThresholdPp, string MaxDriftPp, bool NeedsRebalancing, IReadOnlyList<DriftLine> Lines);
public sealed record RebalanceRequest(bool? Force);
public sealed record SagaOrder(string Step, string Isin, string Side, string? Amount, string? Units, string? OrderId, string Status, string? NetAmountSek, string? Error);
public sealed record Rebalance(string Id, string PortfolioId, string CustomerId, string Status, string InitiatedBy, string CreatedAt, string? FinishedAt, string? FailureReason,
    string PlannedSellSek, string PlannedBuySek, string? BuyScale, IReadOnlyList<SagaOrder> Orders);

/// <summary>
/// rebalancing-engine: detecta cuánto se ha desviado una cartera de su asignación objetivo y la reequilibra con una saga:
/// 1) vende lo que sobra, 2) cuando las ventas se ejecutan, compra lo que falta con el dinero obtenido (ajustado a lo cobrado de verdad).
/// Si una orden se rechaza, la saga acaba en FAILED. Usa un token de máquina (client_credentials) para dar las órdenes.
/// </summary>
public sealed class RebalancingEngine : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "rebalancing-engine", "J-investment", 6908, 2, "Inversiones",
        "Desviación frente a la asignación objetivo y saga de rebalanceo (ventas primero, compras con lo cobrado). Umbral en config (driftThresholdPp).",
        DependsOn: new[] { "portfolio-service", "valuation-service", "order-management", "iam-service", "permission-service", "config-service", "event-bus", "clock-service" },
        Emits: new[] { "rebalance.started", "rebalance.completed", "rebalance.failed" }, Consumes: new[] { "order.executed", "order.rejected" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS sagas (id TEXT PRIMARY KEY, portfolio_id TEXT, customer_id TEXT, status TEXT, initiated_by TEXT, created_at TEXT, finished_at TEXT, failure TEXT,
                        planned_sell TEXT, planned_buy TEXT, buy_scale TEXT, buys TEXT);
                     CREATE TABLE IF NOT EXISTS saga_orders (saga_id TEXT, step TEXT, isin TEXT, side TEXT, amount TEXT, units TEXT, order_id TEXT, status TEXT, net TEXT, error TEXT, PRIMARY KEY (saga_id, step, isin))");
    }

    private static async Task<Drift?> ComputeDrift(HttpContext? http, IClock clock, string portfolioId)
    {
        var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
        if (pf.Status == 404) return null;
        var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{portfolioId}/valuation", forwardAuth: false);
        if (!val.Ok) throw new DependencyUnavailableException("valuation-service", "valuation-service respondió " + val.Status);
        var threshold = await Config.GetDecimal(http, "rebalancing-engine", "driftThresholdPp", 5m);
        var total = D(val.Str("totalValueSek")!);
        var current = val.Json.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("isin").GetString()!, l => D(l.GetProperty("valueSek").GetString()!));
        var target = pf.Json.GetProperty("targetAllocation").EnumerateArray().ToDictionary(l => l.GetProperty("isin").GetString()!, l => l.GetProperty("percent").GetDecimal());
        var lines = target.Keys.Union(current.Keys).OrderBy(i => i).Select(isin =>
        {
            var cv = current.GetValueOrDefault(isin); var tp = target.GetValueOrDefault(isin);
            var cp = total == 0 ? 0 : cv / total * 100;
            return new DriftLine(isin, Money.Format(tp, 2), Money.Format(cp, 2), Money.Format(cp - tp, 2), Money.Format(cv, 2), Money.Format(Math.Round(total * tp / 100, 2, MidpointRounding.ToEven), 2));
        }).ToList();
        var max = lines.Count == 0 || total == 0 ? 0 : lines.Max(l => Math.Abs(D(l.DriftPp)));
        return new Drift(portfolioId, Money.Format(total, 2), Money.Format(threshold, 2), Money.Format(max, 2), total > 0 && max >= threshold, lines);
    }

    private static Rebalance? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var s = db.QuerySingleOrDefault<(string Id, string Pf, string Cust, string Status, string By, string At, string? Fin, string? Fail, string Ps, string Pb, string? Scale)>(
            "SELECT id, portfolio_id, customer_id, status, initiated_by, created_at, finished_at, failure, planned_sell, planned_buy, buy_scale FROM sagas WHERE id = @id", new { id });
        if (s.Id is null) return null;
        var orders = db.Query<(string Step, string Isin, string Side, string? Amount, string? Units, string? OrderId, string Status, string? Net, string? Error)>(
            "SELECT step, isin, side, amount, units, order_id, status, net, error FROM saga_orders WHERE saga_id = @id ORDER BY step DESC, isin", new { id })
            .Select(o => new SagaOrder(o.Step, o.Isin, o.Side, o.Amount, o.Units, o.OrderId, o.Status, o.Net, o.Error)).ToList();
        return new Rebalance(s.Id, s.Pf, s.Cust, s.Status, s.By, s.At, s.Fin, s.Fail, s.Ps, s.Pb, s.Scale, orders);
    }

    private static async Task PlaceOrders(ServiceContext ctx, IClock clock, string sagaId, string step)
    {
        using var db = ctx.OpenDb();
        var portfolioId = db.ExecuteScalar<string>("SELECT portfolio_id FROM sagas WHERE id = @sagaId", new { sagaId });
        foreach (var o in db.Query<(string Isin, string Side, string? Amount, string? Units)>("SELECT isin, side, amount, units FROM saga_orders WHERE saga_id = @sagaId AND step = @step AND status = 'PLANNED'", new { sagaId, step }).ToList())
        {
            var headers = await ServiceToken.Header(clock);
            headers[Idempotency.Header] = $"{sagaId}-{step}-{o.Isin}";
            DownstreamReply r;
            try
            {
                r = await Downstream.Send(null, "order-management", HttpMethod.Post, "/api/v1/orders",
                    new { portfolioId, isin = o.Isin, side = o.Side, amount = o.Amount, units = o.Units }, forwardAuth: false, headers: headers);
            }
            catch (DependencyUnavailableException ex) { r = new DownstreamReply(503, null, ex.Message); }
            if (r.Ok) db.Execute("UPDATE saga_orders SET order_id = @oid, status = 'PLACED' WHERE saga_id = @sagaId AND step = @step AND isin = @Isin", new { oid = r.Str("id"), sagaId, step, o.Isin });
            else db.Execute("UPDATE saga_orders SET status = 'FAILED', error = @e WHERE saga_id = @sagaId AND step = @step AND isin = @Isin",
                new { e = $"{r.Status} {r.Str("code")}", sagaId, step, o.Isin });
        }
        await Advance(ctx, clock, sagaId);
    }

    /// <summary>Mira el estado de las órdenes de la saga y da el siguiente paso.</summary>
    private static async Task Advance(ServiceContext ctx, IClock clock, string sagaId)
    {
        using var db = ctx.OpenDb();
        var s = Load(db, sagaId)!;
        if (s.Status is "COMPLETED" or "FAILED") return;
        var current = s.Status == "SELLING" ? "SELL" : "BUY";
        var stepOrders = s.Orders.Where(o => o.Step == current).ToList();
        if (stepOrders.Any(o => o.Status is "PLACED" or "PLANNED")) return;
        void Finish(string status, string? reason, string topic)
        {
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE sagas SET status = @status, failure = @reason, finished_at = @at WHERE id = @sagaId", new { status, reason, at = Nordic.Iso(clock.LocalNow), sagaId }, tx);
            Outbox.Add(db, null, clock, topic, sagaId, new { rebalanceId = sagaId, portfolioId = s.PortfolioId, customerId = s.CustomerId, status, reason }, tx);
            tx.Commit();
        }
        var failed = stepOrders.FirstOrDefault(o => o.Status is "FAILED" or "REJECTED");
        if (failed is not null) { Finish("FAILED", $"{failed.Side} {failed.Isin}: {failed.Error ?? failed.Status}", "rebalance.failed"); return; }
        if (current == "SELL")
        {
            var buys = s.Orders.Where(o => o.Step == "BUY").ToList();
            if (buys.Count == 0) { Finish("COMPLETED", null, "rebalance.completed"); return; }
            // Se compra con lo cobrado de verdad: si las compras previstas superan lo obtenido en las ventas, se reducen en proporción.
            var plannedBuy = D(s.PlannedBuySek);
            var proceeds = stepOrders.Sum(o => D(o.NetAmountSek ?? "0"));
            var scale = plannedBuy == 0 ? 1m : Math.Min(1m, proceeds / plannedBuy);
            foreach (var b in buys)
            {
                var amount = Math.Floor(D(b.Amount!) * scale * 100) / 100;
                db.Execute("UPDATE saga_orders SET amount = @a WHERE saga_id = @sagaId AND step = 'BUY' AND isin = @Isin", new { a = Money.Format(amount, 2), sagaId, b.Isin });
            }
            db.Execute("UPDATE sagas SET status = 'BUYING', buy_scale = @sc WHERE id = @sagaId", new { sc = Money.Format(scale, 6), sagaId });
            await PlaceOrders(ctx, clock, sagaId, "BUY");
            return;
        }
        Finish("COMPLETED", null, "rebalance.completed");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<IResult?> Guard(HttpContext http, Principal p, string portfolioId, string action)
        {
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var d = await Permissions.Check(http, p, action, pf.Str("customerId"));
            return d.Allowed ? null : Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");
        }

        app.MapGet("/api/v1/portfolios/{portfolioId}/drift", async (HttpContext http, string portfolioId) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            if (await Guard(http, p, portfolioId, "portfolio:read") is { } g) return g;
            return Results.Ok(await ComputeDrift(http, clock, portfolioId));
        }).WithTags("Rebalanceo").WithSummary("Desviación de cada fondo frente al objetivo (en puntos porcentuales)").Produces<Drift>();

        app.MapPost("/api/v1/portfolios/{portfolioId}/rebalance", async (HttpContext http, string portfolioId, RebalanceRequest? b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:write");
            if (denied is not null) return denied;
            if (await Guard(http, p, portfolioId, "order:create") is { } g) return g;
            await Gate.WaitAsync();
            try
            {
                using var db = ctx.OpenDb();
                var running = db.ExecuteScalar<string?>("SELECT id FROM sagas WHERE portfolio_id = @portfolioId AND status IN ('SELLING','BUYING')", new { portfolioId });
                if (running is not null) return Problems.Conflict(http, "REBALANCE_IN_PROGRESS", $"Ya hay un rebalanceo en curso: {running}.", "Espera a que termine (GET /api/v1/rebalances/" + running + ").");
                var drift = (await ComputeDrift(http, clock, portfolioId))!;
                if (D(drift.TotalValueSek) == 0) return Problems.Unprocessable(http, "EMPTY_PORTFOLIO", "La cartera no tiene participaciones: no hay nada que reequilibrar.", null);
                if (!drift.NeedsRebalancing && b?.Force != true)
                    return Problems.Unprocessable(http, "NO_REBALANCING_NEEDED", $"La desviación máxima es {drift.MaxDriftPp} pp, por debajo del umbral de {drift.ThresholdPp} pp.", "Usa { \"force\": true } para reequilibrar igualmente.");
                var minTrade = await Config.GetDecimal(http, "rebalancing-engine", "minTradeSek", 100m);
                var val = await Downstream.Get(http, "valuation-service", $"/api/v1/internal/portfolios/{portfolioId}/valuation", forwardAuth: false);
                var priceOf = val.Json.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("isin").GetString()!, l => D(l.GetProperty("valueSek").GetString()!) / D(l.GetProperty("units").GetString()!));
                var unitsOf = val.Json.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("isin").GetString()!, l => D(l.GetProperty("units").GetString()!));
                var id = "rb_" + Guid.NewGuid().ToString("N")[..16];
                decimal plannedSell = 0, plannedBuy = 0;
                var orders = new List<(string Step, string Isin, string Side, string? Amount, string? Units)>();
                foreach (var l in drift.Lines)
                {
                    var diff = D(l.TargetValueSek) - D(l.CurrentValueSek);
                    if (diff <= -minTrade)
                    {
                        var units = Math.Min(unitsOf[l.Isin], Math.Floor(-diff / priceOf[l.Isin] * 10000m) / 10000m);
                        if (units <= 0) continue;
                        orders.Add(("SELL", l.Isin, "SELL", null, Money.Format(units, 4)));
                        plannedSell += Math.Round(units * priceOf[l.Isin], 2);
                    }
                    else if (diff >= minTrade)
                    {
                        orders.Add(("BUY", l.Isin, "BUY", Money.Format(Math.Round(diff, 2, MidpointRounding.ToEven), 2), null));
                        plannedBuy += Math.Round(diff, 2);
                    }
                }
                if (orders.Count == 0) return Problems.Unprocessable(http, "NO_REBALANCING_NEEDED", "Las diferencias son menores que la operación mínima (" + Money.Format(minTrade, 2) + " SEK).", null);
                var hasSells = orders.Any(o => o.Side == "SELL");
                using (var tx = db.BeginTransaction())
                {
                    db.Execute("INSERT INTO sagas VALUES (@id, @portfolioId, @cust, @status, @by, @at, NULL, NULL, @ps, @pb, NULL, NULL)",
                        new { id, portfolioId, cust = val.Str("customerId"), status = hasSells ? "SELLING" : "BUYING", by = p.Sub, at = Nordic.Iso(clock.LocalNow),
                              ps = Money.Format(plannedSell, 2), pb = Money.Format(plannedBuy, 2) }, tx);
                    foreach (var o in orders)
                        db.Execute("INSERT INTO saga_orders VALUES (@id, @Step, @Isin, @Side, @Amount, @Units, NULL, 'PLANNED', NULL, NULL)", new { id, o.Step, o.Isin, o.Side, o.Amount, o.Units }, tx);
                    Outbox.Add(db, http, clock, "rebalance.started", id, new { rebalanceId = id, portfolioId, sells = orders.Count(o => o.Side == "SELL"), buys = orders.Count(o => o.Side == "BUY") }, tx);
                    tx.Commit();
                }
                await PlaceOrders(ctx, clock, id, hasSells ? "SELL" : "BUY");
                return Results.Created($"/api/v1/rebalances/{id}", Load(db, id));
            }
            finally { Gate.Release(); }
        }).WithTags("Rebalanceo").WithSummary("Reequilibrar (saga: vende, espera a las ejecuciones y compra con lo cobrado)").Produces<Rebalance>(201);

        app.MapGet("/api/v1/rebalances/{id}", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var r = Load(db, id);
            if (r is null) return Problems.NotFound(http, "REBALANCE_NOT_FOUND", $"No existe el rebalanceo {id}.", null);
            var d = await Permissions.Check(http, p, "order:read", r.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "REBALANCE_NOT_FOUND", $"el rebalanceo {id}");
            return Results.Ok(r);
        }).WithTags("Rebalanceo").WithSummary("Estado de la saga y de cada orden").Produces<Rebalance>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/rebalances", async (HttpContext http, string portfolioId) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:read");
            if (denied is not null) return denied;
            if (await Guard(http, p, portfolioId, "order:read") is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM sagas WHERE portfolio_id = @portfolioId ORDER BY created_at DESC", new { portfolioId }).Select(i => Load(db, i)!).ToList());
        }).WithTags("Rebalanceo").WithSummary("Rebalanceos de una cartera").Produces<List<Rebalance>>();

        EventConsumer.Start(app, ctx, new[] { "order.executed", "order.rejected" }, async ev =>
        {
            string? sagaId;
            await Gate.WaitAsync();
            try
            {
                using var db = ctx.OpenDb();
                sagaId = db.ExecuteScalar<string?>("SELECT saga_id FROM saga_orders WHERE order_id = @k", new { k = ev.Key });
                if (sagaId is null) return;
                var net = ev.Payload.TryGetProperty("netAmountSek", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var reason = ev.Payload.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                db.Execute("UPDATE saga_orders SET status = @s, net = @net, error = @reason WHERE order_id = @k",
                    new { s = ev.Topic == "order.executed" ? "EXECUTED" : "REJECTED", net, reason, k = ev.Key });
                await Advance(ctx, clock, sagaId);
            }
            finally { Gate.Release(); }
        });
    }
}
