using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Factor(string Code, int Points, string Evidence);

/// <summary>
/// risk-scoring: puntuación de riesgo de blanqueo (0-100) con reglas explicables. Cada factor dice cuántos puntos suma y por qué.
/// Si una fuente no responde, no se asume "sin riesgo": suma DATA_MISSING. Una coincidencia de sanciones confirmada es PROHIBITED.
/// </summary>
public sealed class RiskScoring : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "risk-scoring", "E-compliance", 6403, 3, "Cumplimiento normativo",
        "Riesgo de blanqueo 0-100 por reglas explicables (PEP, sanciones, residencia fiscal, alertas, edad); LOW, MEDIUM, HIGH o PROHIBITED.",
        DependsOn: new[] { "pep-service", "sanctions-adapter", "tax-residency-service", "transaction-monitoring", "customer-registry", "clock-service" },
        Emits: new[] { "risk.scored" });

    public static readonly object[] RuleTable =
    {
        new { code = "SANCTIONS_CONFIRMED", points = 100, rule = "Coincidencia de sanciones confirmada por Cumplimiento → PROHIBITED" },
        new { code = "SANCTIONS_POTENTIAL", points = 50, rule = "Cribado con posible coincidencia sin decidir" },
        new { code = "SCREENING_INCOMPLETE", points = 20, rule = "Último cribado parcial o inexistente" },
        new { code = "PEP", points = 40, rule = "El cliente es PEP" },
        new { code = "PEP_RCA", points = 25, rule = "El cliente es allegado de un PEP" },
        new { code = "FOREIGN_TAX_RESIDENCE", points = 10, rule = "Residencia fiscal principal fuera de Suecia" },
        new { code = "US_PERSON", points = 5, rule = "Residencia fiscal en EE. UU. (FATCA)" },
        new { code = "OPEN_ALERTS", points = 15, rule = "Por cada alerta de vigilancia abierta (máximo 45)" },
        new { code = "HIGH_SEVERITY_ALERT", points = 20, rule = "Alguna alerta abierta de severidad HIGH" },
        new { code = "YOUNG_CUSTOMER", points = 5, rule = "Menor de 21 años" },
        new { code = "DATA_MISSING", points = 10, rule = "Por cada fuente que no respondió o no tiene datos" },
    };

    public static string Level(int score, bool prohibited) => prohibited ? "PROHIBITED" : score >= 60 ? "HIGH" : score >= 30 ? "MEDIUM" : "LOW";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS assessments (id TEXT PRIMARY KEY, customer_id TEXT, score INTEGER, level TEXT, factors TEXT, assessed_at TEXT, assessed_by TEXT)");
    }

    private static object? Latest(Microsoft.Data.Sqlite.SqliteConnection db, string customerId)
    {
        var r = db.QuerySingleOrDefault<(string Id, long Score, string Level, string Factors, string At, string By)>(
            "SELECT id, score, level, factors, assessed_at, assessed_by FROM assessments WHERE customer_id = @customerId ORDER BY rowid DESC LIMIT 1", new { customerId });
        return r.Id is null ? null : new { id = r.Id, customerId, score = r.Score, level = r.Level, factors = JsonDocument.Parse(r.Factors).RootElement, assessedAt = r.At, assessedBy = r.By };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Advisor", "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/customers/{customerId}/risk-assessments", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var factors = new List<Factor>();
            var prohibited = false;
            var svc = await ServiceToken.Header(clock);

            async Task<DownstreamReply?> Try(string service, string path)
            {
                if (!ServiceDirectory.Knows(service)) { factors.Add(new Factor("DATA_MISSING", 10, $"{service} no está en este perfil")); return null; }
                try
                {
                    var r = await Downstream.Send(http, service, HttpMethod.Get, path, forwardAuth: false, headers: svc);
                    if (r.Ok) return r;
                    if (r.Status != 404) factors.Add(new Factor("DATA_MISSING", 10, $"{service} respondió {r.Status}"));
                    return r;
                }
                catch (DependencyUnavailableException) { factors.Add(new Factor("DATA_MISSING", 10, $"{service} no responde")); return null; }
            }

            var customer = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            if (customer.Ok && customer.Json.GetProperty("age").GetInt64() < 21) factors.Add(new Factor("YOUNG_CUSTOMER", 5, $"{customer.Json.GetProperty("age").GetInt64()} años"));

            var scr = await Try("sanctions-adapter", $"/api/v1/screenings?customerId={customerId}");
            var last = scr is { Ok: true } ? scr.Json.GetProperty("items").EnumerateArray().FirstOrDefault(s => !s.GetProperty("screenedLists").EnumerateArray().All(l => l.GetString() == "PEP_NORDIC")) : default;
            if (scr is { Ok: true })
            {
                var st = last.ValueKind == JsonValueKind.Object ? last.GetProperty("status").GetString() : null;
                // Una coincidencia confirmada no caduca: si alguna vez se confirmó, cuenta aunque luego haya cribados nuevos
                var confirmed = scr.Json.GetProperty("items").EnumerateArray().FirstOrDefault(s => s.GetProperty("status").GetString() == "CONFIRMED_MATCH");
                if (confirmed.ValueKind == JsonValueKind.Object) { last = confirmed; st = "CONFIRMED_MATCH"; }
                if (st == "CONFIRMED_MATCH") { prohibited = true; factors.Add(new Factor("SANCTIONS_CONFIRMED", 100, $"cribado {last.GetProperty("id").GetString()}")); }
                else if (st == "POTENTIAL_MATCH") factors.Add(new Factor("SANCTIONS_POTENTIAL", 50, $"cribado {last.GetProperty("id").GetString()}"));
                else if (st is null or "INCOMPLETE") factors.Add(new Factor("SCREENING_INCOMPLETE", 20, st is null ? "nunca cribado contra sanciones" : "último cribado parcial"));
            }

            var pep = await Try("pep-service", $"/api/v1/customers/{customerId}/pep");
            if (pep is { Ok: true })
            {
                var st = pep.Str("status");
                if (st == "PEP") factors.Add(new Factor("PEP", 40, "pep-service: PEP"));
                else if (st == "RCA") factors.Add(new Factor("PEP_RCA", 25, "pep-service: allegado"));
                else if (st == "UNKNOWN") factors.Add(new Factor("DATA_MISSING", 10, "estado PEP desconocido"));
            }

            var tax = await Try("tax-residency-service", $"/api/v1/internal/customers/{customerId}/tax-residencies");
            if (tax is { Ok: true })
            {
                if (tax.Str("primaryCountry") != "SE") factors.Add(new Factor("FOREIGN_TAX_RESIDENCE", 10, "residencia principal " + tax.Str("primaryCountry")));
                if (tax.Json.GetProperty("fatcaReportable").GetBoolean()) factors.Add(new Factor("US_PERSON", 5, "residencia en EE. UU."));
            }
            else if (tax is { Status: 404 }) factors.Add(new Factor("DATA_MISSING", 10, "sin residencia fiscal declarada"));

            var alerts = await Try("transaction-monitoring", $"/api/v1/alerts?customerId={customerId}&status=OPEN");
            if (alerts is { Ok: true })
            {
                var open = alerts.Json.GetProperty("items").EnumerateArray().ToList();
                if (open.Count > 0) factors.Add(new Factor("OPEN_ALERTS", Math.Min(45, 15 * open.Count), $"{open.Count} alerta(s) abierta(s)"));
                if (open.Any(a => a.GetProperty("severity").GetString() == "HIGH")) factors.Add(new Factor("HIGH_SEVERITY_ALERT", 20, "alerta HIGH abierta"));
            }

            var score = Math.Min(100, factors.Sum(f => f.Points));
            var level = Level(score, prohibited);
            var id = "risk_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO assessments VALUES (@id, @customerId, @score, @level, @f, @at, @by)",
                new { id, customerId, score, level, f = JsonSerializer.Serialize(factors, Canonical.Web), at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "risk.scored", customerId, new { customerId, assessmentId = id, score, level }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/customers/{customerId}/risk", Latest(db, customerId));
        }).WithTags("Riesgo").WithSummary("Calcular el riesgo ahora (consulta cribado, PEP, residencia fiscal y alertas)");

        app.MapGet("/api/v1/customers/{customerId}/risk", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            return Latest(db, customerId) is { } l ? Results.Ok(l) : Problems.NotFound(http, "RISK_NOT_ASSESSED", "Este cliente no tiene evaluación de riesgo.", "Calcúlala con POST /api/v1/customers/{id}/risk-assessments");
        }).WithTags("Riesgo").WithSummary("La última evaluación");

        app.MapGet("/api/v1/rules", () => Results.Ok(new { items = RuleTable, levels = new { LOW = "0-29", MEDIUM = "30-59", HIGH = "60-100", PROHIBITED = "sanciones confirmadas" } }))
            .WithTags("Riesgo").WithSummary("Tabla de reglas y niveles");
    }
}
