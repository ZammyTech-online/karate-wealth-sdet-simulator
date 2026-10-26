using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SecretRequest(string? Name, string? Value, string? Description, int? GraceSeconds);
public sealed record RotateRequest(string? Value, int? GraceSeconds);
public sealed record SecretMeta(string Name, string? Description, int CurrentVersion, string UpdatedAt, int GraceSeconds, int? PreviousVersion, string? PreviousValidUntil);

/// <summary>
/// secrets-service: claves de firma de webhooks y API keys de los sistemas externos, con versiones y rotación.
/// Al rotar, la versión anterior sigue siendo válida durante un periodo de gracia (como en la vida real: así no se cae nada mientras
/// los demás se enteran). La lista nunca muestra valores; leer un valor exige rol Service o Admin y queda auditado.
/// </summary>
public sealed class SecretsService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "secrets-service", "B-platform", 6109, 3, "Plataforma",
        "Secretos con versiones (claves de webhooks, API keys de los simuladores), rotación con periodo de gracia y verificación sin revelar el valor.",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "secret.rotated", "secret.read" });

    private static readonly (string Name, string Value, string Description)[] Seed =
    {
        ("nordpay-webhook-hmac", "nordpay-webhook-secret-2026", "HMAC de los avisos camt.054 de NordPay"),
        ("custodian-webhook-hmac", "custodian-webhook-secret-2026", "HMAC de las ejecuciones del custodio"),
        ("pricefeed-api-key", "pf-live-key-2026", "API key del proveedor de precios"),
        ("nordid-client-cert", "nordid-cert-fingerprint-2026", "Huella del certificado de cliente de NordID"),
        ("sanctionscreen-api-key", "ss-key-2026-b", "API key de SanctionScreen (E3)"),
        ("population-client-cert", "SHA256:nordvik-client-cert-2026", "Certificado de cliente (mTLS) del Registro de Población (E2)"),
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS secrets (name TEXT PRIMARY KEY, description TEXT, current_version INTEGER, updated_at TEXT, grace_seconds INTEGER);
                     CREATE TABLE IF NOT EXISTS secret_versions (name TEXT, version INTEGER, value TEXT, created_at TEXT, valid_until_ticks INTEGER, valid_until TEXT, PRIMARY KEY (name, version))");
        foreach (var (n, v, d) in Seed)
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM secrets WHERE name = @n", new { n }) == 0)
            {
                db.Execute("INSERT INTO secrets VALUES (@n, @d, 1, '2026-01-01T00:00:00.000+01:00', 3600)", new { n, d });
                db.Execute("INSERT INTO secret_versions VALUES (@n, 1, @v, '2026-01-01T00:00:00.000+01:00', NULL, NULL)", new { n, v });
            }
    }

    private static SecretMeta? Meta(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string name)
    {
        var s = db.QuerySingleOrDefault<(string Name, string? Desc, long Ver, string Updated, long Grace)>("SELECT name, description, current_version, updated_at, grace_seconds FROM secrets WHERE name = @name", new { name });
        if (s.Name is null) return null;
        var prev = db.QuerySingleOrDefault<(long Version, long? Ticks, string? Until)>("SELECT version, valid_until_ticks, valid_until FROM secret_versions WHERE name = @name AND version = @v",
            new { name, v = s.Ver - 1 });
        var prevValid = prev.Ticks is { } t && clock.UtcNow.UtcTicks < t;
        return new SecretMeta(s.Name, s.Desc, (int)s.Ver, s.Updated, (int)s.Grace, prevValid ? (int)prev.Version : null, prevValid ? prev.Until : null);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] readers = { "Service", "Admin" };

        app.MapGet("/api/v1/secrets", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin", "Operations", "Service" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT name FROM secrets ORDER BY name").Select(n => Meta(db, clock, n)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Secretos").WithSummary("Listar secretos (solo metadatos, nunca valores)");

        app.MapPost("/api/v1/secrets", (HttpContext http, SecretRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Name) || !System.Text.RegularExpressions.Regex.IsMatch(b.Name, "^[a-z][a-z0-9-]{2,62}$")) errors.Add(new FieldError("name", "minúsculas, números y guiones (3-63)"));
            if (b.Value is { Length: < 12 }) errors.Add(new FieldError("value", "mínimo 12 caracteres (o no lo envíes y se genera uno)"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Secreto no válido.", null, errors);
            using var db = ctx.OpenDb();
            if (Meta(db, clock, b.Name!) is not null) return Problems.Conflict(http, "SECRET_ALREADY_EXISTS", $"Ya existe {b.Name}.", "Para cambiar su valor, rótalo: POST /api/v1/secrets/{name}/rotate");
            var now = Nordic.Iso(clock.LocalNow);
            db.Execute("INSERT INTO secrets VALUES (@Name, @Description, 1, @now, @g)", new { b.Name, b.Description, now, g = b.GraceSeconds ?? 3600 });
            db.Execute("INSERT INTO secret_versions VALUES (@Name, 1, @v, @now, NULL, NULL)", new { b.Name, v = b.Value ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(), now });
            return Results.Created($"/api/v1/secrets/{b.Name}", Meta(db, clock, b.Name!));
        }).WithTags("Secretos").WithSummary("Crear un secreto (Admin)");

        app.MapGet("/api/v1/secrets/{name}", (HttpContext http, string name) =>
        {
            if (Auth.Require(http, clock, out var p, roles: readers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var m = Meta(db, clock, name);
            if (m is null) return Problems.NotFound(http, "SECRET_NOT_FOUND", $"No existe el secreto {name}.", null);
            var value = db.ExecuteScalar<string>("SELECT value FROM secret_versions WHERE name = @name AND version = @v", new { name, v = m.CurrentVersion });
            Outbox.Add(db, http, clock, "secret.read", name, new { name, version = m.CurrentVersion, by = p.Sub });
            return Results.Ok(new { name, version = m.CurrentVersion, value });
        }).WithTags("Secretos").WithSummary("Leer el valor actual (Service o Admin; queda auditado)");

        app.MapPost("/api/v1/secrets/{name}/rotate", async (HttpContext http, string name) =>
        {
            RotateRequest? b = null;
            if (http.Request.ContentLength is > 0) try { b = await System.Text.Json.JsonSerializer.DeserializeAsync<RotateRequest>(http.Request.Body, Canonical.Web); } catch (System.Text.Json.JsonException) { }
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var m = Meta(db, clock, name);
            if (m is null) return Problems.NotFound(http, "SECRET_NOT_FOUND", $"No existe el secreto {name}.", null);
            if (b?.Value is { Length: < 12 }) return Problems.BadRequest(http, "VALIDATION_FAILED", "El valor nuevo necesita al menos 12 caracteres.", null);
            var grace = b?.GraceSeconds ?? m.GraceSeconds;
            var until = clock.UtcNow.AddSeconds(grace);
            var now = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE secret_versions SET valid_until_ticks = @t, valid_until = @u WHERE name = @name AND version = @v",
                new { t = until.UtcTicks, u = Nordic.Iso(Nordic.ToStockholm(until)), name, v = m.CurrentVersion }, tx);
            db.Execute("INSERT INTO secret_versions VALUES (@name, @v, @value, @now, NULL, NULL)",
                new { name, v = m.CurrentVersion + 1, value = b?.Value ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(), now }, tx);
            db.Execute("UPDATE secrets SET current_version = current_version + 1, updated_at = @now, grace_seconds = @grace WHERE name = @name", new { now, grace, name }, tx);
            Outbox.Add(db, http, clock, "secret.rotated", name, new { name, version = m.CurrentVersion + 1, previousValidUntil = Nordic.Iso(Nordic.ToStockholm(until)) }, tx);
            tx.Commit();
            return Results.Ok(Meta(db, clock, name));
        }).WithTags("Secretos").WithSummary("Rotar: versión nueva; la anterior vale durante graceSeconds (reloj virtual)");

        app.MapPost("/api/v1/secrets/{name}/verify", (HttpContext http, string name, SecretRequest b) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var m = Meta(db, clock, name);
            if (m is null) return Problems.NotFound(http, "SECRET_NOT_FOUND", $"No existe el secreto {name}.", null);
            var rows = db.Query<(long Version, string Value, long? Ticks)>("SELECT version, value, valid_until_ticks FROM secret_versions WHERE name = @name", new { name });
            var hit = rows.FirstOrDefault(r => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(r.Value), System.Text.Encoding.UTF8.GetBytes(b.Value ?? "")));
            if (hit.Value is null) return Results.Ok(new { valid = false, reason = "no coincide con ninguna versión" });
            if (hit.Version == m.CurrentVersion) return Results.Ok(new { valid = true, version = hit.Version, current = true });
            var inGrace = hit.Ticks is { } t && clock.UtcNow.UtcTicks < t;
            return Results.Ok(new { valid = inGrace, version = hit.Version, current = false, reason = inGrace ? "versión anterior en periodo de gracia" : "versión anterior caducada" });
        }).WithTags("Secretos").WithSummary("¿Vale este valor? (actual, o anterior en periodo de gracia) sin revelar el secreto");
    }
}
