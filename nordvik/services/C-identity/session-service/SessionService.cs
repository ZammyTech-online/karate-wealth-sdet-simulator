using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateSessionRequest(string? UserId, string? Role, string? Device);
public sealed record Session(string Id, string UserId, string Role, string Device, string Status, string CreatedAt, string LastSeenAt, string? RevokedReason);

/// <summary>session-service: sesiones de usuario. Caducan tras 30 minutos sin uso (reloj virtual) y se pueden cerrar en remoto.</summary>
public sealed class SessionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "session-service", "C-identity", 6201, 1, "Identidad",
        "Sesiones activas por usuario: alta al iniciar sesión, caducidad por inactividad y cierre remoto.",
        DependsOn: new[] { "clock-service" });

    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);
    private const string Sel = "SELECT id, user_id AS UserId, role, device, status, created_at AS CreatedAt, last_seen_at AS LastSeenAt, revoked_reason AS RevokedReason FROM sessions";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, user_id TEXT, role TEXT, device TEXT, status TEXT, created_at TEXT, last_seen_at TEXT, last_seen_ticks INTEGER, revoked_reason TEXT)");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM sessions WHERE user_id LIKE @p", new { p = prefix + "%" });
    }

    private static Session? Load(ServiceContext ctx, IClock clock, string id, bool touch)
    {
        using var db = ctx.OpenDb();
        var s = db.QuerySingleOrDefault<Session>(Sel + " WHERE id = @id", new { id });
        if (s is null) return null;
        var ticks = db.ExecuteScalar<long>("SELECT last_seen_ticks FROM sessions WHERE id = @id", new { id });
        var now = clock.UtcNow;
        if (s.Status == "ACTIVE" && now - new DateTimeOffset(ticks, TimeSpan.Zero) > Idle)
        {
            db.Execute("UPDATE sessions SET status = 'EXPIRED', revoked_reason = 'inactividad' WHERE id = @id", new { id });
            return s with { Status = "EXPIRED", RevokedReason = "inactividad" };
        }
        if (touch && s.Status == "ACTIVE")
        {
            var iso = Nordic.Iso(Nordic.ToStockholm(now));
            db.Execute("UPDATE sessions SET last_seen_at = @iso, last_seen_ticks = @t WHERE id = @id", new { iso, t = now.UtcTicks, id });
            return s with { LastSeenAt = iso };
        }
        return s;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1").WithTags("Sesiones");

        g.MapPost("/sessions", (HttpContext http, IClock clock, CreateSessionRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.UserId) || string.IsNullOrWhiteSpace(body.Role))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "userId y role son obligatorios.", "Normalmente las crea iam-service al iniciar sesión.",
                    new[] { new FieldError("userId", "obligatorio"), new FieldError("role", "obligatorio") });
            var now = clock.UtcNow;
            var iso = Nordic.Iso(Nordic.ToStockholm(now));
            var s = new Session("ses_" + Guid.NewGuid().ToString("N")[..20], body.UserId, body.Role, body.Device ?? "api", "ACTIVE", iso, iso, null);
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO sessions VALUES (@Id, @UserId, @Role, @Device, @Status, @CreatedAt, @LastSeenAt, @t, NULL)", new { s.Id, s.UserId, s.Role, s.Device, s.Status, s.CreatedAt, s.LastSeenAt, t = now.UtcTicks });
            return Results.Created($"/api/v1/sessions/{s.Id}", s);
        }).WithSummary("Crear una sesión (la usa iam-service)").Produces<Session>(201);

        g.MapGet("/sessions/{id}", (HttpContext http, IClock clock, string id, bool? touch) =>
        {
            var s = Load(ctx, clock, id, touch ?? false);
            return s is null ? Problems.NotFound(http, "SESSION_NOT_FOUND", $"No existe la sesión {id}.", "El id de sesión viaja en el claim 'sid' del token.") : Results.Ok(s);
        }).WithSummary("Ver una sesión (touch=true la marca como usada)").Produces<Session>();

        g.MapGet("/users/{userId}/sessions", (IClock clock, string userId, string? status) =>
        {
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM sessions WHERE user_id = @userId ORDER BY created_at", new { userId }).ToList();
            var list = ids.Select(i => Load(ctx, clock, i, false)!).Where(s => status is null || s.Status == status).ToList();
            return Results.Ok(list);
        }).WithSummary("Sesiones de un usuario").Produces<List<Session>>();

        g.MapDelete("/sessions/{id}", (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var s = Load(ctx, clock, id, false);
            if (s is null) return Problems.NotFound(http, "SESSION_NOT_FOUND", $"No existe la sesión {id}.", null);
            if (s.UserId != p.Sub && p.Role != "Admin")
                return Problems.Of(http, 403, "ACCESS_DENIED", "Prohibido", "Solo puedes cerrar tus propias sesiones.", "Un Admin puede cerrar cualquiera.");
            using var db = ctx.OpenDb();
            db.Execute("UPDATE sessions SET status = 'REVOKED', revoked_reason = @r WHERE id = @id", new { r = p.Sub == s.UserId ? "cierre por el usuario" : "cierre por " + p.Sub, id });
            return Results.NoContent();
        }).WithSummary("Cerrar una sesión (la tuya, o cualquiera si eres Admin)");

        g.MapDelete("/users/{userId}/sessions", (HttpContext http, IClock clock, string userId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            if (userId != p.Sub && p.Role != "Admin")
                return Problems.Of(http, 403, "ACCESS_DENIED", "Prohibido", "Solo puedes cerrar tus propias sesiones.", null);
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE sessions SET status = 'REVOKED', revoked_reason = 'cierre de todas las sesiones' WHERE user_id = @userId AND status = 'ACTIVE'", new { userId });
            return Results.Ok(new { userId, revoked = n });
        }).WithSummary("Cerrar todas las sesiones de un usuario");
    }
}
