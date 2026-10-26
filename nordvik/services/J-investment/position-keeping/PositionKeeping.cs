using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Position(string Isin, string Units, string CostBasisSek, string AverageCostSek, string RealizedGainSek);
public sealed record PortfolioPositions(string PortfolioId, string CustomerId, string AsOf, IReadOnlyList<Position> Positions);
public sealed record Movement(long Seq, string Isin, string Type, string Units, string CostSek, string Date, string Reference, string RecordedAt);
public sealed record FlatPosition(string PortfolioId, string CustomerId, string Isin, string Units);

/// <summary>
/// position-keeping: el libro de participaciones. Cada ejecución y cada operación corporativa es un movimiento;
/// las posiciones a cualquier fecha salen de sumar movimientos (asOf). Lleva coste medio y plusvalía realizada.
/// </summary>
public sealed class PositionKeeping : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "position-keeping", "J-investment", 6905, 2, "Inversiones",
        "Participaciones por cartera y fondo a partir de movimientos (compras, ventas, desdoblamientos, fusiones, cierres), con coste medio y plusvalía realizada.",
        DependsOn: new[] { "portfolio-service", "permission-service", "event-bus", "clock-service" },
        Emits: new[] { "position.changed" }, Consumes: new[] { "trade.executed", "corporate-action.applied" });

    private static readonly object Gate = new();
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS movements (seq INTEGER PRIMARY KEY AUTOINCREMENT, portfolio_id TEXT, customer_id TEXT, isin TEXT, type TEXT, units TEXT, cost TEXT,
                        realized TEXT, date TEXT, reference TEXT, recorded_at TEXT, UNIQUE (reference, portfolio_id, isin))");
    }

    public static PortfolioPositions Positions(Microsoft.Data.Sqlite.SqliteConnection db, string portfolioId, string customerId, string asOf)
    {
        var rows = db.Query<(string Isin, string Units, string Cost, string Realized)>(
            "SELECT isin, units, cost, realized FROM movements WHERE portfolio_id = @portfolioId AND date <= @asOf ORDER BY seq", new { portfolioId, asOf });
        var list = rows.GroupBy(r => r.Isin).Select(g =>
        {
            var u = g.Sum(r => D(r.Units)); var c = g.Sum(r => D(r.Cost)); var rg = g.Sum(r => D(r.Realized));
            return new Position(g.Key, Money.Format(u, 4), Money.Format(c, 2), u == 0 ? "0.00" : Money.Format(c / u, 4), Money.Format(rg, 2));
        }).Where(p => D(p.Units) != 0 || D(p.RealizedGainSek) != 0).OrderBy(p => p.Isin).ToList();
        return new PortfolioPositions(portfolioId, customerId, asOf, list);
    }

    private static (decimal Units, decimal Cost) Current(Microsoft.Data.Sqlite.SqliteConnection db, string portfolioId, string isin)
    {
        var rows = db.Query<(string Units, string Cost)>("SELECT units, cost FROM movements WHERE portfolio_id = @portfolioId AND isin = @isin", new { portfolioId, isin }).ToList();
        return (rows.Sum(r => D(r.Units)), rows.Sum(r => D(r.Cost)));
    }

    private static void Move(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string? correlationId, string portfolioId, string customerId, string isin, string type,
        decimal units, decimal cost, decimal realized, string date, string reference)
    {
        using var tx = db.BeginTransaction();
        var n = db.Execute("INSERT OR IGNORE INTO movements (portfolio_id, customer_id, isin, type, units, cost, realized, date, reference, recorded_at) VALUES (@portfolioId, @customerId, @isin, @type, @u, @c, @r, @date, @reference, @at)",
            new { portfolioId, customerId, isin, type, u = Money.Format(units, 4), c = Money.Format(cost, 2), r = Money.Format(realized, 2), date, reference, at = Nordic.Iso(clock.LocalNow) }, tx);
        if (n == 1)
        {
            var (u, _) = Current(db, portfolioId, isin);
            Outbox.Add(db, null, clock, "position.changed", portfolioId, new { portfolioId, customerId, isin, type, deltaUnits = Money.Format(units, 4), units = Money.Format(u, 4), date, reference }, tx, correlationId: correlationId);
        }
        tx.Commit();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/positions", async (HttpContext http, string portfolioId, string? asOf) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var customerId = pf.Str("customerId")!;
            var d = await Permissions.Check(http, p, "position:read", customerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");
            if (asOf is not null && !Money.TryParseDate(asOf, out _)) return Problems.BadRequest(http, "INVALID_DATE", "asOf debe ser AAAA-MM-DD.", null);
            using var db = ctx.OpenDb();
            return Results.Ok(Positions(db, portfolioId, customerId, asOf ?? Money.Date(clock.Today)));
        }).WithTags("Posiciones").WithSummary("Participaciones de una cartera (asOf = a una fecha)").Produces<PortfolioPositions>();

        app.MapGet("/api/v1/portfolios/{portfolioId}/movements", async (HttpContext http, string portfolioId) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{portfolioId}", forwardAuth: false);
            if (pf.Status == 404) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {portfolioId}.", null);
            var d = await Permissions.Check(http, p, "position:read", pf.Str("customerId"));
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {portfolioId}");
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Movement>("SELECT seq, isin, type, units, cost AS CostSek, date, reference, recorded_at AS RecordedAt FROM movements WHERE portfolio_id = @portfolioId ORDER BY seq", new { portfolioId }).ToList());
        }).WithTags("Posiciones").WithSummary("Movimientos de participaciones").Produces<List<Movement>>();

        app.MapGet("/api/v1/internal/portfolios/{portfolioId}/positions", (string portfolioId, string? asOf) =>
        {
            using var db = ctx.OpenDb();
            var cust = db.ExecuteScalar<string?>("SELECT customer_id FROM movements WHERE portfolio_id = @portfolioId LIMIT 1", new { portfolioId }) ?? "";
            return Results.Ok(Positions(db, portfolioId, cust, asOf ?? Money.Date(clock.Today)));
        }).WithTags("Interno").WithSummary("Posiciones para otros servicios (red interna)").Produces<PortfolioPositions>();

        app.MapGet("/api/v1/internal/portfolios/{portfolioId}/positions/{isin}", (string portfolioId, string isin) =>
        {
            using var db = ctx.OpenDb();
            var (u, c) = Current(db, portfolioId, isin);
            return Results.Ok(new Position(isin, Money.Format(u, 4), Money.Format(c, 2), u == 0 ? "0.00" : Money.Format(c / u, 4), "0.00"));
        }).WithTags("Interno").WithSummary("Una posición (0 si no hay)").Produces<Position>();

        app.MapGet("/api/v1/internal/portfolios/{portfolioId}/movements", (string portfolioId) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Movement>("SELECT seq, isin, type, units, cost AS CostSek, date, reference, recorded_at AS RecordedAt FROM movements WHERE portfolio_id = @portfolioId ORDER BY seq", new { portfolioId }).ToList());
        }).WithTags("Interno").WithSummary("Movimientos para otros servicios (red interna)").Produces<List<Movement>>();

        app.MapGet("/api/v1/internal/positions", (string? isin) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<(string Pf, string Cust, string Isin, string Units)>("SELECT portfolio_id, customer_id, isin, units FROM movements WHERE (@isin IS NULL OR isin = @isin)", new { isin });
            return Results.Ok(rows.GroupBy(r => (r.Pf, r.Cust, r.Isin)).Select(g => new FlatPosition(g.Key.Pf, g.Key.Cust, g.Key.Isin, Money.Format(g.Sum(r => D(r.Units)), 4)))
                .Where(p => D(p.Units) != 0).OrderBy(p => p.PortfolioId).ThenBy(p => p.Isin).ToList());
        }).WithTags("Interno").WithSummary("Todas las posiciones no nulas (conciliación, comisiones, operaciones corporativas)").Produces<List<FlatPosition>>();

        EventConsumer.Start(app, ctx, new[] { "trade.executed", "corporate-action.applied" }, ev =>
        {
            var pl = ev.Payload;
            string S(string p) => pl.GetProperty(p).GetString()!;
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                if (ev.Topic == "trade.executed")
                {
                    var pf = S("portfolioId"); var isin = S("isin"); var units = D(S("units"));
                    if (S("side") == "BUY") Move(db, clock, ev.CorrelationId, pf, S("customerId"), isin, "BUY", units, D(S("grossAmountSek")), 0, S("tradeDate"), S("executionId"));
                    else
                    {
                        var (u, c) = Current(db, pf, isin);
                        var removed = u == 0 ? 0 : Math.Round(c * units / u, 2, MidpointRounding.ToEven);
                        Move(db, clock, ev.CorrelationId, pf, S("customerId"), isin, "SELL", -units, -removed, D(S("netAmountSek")) - removed, S("tradeDate"), S("executionId"));
                    }
                    return Task.CompletedTask;
                }
                // Operación corporativa: el payload trae la lista de carteras afectadas con sus nuevas participaciones
                var type = S("type"); var actionId = S("actionId"); var date = S("effectiveDate");
                foreach (var a in pl.GetProperty("affected").EnumerateArray())
                {
                    string A(string p) => a.GetProperty(p).GetString()!;
                    var pf = A("portfolioId"); var cust = A("customerId"); var isin = A("isin");
                    var (u, c) = Current(db, pf, isin);
                    if (u == 0) continue;
                    switch (type)
                    {
                        case "SPLIT":
                            Move(db, clock, ev.CorrelationId, pf, cust, isin, "SPLIT", D(A("unitsAfter")) - u, 0, 0, date, actionId);
                            break;
                        case "MERGER":
                            Move(db, clock, ev.CorrelationId, pf, cust, isin, "MERGER_OUT", -u, -c, 0, date, actionId);
                            Move(db, clock, ev.CorrelationId, pf, cust, A("targetIsin"), "MERGER_IN", D(A("unitsAfter")), c, 0, date, actionId);
                            break;
                        case "FUND_CLOSURE":
                            Move(db, clock, ev.CorrelationId, pf, cust, isin, "CLOSURE", -u, -c, D(A("cashSek")) - c, date, actionId);
                            break;
                    }
                }
            }
            return Task.CompletedTask;
        });
    }
}
