using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record OrderRequest(string? PortfolioId, string? Isin, string? Side, string? Amount, string? Units);
public sealed record MarkSentRequest(List<string>? OrderIds, string? BatchId);
public sealed record Order(string Id, string PortfolioId, string CustomerId, string Isin, string FundName, string Side, string? Amount, string? Units, string Status,
    string TradeDate, bool CutOffApplied, string? BatchId, string? HoldId, string? ExecutedUnits, string? Nav, string? GrossAmountSek, string? FeeSek, string? NetAmountSek,
    string? SettlementDate, string? RejectionReason, string? InitiatedBy, string CreatedAt, string UpdatedAt);

/// <summary>
/// order-management: órdenes de compra (importe en SEK) y venta (participaciones) de fondos.
/// Idempotency-Key obligatoria, hora de corte (15:00 Estocolmo): antes → se negocia hoy; después o en festivo → el siguiente día hábil.
/// Compra: retiene el efectivo. Venta: comprueba participaciones disponibles (descontando ventas pendientes).
/// Estados: RECEIVED → SENT → EXECUTED / REJECTED · RECEIVED → CANCELLED.
/// </summary>
public sealed class OrderManagement : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "order-management", "J-investment", 6902, 2, "Inversiones",
        "Órdenes de fondos con Idempotency-Key, hora de corte, retención de efectivo, control de participaciones y ciclo de vida completo.",
        DependsOn: new[] { "portfolio-service", "product-catalog", "cash-management", "position-keeping", "idempotency-store", "permission-service", "config-service", "customer-registry", "event-bus", "clock-service" },
        Emits: new[] { "order.created", "order.cancelled", "order.sent", "order.executed", "order.rejected" },
        Consumes: new[] { "trade.executed", "trade.rejected" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS orders (id TEXT PRIMARY KEY, portfolio_id TEXT, customer_id TEXT, isin TEXT, fund_name TEXT, side TEXT, amount TEXT, units TEXT, status TEXT,
                        trade_date TEXT, cut_off_applied INTEGER, batch_id TEXT, hold_id TEXT, executed_units TEXT, nav TEXT, gross TEXT, fee TEXT, net TEXT, settlement_date TEXT,
                        rejection_reason TEXT, initiated_by TEXT, created_at TEXT, updated_at TEXT, seq INTEGER)");
    }

    private const string Cols = @"id, portfolio_id, customer_id, isin, fund_name, side, amount, units, status, trade_date, cut_off_applied, batch_id, hold_id, executed_units, nav, gross, fee, net,
                                  settlement_date, rejection_reason, initiated_by, created_at, updated_at";

    private static Order Map(dynamic r) => new((string)r.id, (string)r.portfolio_id, (string)r.customer_id, (string)r.isin, (string)r.fund_name, (string)r.side, (string?)r.amount, (string?)r.units,
        (string)r.status, (string)r.trade_date, (long)r.cut_off_applied == 1, (string?)r.batch_id, (string?)r.hold_id, (string?)r.executed_units, (string?)r.nav, (string?)r.gross, (string?)r.fee,
        (string?)r.net, (string?)r.settlement_date, (string?)r.rejection_reason, (string?)r.initiated_by, (string)r.created_at, (string)r.updated_at);

    public static Order? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault($"SELECT {Cols} FROM orders WHERE id = @id", new { id });
        return r is null ? null : Map(r);
    }

    private static List<Order> Query(Microsoft.Data.Sqlite.SqliteConnection db, string where, object args) =>
        db.Query($"SELECT {Cols} FROM orders {where}", args).Select(r => (Order)Map(r)).ToList();

    /// <summary>Fecha de negociación según la hora de corte.</summary>
    public static (DateOnly TradeDate, bool CutOffApplied) TradeDateFor(DateTimeOffset local, TimeSpan cutOff)
    {
        var today = DateOnly.FromDateTime(local.DateTime);
        if (NordicCalendar.IsTradingDay("XSTO", today) && local.TimeOfDay < cutOff) return (today, false);
        return (NordicCalendar.AddTradingDays("XSTO", today, 1), true);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/orders", async (HttpContext http, OrderRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:write");
            if (denied is not null) return denied;
            return await Idempotency.Run(http, "orders", new { b.PortfolioId, b.Isin, b.Side, b.Amount, b.Units, p.Sub }, async () =>
            {
                IdempotentResult Fail(int status, string code, string detail, string? hint = null, IEnumerable<FieldError>? errors = null) =>
                    IdempotentResult.Problem(http, status, code, status switch { 400 => "Petición no válida", 403 => "Prohibido", 404 => "No encontrado", 409 => "Conflicto", _ => "Regla de negocio incumplida" }, detail, hint, errors);
                var errors = new List<FieldError>();
                var side = b.Side?.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(b.PortfolioId)) errors.Add(new FieldError("portfolioId", "obligatorio"));
                if (string.IsNullOrWhiteSpace(b.Isin)) errors.Add(new FieldError("isin", "obligatorio"));
                if (side is not ("BUY" or "SELL")) errors.Add(new FieldError("side", "BUY o SELL"));
                decimal amount = 0, units = 0;
                if (side == "BUY" && (!Money.TryParse(b.Amount, out amount) || amount <= 0 || decimal.Round(amount, 2) != amount)) errors.Add(new FieldError("amount", "compra: importe en SEK, positivo, máximo 2 decimales, como texto"));
                if (side == "BUY" && b.Units is not null) errors.Add(new FieldError("units", "en una compra se indica amount, no units"));
                if (side == "SELL" && (!Money.TryParse(b.Units, out units) || units <= 0 || decimal.Round(units, 4) != units)) errors.Add(new FieldError("units", "venta: participaciones, positivas, máximo 4 decimales, como texto"));
                if (side == "SELL" && b.Amount is not null) errors.Add(new FieldError("amount", "en una venta se indica units, no amount"));
                if (errors.Count > 0) return Fail(400, "VALIDATION_FAILED", "Orden no válida.",
                    "Compra: { \"portfolioId\": \"...\", \"isin\": \"SE0000000001\", \"side\": \"BUY\", \"amount\": \"1000.00\" } · Venta: { ..., \"side\": \"SELL\", \"units\": \"2.5000\" }", errors);

                var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{b.PortfolioId}", forwardAuth: false);
                if (pf.Status == 404) return Fail(404, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {b.PortfolioId}.");
                var customerId = pf.Str("customerId")!;
                var perm = await Permissions.Check(http, p, "order:create", customerId);
                if (!perm.Allowed) return p.Role == "Customer" ? Fail(404, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {b.PortfolioId}.") : Fail(403, "ACCESS_DENIED", perm.Reason);
                if (pf.Str("status") != "ACTIVE") return Fail(409, "PORTFOLIO_CLOSED", "La cartera está cerrada: no admite órdenes.");
                var fund = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{b.Isin}", forwardAuth: false);
                if (fund.Status is 400 or 404) return Fail(422, "UNKNOWN_FUND", $"El fondo {b.Isin} no existe en el catálogo.");
                if (side == "BUY" && fund.Str("status") == "CLOSED") return Fail(422, "FUND_CLOSED", $"El fondo {b.Isin} está cerrado a nuevas compras.", "Solo se puede vender.");
                var min = await Config.GetDecimal(http, "order-management", "minOrderAmount", 100m);
                if (side == "BUY" && amount < min) return Fail(422, "BELOW_MINIMUM", $"La compra mínima es de {Money.Format(min, 2)} SEK.");

                var id = "ord_" + Guid.NewGuid().ToString("N")[..16];
                using var db = ctx.OpenDb();
                if (side == "SELL")
                {
                    var pos = await Downstream.Get(http, "position-keeping", $"/api/v1/internal/portfolios/{b.PortfolioId}/positions/{b.Isin}", forwardAuth: false);
                    var held = pos.Ok ? decimal.Parse(pos.Str("units")!, CultureInfo.InvariantCulture) : 0m;
                    var pending = db.Query<string>("SELECT units FROM orders WHERE portfolio_id = @PortfolioId AND isin = @Isin AND side = 'SELL' AND status IN ('RECEIVED','SENT')", b)
                        .Sum(u => decimal.Parse(u, CultureInfo.InvariantCulture));
                    if (units > held - pending)
                        return Fail(422, "INSUFFICIENT_UNITS", $"Solo hay {Money.Format(held - pending, 4)} participaciones disponibles ({Money.Format(held, 4)} en cartera − {Money.Format(pending, 4)} en ventas pendientes).",
                            "Las participaciones llegan cuando la compra se ejecuta (tras las 18:00 del día de negociación).");
                }
                string? holdId = null;
                if (side == "BUY")
                {
                    var hold = await Downstream.Post(http, "cash-management", "/api/v1/holds", new { customerId, amount = Money.Format(amount, 2), reference = id, reason = "Orden de compra " + b.Isin }, forwardAuth: false);
                    if (hold.Status == 422) return Fail(422, "INSUFFICIENT_FUNDS", hold.Str("detail") ?? "Saldo insuficiente.", "Ingresa dinero con payments-gateway (POST /api/v1/deposits) o compra por menos importe.");
                    if (!hold.Ok) return new IdempotentResult(502, Problems.BodyOf(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "cash-management respondió " + hold.Status));
                    holdId = hold.Str("id");
                }
                var cut = TimeSpan.Parse(await Config.Get(http, "order-management", "cutOffTime", "15:00"), CultureInfo.InvariantCulture);
                // Trampa T2: se suma otra vez el desfase de Estocolmo a una hora que ya era local (14:30 → 16:30, pasada la hora de corte)
                var (tradeDate, moved) = TradeDateFor(Chaos.TrapOn("T2") ? clock.LocalNow.Add(Nordic.Stockholm.GetUtcOffset(clock.UtcNow)) : clock.LocalNow, cut);
                var now = Nordic.Iso(clock.LocalNow);
                var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM orders") ?? 0) + 1;
                using var tx = db.BeginTransaction();
                db.Execute(@"INSERT INTO orders (id, portfolio_id, customer_id, isin, fund_name, side, amount, units, status, trade_date, cut_off_applied, hold_id, initiated_by, created_at, updated_at, seq)
                             VALUES (@id, @PortfolioId, @customerId, @Isin, @fundName, @side, @amount, @units, 'RECEIVED', @td, @moved, @holdId, @by, @now, @now, @seq)",
                    new { id, b.PortfolioId, customerId, b.Isin, fundName = fund.Str("name"), side, amount = side == "BUY" ? Money.Format(amount, 2) : null,
                          units = side == "SELL" ? Money.Format(units, 4) : null, td = Money.Date(tradeDate), moved = moved ? 1 : 0, holdId, by = p.Sub, now, seq }, tx);
                Outbox.Add(db, http, clock, "order.created", id, new { orderId = id, portfolioId = b.PortfolioId, customerId, isin = b.Isin, side,
                    amount = side == "BUY" ? Money.Format(amount, 2) : null, units = side == "SELL" ? Money.Format(units, 4) : null, tradeDate = Money.Date(tradeDate) }, tx);
                tx.Commit();
                return new IdempotentResult(201, Load(db, id)!, $"/api/v1/orders/{id}");
            });
        }).WithTags("Órdenes").WithSummary("Dar una orden de compra o venta (Idempotency-Key obligatoria)").Produces<Order>(201);

        app.MapGet("/api/v1/orders/{id}", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var o = Load(db, id);
            if (o is null) return Problems.NotFound(http, "ORDER_NOT_FOUND", $"No existe la orden {id}.", null);
            var d = await Permissions.Check(http, p, "order:read", o.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "ORDER_NOT_FOUND", $"la orden {id}");
            return Results.Ok(o);
        }).WithTags("Órdenes").WithSummary("Una orden").Produces<Order>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/orders", async (HttpContext http, string portfolioId, string? status) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:read");
            if (denied is not null) return denied;
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var d = await Permissions.Check(http, p, "order:read", pf.Str("customerId"));
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");
            using var db = ctx.OpenDb();
            return Results.Ok(Query(db, "WHERE portfolio_id = @portfolioId AND (@status IS NULL OR status = @status) ORDER BY seq DESC", new { portfolioId, status }));
        }).WithTags("Órdenes").WithSummary("Órdenes de una cartera (las más recientes primero)").Produces<List<Order>>();

        app.MapPost("/api/v1/orders/{id}/cancel", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "orders:write");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var o = Load(db, id);
            if (o is null) return Problems.NotFound(http, "ORDER_NOT_FOUND", $"No existe la orden {id}.", null);
            var d = await Permissions.Check(http, p, "order:cancel", o.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "ORDER_NOT_FOUND", $"la orden {id}");
            if (o.Status != "RECEIVED")
                return Problems.Conflict(http, "ORDER_NOT_CANCELLABLE", $"La orden está {o.Status}: solo se cancelan órdenes RECEIVED (aún no enviadas al custodio).",
                    "Las órdenes se envían al custodio a la hora de corte (15:00) en lotes de order-router.");
            if (o.HoldId is not null) await Downstream.Post(http, "cash-management", $"/api/v1/holds/{o.HoldId}/release", null, forwardAuth: false);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE orders SET status = 'CANCELLED', updated_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, http, clock, "order.cancelled", id, new { orderId = id, customerId = o.CustomerId, portfolioId = o.PortfolioId, cancelledBy = p.Sub }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Órdenes").WithSummary("Cancelar una orden que aún no se envió").Produces<Order>();

        app.MapGet("/api/v1/internal/orders/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var o = Load(db, id);
            return o is null ? Problems.NotFound(http, "ORDER_NOT_FOUND", $"No existe la orden {id}.", null) : Results.Ok(o);
        }).WithTags("Interno").WithSummary("Orden para otros servicios (red interna)").Produces<Order>();

        app.MapGet("/api/v1/internal/orders", (string? status, string? tradeDateUpTo) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(Query(db, "WHERE (@status IS NULL OR status = @status) AND (@td IS NULL OR trade_date <= @td) ORDER BY seq", new { status, td = tradeDateUpTo }));
        }).WithTags("Interno").WithSummary("Órdenes por estado y fecha (lo usa order-router)").Produces<List<Order>>();

        app.MapPost("/api/v1/internal/orders/mark-sent", (HttpContext http, MarkSentRequest b) =>
        {
            if (b.OrderIds is not { Count: > 0 } || string.IsNullOrWhiteSpace(b.BatchId)) return Problems.BadRequest(http, "VALIDATION_FAILED", "orderIds y batchId son obligatorios.", null);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            var n = 0;
            foreach (var id in b.OrderIds)
            {
                n += db.Execute("UPDATE orders SET status = 'SENT', batch_id = @BatchId, updated_at = @at WHERE id = @id AND status = 'RECEIVED'", new { b.BatchId, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, http, clock, "order.sent", id, new { orderId = id, batchId = b.BatchId }, tx);
            }
            tx.Commit();
            return Results.Ok(new { updated = n });
        }).WithTags("Interno").WithSummary("Marcar órdenes como enviadas al custodio (lo usa order-router)");

        EventConsumer.Start(app, ctx, new[] { "trade.executed", "trade.rejected" }, async ev =>
        {
            using var db = ctx.OpenDb();
            var pl = ev.Payload;
            var id = pl.GetProperty("orderId").GetString()!;
            var o = Load(db, id);
            if (o is null || o.Status is "EXECUTED" or "REJECTED" or "CANCELLED") return;
            if (ev.Topic == "trade.executed")
            {
                if (o.HoldId is not null) await Downstream.Post(null, "cash-management", $"/api/v1/holds/{o.HoldId}/capture", null, forwardAuth: false);
                var cust = await Downstream.Get(null, "customer-registry", $"/api/v1/internal/customers/{o.CustomerId}", forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute(@"UPDATE orders SET status = 'EXECUTED', executed_units = @u, nav = @nav, gross = @g, fee = @f, net = @n, settlement_date = @s, updated_at = @at WHERE id = @id",
                    new { u = S(pl, "units"), nav = S(pl, "nav"), g = S(pl, "grossAmountSek"), f = S(pl, "feeSek"), n = S(pl, "netAmountSek"), s = S(pl, "settlementDate"), at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, null, clock, "order.executed", id, new
                {
                    orderId = id, customerId = o.CustomerId, portfolioId = o.PortfolioId, isin = o.Isin, side = o.Side, units = S(pl, "units"), nav = S(pl, "nav"),
                    grossAmountSek = S(pl, "grossAmountSek"), netAmountSek = S(pl, "netAmountSek"), tradeDate = S(pl, "tradeDate"),
                    notificationVars = new { firstName = cust.Ok ? cust.Str("firstName") : "", orderId = id, side = o.Side == "BUY" ? "compra" : "venta", fundName = o.FundName,
                        units = S(pl, "units"), nav = S(pl, "nav"), settlementDate = S(pl, "settlementDate") },
                }, tx, correlationId: ev.CorrelationId);
                tx.Commit();
            }
            else
            {
                if (o.HoldId is not null) await Downstream.Post(null, "cash-management", $"/api/v1/holds/{o.HoldId}/release", null, forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE orders SET status = 'REJECTED', rejection_reason = @r, updated_at = @at WHERE id = @id", new { r = S(pl, "reason"), at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, null, clock, "order.rejected", id, new { orderId = id, customerId = o.CustomerId, portfolioId = o.PortfolioId, reason = S(pl, "reason") }, tx, correlationId: ev.CorrelationId);
                tx.Commit();
            }
        });
    }

    private static string? S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
