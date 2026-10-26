using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FlagRequest(bool? Enabled, int? Percentage, string[]? AllowCustomers, string[]? DenyCustomers, string? Description);

/// <summary>
/// feature-flags: funcionalidades activables. Orden de evaluación: apagada (OFF) → lista de bloqueo (DENYLIST) → lista de permitidos (ALLOWLIST)
/// → porcentaje (el mismo cliente cae siempre en el mismo cubo: hash(flag + cliente) mod 100) → por defecto. Cambiar un flag guarda versión.
/// </summary>
public sealed class FeatureFlags : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "feature-flags", "B-platform", 6112, 4, "Plataforma",
        "Feature flags con lista de permitidos y bloqueados y despliegue por porcentaje determinista por cliente; versión e historial de cambios.",
        DependsOn: new[] { "clock-service" });

    public static int Bucket(string flag, string customerId) => Convert.ToInt32(Canonical.Sha256Hex(flag + ":" + customerId)[..4], 16) % 100;

    public static (bool On, string Reason) Evaluate(bool enabled, int pct, string[] allow, string[] deny, string? customerId, string flag)
    {
        if (!enabled) return (false, "OFF");
        if (customerId is not null && deny.Contains(customerId)) return (false, "DENYLIST");
        if (customerId is not null && allow.Contains(customerId)) return (true, "ALLOWLIST");
        if (pct >= 100) return (true, "ALL");
        if (customerId is null) return (false, "NO_CUSTOMER");
        return Bucket(flag, customerId) < pct ? (true, "PERCENTAGE") : (false, "PERCENTAGE");
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS flags (key TEXT PRIMARY KEY, enabled INTEGER, percentage INTEGER, allow TEXT, deny TEXT, description TEXT, version INTEGER, updated_at TEXT, updated_by TEXT);
                     CREATE TABLE IF NOT EXISTS history (key TEXT, version INTEGER, snapshot TEXT, at TEXT, by TEXT, PRIMARY KEY (key, version))");
        foreach (var (k, e, p, d) in new[] { ("new-dashboard", true, 25, "Nuevo panel del cliente"), ("esg-filter", true, 100, "Filtro ESG en el catálogo de fondos"), ("crypto-funds", false, 0, "Fondos cripto (apagado)") })
            db.Execute("INSERT OR IGNORE INTO flags VALUES (@k, @e, @p, '[]', '[]', @d, 1, @at, 'seed')", new { k, e = e ? 1 : 0, p, d, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string key)
    {
        var r = db.QuerySingleOrDefault<(string Key, long En, long Pct, string Allow, string Deny, string? Desc, long V, string At, string By)>(
            "SELECT key, enabled, percentage, allow, deny, description, version, updated_at, updated_by FROM flags WHERE key = @key", new { key });
        return r.Key is null ? null : new
        {
            key = r.Key, enabled = r.En == 1, percentage = r.Pct, allowCustomers = JsonSerializer.Deserialize<string[]>(r.Allow), denyCustomers = JsonSerializer.Deserialize<string[]>(r.Deny),
            description = r.Desc, version = r.V, updatedAt = r.At, updatedBy = r.By,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/flags", () =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT key FROM flags ORDER BY key").Select(k => Load(db, k)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Flags").WithSummary("Todos los flags");

        app.MapGet("/api/v1/flags/{key}", (HttpContext http, string key) =>
        {
            using var db = ctx.OpenDb();
            return Load(db, key) is { } f ? Results.Ok(f) : Problems.NotFound(http, "FLAG_NOT_FOUND", $"No existe el flag {key}.", null);
        }).WithTags("Flags").WithSummary("Un flag");

        app.MapPut("/api/v1/flags/{key}", (HttpContext http, string key, FlagRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Admin" }) is { } denied) return denied;
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z][a-z0-9-]{2,40}$")) return Problems.BadRequest(http, "VALIDATION_FAILED", "key en minúsculas con guiones (3-41).", null);
            if (b.Enabled is null || b.Percentage is null or < 0 or > 100) return Problems.BadRequest(http, "VALIDATION_FAILED", "enabled (bool) y percentage (0-100) son obligatorios.", null);
            var allow = b.AllowCustomers ?? Array.Empty<string>(); var deny = b.DenyCustomers ?? Array.Empty<string>();
            if (allow.Intersect(deny).Any()) return Problems.Unprocessable(http, "CONFLICTING_LISTS", "Un cliente no puede estar en las dos listas.", null);
            using var db = ctx.OpenDb();
            var v = (db.ExecuteScalar<long?>("SELECT version FROM flags WHERE key = @key", new { key }) ?? 0) + 1;
            var at = Nordic.Iso(clock.LocalNow);
            db.Execute(@"INSERT INTO flags VALUES (@key, @en, @Percentage, @a, @d, @Description, @v, @at, @by)
                         ON CONFLICT(key) DO UPDATE SET enabled = @en, percentage = @Percentage, allow = @a, deny = @d, description = COALESCE(@Description, description), version = @v, updated_at = @at, updated_by = @by",
                new { key, en = b.Enabled.Value ? 1 : 0, b.Percentage, a = JsonSerializer.Serialize(allow), d = JsonSerializer.Serialize(deny), b.Description, v, at, by = p.Sub });
            db.Execute("INSERT INTO history VALUES (@key, @v, @s, @at, @by)", new { key, v, s = JsonSerializer.Serialize(Load(db, key), Canonical.Web), at, by = p.Sub });
            return v == 1 ? Results.Created($"/api/v1/flags/{key}", Load(db, key)) : Results.Ok(Load(db, key));
        }).WithTags("Flags").WithSummary("Crear o cambiar un flag (Admin)");

        app.MapGet("/api/v1/flags/{key}/evaluate", (HttpContext http, string key, string? customerId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Key, long En, long Pct, string Allow, string Deny)>("SELECT key, enabled, percentage, allow, deny FROM flags WHERE key = @key", new { key });
            if (r.Key is null) return Problems.NotFound(http, "FLAG_NOT_FOUND", $"No existe el flag {key}.", "Un flag desconocido se trata como apagado en los clientes.");
            var (on, reason) = Evaluate(r.En == 1, (int)r.Pct, JsonSerializer.Deserialize<string[]>(r.Allow)!, JsonSerializer.Deserialize<string[]>(r.Deny)!, customerId, key);
            return Results.Ok(new { key, customerId, enabled = on, reason, bucket = customerId is null ? (int?)null : Bucket(key, customerId), percentage = r.Pct });
        }).WithTags("Flags").WithSummary("¿Está activo para este cliente? (con el motivo)");

        app.MapGet("/api/v1/flags/{key}/history", (HttpContext http, string key) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<(long V, string S, string At, string By)>("SELECT version, snapshot, at, by FROM history WHERE key = @key ORDER BY version", new { key })
                .Select(h => new { version = h.V, snapshot = JsonDocument.Parse(h.S).RootElement, at = h.At, by = h.By }).ToList();
            return Results.Ok(new { key, items, count = items.Count });
        }).WithTags("Flags").WithSummary("Historial de cambios");
    }
}
