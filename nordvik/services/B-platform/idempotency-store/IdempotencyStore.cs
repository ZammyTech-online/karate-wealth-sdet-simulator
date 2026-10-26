using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ClaimRequest(string? RequestHash, int? TtlSeconds);
public sealed record StoreResponseRequest(int? Status, JsonElement? Body, string? Location);
public sealed record IdempotencyEntry(string Scope, string Key, string RequestHash, string State, int? Status, JsonElement? Body, string? Location, string CreatedAt, string ExpiresAt);

/// <summary>
/// idempotency-store: guarda cada Idempotency-Key 24 h (reloj virtual) con el hash del cuerpo y la respuesta.
/// PUT reserva la clave (201), o devuelve lo guardado (200), o 422 si el cuerpo es distinto.
/// </summary>
public sealed class IdempotencyStore : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "idempotency-store", "B-platform", 6103, 2, "Plataforma",
        "Almacén de claves de idempotencia con el hash del cuerpo y la respuesta guardada. Lo usan order-management y payments-gateway.",
        DependsOn: new[] { "clock-service" });

    private static readonly object Gate = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS keys (scope TEXT, key TEXT, request_hash TEXT, state TEXT, status INTEGER, body TEXT, location TEXT,
                     created_at TEXT, expires_ticks INTEGER, expires_at TEXT, PRIMARY KEY (scope, key))");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM keys WHERE key LIKE @p", new { p = prefix + "%" });
    }

    private static IdempotencyEntry? Load(Microsoft.Data.Sqlite.SqliteConnection db, string scope, string key)
    {
        var r = db.QuerySingleOrDefault<(string Scope, string Key, string Hash, string State, long? Status, string? Body, string? Location, string CreatedAt, string ExpiresAt)>(
            "SELECT scope, key, request_hash, state, status, body, location, created_at, expires_at FROM keys WHERE scope = @scope AND key = @key", new { scope, key });
        return r.Key is null ? null : new IdempotencyEntry(r.Scope, r.Key, r.Hash, r.State, (int?)r.Status, r.Body is null ? null : JsonDocument.Parse(r.Body).RootElement, r.Location, r.CreatedAt, r.ExpiresAt);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/keys/{scope}/{key}").WithTags("Claves");

        g.MapPut("", (HttpContext http, IClock clock, string scope, string key, ClaimRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.RequestHash))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta requestHash.", "SHA-256 del JSON canónico del cuerpo.", new[] { new FieldError("requestHash", "obligatorio") });
            var now = clock.UtcNow;
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                db.Execute("DELETE FROM keys WHERE scope = @scope AND key = @key AND expires_ticks <= @t", new { scope, key, t = now.UtcTicks });
                var existing = Load(db, scope, key);
                if (existing is not null)
                {
                    if (existing.RequestHash != body.RequestHash)
                        return Problems.Unprocessable(http, "REQUEST_HASH_MISMATCH", "La clave ya existe con otro cuerpo.", null);
                    return Results.Ok(existing);
                }
                var ttl = Math.Clamp(body.TtlSeconds ?? 86400, 60, 7 * 86400);
                var exp = now.AddSeconds(ttl);
                db.Execute("INSERT INTO keys VALUES (@scope, @key, @h, 'IN_PROGRESS', NULL, NULL, NULL, @created, @ticks, @expiresAt)",
                    new { scope, key, h = body.RequestHash, created = Nordic.Iso(Nordic.ToStockholm(now)), ticks = exp.UtcTicks, expiresAt = Nordic.Iso(Nordic.ToStockholm(exp)) });
                return Results.Created($"/api/v1/keys/{scope}/{key}", Load(db, scope, key));
            }
        }).WithSummary("Reservar una clave (201) o recuperar lo guardado (200)").Produces<IdempotencyEntry>(201).Produces<IdempotencyEntry>();

        g.MapPost("/response", (HttpContext http, string scope, string key, StoreResponseRequest body) =>
        {
            if (body.Status is null || body.Body is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Faltan status y body.", null);
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE keys SET state = 'COMPLETED', status = @Status, body = @b, location = @Location WHERE scope = @scope AND key = @key",
                new { body.Status, b = body.Body.Value.GetRawText(), body.Location, scope, key });
            return n == 0 ? Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {scope}/{key}.", null) : Results.Ok(Load(db, scope, key));
        }).WithSummary("Guardar la respuesta de una clave reservada").Produces<IdempotencyEntry>();

        g.MapGet("", (HttpContext http, IClock clock, string scope, string key) =>
        {
            using var db = ctx.OpenDb();
            db.Execute("DELETE FROM keys WHERE expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
            var e = Load(db, scope, key);
            return e is null ? Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {scope}/{key} (o ya caducó a las 24 h).", null) : Results.Ok(e);
        }).WithSummary("Ver una clave").Produces<IdempotencyEntry>();

        g.MapDelete("", (HttpContext http, string scope, string key) =>
        {
            using var db = ctx.OpenDb();
            return db.Execute("DELETE FROM keys WHERE scope = @scope AND key = @key", new { scope, key }) == 0
                ? Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {scope}/{key}.", null)
                : Results.NoContent();
        }).WithSummary("Liberar una clave (se usa cuando la operación falló con 5xx)");
    }
}
