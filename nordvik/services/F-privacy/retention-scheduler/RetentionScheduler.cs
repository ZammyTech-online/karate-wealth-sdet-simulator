using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PolicyRequest(long? RetentionSeconds, string? Action, string? Legal);
public sealed record RetentionItemRequest(string? DataClass, string? OwnerService, string? Ref, string? Subject, string? CreatedAt);
public sealed record LegalHoldRequest(bool? Hold, string? Reason);
public sealed record RetentionPolicy(string DataClass, long RetentionSeconds, string Action, string Legal, string Human);
public sealed record RetentionItem(string Id, string DataClass, string OwnerService, string Ref, string? Subject, string CreatedAt, string DueAt, string Status, bool LegalHold, string? HoldReason, string? PurgedAt, string? RunId);

/// <summary>
/// retention-scheduler: cuánto tiempo puede vivir cada clase de dato y qué pasa al caducar (DELETE, ANONYMIZE o SHRED).
/// Los servicios registran sus datos con fecha; una ejecución (POST /runs) mira el reloj virtual, purga lo vencido
/// (evento retention.expired, y crypto-shredding en key-management si la acción es SHRED) y respeta las retenciones legales.
/// </summary>
public sealed class RetentionScheduler : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "retention-scheduler", "F-privacy", 6506, 3, "Privacidad y protección de datos",
        "Políticas de retención por clase de dato, registro de datos con vencimiento, ejecuciones de purga con el reloj virtual y retención legal (legal hold).",
        DependsOn: new[] { "key-management", "clock-service" }, Emits: new[] { "retention.expired" });

    public static readonly string[] Actions = { "DELETE", "ANONYMIZE", "SHRED" };

    private static readonly RetentionPolicy[] Seed =
    {
        new("VOICE_AUDIO", 600, "SHRED", "Grabación de voz: solo hasta transcribir (10 min)", ""),
        new("TRANSCRIPT_RAW", 86_400, "SHRED", "Transcripción sin redactar: 24 h", ""),
        new("MFA_RESULT", 600, "DELETE", "Resultado de un segundo factor: 10 min", ""),
        new("EID_RESULT", 600, "DELETE", "Resultado de NordID: 10 min", ""),
        new("HEALTH_DECLARATION", 3_600, "SHRED", "Cuestionario de salud: solo queda el resultado (1 h)", ""),
        new("SESSION_LOG", 7_776_000, "DELETE", "Registros de sesión: 90 días", ""),
        new("MARKETING_PROFILE", 63_072_000, "ANONYMIZE", "Perfil comercial: 2 años sin actividad", ""),
        new("KYC_DOCUMENT", 157_680_000, "SHRED", "Ley de blanqueo: 5 años tras la relación", ""),
        new("CONTRACT", 315_360_000, "DELETE", "Contratos: 10 años", ""),
    };

    public static string Human(long s) => s switch
    {
        < 3600 => $"{s / 60} min",
        < 86_400 => $"{s / 3600} h",
        < 31_536_000 => $"{s / 86_400} días",
        _ => $"{s / 31_536_000} años",
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS policies (data_class TEXT PRIMARY KEY, retention_seconds INTEGER, action TEXT, legal TEXT);
                     CREATE TABLE IF NOT EXISTS items (id TEXT PRIMARY KEY, data_class TEXT, owner_service TEXT, ref TEXT, subject TEXT, created_at TEXT, due_ticks INTEGER, due_at TEXT,
                        status TEXT, legal_hold INTEGER, hold_reason TEXT, purged_at TEXT, run_id TEXT);
                     CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, ran_at TEXT, examined INTEGER, purged INTEGER, held INTEGER, run_by TEXT)");
        foreach (var p in Seed)
            db.Execute("INSERT OR IGNORE INTO policies VALUES (@DataClass, @RetentionSeconds, @Action, @Legal)", p);
    }

    private static List<RetentionPolicy> Policies(Microsoft.Data.Sqlite.SqliteConnection db) =>
        db.Query<(string C, long S, string A, string L)>("SELECT data_class, retention_seconds, action, legal FROM policies ORDER BY retention_seconds")
          .Select(r => new RetentionPolicy(r.C, r.S, r.A, r.L, Human(r.S))).ToList();

    private static RetentionItem? Item(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string C, string O, string Ref, string? Sub, string Created, string Due, string Status, long Hold, string? Reason, string? Purged, string? Run)>(
            "SELECT id, data_class, owner_service, ref, subject, created_at, due_at, status, legal_hold, hold_reason, purged_at, run_id FROM items WHERE id = @id", new { id });
        return r.Id is null ? null : new RetentionItem(r.Id, r.C, r.O, r.Ref, r.Sub, r.Created, r.Due, r.Status, r.Hold == 1, r.Reason, r.Purged, r.Run);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] governance = { "Compliance", "Admin" };
        string[] operators = { "Operations", "Compliance", "Admin", "Service" };

        app.MapGet("/api/v1/policies", () => { using var db = ctx.OpenDb(); return Results.Ok(new { items = Policies(db) }); })
            .WithTags("Políticas").WithSummary("Cuánto vive cada clase de dato y qué pasa al vencer");

        app.MapPut("/api/v1/policies/{dataClass}", (HttpContext http, string dataClass, PolicyRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: governance) is { } denied) return denied;
            var action = b.Action?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (b.RetentionSeconds is null or < 1) errors.Add(new FieldError("retentionSeconds", "obligatorio, mayor que 0"));
            if (action is not null && !Actions.Contains(action)) errors.Add(new FieldError("action", "DELETE, ANONYMIZE o SHRED"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Política no válida.", "Ejemplo: { \"retentionSeconds\": 600, \"action\": \"SHRED\" }", errors);
            var cls = dataClass.ToUpperInvariant();
            using var db = ctx.OpenDb();
            var current = Policies(db).FirstOrDefault(p => p.DataClass == cls);
            db.Execute("INSERT INTO policies VALUES (@cls, @s, @a, @l) ON CONFLICT(data_class) DO UPDATE SET retention_seconds = @s, action = @a, legal = @l",
                new { cls, s = b.RetentionSeconds, a = action ?? current?.Action ?? "DELETE", l = b.Legal ?? current?.Legal ?? "" });
            return Results.Ok(Policies(db).First(p => p.DataClass == cls));
        }).WithTags("Políticas").WithSummary("Crear o cambiar una política (Compliance, Admin). No recalcula lo ya registrado.");

        app.MapPost("/api/v1/items", (HttpContext http, RetentionItemRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: operators) is { } denied) return denied;
            var errors = new List<FieldError>();
            using var db = ctx.OpenDb();
            var cls = b.DataClass?.ToUpperInvariant();
            var policy = Policies(db).FirstOrDefault(p => p.DataClass == cls);
            if (policy is null) errors.Add(new FieldError("dataClass", "clase desconocida: mira GET /api/v1/policies"));
            if (string.IsNullOrWhiteSpace(b.OwnerService)) errors.Add(new FieldError("ownerService", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.Ref)) errors.Add(new FieldError("ref", "obligatorio: el id del dato en su servicio"));
            DateTimeOffset created = clock.UtcNow;
            if (b.CreatedAt is not null && !DateTimeOffset.TryParse(b.CreatedAt, out created)) errors.Add(new FieldError("createdAt", "fecha ISO 8601"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Dato no válido.", "Ejemplo: { \"dataClass\": \"VOICE_AUDIO\", \"ownerService\": \"voice-service\", \"ref\": \"call_123\", \"subject\": \"call:call_123\" }", errors);
            var due = created.AddSeconds(policy!.RetentionSeconds);
            var id = "ret_" + Guid.NewGuid().ToString("N")[..16];
            db.Execute("INSERT INTO items VALUES (@id, @cls, @OwnerService, @Ref, @Subject, @c, @dt, @d, 'SCHEDULED', 0, NULL, NULL, NULL)",
                new { id, cls, b.OwnerService, b.Ref, b.Subject, c = Nordic.Iso(Nordic.ToStockholm(created)), dt = due.UtcTicks, d = Nordic.Iso(Nordic.ToStockholm(due)) });
            return Results.Created($"/api/v1/items/{id}", Item(db, id));
        }).WithTags("Datos").WithSummary("Registrar un dato con su clase: se calcula cuándo vence").Produces<RetentionItem>(201);

        app.MapGet("/api/v1/items", (HttpContext http, string? status, string? dataClass, string? ownerService) =>
        {
            if (Auth.Require(http, clock, out _, roles: operators) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>(@"SELECT id FROM items WHERE (@status IS NULL OR status = @status) AND (@dataClass IS NULL OR data_class = @dataClass)
                                           AND (@ownerService IS NULL OR owner_service = @ownerService) ORDER BY due_ticks, id LIMIT 500",
                new { status = status?.ToUpperInvariant(), dataClass = dataClass?.ToUpperInvariant(), ownerService }).Select(i => Item(db, i)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Datos").WithSummary("Datos registrados (por estado, clase o servicio dueño)");

        app.MapGet("/api/v1/items/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: operators) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Item(db, id) is { } i ? Results.Ok(i) : Problems.NotFound(http, "ITEM_NOT_FOUND", $"No existe el dato {id}.", null);
        }).WithTags("Datos").WithSummary("Un dato registrado").Produces<RetentionItem>();

        app.MapPost("/api/v1/items/{id}/legal-hold", (HttpContext http, string id, LegalHoldRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: governance) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var i = Item(db, id);
            if (i is null) return Problems.NotFound(http, "ITEM_NOT_FOUND", $"No existe el dato {id}.", null);
            if (i.Status == "PURGED") return Problems.Conflict(http, "ITEM_ALREADY_PURGED", $"El dato se purgó el {i.PurgedAt}: ya no se puede retener.", null);
            var hold = b.Hold ?? true;
            if (hold && string.IsNullOrWhiteSpace(b.Reason)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Una retención legal necesita un motivo.", null, new[] { new FieldError("reason", "obligatorio") });
            db.Execute("UPDATE items SET legal_hold = @h, hold_reason = @r WHERE id = @id", new { h = hold ? 1 : 0, r = hold ? b.Reason : null, id });
            return Results.Ok(Item(db, id));
        }).WithTags("Datos").WithSummary("Poner o quitar una retención legal (un dato retenido no se purga aunque venza)");

        app.MapPost("/api/v1/runs", async (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out var p, roles: operators) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var policies = Policies(db).ToDictionary(x => x.DataClass);
            var now = clock.UtcNow;
            var due = db.Query<(string Id, long Hold)>("SELECT id, legal_hold FROM items WHERE status = 'SCHEDULED' AND due_ticks <= @t ORDER BY due_ticks", new { t = now.UtcTicks }).ToList();
            var runId = "run_" + Guid.NewGuid().ToString("N")[..12];
            var at = Nordic.Iso(Nordic.ToStockholm(now));
            var purged = new List<RetentionItem>(); var held = new List<RetentionItem>();
            foreach (var (id, hold) in due)
            {
                var item = Item(db, id)!;
                if (hold == 1) { held.Add(item); continue; }
                var action = policies.TryGetValue(item.DataClass, out var pol) ? pol.Action : "DELETE";
                if (action == "SHRED" && item.Subject is not null)
                    await Downstream.Post(http, "key-management", "/api/v1/internal/subjects/shred", new { subject = item.Subject, purpose = "retención " + item.DataClass }, forwardAuth: false);
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE items SET status = 'PURGED', purged_at = @at, run_id = @runId WHERE id = @id", new { at, runId, id }, tx);
                Outbox.Add(db, http, clock, "retention.expired", item.Ref, new { itemId = id, dataClass = item.DataClass, ownerService = item.OwnerService, @ref = item.Ref, subject = item.Subject, action }, tx);
                tx.Commit();
                purged.Add(Item(db, id)!);
            }
            var examined = db.ExecuteScalar<long>("SELECT COUNT(*) FROM items WHERE status = 'SCHEDULED'") + purged.Count;
            db.Execute("INSERT INTO runs VALUES (@runId, @at, @examined, @p, @h, @by)", new { runId, at, examined, p = purged.Count, h = held.Count, by = p.Sub });
            return Results.Created($"/api/v1/runs/{runId}", new { id = runId, ranAt = at, examined, purged = purged.Count, held = held.Count, purgedItems = purged, heldItems = held });
        }).WithTags("Ejecuciones").WithSummary("Ejecutar la purga ahora (según el reloj virtual)");

        app.MapGet("/api/v1/runs/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: operators) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string At, long Ex, long P, long H, string By)>("SELECT id, ran_at, examined, purged, held, run_by FROM runs WHERE id = @id", new { id });
            if (r.Id is null) return Problems.NotFound(http, "RUN_NOT_FOUND", $"No existe la ejecución {id}.", null);
            var items = db.Query<string>("SELECT id FROM items WHERE run_id = @id", new { id }).Select(i => Item(db, i)!).ToList();
            return Results.Ok(new { id = r.Id, ranAt = r.At, examined = r.Ex, purged = r.P, held = r.H, by = r.By, purgedItems = items });
        }).WithTags("Ejecuciones").WithSummary("Informe de una ejecución");
    }
}
