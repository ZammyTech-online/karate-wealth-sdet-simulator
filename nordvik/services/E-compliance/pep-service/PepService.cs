using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PepDeclarationRequest(bool? IsPep, bool? IsRelative, string? Role, string? Relation);

/// <summary>
/// pep-service: ¿es el cliente una persona políticamente expuesta (PEP) o un allegado (RCA)? Junta lo que el cliente declara
/// con lo que dice el cribado de la lista PEP_NORDIC. Si la declaración y el cribado no coinciden, lo marca: DECLARATION_MISMATCH.
/// </summary>
public sealed class PepService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "pep-service", "E-compliance", 6402, 3, "Cumplimiento normativo",
        "Estado PEP/RCA del cliente: autodeclaración + cribado de la lista PEP, con detección de declaraciones que no cuadran.",
        DependsOn: new[] { "sanctions-adapter", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "pep.identified" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS declarations (customer_id TEXT PRIMARY KEY, is_pep INTEGER, is_relative INTEGER, role TEXT, relation TEXT, declared_at TEXT, declared_by TEXT);
                     CREATE TABLE IF NOT EXISTS checks (id TEXT PRIMARY KEY, customer_id TEXT, screening_id TEXT, is_pep INTEGER, is_rca INTEGER, matches TEXT, checked_at TEXT)");
    }

    private static object Status(Microsoft.Data.Sqlite.SqliteConnection db, string customerId)
    {
        var d = db.QuerySingleOrDefault<(string Cust, long Pep, long Rel, string? Role, string? Relation, string At, string By)>(
            "SELECT customer_id, is_pep, is_relative, role, relation, declared_at, declared_by FROM declarations WHERE customer_id = @customerId", new { customerId });
        var c = db.QuerySingleOrDefault<(string Id, string Scr, long Pep, long Rca, string Matches, string At)>(
            "SELECT id, screening_id, is_pep, is_rca, matches, checked_at FROM checks WHERE customer_id = @customerId ORDER BY checked_at DESC, rowid DESC LIMIT 1", new { customerId });
        bool? declaredPep = d.Cust is null ? null : d.Pep == 1, declaredRel = d.Cust is null ? null : d.Rel == 1;
        bool? screenedPep = c.Id is null ? null : c.Pep == 1, screenedRca = c.Id is null ? null : c.Rca == 1;
        var status = (screenedPep ?? false) || (declaredPep ?? false) ? "PEP" : (screenedRca ?? false) || (declaredRel ?? false) ? "RCA" : d.Cust is null && c.Id is null ? "UNKNOWN" : "NOT_PEP";
        var flags = new List<string>();
        if (d.Cust is not null && c.Id is not null && ((d.Pep == 1) != (c.Pep == 1) || (d.Rel == 1) != (c.Rca == 1))) flags.Add("DECLARATION_MISMATCH");
        if (c.Id is null) flags.Add("NEVER_SCREENED");
        return new
        {
            customerId, status, flags,
            declaration = d.Cust is null ? null : new { isPep = d.Pep == 1, isRelative = d.Rel == 1, role = d.Role, relation = d.Relation, declaredAt = d.At, declaredBy = d.By },
            lastCheck = c.Id is null ? null : new { id = c.Id, screeningId = c.Scr, isPep = c.Pep == 1, isRca = c.Rca == 1, matches = JsonDocument.Parse(c.Matches).RootElement, checkedAt = c.At },
            enhancedDueDiligence = status is "PEP" or "RCA",
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/customers/{customerId}/pep-checks", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            var s = await Downstream.Post(http, "sanctions-adapter", "/api/v1/screenings", new { customerId, lists = new[] { "PEP_NORDIC" } });
            if (!s.Ok) return Results.Content(s.Raw, "application/problem+json", System.Text.Encoding.UTF8, s.Status);
            var matches = s.Json.GetProperty("matches").EnumerateArray().ToList();
            var isPep = matches.Any(m => m.GetProperty("type").GetString() == "PEP");
            var isRca = matches.Any(m => m.GetProperty("type").GetString() == "PEP_RCA");
            var id = "pepc_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO checks VALUES (@id, @customerId, @scr, @pep, @rca, @m, @at)",
                new { id, customerId, scr = s.Str("id"), pep = isPep ? 1 : 0, rca = isRca ? 1 : 0, m = JsonSerializer.Serialize(matches), at = Nordic.Iso(clock.LocalNow) }, tx);
            if (isPep || isRca) Outbox.Add(db, http, clock, "pep.identified", customerId, new { customerId, isPep, isRca, screeningId = s.Str("id") }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/customers/{customerId}/pep", Status(db, customerId));
        }).WithTags("PEP").WithSummary("Cribar al cliente contra la lista PEP (vía sanctions-adapter) y guardar el resultado");

        app.MapPut("/api/v1/customers/{customerId}/pep-declaration", async (HttpContext http, string customerId, PepDeclarationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            if (b.IsPep is null || b.IsRelative is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "isPep e isRelative son obligatorios.", "Ejemplo: { \"isPep\": false, \"isRelative\": true, \"relation\": \"Cónyuge\" }");
            if (b.IsPep == true && string.IsNullOrWhiteSpace(b.Role)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Si es PEP, indica el cargo (role).", null, new[] { new FieldError("role", "obligatorio si isPep") });
            if (b.IsRelative == true && string.IsNullOrWhiteSpace(b.Relation)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Si es allegado, indica la relación.", null, new[] { new FieldError("relation", "obligatorio si isRelative") });
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO declarations VALUES (@customerId, @pep, @rel, @Role, @Relation, @at, @by) ON CONFLICT(customer_id) DO UPDATE SET is_pep = @pep, is_relative = @rel, role = @Role, relation = @Relation, declared_at = @at, declared_by = @by",
                new { customerId, pep = b.IsPep.Value ? 1 : 0, rel = b.IsRelative.Value ? 1 : 0, b.Role, b.Relation, at = Nordic.Iso(clock.LocalNow), by = p.Sub });
            return Results.Ok(Status(db, customerId));
        }).WithTags("PEP").WithSummary("Autodeclaración PEP del cliente (el propio cliente o su asesor)");

        app.MapGet("/api/v1/customers/{customerId}/pep", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Status(db, customerId));
        }).WithTags("PEP").WithSummary("Estado PEP: PEP, RCA, NOT_PEP o UNKNOWN, con banderas (DECLARATION_MISMATCH, NEVER_SCREENED)");
    }
}
