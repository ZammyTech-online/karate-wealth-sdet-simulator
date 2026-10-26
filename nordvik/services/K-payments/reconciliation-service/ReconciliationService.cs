using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ReconcileRequest(string? Date);
public sealed record CashCheck(string LedgerBalance, string BankBalance, string Difference, bool Matched);
public sealed record PositionBreak(string AccountId, string Isin, string OurUnits, string CustodianUnits, string Difference);
public sealed record Reconciliation(string Id, string Date, string Status, CashCheck? Cash, string? CashError, int CheckedPositions, IReadOnlyList<PositionBreak> PositionBreaks, string? PositionsError, string RunAt, string RunBy);

/// <summary>
/// reconciliation-service: el cuadre diario. Compara (1) el saldo del ledger en bank:client-money con el extracto camt.053 del banco
/// y (2) las participaciones de position-keeping con las del custodio. Cualquier diferencia es un "break" que Operaciones debe investigar.
/// </summary>
public sealed class ReconciliationService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "reconciliation-service", "K-payments", 7005, 2, "Pagos y contabilidad",
        "Cuadre diario: ledger contra extracto del banco y libro de posiciones contra el custodio. Lista los descuadres (breaks).",
        DependsOn: new[] { "ledger-service", "nordpay-adapter", "position-keeping", "custody-adapter", "clock-service" }, Emits: new[] { "reconciliation.completed" });

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, date TEXT, json TEXT, run_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/reconciliations", async (HttpContext http, IClock clock, ReconcileRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin", "Compliance" });
            if (denied is not null) return denied;
            var date = b.Date ?? Money.Date(clock.Today);
            if (!Money.TryParseDate(date, out var d)) return Problems.BadRequest(http, "INVALID_DATE", "date debe ser AAAA-MM-DD.", null);
            if (d > clock.Today) return Problems.Unprocessable(http, "FUTURE_DATE", "No se concilia un día futuro.", null);
            CashCheck? cash = null; string? cashError = null;
            try
            {
                var ledger = await Downstream.Get(http, "ledger-service", $"/api/v1/accounts/bank:client-money/balance?asOf={date}", forwardAuth: false);
                var bank = await Downstream.Get(http, "nordpay-adapter", $"/api/v1/statements?date={date}", forwardAuth: false);
                if (!bank.Ok) cashError = "nordpay-adapter respondió " + bank.Status;
                else
                {
                    var l = D(ledger.Str("balance")!); var k = D(bank.Str("closingBalance")!);
                    cash = new CashCheck(Money.Format(l, 2), Money.Format(k, 2), Money.Format(l - k, 2), l == k);
                }
            }
            catch (DependencyUnavailableException ex) { cashError = ex.Message; }
            var breaks = new List<PositionBreak>(); string? posError = null; var checkedCount = 0;
            try
            {
                var ours = await Downstream.Get(http, "position-keeping", "/api/v1/internal/positions", forwardAuth: false);
                var theirs = await Downstream.Get(http, "custody-adapter", "/api/v1/holdings", forwardAuth: false);
                if (!theirs.Ok) posError = "custody-adapter respondió " + theirs.Status;
                else
                {
                    var a = ours.Json.EnumerateArray().ToDictionary(x => (x.GetProperty("portfolioId").GetString()!, x.GetProperty("isin").GetString()!), x => D(x.GetProperty("units").GetString()!));
                    var c = theirs.Json.GetProperty("holdings").EnumerateArray().ToDictionary(x => (x.GetProperty("accountId").GetString()!, x.GetProperty("isin").GetString()!), x => D(x.GetProperty("units").GetString()!));
                    // Solo se comparan las cuentas que conocemos: el custodio tiene también cuentas de pruebas manuales
                    var keys = a.Keys.Union(c.Keys.Where(k => a.Keys.Any(o => o.Item1 == k.Item1))).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList();
                    checkedCount = keys.Count;
                    foreach (var k in keys)
                    {
                        var x = a.GetValueOrDefault(k); var y = c.GetValueOrDefault(k);
                        if (x != y) breaks.Add(new PositionBreak(k.Item1, k.Item2, Money.Format(x, 4), Money.Format(y, 4), Money.Format(x - y, 4)));
                    }
                }
            }
            catch (DependencyUnavailableException ex) { posError = ex.Message; }
            var status = cashError is not null || posError is not null ? "INCOMPLETE" : (cash!.Matched && breaks.Count == 0 ? "MATCHED" : "BREAKS");
            var id = "rec_" + Guid.NewGuid().ToString("N")[..12];
            var result = new Reconciliation(id, date, status, cash, cashError, checkedCount, breaks, posError, Nordic.Iso(clock.LocalNow), p.Sub);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO runs VALUES (@id, @date, @json, @at)", new { id, date, json = JsonSerializer.Serialize(result, Canonical.Web), at = result.RunAt }, tx);
            Outbox.Add(db, http, clock, "reconciliation.completed", id, new { reconciliationId = id, date, status, breaks = breaks.Count }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/reconciliations/{id}", result);
        }).WithTags("Conciliación").WithSummary("Conciliar un día (Operaciones, Compliance)").Produces<Reconciliation>(201);

        app.MapGet("/api/v1/reconciliations/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var json = db.ExecuteScalar<string?>("SELECT json FROM runs WHERE id = @id", new { id });
            return json is null ? Problems.NotFound(http, "RECONCILIATION_NOT_FOUND", $"No existe la conciliación {id}.", null) : Results.Content(json, "application/json");
        }).WithTags("Conciliación").WithSummary("Resultado de una conciliación");

        app.MapGet("/api/v1/reconciliations", (string? date) =>
        {
            using var db = ctx.OpenDb();
            var list = db.Query<string>("SELECT json FROM runs WHERE (@date IS NULL OR date = @date) ORDER BY run_at DESC", new { date }).Select(j => JsonDocument.Parse(j).RootElement).ToList();
            return Results.Ok(list);
        }).WithTags("Conciliación").WithSummary("Conciliaciones hechas");
    }
}
