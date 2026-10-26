using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ConfigEntry(string Service, string Key, string Value, long Version, string UpdatedAt, string UpdatedBy);
public sealed record ServiceConfig(string Service, IReadOnlyDictionary<string, string> Values, long Version);
public sealed record SetConfigRequest(string? Value);

/// <summary>config-service: configuración por servicio con versiones e historial. Cambiarla exige rol Admin.</summary>
public sealed class ConfigService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "config-service", "B-platform", 6102, 1, "Plataforma",
        "Valores de configuración de cada servicio (horas de corte, límites, TTL) con versión e historial de cambios.",
        DependsOn: new[] { "clock-service" });

    private static readonly (string Service, string Key, string Value)[] Defaults =
    {
        ("rate-limiter", "limitPerMinute", "100"),
        ("order-management", "cutOffTime", "15:00"),
        ("order-management", "timezone", "Europe/Stockholm"),
        ("market-data", "publishTime", "18:00"),
        ("iam-service", "accessTokenTtlSeconds", "300"),
        ("iam-service", "maxFailedLogins", "5"),
        ("session-service", "idleTimeoutMinutes", "30"),
        ("contact-service", "verificationCodeTtlMinutes", "10"),
        ("portfolio-service", "maxFundsPerPortfolio", "10"),
        ("event-bus", "retryBackoffSeconds", "1,2,4,8"),
        ("event-bus", "maxAttempts", "5"),
        ("payments-gateway", "stepUpThreshold", "10000.00"),
        ("payments-gateway", "dailyWithdrawalLimit", "50000.00"),
        ("order-management", "minOrderAmount", "100.00"),
        ("order-router", "autoRun", "true"),
        ("rebalancing-engine", "driftThresholdPp", "5.00"),
        ("rebalancing-engine", "minTradeSek", "100.00"),
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS config (service TEXT, key TEXT, value TEXT, version INTEGER, updated_at TEXT, updated_by TEXT, PRIMARY KEY (service, key));
                     CREATE TABLE IF NOT EXISTS history (service TEXT, key TEXT, value TEXT, version INTEGER, updated_at TEXT, updated_by TEXT)");
        foreach (var (s, k, v) in Defaults)
            db.Execute("INSERT OR IGNORE INTO config VALUES (@s, @k, @v, 1, @t, 'semilla')", new { s, k, v, t = DateTimeOffset.UtcNow.ToString("O") });
    }

    private const string Sel = "SELECT service, key, value, version, updated_at AS UpdatedAt, updated_by AS UpdatedBy FROM config";

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/config").WithTags("Configuración");

        g.MapGet("", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT DISTINCT service FROM config ORDER BY service").ToList());
        }).WithSummary("Servicios con configuración");

        g.MapGet("/{service}", (HttpContext http, string service) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<ConfigEntry>(Sel + " WHERE service = @service ORDER BY key", new { service }).ToList();
            if (rows.Count == 0) return Problems.NotFound(http, "SERVICE_CONFIG_NOT_FOUND", $"No hay configuración de {service}.", "Lista los servicios con GET /api/v1/config");
            return Results.Ok(new ServiceConfig(service, rows.ToDictionary(r => r.Key, r => r.Value), rows.Max(r => r.Version)));
        }).WithSummary("Toda la configuración de un servicio").Produces<ServiceConfig>();

        g.MapGet("/{service}/{key}", (HttpContext http, string service, string key) =>
        {
            using var db = ctx.OpenDb();
            var row = db.QuerySingleOrDefault<ConfigEntry>(Sel + " WHERE service = @service AND key = @key", new { service, key });
            return row is null ? NotFound(http, service, key) : Results.Ok(row);
        }).WithSummary("Un valor").Produces<ConfigEntry>();

        g.MapPut("/{service}/{key}", (HttpContext http, IClock clock, string service, string key, SetConfigRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Admin" });
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(body.Value))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta value.", "Ejemplo: { \"value\": \"200\" }", new[] { new FieldError("value", "obligatorio") });
            using var db = ctx.OpenDb();
            var current = db.QuerySingleOrDefault<ConfigEntry>(Sel + " WHERE service = @service AND key = @key", new { service, key });
            if (current is null) return NotFound(http, service, key);
            var version = current.Version + 1;
            var now = Nordic.Iso(clock.LocalNow);
            db.Execute("INSERT INTO history VALUES (@Service, @Key, @Value, @Version, @UpdatedAt, @UpdatedBy)", current);
            db.Execute("UPDATE config SET value = @v, version = @version, updated_at = @now, updated_by = @by WHERE service = @service AND key = @key",
                new { v = body.Value, version, now, by = p.Sub, service, key });
            return Results.Ok(new ConfigEntry(service, key, body.Value, version, now, p.Sub));
        }).WithSummary("Cambiar un valor (rol Admin)").Produces<ConfigEntry>();

        g.MapGet("/{service}/{key}/history", (HttpContext http, string service, string key) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<ConfigEntry>("SELECT service, key, value, version, updated_at AS UpdatedAt, updated_by AS UpdatedBy FROM history WHERE service = @service AND key = @key ORDER BY version", new { service, key }).ToList();
            return Results.Ok(rows);
        }).WithSummary("Valores anteriores").Produces<List<ConfigEntry>>();
    }

    private static IResult NotFound(HttpContext http, string service, string key) =>
        Problems.NotFound(http, "CONFIG_KEY_NOT_FOUND", $"No existe la clave {key} en {service}.", $"Mira las claves con GET /api/v1/config/{service}");
}
