using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AssessmentRequest(Dictionary<string, string>? Answers);
public sealed record SuitabilityCheckRequest(string? CustomerId, string? Isin, string? Amount);
public sealed record Question(string Id, string Block, string Text, (string Code, string Text, int Points)[] Options);

/// <summary>
/// suitability-service: el test de idoneidad de MiFID II antes de recomendar un fondo. El cuestionario da un perfil de riesgo 1-7
/// (la misma escala SRI de los fondos) y la capacidad de asumir pérdidas. Un fondo es idóneo si su SRI no supera el perfil,
/// el horizonte encaja y, si el cliente pidió criterios sostenibles, el fondo los tiene. El test caduca a los 2 años.
/// </summary>
public sealed class SuitabilityService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "suitability-service", "E-compliance", 6405, 3, "Cumplimiento normativo",
        "Test de idoneidad MiFID II: cuestionario → perfil de riesgo 1-7, y comprobación fondo a fondo (SRI, horizonte, sostenibilidad). Caduca a los 2 años.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "suitability.assessed", "suitability.checked" });

    public static readonly Question[] Questions =
    {
        new("Q1", "KNOWLEDGE", "¿Qué pasa con un fondo de renta variable si la bolsa cae un 20 %?",
            new[] { ("A", "No le afecta", 0), ("B", "Puede bajar algo", 1), ("C", "Puede bajar de forma parecida o más", 2) }),
        new("Q2", "EXPERIENCE", "¿Cuántos años llevas invirtiendo en fondos o acciones?", new[] { ("A", "Nunca", 0), ("B", "Menos de 3", 1), ("C", "3 o más", 2) }),
        new("Q3", "FINANCIAL_SITUATION", "¿Qué parte de tu patrimonio vas a invertir?", new[] { ("A", "Más del 50 %", 0), ("B", "Entre 20 y 50 %", 1), ("C", "Menos del 20 %", 2) }),
        new("Q4", "LOSS_CAPACITY", "¿Qué pérdida podrías asumir en un año sin cambiar tus planes?", new[] { ("A", "Ninguna", 0), ("B", "Hasta un 10 %", 1), ("C", "Hasta un 25 %", 2), ("D", "Más del 25 %", 3) }),
        new("Q5", "HORIZON", "¿Cuándo necesitarás este dinero?", new[] { ("A", "En menos de 2 años", 0), ("B", "Entre 2 y 5 años", 1), ("C", "En más de 5 años", 2) }),
        new("Q6", "RISK_TOLERANCE", "Si tu inversión cae un 15 % en un mes, ¿qué haces?", new[] { ("A", "Vendo todo", 0), ("B", "Espero", 1), ("C", "Compro más", 2) }),
        new("Q7", "SUSTAINABILITY", "¿Quieres que tus inversiones sigan criterios de sostenibilidad (ESG)?", new[] { ("A", "No es importante", 0), ("B", "Sí, obligatorio", 0) }),
    };

    public static (int Profile, string Horizon, string LossCapacity, bool Esg, int Points) Evaluate(Dictionary<string, string> answers)
    {
        int Pts(string q) => Questions.First(x => x.Id == q).Options.First(o => o.Code == answers[q]).Points;
        var points = new[] { "Q1", "Q2", "Q3", "Q4", "Q5", "Q6" }.Sum(Pts);
        var profile = Math.Clamp(1 + points * 6 / 13, 1, 7);
        if (Pts("Q4") == 0) profile = Math.Min(profile, 2);        // sin capacidad de pérdida, nunca más de 2
        if (Pts("Q1") == 0) profile = Math.Min(profile, 3);        // sin conocimientos básicos, nunca más de 3
        var horizon = answers["Q5"] switch { "A" => "SHORT", "B" => "MEDIUM", _ => "LONG" };
        var loss = answers["Q4"] switch { "A" => "NONE", "B" => "LOW", "C" => "MEDIUM", _ => "HIGH" };
        return (profile, horizon, loss, answers["Q7"] == "B", points);
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS assessments (id TEXT PRIMARY KEY, customer_id TEXT, profile INTEGER, horizon TEXT, loss_capacity TEXT, esg INTEGER, points INTEGER, answers_hash TEXT,
                        assessed_at TEXT, valid_until TEXT, assessed_by TEXT)");
    }

    private static object? Latest(Microsoft.Data.Sqlite.SqliteConnection db, string customerId, DateOnly today)
    {
        var r = db.QuerySingleOrDefault<(string Id, long Profile, string Horizon, string Loss, long Esg, long Points, string Hash, string At, string Until, string By)>(
            "SELECT id, profile, horizon, loss_capacity, esg, points, answers_hash, assessed_at, valid_until, assessed_by FROM assessments WHERE customer_id = @customerId ORDER BY rowid DESC LIMIT 1", new { customerId });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId, riskProfile = r.Profile, horizon = r.Horizon, lossCapacity = r.Loss, sustainabilityPreference = r.Esg == 1, points = r.Points,
            answersHash = r.Hash, assessedAt = r.At, validUntil = r.Until, expired = string.CompareOrdinal(r.Until, Money.Date(today)) < 0, assessedBy = r.By,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/questionnaire", () => Results.Ok(new
        {
            version = "2026-1",
            questions = Questions.Select(q => new { id = q.Id, block = q.Block, text = q.Text, options = q.Options.Select(o => new { code = o.Code, text = o.Text }) }),
        })).WithTags("Cuestionario").WithSummary("Las preguntas del test (sin los puntos: el cliente no debe ver cómo puntúa)");

        app.MapPost("/api/v1/customers/{customerId}/assessments", async (HttpContext http, string customerId, AssessmentRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Customer" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            var answers = b.Answers ?? new();
            var errors = new List<FieldError>();
            foreach (var q in Questions)
                if (!answers.TryGetValue(q.Id, out var a)) errors.Add(new FieldError($"answers.{q.Id}", "sin responder"));
                else if (q.Options.All(o => o.Code != a)) errors.Add(new FieldError($"answers.{q.Id}", "opciones: " + string.Join(", ", q.Options.Select(o => o.Code))));
            foreach (var extra in answers.Keys.Where(k => Questions.All(q => q.Id != k))) errors.Add(new FieldError($"answers.{extra}", "pregunta desconocida"));
            if (errors.Count > 0) return Problems.Unprocessable(http, "INCOMPLETE_QUESTIONNAIRE", "El cuestionario no está completo o tiene respuestas no válidas.", "Ejemplo: { \"answers\": { \"Q1\": \"C\", \"Q2\": \"B\", ... \"Q7\": \"A\" } }", errors);
            var e = Evaluate(answers);
            var id = "suit_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO assessments VALUES (@id, @customerId, @profile, @horizon, @loss, @esg, @points, @hash, @at, @until, @by)",
                new { id, customerId, profile = e.Profile, horizon = e.Horizon, loss = e.LossCapacity, esg = e.Esg ? 1 : 0, points = e.Points, hash = Canonical.Hash(answers),
                      at = Nordic.Iso(clock.LocalNow), until = Money.Date(clock.Today.AddYears(2)), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "suitability.assessed", customerId, new { customerId, assessmentId = id, riskProfile = e.Profile }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/customers/{customerId}/assessments/latest", Latest(db, customerId, clock.Today));
        }).WithTags("Perfil").WithSummary("Responder el test: devuelve el perfil de riesgo 1-7, horizonte y capacidad de pérdida");

        app.MapGet("/api/v1/customers/{customerId}/assessments/latest", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            return Latest(db, customerId, clock.Today) is { } l ? Results.Ok(l) : Problems.NotFound(http, "ASSESSMENT_NOT_FOUND", "El cliente no ha hecho el test de idoneidad.", null);
        }).WithTags("Perfil").WithSummary("El último perfil (con expired si han pasado 2 años)");

        app.MapPost("/api/v1/suitability-checks", async (HttpContext http, SuitabilityCheckRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Compliance", "Service" }) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.CustomerId) || string.IsNullOrWhiteSpace(b.Isin)) return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId e isin son obligatorios.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", b.CustomerId) is { } g) return g;
            var fund = FundSeed.All.FirstOrDefault(f => f.Isin == b.Isin);
            if (fund is null) return Problems.Unprocessable(http, "UNKNOWN_FUND", $"No existe el fondo {b.Isin}.", null);
            using var db = ctx.OpenDb();
            var a = db.QuerySingleOrDefault<(string Id, long Profile, string Horizon, long Esg, string Until)>(
                "SELECT id, profile, horizon, esg, valid_until FROM assessments WHERE customer_id = @c ORDER BY rowid DESC LIMIT 1", new { c = b.CustomerId });
            if (a.Id is null) return Problems.Conflict(http, "ASSESSMENT_REQUIRED", "Sin test de idoneidad no se puede recomendar.", "Primero POST /api/v1/customers/{id}/assessments");
            if (string.CompareOrdinal(a.Until, Money.Date(clock.Today)) < 0) return Problems.Conflict(http, "ASSESSMENT_EXPIRED", $"El test caducó el {a.Until}.", "Repite el cuestionario.");
            var reasons = new List<string>();
            if (fund.Sri > a.Profile) reasons.Add($"El SRI del fondo ({fund.Sri}) supera el perfil del cliente ({a.Profile}).");
            if (a.Horizon == "SHORT" && fund.Sri >= 4) reasons.Add("Horizonte corto: no se recomiendan fondos con SRI 4 o más.");
            if (a.Esg == 1 && !fund.Name.Contains("ESG", StringComparison.OrdinalIgnoreCase)) reasons.Add("El cliente exige criterios ESG y el fondo no los tiene.");
            if (fund.Status != "OPEN") reasons.Add("El fondo está cerrado a nuevas suscripciones.");
            var result = reasons.Count == 0 ? "SUITABLE" : "NOT_SUITABLE";
            Outbox.Add(db, http, clock, "suitability.checked", b.CustomerId!, new { customerId = b.CustomerId, isin = b.Isin, result, assessmentId = a.Id });
            return Results.Ok(new { customerId = b.CustomerId, isin = b.Isin, fundSri = fund.Sri, riskProfile = a.Profile, result, reasons, assessmentId = a.Id, checkedAt = Nordic.Iso(clock.LocalNow) });
        }).WithTags("Idoneidad").WithSummary("¿Es idóneo este fondo para este cliente? (SUITABLE o NOT_SUITABLE con motivos)");
    }
}
