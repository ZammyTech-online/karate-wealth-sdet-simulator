using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TransactionRequest(string? CustomerId, string? Direction, string? Amount, string? Country, string? Reference);
public sealed record CloseAlertRequest(string? Resolution, string? Comment);
public sealed record Alert(string Id, string CustomerId, string Rule, string Severity, string Status, string Summary, JsonElement Evidence, string RaisedAt, string? Resolution, string? Comment, string? ClosedBy);

/// <summary>
/// transaction-monitoring: vigila los movimientos de dinero (eventos de pagos o alta directa) y levanta alertas por patrones de blanqueo.
/// Las ventanas de tiempo usan el reloj virtual: puedes provocar una estructuración en segundos.
/// </summary>
public sealed class TransactionMonitoring : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "transaction-monitoring", "E-compliance", 6404, 3, "Cumplimiento normativo",
        "Alertas por patrones: estructuración, transferencias grandes, entrada y salida rápida, países de riesgo. Ventanas sobre el reloj virtual.",
        DependsOn: new[] { "event-bus", "clock-service" }, Emits: new[] { "monitoring.alert.raised", "monitoring.alert.closed" },
        Consumes: new[] { "deposit.received", "payment.settled" });

    public static readonly string[] HighRiskCountries = { "XX", "YY", "KP", "IR", "MM" };
    public static readonly object[] Rules =
    {
        new { rule = "STRUCTURING", severity = "MEDIUM", description = "3 o más ingresos de menos de 10 000 SEK que suman 25 000 SEK o más en 24 h" },
        new { rule = "LARGE_TRANSFER", severity = "MEDIUM", description = "Un ingreso de 150 000 SEK o más" },
        new { rule = "RAPID_MOVEMENT", severity = "HIGH", description = "Una salida de al menos el 80 % de lo ingresado en las 48 h anteriores" },
        new { rule = "HIGH_RISK_COUNTRY", severity = "HIGH", description = "Movimiento con un país de riesgo (" + string.Join(", ", HighRiskCountries) + ")" },
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS transactions (id TEXT PRIMARY KEY, customer_id TEXT, direction TEXT, amount TEXT, country TEXT, reference TEXT, at_ticks INTEGER, at TEXT, source TEXT);
                     CREATE TABLE IF NOT EXISTS alerts (id TEXT PRIMARY KEY, customer_id TEXT, rule TEXT, severity TEXT, status TEXT, summary TEXT, evidence TEXT, raised_at TEXT, resolution TEXT, comment TEXT, closed_by TEXT, window_key TEXT UNIQUE)");
    }

    private static decimal Dec(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static Alert? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Rule, string Sev, string Status, string Summary, string Ev, string At, string? Res, string? Com, string? By)>(
            "SELECT id, customer_id, rule, severity, status, summary, evidence, raised_at, resolution, comment, closed_by FROM alerts WHERE id = @id", new { id });
        return r.Id is null ? null : new Alert(r.Id, r.Cust, r.Rule, r.Sev, r.Status, r.Summary, JsonDocument.Parse(r.Ev).RootElement, r.At, r.Res, r.Com, r.By);
    }

    /// <summary>Registra un movimiento y evalúa las reglas. Devuelve las alertas nuevas.</summary>
    public static List<string> Record(ServiceContext ctx, HttpContext? http, IClock clock, string customerId, string direction, decimal amount, string? country, string? reference, string source)
    {
        using var db = ctx.OpenDb();
        var now = clock.UtcNow;
        var id = "trx_" + Guid.NewGuid().ToString("N")[..14];
        db.Execute("INSERT INTO transactions VALUES (@id, @customerId, @direction, @amount, @country, @reference, @t, @at, @source)",
            new { id, customerId, direction, amount = Money.Format(amount, 2), country, reference, t = now.UtcTicks, at = Nordic.Iso(Nordic.ToStockholm(now)), source });
        var recent = db.Query<(string Id, string Dir, string Amount, long Ticks)>("SELECT id, direction, amount, at_ticks FROM transactions WHERE customer_id = @customerId AND at_ticks > @from AND at_ticks <= @to",
            new { customerId, from = now.AddHours(-48).UtcTicks, to = now.UtcTicks }).ToList();
        var raised = new List<string>();
        void Raise(string rule, string severity, string summary, IEnumerable<string> evidence, string windowKey)
        {
            var aid = "alr_" + Guid.NewGuid().ToString("N")[..14];
            using var tx = db.BeginTransaction();
            var n = db.Execute("INSERT OR IGNORE INTO alerts VALUES (@aid, @customerId, @rule, @severity, 'OPEN', @summary, @ev, @at, NULL, NULL, NULL, @windowKey)",
                new { aid, customerId, rule, severity, summary, ev = JsonSerializer.Serialize(evidence), at = Nordic.Iso(Nordic.ToStockholm(now)), windowKey }, tx);
            if (n == 1) { Outbox.Add(db, http, clock, "monitoring.alert.raised", aid, new { alertId = aid, customerId, rule, severity }, tx); raised.Add(aid); }
            tx.Commit();
        }
        var day = recent.Where(t => t.Dir == "IN" && t.Ticks > now.AddHours(-24).UtcTicks && Dec(t.Amount) < 10_000m).ToList();
        if (direction == "IN" && day.Count >= 3 && day.Sum(t => Dec(t.Amount)) >= 25_000m)
            Raise("STRUCTURING", "MEDIUM", $"{day.Count} ingresos pequeños suman {Money.Format(day.Sum(t => Dec(t.Amount)), 2)} SEK en 24 h", day.Select(t => t.Id), $"{customerId}|STRUCTURING|{now:yyyyMMdd}");
        if (direction == "IN" && amount >= 150_000m)
            Raise("LARGE_TRANSFER", "MEDIUM", $"Ingreso de {Money.Format(amount, 2)} SEK", new[] { id }, $"{customerId}|LARGE|{id}");
        if (direction == "OUT")
        {
            var inflow = recent.Where(t => t.Dir == "IN").Sum(t => Dec(t.Amount));
            if (inflow > 0 && amount >= inflow * 0.8m)
                Raise("RAPID_MOVEMENT", "HIGH", $"Salen {Money.Format(amount, 2)} SEK de {Money.Format(inflow, 2)} SEK entrados en 48 h", recent.Select(t => t.Id), $"{customerId}|RAPID|{id}");
        }
        if (country is not null && HighRiskCountries.Contains(country.ToUpperInvariant()))
            Raise("HIGH_RISK_COUNTRY", "HIGH", $"Movimiento con {country.ToUpperInvariant()}", new[] { id }, $"{customerId}|COUNTRY|{id}");
        return raised;
    }

    private static decimal AmountOf(JsonElement p) =>
        p.TryGetProperty("amount", out var a) ? (a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : Dec(a.GetString()!)) : 0m;

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Compliance", "Admin", "Service" };

        EventConsumer.Start(app, ctx, Info.Consumes!, ev =>
        {
            var p = ev.Payload;
            var cust = p.TryGetProperty("customerId", out var c) ? c.GetString() : null;
            if (cust is null) return Task.CompletedTask;
            Record(ctx, null, clock, cust, ev.Topic == "deposit.received" ? "IN" : "OUT", AmountOf(p), null, ev.Key, ev.Topic);
            return Task.CompletedTask;
        });

        app.MapPost("/api/v1/transactions", (HttpContext http, TransactionRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin", "Service" }) is { } denied) return denied;
            var dir = b.Direction?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (dir is not ("IN" or "OUT")) errors.Add(new FieldError("direction", "IN u OUT"));
            if (!Money.TryParse(b.Amount, out var amount) || amount <= 0) errors.Add(new FieldError("amount", "importe positivo como texto, p. ej. \"9500.00\""));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Movimiento no válido.", "Ejemplo: { \"customerId\": \"c000...\", \"direction\": \"IN\", \"amount\": \"9500.00\", \"country\": \"SE\" }", errors);
            var raised = Record(ctx, http, clock, b.CustomerId!, dir!, amount, b.Country, b.Reference, "manual");
            using var db = ctx.OpenDb();
            return Results.Created("/api/v1/alerts?customerId=" + b.CustomerId, new { recorded = true, alertsRaised = raised.Select(a => Load(db, a)).ToList() });
        }).WithTags("Movimientos").WithSummary("Registrar un movimiento a mano (laboratorio) y evaluar las reglas al momento");

        app.MapGet("/api/v1/customers/{customerId}/transactions", (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Id, string Dir, string Amount, string? Country, string? Ref, string At, string Source)>(
                "SELECT id, direction, amount, country, reference, at, source FROM transactions WHERE customer_id = @customerId ORDER BY at_ticks", new { customerId })
                .Select(t => new { id = t.Id, direction = t.Dir, amount = t.Amount, country = t.Country, reference = t.Ref, at = t.At, source = t.Source }).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Movimientos").WithSummary("Movimientos vigilados de un cliente");

        app.MapGet("/api/v1/alerts", (HttpContext http, string? customerId, string? status, string? rule) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM alerts WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@status IS NULL OR status = @status) AND (@rule IS NULL OR rule = @rule) ORDER BY raised_at, id",
                new { customerId, status = status?.ToUpperInvariant(), rule = rule?.ToUpperInvariant() }).Select(a => Load(db, a)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Alertas").WithSummary("Alertas (por cliente, estado o regla)");

        app.MapGet("/api/v1/alerts/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } a ? Results.Ok(a) : Problems.NotFound(http, "ALERT_NOT_FOUND", $"No existe la alerta {id}.", null);
        }).WithTags("Alertas").WithSummary("Una alerta con su evidencia (ids de los movimientos)");

        app.MapPost("/api/v1/alerts/{id}/close", (HttpContext http, string id, CloseAlertRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Compliance" }) is { } denied) return denied;
            var res = b.Resolution?.ToUpperInvariant();
            if (res is not ("FALSE_POSITIVE" or "ESCALATED" or "SAR_FILED") || string.IsNullOrWhiteSpace(b.Comment))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "resolution (FALSE_POSITIVE, ESCALATED o SAR_FILED) y comment son obligatorios.", null);
            using var db = ctx.OpenDb();
            var a = Load(db, id);
            if (a is null) return Problems.NotFound(http, "ALERT_NOT_FOUND", $"No existe la alerta {id}.", null);
            if (a.Status != "OPEN") return Problems.Conflict(http, "ALERT_ALREADY_CLOSED", $"La alerta ya está {a.Status}.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE alerts SET status = 'CLOSED', resolution = @res, comment = @c, closed_by = @by WHERE id = @id", new { res, c = b.Comment, by = p.Sub, id }, tx);
            Outbox.Add(db, http, clock, "monitoring.alert.closed", id, new { alertId = id, customerId = a.CustomerId, resolution = res }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Alertas").WithSummary("Cerrar una alerta (Cumplimiento) con su resolución");

        app.MapGet("/api/v1/rules", () => Results.Ok(new { items = Rules, highRiskCountries = HighRiskCountries })).WithTags("Alertas").WithSummary("Reglas de vigilancia");
    }
}
