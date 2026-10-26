using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DeviceRequest(string? CustomerId, string? Platform, string? UserAgent, string? Screen, string? Timezone, string? Name);

/// <summary>
/// device-trust: dispositivos de confianza. La huella es el SHA-256 del JSON canónico de plataforma, navegador, pantalla y zona horaria.
/// Registrar un dispositivo no da confianza: marcarlo como de confianza exige un token de alta seguridad (acr=high, tras step-up) y como
/// mucho 3 dispositivos de confianza por cliente. La evaluación de riesgo dice LOW (de confianza), MEDIUM (conocido) o HIGH (nuevo).
/// </summary>
public sealed class DeviceTrust : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "device-trust", "C-identity", 6208, 4, "Identidad",
        "Dispositivos de confianza: huella del dispositivo, confianza solo con step-up (acr=high), máximo 3 por cliente, revocación y evaluación de riesgo.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "device.trusted", "device.revoked" });

    public const int MaxTrusted = 3;

    public static string Fingerprint(string platform, string ua, string screen, string tz) =>
        Canonical.Sha256Hex(Canonical.Of(new { platform = platform.Trim().ToLowerInvariant(), userAgent = ua.Trim(), screen = screen.Trim(), timezone = tz.Trim() }));

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS devices (id TEXT PRIMARY KEY, customer_id TEXT, fingerprint TEXT, name TEXT, platform TEXT, timezone TEXT, status TEXT, first_seen TEXT, trusted_at TEXT, UNIQUE (customer_id, fingerprint))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Fp, string? Name, string Plat, string Tz, string Status, string First, string? Trusted)>(
            "SELECT id, customer_id, fingerprint, name, platform, timezone, status, first_seen, trusted_at FROM devices WHERE id = @id", new { id });
        return r.Id is null ? null : new { id = r.Id, customerId = r.Cust, fingerprintHash = r.Fp, name = r.Name, platform = r.Plat, timezone = r.Tz, status = r.Status, firstSeen = r.First, trustedAt = r.Trusted };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        IResult? Validate(HttpContext http, DeviceRequest b)
        {
            var errors = new List<FieldError>();
            if (b.CustomerId is null) errors.Add(new FieldError("customerId", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.Platform)) errors.Add(new FieldError("platform", "obligatorio (ios, android, web...)"));
            if (string.IsNullOrWhiteSpace(b.UserAgent)) errors.Add(new FieldError("userAgent", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.Screen)) errors.Add(new FieldError("screen", "obligatorio (1920x1080)"));
            if (string.IsNullOrWhiteSpace(b.Timezone)) errors.Add(new FieldError("timezone", "obligatorio (Europe/Stockholm)"));
            return errors.Count > 0 ? Problems.BadRequest(http, "VALIDATION_FAILED", "Dispositivo no válido.", null, errors) : null;
        }

        async Task<(IResult? Fail, string Customer)> Owner(HttpContext http, Principal p, string id, string action)
        {
            string? c;
            using (var db = ctx.OpenDb()) c = db.ExecuteScalar<string?>("SELECT customer_id FROM devices WHERE id = @id", new { id });
            if (c is null) return (Problems.NotFound(http, "DEVICE_NOT_FOUND", $"No existe el dispositivo {id}.", null), "");
            return (await CustomerGuard.Check(http, p, action, c), c);
        }

        app.MapPost("/api/v1/devices", async (HttpContext http, DeviceRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor", "Operations" }) is { } denied) return denied;
            if (Validate(http, b) is { } bad) return bad;
            if (await CustomerGuard.Check(http, p, "customer:update", b.CustomerId!) is { } g) return g;
            var fp = Fingerprint(b.Platform!, b.UserAgent!, b.Screen!, b.Timezone!);
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT id FROM devices WHERE customer_id = @CustomerId AND fingerprint = @fp", new { b.CustomerId, fp }) is { } known) return Results.Ok(Load(db, known));
            var id = "dev_" + Guid.NewGuid().ToString("N")[..12];
            db.Execute("INSERT INTO devices VALUES (@id, @CustomerId, @fp, @Name, @pl, @Timezone, 'UNTRUSTED', @at, NULL)",
                new { id, b.CustomerId, fp, b.Name, pl = b.Platform!.ToLowerInvariant(), b.Timezone, at = Nordic.Iso(clock.LocalNow) });
            return Results.Created($"/api/v1/devices/{id}", Load(db, id));
        }).WithTags("Dispositivos").WithSummary("Registrar un dispositivo (nace UNTRUSTED; si ya existía, el mismo)");

        app.MapPost("/api/v1/devices/{id}/trust", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var (fail, cust) = await Owner(http, p, id, "customer:update");
            if (fail is not null) return fail;
            if (p.Acr != "high") return Problems.Of(http, 403, "STEP_UP_REQUIRED", "Hace falta más seguridad", "Para confiar en un dispositivo hace falta un token de alta seguridad (acr=high).", "Pide uno en step-up-service (6205): POST /api/v1/step-up");
            using var db = ctx.OpenDb();
            var status = db.ExecuteScalar<string>("SELECT status FROM devices WHERE id = @id", new { id });
            if (status == "TRUSTED") return Results.Ok(Load(db, id));
            if (status == "REVOKED") return Problems.Conflict(http, "DEVICE_REVOKED", "Un dispositivo revocado no vuelve a ser de confianza: regístralo de nuevo.", null);
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM devices WHERE customer_id = @cust AND status = 'TRUSTED'", new { cust }) >= MaxTrusted)
                return Problems.Conflict(http, "DEVICE_LIMIT_REACHED", $"Como mucho {MaxTrusted} dispositivos de confianza: revoca uno antes.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE devices SET status = 'TRUSTED', trusted_at = @at WHERE id = @id", new { id, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "device.trusted", id, new { deviceId = id, customerId = cust }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Dispositivos").WithSummary("Marcar como de confianza (exige acr=high)");

        app.MapPost("/api/v1/devices/{id}/revoke", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var (fail, cust) = await Owner(http, p, id, "customer:update");
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string>("SELECT status FROM devices WHERE id = @id", new { id }) == "REVOKED") return Problems.Conflict(http, "DEVICE_REVOKED", "Ya estaba revocado.", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE devices SET status = 'REVOKED' WHERE id = @id", new { id }, tx);
            Outbox.Add(db, http, clock, "device.revoked", id, new { deviceId = id, customerId = cust }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Dispositivos").WithSummary("Revocar un dispositivo");

        app.MapGet("/api/v1/customers/{customerId}/devices", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM devices WHERE customer_id = @customerId ORDER BY first_seen, id", new { customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count, trusted = db.ExecuteScalar<long>("SELECT COUNT(*) FROM devices WHERE customer_id = @customerId AND status = 'TRUSTED'", new { customerId }) });
        }).WithTags("Dispositivos").WithSummary("Dispositivos del cliente");

        app.MapPost("/api/v1/devices/assess", async (HttpContext http, DeviceRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (Validate(http, b) is { } bad) return bad;
            if (await CustomerGuard.Check(http, p, "customer:read", b.CustomerId!) is { } g) return g;
            var fp = Fingerprint(b.Platform!, b.UserAgent!, b.Screen!, b.Timezone!);
            using var db = ctx.OpenDb();
            var dev = db.QuerySingleOrDefault<(string? Id, string? Status)>("SELECT id, status FROM devices WHERE customer_id = @CustomerId AND fingerprint = @fp", new { b.CustomerId, fp });
            var zones = db.Query<string>("SELECT DISTINCT timezone FROM devices WHERE customer_id = @CustomerId AND status <> 'REVOKED'", new { b.CustomerId }).ToList();
            var reasons = new List<string>();
            var risk = dev.Status switch { "TRUSTED" => "LOW", "UNTRUSTED" => "MEDIUM", _ => "HIGH" };
            if (dev.Id is null) reasons.Add("NEW_DEVICE");
            if (dev.Status == "REVOKED") reasons.Add("REVOKED_DEVICE");
            if (zones.Count > 0 && !zones.Contains(b.Timezone!)) { reasons.Add("UNUSUAL_TIMEZONE"); if (risk == "MEDIUM") risk = "HIGH"; }
            return Results.Ok(new { customerId = b.CustomerId, fingerprintHash = fp, deviceId = dev.Id, known = dev.Id is not null, trusted = dev.Status == "TRUSTED", risk, reasons });
        }).WithTags("Dispositivos").WithSummary("Evaluar el riesgo de un dispositivo (LOW, MEDIUM, HIGH)");
    }
}
