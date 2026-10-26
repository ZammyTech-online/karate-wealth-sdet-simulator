using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TaskRequest(string? Type, string? Title, string? AssigneeRole, int? DueInHours, JsonElement? Payload, string? SourceService, string? SourceRef);
public sealed record CompleteRequest(string? Outcome, string? Comment);

/// <summary>
/// task-queue: tareas para personas (aprobar, revisar, confirmar). Cada tarea es para un rol; alguien de ese rol la reclama (claim),
/// la completa con un resultado o la suelta. Vencimiento sobre el reloj virtual: overdue se calcula en cada lectura.
/// </summary>
public sealed class TaskQueue : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "task-queue", "O-operations", 7404, 3, "Operaciones y back office",
        "Tareas humanas por rol: crear, reclamar, completar o soltar; vencimiento con el reloj virtual y quién hizo cada cosa.",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "task.created", "task.completed" });

    public static readonly string[] Roles = { "Advisor", "Operations", "Compliance", "Admin" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS tasks (id TEXT PRIMARY KEY, type TEXT, title TEXT, assignee_role TEXT, status TEXT, payload TEXT, source_service TEXT, source_ref TEXT,
                        created_by TEXT, created_at TEXT, due_ticks INTEGER, due_at TEXT, claimed_by TEXT, claimed_at TEXT, outcome TEXT, comment TEXT, completed_at TEXT)");
    }

    public static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, string Title, string Role, string Status, string? Payload, string? SrcS, string? SrcR, string By, string At, long DueT, string Due,
            string? Claimed, string? ClaimedAt, string? Outcome, string? Comment, string? Done)>(
            "SELECT id, type, title, assignee_role, status, payload, source_service, source_ref, created_by, created_at, due_ticks, due_at, claimed_by, claimed_at, outcome, comment, completed_at FROM tasks WHERE id = @id", new { id });
        if (r.Id is null) return null;
        return new
        {
            id = r.Id, type = r.Type, title = r.Title, assigneeRole = r.Role, status = r.Status, payload = r.Payload is null ? (JsonElement?)null : JsonDocument.Parse(r.Payload).RootElement,
            source = r.SrcS is null ? null : new { service = r.SrcS, @ref = r.SrcR }, createdBy = r.By, createdAt = r.At, dueAt = r.Due,
            overdue = r.Status is "OPEN" or "CLAIMED" && clock.UtcNow.UtcTicks > r.DueT, claimedBy = r.Claimed, claimedAt = r.ClaimedAt, outcome = r.Outcome, comment = r.Comment, completedAt = r.Done,
        };
    }

    /// <summary>Crear una tarea desde otro servicio (approval-service, case-management).</summary>
    public static string Create(ServiceContext ctx, HttpContext? http, IClock clock, string type, string title, string role, int dueInHours, object? payload, string? srcService, string? srcRef, string by)
    {
        var id = "tsk_" + Guid.NewGuid().ToString("N")[..14];
        var due = clock.UtcNow.AddHours(dueInHours);
        using var db = ctx.OpenDb();
        using var tx = db.BeginTransaction();
        db.Execute("INSERT INTO tasks VALUES (@id, @type, @title, @role, 'OPEN', @payload, @srcService, @srcRef, @by, @at, @dt, @d, NULL, NULL, NULL, NULL, NULL)",
            new { id, type, title, role, payload = payload is null ? null : JsonSerializer.Serialize(payload), srcService, srcRef, by, at = Nordic.Iso(clock.LocalNow), dt = due.UtcTicks, d = Nordic.Iso(Nordic.ToStockholm(due)) }, tx);
        Outbox.Add(db, http, clock, "task.created", id, new { taskId = id, type, assigneeRole = role, sourceService = srcService, sourceRef = srcRef }, tx);
        tx.Commit();
        return id;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] anyone = { "Advisor", "Operations", "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/tasks", (HttpContext http, TaskRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: anyone) is { } denied) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Type)) errors.Add(new FieldError("type", "obligatorio (APPROVE, REVIEW, CONFIRM, CALLBACK...)"));
            if (string.IsNullOrWhiteSpace(b.Title)) errors.Add(new FieldError("title", "obligatorio"));
            if (b.AssigneeRole is null || !Roles.Contains(b.AssigneeRole)) errors.Add(new FieldError("assigneeRole", string.Join(", ", Roles)));
            if (b.DueInHours is < 1 or > 2160) errors.Add(new FieldError("dueInHours", "entre 1 y 2160"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Tarea no válida.", "Ejemplo: { \"type\": \"REVIEW\", \"title\": \"Revisar alerta\", \"assigneeRole\": \"Compliance\", \"dueInHours\": 24 }", errors);
            var id = Create(ctx, http, clock, b.Type!.ToUpperInvariant(), b.Title!, b.AssigneeRole!, b.DueInHours ?? 48, b.Payload, b.SourceService, b.SourceRef, p.Sub);
            using var db = ctx.OpenDb();
            return Results.Created($"/api/v1/tasks/{id}", Load(db, clock, id));
        }).WithTags("Tareas").WithSummary("Crear una tarea para un rol");

        app.MapGet("/api/v1/tasks", (HttpContext http, string? role, string? status, bool? overdue, string? sourceRef) =>
        {
            if (Auth.Require(http, clock, out var p, roles: anyone) is { } denied) return denied;
            var r = role ?? (p.Role is "Service" or "Admin" ? null : p.Role);
            if (r is not null && p.Role is not ("Admin" or "Service") && r != p.Role)
                return Problems.Of(http, 403, "ROLE_NOT_ALLOWED", "Prohibido", $"Solo ves las tareas de tu rol ({p.Role}).", null);
            using var db = ctx.OpenDb();
            var items = db.Query<string>(@"SELECT id FROM tasks WHERE (@r IS NULL OR assignee_role = @r) AND (@s IS NULL OR status = @s) AND (@sourceRef IS NULL OR source_ref = @sourceRef)
                                           AND (@overdue IS NULL OR (@overdue = 1 AND status IN ('OPEN','CLAIMED') AND due_ticks < @now) OR (@overdue = 0 AND NOT (status IN ('OPEN','CLAIMED') AND due_ticks < @now)))
                                           ORDER BY due_ticks, id",
                new { r, s = status?.ToUpperInvariant(), sourceRef, overdue = overdue is null ? (int?)null : overdue.Value ? 1 : 0, now = clock.UtcNow.UtcTicks }).Select(i => Load(db, clock, i)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Tareas").WithSummary("Bandeja de tareas de tu rol (por estado, vencidas, origen)");

        app.MapGet("/api/v1/tasks/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: anyone) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, clock, id) is { } t ? Results.Ok(t) : Problems.NotFound(http, "TASK_NOT_FOUND", $"No existe la tarea {id}.", null);
        }).WithTags("Tareas").WithSummary("Una tarea");

        IResult Transition(HttpContext http, string id, Principal p, string from, Func<Microsoft.Data.Sqlite.SqliteConnection, (string Role, string Status, string? Claimed), IResult?> act)
        {
            using var db = ctx.OpenDb();
            var t = db.QuerySingleOrDefault<(string Role, string Status, string? Claimed)>("SELECT assignee_role, status, claimed_by FROM tasks WHERE id = @id", new { id });
            if (t.Role is null) return Problems.NotFound(http, "TASK_NOT_FOUND", $"No existe la tarea {id}.", null);
            if (p.Role != t.Role && p.Role != "Admin") return Problems.Of(http, 403, "NOT_YOUR_ROLE", "Prohibido", $"La tarea es para {t.Role}.", null);
            if (from != "*" && t.Status != from) return Problems.Conflict(http, "TASK_WRONG_STATE", $"La tarea está {t.Status}.", null);
            return act(db, t) ?? Results.Ok(Load(db, clock, id));
        }

        app.MapPost("/api/v1/tasks/{id}/claim", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: Roles) is { } denied) return denied;
            return Transition(http, id, p, "OPEN", (db, _) =>
            {
                db.Execute("UPDATE tasks SET status = 'CLAIMED', claimed_by = @by, claimed_at = @at WHERE id = @id AND status = 'OPEN'", new { by = p.Sub, at = Nordic.Iso(clock.LocalNow), id });
                return null;
            });
        }).WithTags("Tareas").WithSummary("Reclamar una tarea de tu rol (pasa a CLAIMED a tu nombre)");

        app.MapPost("/api/v1/tasks/{id}/release", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: Roles) is { } denied) return denied;
            return Transition(http, id, p, "CLAIMED", (db, t) =>
            {
                if (t.Claimed != p.Sub && p.Role != "Admin") return Problems.Of(http, 403, "NOT_YOUR_TASK", "Prohibido", $"La tiene {t.Claimed}.", null);
                db.Execute("UPDATE tasks SET status = 'OPEN', claimed_by = NULL, claimed_at = NULL WHERE id = @id", new { id });
                return null;
            });
        }).WithTags("Tareas").WithSummary("Soltar una tarea que habías reclamado");

        app.MapPost("/api/v1/tasks/{id}/complete", (HttpContext http, string id, CompleteRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: Roles) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Outcome)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta outcome (DONE, APPROVED, REJECTED...).", null);
            return Transition(http, id, p, "CLAIMED", (db, t) =>
            {
                if (t.Claimed != p.Sub) return Problems.Of(http, 403, "NOT_YOUR_TASK", "Prohibido", $"La tiene {t.Claimed}: reclámala tú primero.", null);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE tasks SET status = 'DONE', outcome = @o, comment = @c, completed_at = @at WHERE id = @id", new { o = b.Outcome!.ToUpperInvariant(), c = b.Comment, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, http, clock, "task.completed", id, new { taskId = id, outcome = b.Outcome!.ToUpperInvariant(), by = p.Sub }, tx);
                tx.Commit();
                return null;
            });
        }).WithTags("Tareas").WithSummary("Completar una tarea que tienes reclamada");

        app.MapPost("/api/v1/tasks/{id}/cancel", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Admin", "Service" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE tasks SET status = 'CANCELLED', completed_at = @at WHERE id = @id AND status IN ('OPEN','CLAIMED')", new { id, at = Nordic.Iso(clock.LocalNow) });
            return n == 0 ? Problems.Conflict(http, "TASK_WRONG_STATE", "La tarea no existe o ya terminó.", null) : Results.Ok(Load(db, clock, id));
        }).WithTags("Tareas").WithSummary("Cancelar (Admin o el servicio que la creó)");
    }
}
