using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AppropriatenessRequest(string? Isin, Dictionary<string, string>? Answers);

/// <summary>
/// appropriateness-service: el test de conveniencia para productos complejos que el cliente compra SIN asesoramiento.
/// En Nordvik es complejo todo fondo con SRI 6 o 7. Tres preguntas de conocimiento: con 2 aciertos es APPROPRIATE; si no,
/// NOT_APPROPRIATE con una advertencia que el cliente debe reconocer (acknowledge) si aun así quiere comprar.
/// </summary>
public sealed class AppropriatenessService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "appropriateness-service", "E-compliance", 6406, 3, "Cumplimiento normativo",
        "Test de conveniencia para productos complejos (SRI 6-7) sin asesoramiento: NOT_REQUIRED, APPROPRIATE o NOT_APPROPRIATE con advertencia y reconocimiento.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "appropriateness.assessed", "appropriateness.warning-acknowledged" });

    public static readonly (string Id, string Text, string[] Options, string Correct)[] Quiz =
    {
        ("K1", "Un fondo con SRI 7 puede perder en un año...", new[] { "A: casi nada", "B: una parte importante de su valor" }, "B"),
        ("K2", "Los fondos de mercados emergentes tienen además riesgo de...", new[] { "A: divisa y país", "B: ninguno adicional" }, "A"),
        ("K3", "Si vendes en una caída fuerte...", new[] { "A: conviertes la pérdida en real", "B: recuperas lo invertido" }, "A"),
    };
    public const string Warning = "Este producto es complejo y, según tus respuestas, puede no ser conveniente para ti. Puedes perder una parte importante de lo invertido.";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS tests (id TEXT PRIMARY KEY, customer_id TEXT, isin TEXT, result TEXT, correct INTEGER, warning TEXT, acknowledged_at TEXT, tested_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/quiz", () => Results.Ok(new { questions = Quiz.Select(q => new { id = q.Id, text = q.Text, options = q.Options }), complexWhen = "SRI 6 o 7" }))
            .WithTags("Conveniencia").WithSummary("Las preguntas de conocimiento");

        app.MapGet("/api/v1/products/{isin}/complexity", (HttpContext http, string isin) =>
        {
            var f = FundSeed.All.FirstOrDefault(x => x.Isin == isin);
            return f is null ? Problems.NotFound(http, "UNKNOWN_FUND", $"No existe el fondo {isin}.", null) : Results.Ok(new { isin, sri = f.Sri, complex = f.Sri >= 6, testRequired = f.Sri >= 6 });
        }).WithTags("Conveniencia").WithSummary("¿Es complejo este fondo? (SRI 6-7)");

        app.MapPost("/api/v1/customers/{customerId}/appropriateness-tests", async (HttpContext http, string customerId, AppropriatenessRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            var f = FundSeed.All.FirstOrDefault(x => x.Isin == b.Isin);
            if (f is null) return Problems.Unprocessable(http, "UNKNOWN_FUND", $"No existe el fondo {b.Isin}.", null);
            string result; int correct = 0;
            if (f.Sri < 6) result = "NOT_REQUIRED";
            else
            {
                var answers = b.Answers ?? new();
                var missing = Quiz.Where(q => !answers.ContainsKey(q.Id)).Select(q => new FieldError($"answers.{q.Id}", "sin responder")).ToList();
                if (missing.Count > 0) return Problems.Unprocessable(http, "INCOMPLETE_QUIZ", "Para un producto complejo hay que responder las 3 preguntas.", "Míralas en GET /api/v1/quiz", missing);
                correct = Quiz.Count(q => answers[q.Id] == q.Correct);
                result = correct >= 2 ? "APPROPRIATE" : "NOT_APPROPRIATE";
            }
            var id = "app_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO tests VALUES (@id, @customerId, @isin, @result, @correct, @w, NULL, @at)",
                new { id, customerId, isin = b.Isin, result, correct, w = result == "NOT_APPROPRIATE" ? Warning : null, at = Nordic.Iso(clock.LocalNow) });
            Outbox.Add(db, http, clock, "appropriateness.assessed", customerId, new { customerId, testId = id, isin = b.Isin, result });
            return Results.Created($"/api/v1/appropriateness-tests/{id}", new { id, customerId, isin = b.Isin, sri = f.Sri, result, correctAnswers = correct, warning = result == "NOT_APPROPRIATE" ? Warning : null, mayProceed = result != "NOT_APPROPRIATE" });
        }).WithTags("Conveniencia").WithSummary("Hacer el test para un fondo (si no es complejo: NOT_REQUIRED)");

        app.MapPost("/api/v1/appropriateness-tests/{id}/acknowledge", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var t = db.QuerySingleOrDefault<(string Id, string Cust, string Result, string? Ack)>("SELECT id, customer_id, result, acknowledged_at FROM tests WHERE id = @id", new { id });
            if (t.Id is null || t.Cust != p.CustomerId) return Problems.NotFound(http, "TEST_NOT_FOUND", $"No existe el test {id}.", null);
            if (t.Result != "NOT_APPROPRIATE") return Problems.Conflict(http, "NOTHING_TO_ACKNOWLEDGE", $"El resultado es {t.Result}: no hay advertencia que reconocer.", null);
            if (t.Ack is not null) return Problems.Conflict(http, "ALREADY_ACKNOWLEDGED", $"Ya la reconociste el {t.Ack}.", null);
            var at = Nordic.Iso(clock.LocalNow);
            db.Execute("UPDATE tests SET acknowledged_at = @at WHERE id = @id", new { at, id });
            Outbox.Add(db, http, clock, "appropriateness.warning-acknowledged", t.Cust, new { customerId = t.Cust, testId = id });
            await Task.CompletedTask;
            return Results.Ok(new { id, result = t.Result, acknowledgedAt = at, mayProceed = true });
        }).WithTags("Conveniencia").WithSummary("El cliente reconoce la advertencia y puede seguir (queda registrado). Solo el propio cliente.");
    }
}
