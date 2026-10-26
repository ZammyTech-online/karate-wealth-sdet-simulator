using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record HealthAnswers(bool? Smoker, int? HeightCm, int? WeightKg, string[]? Conditions, bool? HospitalizedLast5y);
public sealed record HealthDeclarationRequest(string? CustomerId, bool? ConsentToProcessHealthData, HealthAnswers? Answers);

/// <summary>
/// health-declaration: cuestionario de salud EFÍMERO. Exige consentimiento explícito (dato de salud, art. 9 RGPD), manda las respuestas a la
/// aseguradora por insurer-adapter y se queda SOLO con el resultado (STANDARD / RATED con recargo / DECLINED). Las respuestas no se escriben
/// en ninguna base de datos ni en el log, y el resultado caduca a los 90 días (reloj virtual).
/// </summary>
public sealed class HealthDeclaration : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "health-declaration", "M-insurance", 7201, 4, "Seguros",
        "Cuestionario de salud efímero con consentimiento explícito: se suscribe en la aseguradora y solo queda el resultado, que caduca a los 90 días.",
        DependsOn: new[] { "insurer-adapter", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "health-declaration.completed" });

    public const int ValidDays = 90;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS outcomes (id TEXT PRIMARY KEY, customer_id TEXT, outcome TEXT, loading_pct INTEGER, underwriting_id TEXT, created_at TEXT, expires_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Outcome, long Loading, string Uw, string At, string Exp)>(
            "SELECT id, customer_id, outcome, loading_pct, underwriting_id, created_at, expires_at FROM outcomes WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, outcome = r.Outcome, loadingPct = r.Loading, underwritingId = r.Uw, createdAt = r.At, expiresAt = r.Exp,
            status = DateTimeOffset.Parse(r.Exp) < clock.UtcNow ? "EXPIRED" : "VALID", answersStored = false,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/health-declarations", async (HttpContext http, HealthDeclarationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            if (b.CustomerId is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId es obligatorio.", null);
            if (await CustomerGuard.Check(http, p, "customer:update", b.CustomerId) is { } g) return g;
            if (b.ConsentToProcessHealthData != true)
                return Problems.Unprocessable(http, "EXPLICIT_CONSENT_REQUIRED", "Los datos de salud necesitan consentimiento explícito para esta declaración.", "Envía consentToProcessHealthData: true.");
            var a = b.Answers;
            var errors = new List<FieldError>();
            if (a is null) errors.Add(new FieldError("answers", "obligatorio"));
            else
            {
                if (a.Smoker is null) errors.Add(new FieldError("answers.smoker", "true o false"));
                if (a.HeightCm is null or < 120 or > 230) errors.Add(new FieldError("answers.heightCm", "entre 120 y 230"));
                if (a.WeightKg is null or < 35 or > 300) errors.Add(new FieldError("answers.weightKg", "entre 35 y 300"));
                if (a.HospitalizedLast5y is null) errors.Add(new FieldError("answers.hospitalizedLast5y", "true o false"));
            }
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Cuestionario incompleto.", null, errors);
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            var bmi = (int)Math.Round(a!.WeightKg!.Value / Math.Pow(a.HeightCm!.Value / 100.0, 2));
            var uw = await Downstream.Send(http, "insurer-adapter", HttpMethod.Post, "/api/v1/internal/underwriting", new
            {
                applicantRef = "tok_cu_" + Canonical.Sha256Hex(b.CustomerId)[..6], age = cust.Json.GetProperty("age").GetInt32(),
                smoker = a.Smoker, bmi, conditions = a.Conditions ?? Array.Empty<string>(), hospitalizedLast5y = a.HospitalizedLast5y,
            }, forwardAuth: false);
            if (!uw.Ok) return Results.Content(uw.Raw, "application/problem+json", System.Text.Encoding.UTF8, uw.Status);
            var id = "hd_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO outcomes VALUES (@id, @CustomerId, @o, @l, @u, @at, @exp)",
                    new { id, b.CustomerId, o = uw.Str("decision"), l = uw.Json.GetProperty("loadingPct").GetInt32(), u = uw.Str("underwritingId"), at = Nordic.Iso(clock.LocalNow), exp = clock.UtcNow.AddDays(ValidDays).ToString("O") }, tx);
                Outbox.Add(db, http, clock, "health-declaration.completed", id, new { declarationId = id, customerId = b.CustomerId, outcome = uw.Str("decision") }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/health-declarations/{id}", Load(db, id, clock));
        }).WithTags("Declaración de salud").WithSummary("Rellenar el cuestionario: solo se guarda el resultado");

        app.MapGet("/api/v1/health-declarations/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? owner;
            using (var db0 = ctx.OpenDb()) owner = db0.ExecuteScalar<string?>("SELECT customer_id FROM outcomes WHERE id = @id", new { id });
            if (owner is null) return Problems.NotFound(http, "DECLARATION_NOT_FOUND", $"No existe la declaración {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", owner) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Declaración de salud").WithSummary("El resultado (nunca las respuestas)");

        app.MapGet("/api/v1/internal/health-declarations/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            return Load(db, id, clock) is { } d ? Results.Ok(d) : Problems.NotFound(http, "DECLARATION_NOT_FOUND", $"No existe la declaración {id}.", null);
        }).WithTags("Interno").WithSummary("(interno) Resultado de una declaración");
    }
}
