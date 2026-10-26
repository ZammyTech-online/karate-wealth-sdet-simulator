using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ApprovalRequest(string? Action, string? SubjectRef, string? Summary, JsonElement? Payload, string? ApproverRole, int? ApprovalsNeeded, int? ExpiresInHours);
public sealed record ApprovalDecisionRequest(string? Decision, string? Comment);

/// <summary>
/// approval-service: el principio de cuatro ojos. Quien pide una aprobación nunca puede darla; con approvalsNeeded = 2 hacen falta
/// dos personas distintas del rol aprobador. Un solo rechazo la cierra. Caduca con el reloj virtual. Cada petición crea una tarea en task-queue.
/// </summary>
public sealed class ApprovalService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "approval-service", "O-operations", 7405, 3, "Operaciones y back office",
        "Aprobaciones con cuatro ojos: quien pide no aprueba, 1 o 2 aprobadores distintos del rol, un rechazo cierra, caducidad con el reloj virtual y tarea en task-queue.",
        DependsOn: new[] { "task-queue", "clock-service" }, Emits: new[] { "approval.requested", "approval.granted", "approval.rejected" });

    public static readonly string[] ApproverRoles = { "Operations", "Compliance", "Admin" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS approvals (id TEXT PRIMARY KEY, action TEXT, subject_ref TEXT, summary TEXT, payload TEXT, approver_role TEXT, needed INTEGER, status TEXT,
                        requested_by TEXT, requested_at TEXT, expires_ticks INTEGER, expires_at TEXT, task_id TEXT, closed_at TEXT);
                     CREATE TABLE IF NOT EXISTS decisions (approval_id TEXT, approver TEXT, decision TEXT, comment TEXT, at TEXT, PRIMARY KEY (approval_id, approver))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Action, string Subj, string? Sum, string? Payload, string Role, long Needed, string Status, string By, string At, long ExpT, string Exp, string? Task, string? Closed)>(
            "SELECT id, action, subject_ref, summary, payload, approver_role, needed, status, requested_by, requested_at, expires_ticks, expires_at, task_id, closed_at FROM approvals WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var status = r.Status == "PENDING" && clock.UtcNow.UtcTicks >= r.ExpT ? "EXPIRED" : r.Status;
        if (status != r.Status) db.Execute("UPDATE approvals SET status = 'EXPIRED', closed_at = @at WHERE id = @id", new { at = r.Exp, id });
        var decisions = db.Query<(string Approver, string Decision, string? Comment, string At)>("SELECT approver, decision, comment, at FROM decisions WHERE approval_id = @id ORDER BY at", new { id })
            .Select(d => new { approver = d.Approver, decision = d.Decision, comment = d.Comment, at = d.At }).ToList();
        return new
        {
            id = r.Id, action = r.Action, subjectRef = r.Subj, summary = r.Sum, payload = r.Payload is null ? (JsonElement?)null : JsonDocument.Parse(r.Payload).RootElement,
            approverRole = r.Role, approvalsNeeded = r.Needed, approvalsGiven = decisions.Count(d => d.decision == "APPROVE"), status, requestedBy = r.By, requestedAt = r.At,
            expiresAt = r.Exp, taskId = r.Task, closedAt = status == "EXPIRED" ? r.Exp : r.Closed, decisions,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Advisor", "Operations", "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/approvals", async (HttpContext http, ApprovalRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Action)) errors.Add(new FieldError("action", "obligatorio, p. ej. payment.release, fee.waive, limit.raise"));
            if (string.IsNullOrWhiteSpace(b.SubjectRef)) errors.Add(new FieldError("subjectRef", "obligatorio: el id de lo que se aprueba"));
            if (b.ApproverRole is null || !ApproverRoles.Contains(b.ApproverRole)) errors.Add(new FieldError("approverRole", string.Join(", ", ApproverRoles)));
            if (b.ApprovalsNeeded is not (null or 1 or 2)) errors.Add(new FieldError("approvalsNeeded", "1 o 2"));
            if (b.ExpiresInHours is < 1 or > 720) errors.Add(new FieldError("expiresInHours", "entre 1 y 720"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Aprobación no válida.",
                "Ejemplo: { \"action\": \"payment.release\", \"subjectRef\": \"pay_123\", \"approverRole\": \"Operations\", \"approvalsNeeded\": 2 }", errors);
            var id = "apr_" + Guid.NewGuid().ToString("N")[..14];
            var exp = clock.UtcNow.AddHours(b.ExpiresInHours ?? 48);
            var task = await Downstream.Send(http, "task-queue", HttpMethod.Post, "/api/v1/tasks",
                new { type = "APPROVE", title = $"Aprobar {b.Action} · {b.SubjectRef}", assigneeRole = b.ApproverRole, dueInHours = b.ExpiresInHours ?? 48, sourceService = "approval-service", sourceRef = id },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO approvals VALUES (@id, @Action, @SubjectRef, @Summary, @payload, @ApproverRole, @needed, 'PENDING', @by, @at, @et, @e, @task, NULL)",
                new { id, b.Action, b.SubjectRef, b.Summary, payload = b.Payload?.GetRawText(), b.ApproverRole, needed = b.ApprovalsNeeded ?? 1, by = p.Sub, at = Nordic.Iso(clock.LocalNow),
                      et = exp.UtcTicks, e = Nordic.Iso(Nordic.ToStockholm(exp)), task = task.Ok ? task.Str("id") : null }, tx);
            Outbox.Add(db, http, clock, "approval.requested", id, new { approvalId = id, action = b.Action, subjectRef = b.SubjectRef, approverRole = b.ApproverRole }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/approvals/{id}", Load(db, clock, id));
        }).WithTags("Aprobaciones").WithSummary("Pedir una aprobación (crea una tarea APPROVE para el rol aprobador)");

        app.MapGet("/api/v1/approvals/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, clock, id) is { } a ? Results.Ok(a) : Problems.NotFound(http, "APPROVAL_NOT_FOUND", $"No existe la aprobación {id}.", null);
        }).WithTags("Aprobaciones").WithSummary("Una aprobación con sus decisiones");

        app.MapGet("/api/v1/approvals", (HttpContext http, string? status, string? subjectRef) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM approvals WHERE (@subjectRef IS NULL OR subject_ref = @subjectRef) ORDER BY requested_at, id", new { subjectRef })
                .Select(i => Load(db, clock, i)!).Where(a => status is null || JsonSerializer.SerializeToElement(a, Canonical.Web).GetProperty("status").GetString() == status.ToUpperInvariant()).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Aprobaciones").WithSummary("Aprobaciones (por estado u objeto)");

        app.MapPost("/api/v1/approvals/{id}/decisions", async (HttpContext http, string id, ApprovalDecisionRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: ApproverRoles) is { } denied) return denied;
            var decision = b.Decision?.ToUpperInvariant();
            if (decision is not ("APPROVE" or "REJECT")) return Problems.BadRequest(http, "VALIDATION_FAILED", "decision: APPROVE o REJECT.", null);
            if (decision == "REJECT" && string.IsNullOrWhiteSpace(b.Comment)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Un rechazo necesita comment.", null);
            using var db = ctx.OpenDb();
            if (Load(db, clock, id) is null) return Problems.NotFound(http, "APPROVAL_NOT_FOUND", $"No existe la aprobación {id}.", null);
            var a = db.QuerySingle<(string Status, string Role, string By, long Needed, string? Task, string Action, string Subj)>(
                "SELECT status, approver_role, requested_by, needed, task_id, action, subject_ref FROM approvals WHERE id = @id", new { id });
            if (a.Status == "EXPIRED") return Problems.Of(http, 410, "APPROVAL_EXPIRED", "Aprobación caducada", "Ya no se puede decidir: pide una nueva.", null);
            if (a.Status != "PENDING") return Problems.Conflict(http, "APPROVAL_CLOSED", $"La aprobación ya está {a.Status}.", null);
            if (p.Role != a.Role && p.Role != "Admin") return Problems.Of(http, 403, "NOT_AN_APPROVER", "Prohibido", $"Aprueba el rol {a.Role}.", null);
            if (p.Sub == a.By) return Problems.Of(http, 403, "FOUR_EYES_VIOLATION", "Prohibido", "Quien pide una aprobación no puede darla.", "Principio de cuatro ojos: la decide otra persona.");
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM decisions WHERE approval_id = @id AND approver = @s", new { id, s = p.Sub }) > 0)
                return Problems.Conflict(http, "ALREADY_DECIDED", "Ya decidiste sobre esta aprobación: la segunda aprobación tiene que ser de otra persona.", null);
            var now = Nordic.Iso(clock.LocalNow);
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO decisions VALUES (@id, @s, @decision, @c, @now)", new { id, s = p.Sub, decision, c = b.Comment, now }, tx);
                var approvals = db.ExecuteScalar<long>("SELECT COUNT(*) FROM decisions WHERE approval_id = @id AND decision = 'APPROVE'", new { id }, tx);
                var final = decision == "REJECT" ? "REJECTED" : approvals >= a.Needed ? "APPROVED" : null;
                if (final is not null)
                {
                    db.Execute("UPDATE approvals SET status = @final, closed_at = @now WHERE id = @id", new { final, now, id }, tx);
                    Outbox.Add(db, http, clock, final == "APPROVED" ? "approval.granted" : "approval.rejected", id, new { approvalId = id, action = a.Action, subjectRef = a.Subj }, tx);
                }
                tx.Commit();
                if (final is not null && a.Task is not null)
                    await Downstream.Send(http, "task-queue", HttpMethod.Post, $"/api/v1/tasks/{a.Task}/cancel", null, forwardAuth: false, headers: await ServiceToken.Header(clock));
            }
            return Results.Ok(Load(db, clock, id));
        }).WithTags("Aprobaciones").WithSummary("Aprobar o rechazar (nunca quien la pidió; con 2 aprobaciones, dos personas distintas)");
    }
}
