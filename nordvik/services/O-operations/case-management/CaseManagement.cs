using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CaseRequest(string? Type, string? CustomerId, string? Priority, string? Summary, string? SourceService, string? SourceRef);
public sealed record NoteRequest(string? Text);
public sealed record AssignRequest(string? Assignee);
public sealed record ResolveRequest(string? Resolution);

/// <summary>
/// case-management: los expedientes de Operaciones y Cumplimiento con plazo (SLA) según tipo y prioridad.
/// Se abren a mano o solos: cada alerta de transaction-monitoring abre un caso AML_ALERT y cada KYC en revisión un KYC_REVIEW.
/// El SLA se mide con el reloj virtual; resolver fuera de plazo queda marcado.
/// </summary>
public sealed class CaseManagement : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "case-management", "O-operations", 7403, 3, "Operaciones y back office",
        "Casos con SLA por tipo y prioridad (reloj virtual), notas, asignación y resolución; se abren solos desde alertas AML y KYC en revisión.",
        DependsOn: new[] { "task-queue", "event-bus", "clock-service" }, Emits: new[] { "case.opened", "case.resolved" },
        Consumes: new[] { "monitoring.alert.raised", "kyc.review-required" });

    public static readonly Dictionary<string, string> OwnerRole = new()
    {
        ["AML_ALERT"] = "Compliance", ["KYC_REVIEW"] = "Compliance", ["DATA_REQUEST"] = "Compliance", ["COMPLAINT"] = "Operations", ["OPERATIONAL"] = "Operations",
    };
    public static readonly Dictionary<string, int> SlaHours = new() { ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 72, ["LOW"] = 168 };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS cases (id TEXT PRIMARY KEY, type TEXT, customer_id TEXT, priority TEXT, summary TEXT, status TEXT, owner_role TEXT, assignee TEXT,
                        source_service TEXT, source_ref TEXT, opened_by TEXT, opened_at TEXT, sla_ticks INTEGER, sla_due TEXT, resolution TEXT, resolved_at TEXT, resolved_ticks INTEGER, task_id TEXT,
                        UNIQUE (source_service, source_ref));
                     CREATE TABLE IF NOT EXISTS notes (case_id TEXT, seq INTEGER, author TEXT, text TEXT, at TEXT, PRIMARY KEY (case_id, seq))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, string? Cust, string Prio, string Sum, string Status, string Role, string? Assignee, string? SrcS, string? SrcR,
            string By, string At, long SlaT, string Sla, string? Res, string? ResAt, long? ResT, string? Task)>(
            "SELECT id, type, customer_id, priority, summary, status, owner_role, assignee, source_service, source_ref, opened_by, opened_at, sla_ticks, sla_due, resolution, resolved_at, resolved_ticks, task_id FROM cases WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var now = clock.UtcNow.UtcTicks;
        var notes = db.Query<(long Seq, string Author, string Text, string At)>("SELECT seq, author, text, at FROM notes WHERE case_id = @id ORDER BY seq", new { id })
            .Select(n => new { seq = n.Seq, author = n.Author, text = n.Text, at = n.At }).ToList();
        return new
        {
            id = r.Id, type = r.Type, customerId = r.Cust, priority = r.Prio, summary = r.Sum, status = r.Status, ownerRole = r.Role, assignee = r.Assignee,
            source = r.SrcS is null ? null : new { service = r.SrcS, @ref = r.SrcR }, openedBy = r.By, openedAt = r.At, slaDue = r.Sla,
            slaBreached = r.Status == "RESOLVED" ? r.ResT > r.SlaT : now > r.SlaT,
            minutesLeft = r.Status == "RESOLVED" ? (long?)null : (r.SlaT - now) / TimeSpan.TicksPerMinute,
            resolution = r.Res, resolvedAt = r.ResAt, taskId = r.Task, notes,
        };
    }

    private static async Task<string> Open(ServiceContext ctx, HttpContext? http, IClock clock, string type, string? customerId, string priority, string summary, string? srcS, string? srcR, string by)
    {
        using var db = ctx.OpenDb();
        if (srcS is not null && db.ExecuteScalar<string?>("SELECT id FROM cases WHERE source_service = @srcS AND source_ref = @srcR", new { srcS, srcR }) is { } existing) return existing;
        var id = "case_" + Guid.NewGuid().ToString("N")[..12];
        var sla = clock.UtcNow.AddHours(SlaHours[priority]);
        var role = OwnerRole[type];
        string? taskId = null;
        try
        {
            var t = await Downstream.Send(http, "task-queue", HttpMethod.Post, "/api/v1/tasks",
                new { type = "REVIEW", title = $"{type} · {summary}", assigneeRole = role, dueInHours = SlaHours[priority], sourceService = "case-management", sourceRef = id },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            taskId = t.Ok ? t.Str("id") : null;
        }
        catch (DependencyUnavailableException) { /* sin task-queue el caso se abre igual */ }
        using var tx = db.BeginTransaction();
        db.Execute("INSERT INTO cases VALUES (@id, @type, @customerId, @priority, @summary, 'OPEN', @role, NULL, @srcS, @srcR, @by, @at, @st, @s, NULL, NULL, NULL, @taskId)",
            new { id, type, customerId, priority, summary, role, srcS, srcR, by, at = Nordic.Iso(clock.LocalNow), st = sla.UtcTicks, s = Nordic.Iso(Nordic.ToStockholm(sla)), taskId }, tx);
        Outbox.Add(db, http, clock, "case.opened", id, new { caseId = id, type, customerId, priority }, tx);
        tx.Commit();
        return id;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Compliance", "Admin", "Service" };

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var p = ev.Payload;
            string? S(string n) => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (ev.Topic == "monitoring.alert.raised")
                await Open(ctx, null, clock, "AML_ALERT", S("customerId"), S("severity") == "HIGH" ? "HIGH" : "MEDIUM", $"Alerta {S("rule")} de vigilancia", ev.Producer, S("alertId"), "case-management (automático)");
            else
                await Open(ctx, null, clock, "KYC_REVIEW", S("customerId"), "HIGH", $"KYC en revisión ({S("riskLevel")})", ev.Producer, S("caseId"), "case-management (automático)");
        });

        app.MapPost("/api/v1/cases", async (HttpContext http, CaseRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            var type = b.Type?.ToUpperInvariant(); var prio = (b.Priority ?? "MEDIUM").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (type is null || !OwnerRole.ContainsKey(type)) errors.Add(new FieldError("type", string.Join(", ", OwnerRole.Keys)));
            if (!SlaHours.ContainsKey(prio)) errors.Add(new FieldError("priority", string.Join(", ", SlaHours.Keys)));
            if (string.IsNullOrWhiteSpace(b.Summary)) errors.Add(new FieldError("summary", "obligatorio"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Caso no válido.", "Ejemplo: { \"type\": \"COMPLAINT\", \"customerId\": \"c000...\", \"priority\": \"HIGH\", \"summary\": \"Comisión cobrada dos veces\" }", errors);
            var id = await Open(ctx, http, clock, type!, b.CustomerId, prio, b.Summary!, b.SourceService, b.SourceRef, p.Sub);
            using var db = ctx.OpenDb();
            return Results.Created($"/api/v1/cases/{id}", Load(db, clock, id));
        }).WithTags("Casos").WithSummary("Abrir un caso (el SLA sale del tipo y la prioridad; crea su tarea en task-queue)");

        app.MapGet("/api/v1/cases", (HttpContext http, string? type, string? status, string? customerId, bool? breached) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var now = clock.UtcNow.UtcTicks;
            var items = db.Query<string>(@"SELECT id FROM cases WHERE (@type IS NULL OR type = @type) AND (@status IS NULL OR status = @status) AND (@customerId IS NULL OR customer_id = @customerId)
                                           AND (@b IS NULL OR (@b = 1 AND ((status <> 'RESOLVED' AND sla_ticks < @now) OR resolved_ticks > sla_ticks)) OR (@b = 0 AND NOT ((status <> 'RESOLVED' AND sla_ticks < @now) OR resolved_ticks > sla_ticks)))
                                           ORDER BY sla_ticks, id",
                new { type = type?.ToUpperInvariant(), status = status?.ToUpperInvariant(), customerId, b = breached is null ? (int?)null : breached.Value ? 1 : 0, now })
                .Select(i => Load(db, clock, i)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Casos").WithSummary("Casos (por tipo, estado, cliente o SLA incumplido), el que vence antes primero");

        app.MapGet("/api/v1/cases/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, clock, id) is { } c ? Results.Ok(c) : Problems.NotFound(http, "CASE_NOT_FOUND", $"No existe el caso {id}.", null);
        }).WithTags("Casos").WithSummary("Un caso con sus notas y su SLA");

        app.MapPost("/api/v1/cases/{id}/notes", (HttpContext http, string id, NoteRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: staff) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Text)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta text.", null);
            using var db = ctx.OpenDb();
            if (Load(db, clock, id) is null) return Problems.NotFound(http, "CASE_NOT_FOUND", $"No existe el caso {id}.", null);
            var seq = db.ExecuteScalar<long>("SELECT COALESCE(MAX(seq), 0) + 1 FROM notes WHERE case_id = @id", new { id });
            db.Execute("INSERT INTO notes VALUES (@id, @seq, @by, @Text, @at)", new { id, seq, by = p.Sub, b.Text, at = Nordic.Iso(clock.LocalNow) });
            return Results.Created($"/api/v1/cases/{id}", Load(db, clock, id));
        }).WithTags("Casos").WithSummary("Añadir una nota (solo se añaden, nunca se editan)");

        app.MapPost("/api/v1/cases/{id}/assign", (HttpContext http, string id, AssignRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Compliance", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<(string Role, string Status)>("SELECT owner_role, status FROM cases WHERE id = @id", new { id });
            if (c.Role is null) return Problems.NotFound(http, "CASE_NOT_FOUND", $"No existe el caso {id}.", null);
            if (p.Role != c.Role && p.Role != "Admin") return Problems.Of(http, 403, "NOT_YOUR_QUEUE", "Prohibido", $"Este caso es de {c.Role}.", null);
            if (c.Status == "RESOLVED") return Problems.Conflict(http, "CASE_RESOLVED", "El caso ya está resuelto.", null);
            db.Execute("UPDATE cases SET assignee = @a, status = 'IN_PROGRESS' WHERE id = @id", new { a = b.Assignee ?? p.Sub, id });
            return Results.Ok(Load(db, clock, id));
        }).WithTags("Casos").WithSummary("Asignar (a ti si no dices a quién): pasa a IN_PROGRESS");

        app.MapPost("/api/v1/cases/{id}/resolve", (HttpContext http, string id, ResolveRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Compliance", "Admin" }) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Resolution)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta resolution.", null);
            using var db = ctx.OpenDb();
            var c = db.QuerySingleOrDefault<(string Role, string Status, long SlaT)>("SELECT owner_role, status, sla_ticks FROM cases WHERE id = @id", new { id });
            if (c.Role is null) return Problems.NotFound(http, "CASE_NOT_FOUND", $"No existe el caso {id}.", null);
            if (p.Role != c.Role && p.Role != "Admin") return Problems.Of(http, 403, "NOT_YOUR_QUEUE", "Prohibido", $"Este caso es de {c.Role}.", null);
            if (c.Status == "RESOLVED") return Problems.Conflict(http, "CASE_RESOLVED", "El caso ya está resuelto.", null);
            var now = clock.UtcNow;
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE cases SET status = 'RESOLVED', resolution = @r, resolved_at = @at, resolved_ticks = @t, assignee = COALESCE(assignee, @by) WHERE id = @id",
                new { r = b.Resolution, at = Nordic.Iso(Nordic.ToStockholm(now)), t = now.UtcTicks, by = p.Sub, id }, tx);
            Outbox.Add(db, http, clock, "case.resolved", id, new { caseId = id, withinSla = now.UtcTicks <= c.SlaT }, tx);
            tx.Commit();
            return Results.Ok(Load(db, clock, id));
        }).WithTags("Casos").WithSummary("Resolver (queda marcado si fue fuera de plazo)");

        app.MapGet("/api/v1/sla", () => Results.Ok(new { hoursByPriority = SlaHours, ownerRoleByType = OwnerRole })).WithTags("Casos").WithSummary("Matriz de SLA y colas por tipo");
    }
}
