using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PermissionCheck(string? Role, string? CustomerId, string? AdvisorId, string? Action, string? ResourceCustomerId);
public sealed record PermissionDecision(bool Allowed, string Reason, string Rule);
public sealed record Assignment(string AdvisorId, string CustomerId, string AssignedAt);
public sealed record AssignRequest(string? AdvisorId, string? CustomerId);
public sealed record Policy(string Role, string Action, string Scope);

/// <summary>permission-service: quién puede hacer qué sobre los datos de qué cliente (roles + asignación asesor-cliente).</summary>
public sealed class PermissionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "permission-service", "C-identity", 6202, 1, "Identidad",
        "Decide permisos por rol y propiedad del recurso: el cliente ve lo suyo, el asesor a sus clientes asignados, back office según su función.",
        DependsOn: new[] { "clock-service" });

    public static readonly string[] Actions =
    {
        "customer:create", "customer:read", "customer:update", "customer:close",
        "portfolio:create", "portfolio:read", "portfolio:update", "portfolio:close",
        "contact:read", "contact:write", "preferences:read", "preferences:write", "address:read", "address:write",
    };

    /// <summary>Política: ALL = cualquier cliente, OWN = solo el suyo, ASSIGNED = solo asignados, NONE = nunca.</summary>
    private static string Scope(string role, string action) => role switch
    {
        "Admin" => "ALL",
        "Operations" => action is "customer:close" ? "NONE" : "ALL",
        "Compliance" => action.EndsWith(":read") ? "ALL" : "NONE",
        "Advisor" => action is "customer:create" ? "ALL" : action is "customer:close" or "portfolio:close" ? "NONE" : "ASSIGNED",
        "Customer" => action is "customer:create" or "customer:close" ? "NONE" : "OWN",
        _ => "NONE",
    };


    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS assignments (advisor_id TEXT, customer_id TEXT, assigned_at TEXT, PRIMARY KEY (advisor_id, customer_id))");
        foreach (var (a, c) in new[] { (SeedIds.AdvisorErik, SeedIds.CustomerAna), (SeedIds.AdvisorErik, SeedIds.CustomerLars), (SeedIds.AdvisorMaja, SeedIds.CustomerSofia) })
            db.Execute("INSERT OR IGNORE INTO assignments VALUES (@a, @c, '2026-01-02T09:00:00.000+01:00')", new { a, c });
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM assignments WHERE customer_id LIKE @p", new { p = prefix + "%" });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/permissions/check", (HttpContext http, PermissionCheck body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Role)) errors.Add(new FieldError("role", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.Action) || !Actions.Contains(body.Action)) errors.Add(new FieldError("action", "una de: " + string.Join(", ", Actions)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Consulta de permiso incompleta.", "Ejemplo: { \"role\": \"Advisor\", \"advisorId\": \"...\", \"action\": \"customer:read\", \"resourceCustomerId\": \"...\" }", errors);
            var scope = Scope(body.Role!, body.Action!);
            var rule = $"{body.Role} · {body.Action} · {scope}";
            PermissionDecision Decide(bool ok, string why) => new(ok, why, rule);
            var decision = scope switch
            {
                "ALL" => Decide(true, $"El rol {body.Role} puede {body.Action} sobre cualquier cliente."),
                "NONE" => Decide(false, $"El rol {body.Role} nunca puede {body.Action}."),
                "OWN" => body.ResourceCustomerId is not null && body.ResourceCustomerId == body.CustomerId
                    ? Decide(true, "Es su propio dato.")
                    : Decide(false, "Un cliente solo puede acceder a sus propios datos."),
                _ => IsAssigned(ctx, body.AdvisorId, body.ResourceCustomerId)
                    ? Decide(true, "El cliente está asignado a este asesor.")
                    : Decide(false, "El cliente no está asignado a este asesor."),
            };
            return Results.Ok(decision);
        }).WithTags("Permisos").WithSummary("¿Puede este principal hacer esta acción sobre este cliente?").Produces<PermissionDecision>();

        app.MapGet("/api/v1/policies", () =>
            Results.Ok(new[] { "Customer", "Advisor", "Operations", "Compliance", "Admin" }
                .SelectMany(r => Actions.Select(a => new Policy(r, a, Scope(r, a)))).ToList()))
            .WithTags("Permisos").WithSummary("La matriz completa de permisos (rol × acción → alcance)").Produces<List<Policy>>();

        app.MapGet("/api/v1/advisors/{advisorId}/customers", (string advisorId) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Assignment>("SELECT advisor_id AS AdvisorId, customer_id AS CustomerId, assigned_at AS AssignedAt FROM assignments WHERE advisor_id = @advisorId ORDER BY customer_id", new { advisorId }).ToList());
        }).WithTags("Asignaciones").WithSummary("Clientes asignados a un asesor").Produces<List<Assignment>>();

        app.MapPost("/api/v1/assignments", (HttpContext http, IClock clock, AssignRequest body) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Admin", "Operations", "Advisor" });
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(body.AdvisorId) || string.IsNullOrWhiteSpace(body.CustomerId))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "advisorId y customerId son obligatorios.", null,
                    new[] { new FieldError("advisorId", "obligatorio"), new FieldError("customerId", "obligatorio") });
            using var db = ctx.OpenDb();
            var at = Nordic.Iso(clock.LocalNow);
            if (db.Execute("INSERT OR IGNORE INTO assignments VALUES (@AdvisorId, @CustomerId, @at)", new { body.AdvisorId, body.CustomerId, at }) == 0)
                return Problems.Conflict(http, "ALREADY_ASSIGNED", "Ese cliente ya está asignado a ese asesor.", null);
            return Results.Created($"/api/v1/advisors/{body.AdvisorId}/customers", new Assignment(body.AdvisorId, body.CustomerId, at));
        }).WithTags("Asignaciones").WithSummary("Asignar un cliente a un asesor (Admin, Operations o Advisor)").Produces<Assignment>(201);

        app.MapDelete("/api/v1/assignments/{advisorId}/{customerId}", (HttpContext http, IClock clock, string advisorId, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Admin", "Operations" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            return db.Execute("DELETE FROM assignments WHERE advisor_id = @advisorId AND customer_id = @customerId", new { advisorId, customerId }) == 0
                ? Problems.NotFound(http, "ASSIGNMENT_NOT_FOUND", "No existe esa asignación.", null)
                : Results.NoContent();
        }).WithTags("Asignaciones").WithSummary("Quitar una asignación (Admin u Operations)");
    }

    private static bool IsAssigned(ServiceContext ctx, string? advisorId, string? customerId)
    {
        if (advisorId is null || customerId is null) return false;
        using var db = ctx.OpenDb();
        return db.ExecuteScalar<long>("SELECT COUNT(*) FROM assignments WHERE advisor_id = @advisorId AND customer_id = @customerId", new { advisorId, customerId }) > 0;
    }
}
