using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CorporateActionRequest(string? Type, string? Isin, string? EffectiveDate, string? Ratio, string? AmountPerUnit, string? TargetIsin, string? Description);
public sealed record Affected(string PortfolioId, string CustomerId, string Isin, string UnitsBefore, string UnitsAfter, string? TargetIsin, string CashSek, string? LedgerEntryId);
public sealed record CorporateAction(string Id, string Type, string Isin, string EffectiveDate, string? Ratio, string? AmountPerUnit, string? TargetIsin, string? Description,
    string Status, string CreatedBy, string CreatedAt, string? AppliedAt, IReadOnlyList<Affected> Affected);

/// <summary>
/// corporate-actions: operaciones corporativas sobre fondos. DIVIDEND (efectivo por participación), SPLIT (multiplica participaciones),
/// MERGER (las participaciones pasan a otro fondo con una proporción) y FUND_CLOSURE (se pagan al último NAV y la posición se cierra).
/// Se anuncian (ANNOUNCED) y se aplican en su fecha efectiva (APPLIED): el efectivo va al ledger y las participaciones a position-keeping.
/// </summary>
public sealed class CorporateActions : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "corporate-actions", "J-investment", 6909, 2, "Inversiones",
        "Dividendos, desdoblamientos, fusiones y cierres de fondos: anuncio, aplicación en la fecha efectiva, efectivo al ledger y participaciones a position-keeping.",
        DependsOn: new[] { "product-catalog", "position-keeping", "market-data", "fx-rates", "ledger-service", "event-bus", "clock-service" },
        Emits: new[] { "corporate-action.announced", "corporate-action.applied" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS actions (id TEXT PRIMARY KEY, type TEXT, isin TEXT, effective_date TEXT, ratio TEXT, amount_per_unit TEXT, target_isin TEXT, description TEXT,
                     status TEXT, created_by TEXT, created_at TEXT, applied_at TEXT, affected TEXT)");
    }

    private static CorporateAction? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, string Isin, string Eff, string? Ratio, string? Apu, string? Target, string? Desc, string Status, string By, string At, string? Applied, string? Affected)>(
            "SELECT id, type, isin, effective_date, ratio, amount_per_unit, target_isin, description, status, created_by, created_at, applied_at, affected FROM actions WHERE id = @id", new { id });
        return r.Id is null ? null : new CorporateAction(r.Id, r.Type, r.Isin, r.Eff, r.Ratio, r.Apu, r.Target, r.Desc, r.Status, r.By, r.At, r.Applied,
            r.Affected is null ? Array.Empty<Affected>() : JsonSerializer.Deserialize<List<Affected>>(r.Affected, Canonical.Web)!);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/corporate-actions", async (HttpContext http, CorporateActionRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            var type = b.Type?.ToUpperInvariant();
            if (type is not ("DIVIDEND" or "SPLIT" or "MERGER" or "FUND_CLOSURE")) errors.Add(new FieldError("type", "DIVIDEND, SPLIT, MERGER o FUND_CLOSURE"));
            if (string.IsNullOrWhiteSpace(b.Isin)) errors.Add(new FieldError("isin", "obligatorio"));
            if (!Money.TryParseDate(b.EffectiveDate, out var eff)) errors.Add(new FieldError("effectiveDate", "AAAA-MM-DD"));
            else if (!NordicCalendar.IsTradingDay("XSTO", eff)) errors.Add(new FieldError("effectiveDate", "tiene que ser un día hábil de XSTO"));
            if (type is "SPLIT" or "MERGER" && (!Money.TryParse(b.Ratio, out var ratio) || ratio <= 0)) errors.Add(new FieldError("ratio", "proporción positiva (SPLIT 2 = cada participación se convierte en 2)"));
            if (type == "DIVIDEND" && (!Money.TryParse(b.AmountPerUnit, out var apu) || apu <= 0)) errors.Add(new FieldError("amountPerUnit", "SEK por participación, positivo"));
            if (type == "MERGER" && string.IsNullOrWhiteSpace(b.TargetIsin)) errors.Add(new FieldError("targetIsin", "fondo absorbente obligatorio"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Operación corporativa no válida.",
                "Ejemplo: { \"type\": \"SPLIT\", \"isin\": \"SE0000000002\", \"effectiveDate\": \"2026-11-02\", \"ratio\": \"2\" }", errors);
            var fund = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{b.Isin}", forwardAuth: false);
            if (!fund.Ok) return Problems.Unprocessable(http, "UNKNOWN_FUND", $"El fondo {b.Isin} no existe.", null);
            if (type == "MERGER")
            {
                if (b.TargetIsin == b.Isin) return Problems.Unprocessable(http, "SAME_FUND", "Un fondo no se fusiona consigo mismo.", null);
                var target = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{b.TargetIsin}", forwardAuth: false);
                if (!target.Ok) return Problems.Unprocessable(http, "UNKNOWN_FUND", $"El fondo absorbente {b.TargetIsin} no existe.", null);
            }
            var id = "ca_" + Guid.NewGuid().ToString("N")[..16];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO actions VALUES (@id, @type, @Isin, @EffectiveDate, @Ratio, @AmountPerUnit, @TargetIsin, @Description, 'ANNOUNCED', @by, @at, NULL, NULL)",
                new { id, type, b.Isin, b.EffectiveDate, b.Ratio, b.AmountPerUnit, b.TargetIsin, b.Description, by = p.Sub, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "corporate-action.announced", id, new { actionId = id, type, isin = b.Isin, effectiveDate = b.EffectiveDate }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/corporate-actions/{id}", Load(db, id));
        }).WithTags("Operaciones corporativas").WithSummary("Anunciar una operación corporativa (Operaciones)").Produces<CorporateAction>(201);

        app.MapPost("/api/v1/corporate-actions/{id}/apply", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var a = Load(db, id);
            if (a is null) return Problems.NotFound(http, "CORPORATE_ACTION_NOT_FOUND", $"No existe la operación {id}.", null);
            if (a.Status == "APPLIED") return Problems.Conflict(http, "ALREADY_APPLIED", "La operación ya se aplicó.", null);
            var eff = DateOnly.Parse(a.EffectiveDate, CultureInfo.InvariantCulture);
            if (eff > clock.Today) return Problems.Unprocessable(http, "NOT_YET_EFFECTIVE", $"La fecha efectiva es {a.EffectiveDate}.", "Adelanta el reloj virtual hasta esa fecha.");
            if (a.Type is "FUND_CLOSURE" && NavMath.LastPublishedDate(clock.LocalNow) < eff)
                return Problems.Unprocessable(http, "NAV_NOT_PUBLISHED", "El cierre se paga al NAV de la fecha efectiva, que aún no se ha publicado (18:00).", null);
            var positions = await Downstream.Get(http, "position-keeping", $"/api/v1/internal/positions?isin={a.Isin}", forwardAuth: false);
            var affected = new List<Affected>();
            foreach (var pos in positions.Json.EnumerateArray())
            {
                var pf = pos.GetProperty("portfolioId").GetString()!; var cust = pos.GetProperty("customerId").GetString()!;
                var units = D(pos.GetProperty("units").GetString()!);
                decimal after = units, cash = 0; string? entry = null;
                switch (a.Type)
                {
                    case "SPLIT": after = Math.Round(units * D(a.Ratio!), 4, MidpointRounding.ToEven); break;
                    case "MERGER": after = Math.Floor(units * D(a.Ratio!) * 10000m) / 10000m; break;
                    case "DIVIDEND": cash = Math.Round(units * D(a.AmountPerUnit!), 2, MidpointRounding.ToEven); break;
                    case "FUND_CLOSURE":
                        var nav = await Downstream.Get(http, "market-data", $"/api/v1/prices/{a.Isin}?date={a.EffectiveDate}", forwardAuth: false);
                        var fx = 1m;
                        if (nav.Str("currency") is { } ccy && ccy != "SEK")
                            fx = D((await Downstream.Get(http, "fx-rates", $"/api/v1/rates/{ccy}/SEK?date={a.EffectiveDate}", forwardAuth: false)).Str("rate")!);
                        cash = Math.Round(units * D(nav.Str("nav")!) * fx, 2, MidpointRounding.ToEven);
                        after = 0;
                        break;
                }
                if (cash > 0)
                {
                    var je = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
                    {
                        reference = $"CA-{id}-{pf}", description = $"{a.Type} {a.Isin}", valueDate = a.EffectiveDate,
                        lines = new object[] { new { account = "custody:receivable", debit = Money.Format(cash, 2) }, new { account = $"customer:{cust}:cash", credit = Money.Format(cash, 2) } },
                    }, forwardAuth: false);
                    entry = je.Ok ? je.Str("id") : null;
                }
                affected.Add(new Affected(pf, cust, a.Isin, Money.Format(units, 4), Money.Format(after, 4), a.TargetIsin, Money.Format(cash, 2), entry));
            }
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE actions SET status = 'APPLIED', applied_at = @at, affected = @aff WHERE id = @id",
                new { at = Nordic.Iso(clock.LocalNow), aff = JsonSerializer.Serialize(affected, Canonical.Web), id }, tx);
            Outbox.Add(db, http, clock, "corporate-action.applied", id, new { actionId = id, type = a.Type, isin = a.Isin, effectiveDate = a.EffectiveDate, affected }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Operaciones corporativas").WithSummary("Aplicar una operación en su fecha efectiva (Operaciones)").Produces<CorporateAction>();

        app.MapGet("/api/v1/corporate-actions/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var a = Load(db, id);
            return a is null ? Problems.NotFound(http, "CORPORATE_ACTION_NOT_FOUND", $"No existe la operación {id}.", null) : Results.Ok(a);
        }).WithTags("Operaciones corporativas").WithSummary("Una operación con las carteras afectadas").Produces<CorporateAction>();

        app.MapGet("/api/v1/corporate-actions", (string? isin, string? status) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM actions WHERE (@isin IS NULL OR isin = @isin) AND (@status IS NULL OR status = @status) ORDER BY effective_date", new { isin, status })
                .Select(i => Load(db, i)!).ToList());
        }).WithTags("Operaciones corporativas").WithSummary("Operaciones por fondo o estado").Produces<List<CorporateAction>>();
    }
}
