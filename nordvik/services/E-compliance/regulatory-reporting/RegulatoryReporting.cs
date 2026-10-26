using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RegulatoryReportRequest(string? Type, string? Date, string? Month);

/// <summary>
/// regulatory-reporting: informes al supervisor. TRANSACTIONS: las órdenes ejecutadas de un día de negociación, agregadas por ISIN y con los
/// clientes seudonimizados (tok_cu_), con plazo hasta el final del siguiente día hábil (T+1); si se presenta más tarde, LATE.
/// INCIDENTS: las brechas de datos de un mes. Un informe por tipo y periodo (409 si se repite).
/// </summary>
public sealed class RegulatoryReporting : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "regulatory-reporting", "E-compliance", 6408, 4, "Cumplimiento",
        "Informes al supervisor: operaciones ejecutadas por día (T+1, seudonimizadas, agregadas por ISIN) e incidentes de datos por mes; marca los envíos tardíos.",
        DependsOn: new[] { "order-management", "breach-service", "clock-service" }, Emits: new[] { "regulatory-report.submitted" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS reports (id TEXT PRIMARY KEY, type TEXT, period TEXT, status TEXT, due_by TEXT, submitted_at TEXT, content TEXT, submitted_by TEXT, UNIQUE (type, period))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, string Period, string Status, string Due, string At, string Content, string By)>(
            "SELECT id, type, period, status, due_by, submitted_at, content, submitted_by FROM reports WHERE id = @id", new { id });
        return r.Id is null ? null : new { id = r.Id, type = r.Type, period = r.Period, status = r.Status, dueBy = r.Due, submittedAt = r.At, content = JsonDocument.Parse(r.Content).RootElement, submittedBy = r.By };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Compliance", "Admin" };

        app.MapPost("/api/v1/regulatory-reports", async (HttpContext http, RegulatoryReportRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            var type = (b.Type ?? "").ToUpperInvariant();
            string period; DateTimeOffset due; object content;
            if (type == "TRANSACTIONS")
            {
                if (!Money.TryParseDate(b.Date, out var day)) return Problems.BadRequest(http, "VALIDATION_FAILED", "date (AAAA-MM-DD) es obligatorio para TRANSACTIONS.", null);
                if (!NordicCalendar.IsTradingDay("XSTO", day)) return Problems.Unprocessable(http, "NOT_A_TRADING_DAY", $"{Money.Date(day)} no es día de negociación.", null);
                if (day >= clock.Today) return Problems.Unprocessable(http, "DAY_NOT_CLOSED", "Solo se informa de días ya cerrados.", null);
                period = Money.Date(day);
                var next = NordicCalendar.AddTradingDays("XSTO", day, 1);
                due = new DateTimeOffset(next.Year, next.Month, next.Day, 23, 59, 59, Nordic.Stockholm.GetUtcOffset(next.ToDateTime(TimeOnly.MinValue)));
                var orders = await Downstream.Get(http, "order-management", $"/api/v1/internal/orders?status=EXECUTED&tradeDateUpTo={period}", forwardAuth: false);
                var list = orders.Json.EnumerateArray().Where(o => o.GetProperty("tradeDate").GetString() == period).ToList();
                decimal Amount(JsonElement o) => o.TryGetProperty("netAmountSek", out var n) && n.ValueKind == JsonValueKind.String ? decimal.Parse(n.GetString()!, Money.Inv) : 0;
                content = new
                {
                    tradeDate = period, transactions = list.Count,
                    byIsin = list.GroupBy(o => o.GetProperty("isin").GetString()).OrderBy(g => g.Key).Select(g => new
                    {
                        isin = g.Key, buys = g.Count(o => o.GetProperty("side").GetString() == "BUY"), sells = g.Count(o => o.GetProperty("side").GetString() == "SELL"),
                        buyAmountSek = Money.Format(g.Where(o => o.GetProperty("side").GetString() == "BUY").Sum(Amount), 2),
                        sellAmountSek = Money.Format(g.Where(o => o.GetProperty("side").GetString() == "SELL").Sum(Amount), 2),
                        clients = g.Select(o => "tok_cu_" + Canonical.Sha256Hex(o.GetProperty("customerId").GetString()!)[..6]).Distinct().OrderBy(x => x).ToList(),
                    }).ToList(),
                };
            }
            else if (type == "INCIDENTS")
            {
                if (b.Month is null || !System.Text.RegularExpressions.Regex.IsMatch(b.Month, @"^\d{4}-(0[1-9]|1[0-2])$")) return Problems.BadRequest(http, "VALIDATION_FAILED", "month (AAAA-MM) es obligatorio para INCIDENTS.", null);
                period = b.Month;
                var first = DateOnly.ParseExact(b.Month + "-01", "yyyy-MM-dd");
                if (first.AddMonths(1) > clock.Today) return Problems.Unprocessable(http, "MONTH_NOT_CLOSED", "Solo se informa de meses ya cerrados.", null);
                var end = first.AddMonths(1).AddDays(14);
                due = new DateTimeOffset(end.Year, end.Month, end.Day, 23, 59, 59, TimeSpan.Zero);
                var br = await Downstream.Get(http, "breach-service", $"/api/v1/internal/breaches?month={period}", forwardAuth: false);
                var items = br.Json.GetProperty("items").EnumerateArray().ToList();
                content = new
                {
                    month = period, incidents = items.Count, highRisk = items.Count(i => i.GetProperty("riskToRights").GetString() == "HIGH"),
                    lateAuthorityNotices = items.Count(i => i.GetProperty("authorityNoticeLate").GetBoolean()),
                    items = items.Select(i => new { breachId = i.GetProperty("id").GetString(), risk = i.GetProperty("riskToRights").GetString(), status = i.GetProperty("status").GetString(), affected = i.GetProperty("affectedCount").GetInt32() }),
                };
            }
            else return Problems.BadRequest(http, "VALIDATION_FAILED", "type: TRANSACTIONS o INCIDENTS.", null);
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT id FROM reports WHERE type = @type AND period = @period", new { type, period }) is { } dup)
                return Problems.Conflict(http, "REPORT_ALREADY_SUBMITTED", $"Ese informe ya se presentó: {dup}.", null);
            var id = "reg_" + Guid.NewGuid().ToString("N")[..12];
            var status = clock.UtcNow > due ? "SUBMITTED_LATE" : "SUBMITTED";
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO reports VALUES (@id, @type, @period, @status, @due, @at, @c, @by)",
                    new { id, type, period, status, due = due.ToString("O"), at = clock.UtcNow.ToString("O"), c = JsonSerializer.Serialize(content, Canonical.Web), by = p.Sub }, tx);
                Outbox.Add(db, http, clock, "regulatory-report.submitted", id, new { reportId = id, type, period, status }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/regulatory-reports/{id}", Load(db, id));
        }).WithTags("Supervisor").WithSummary("Presentar un informe (TRANSACTIONS de un día o INCIDENTS de un mes)");

        app.MapGet("/api/v1/regulatory-reports/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } x ? Results.Ok(x) : Problems.NotFound(http, "REPORT_NOT_FOUND", $"No existe el informe {id}.", null);
        }).WithTags("Supervisor").WithSummary("Un informe presentado");

        app.MapGet("/api/v1/regulatory-reports", (HttpContext http, string? type) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM reports WHERE (@type IS NULL OR type = @type) ORDER BY submitted_at", new { type = type?.ToUpperInvariant() }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Supervisor").WithSummary("Informes presentados");
    }
}
