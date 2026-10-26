using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ChallengeRequest(string? Channel, string? Destination, string? Purpose, string? Language);
public sealed record VerifyCodeRequest(string? Code);
public sealed record Challenge(string Id, string UserId, string Channel, string MaskedDestination, string Purpose, string Status, int AttemptsLeft, string CreatedAt, string ExpiresAt, string? VerifiedAt);

/// <summary>
/// mfa-service: códigos de un solo uso (6 dígitos) por SMS o email. El código nunca se devuelve en la API:
/// llega al buzón de notification-service, como en la vida real. 5 minutos de validez, 3 intentos y bloqueo.
/// </summary>
public sealed class MfaService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "mfa-service", "C-identity", 6204, 2, "Identidad",
        "Segundo factor: código de 6 dígitos por SMS o email (vía notification-service), 5 min, 3 intentos. Solo se guarda el hash del código.",
        DependsOn: new[] { "notification-service", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS challenges (id TEXT PRIMARY KEY, user_id TEXT, channel TEXT, masked TEXT, purpose TEXT, code_hash TEXT, status TEXT, attempts_left INTEGER,
                     created_at TEXT, expires_ticks INTEGER, expires_at TEXT, verified_at TEXT)");
    }

    private static string Mask(string channel, string d) =>
        channel == "EMAIL" && d.Contains('@') ? d[0] + "***" + d[d.IndexOf('@')..] : d.Length > 4 ? new string('*', d.Length - 4) + d[^4..] : "****";

    private static Challenge? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string User, string Channel, string Masked, string Purpose, string Status, long Left, string Created, string Expires, string? Verified)>(
            "SELECT id, user_id, channel, masked, purpose, status, attempts_left, created_at, expires_at, verified_at FROM challenges WHERE id = @id", new { id });
        return r.Id is null ? null : new Challenge(r.Id, r.User, r.Channel, r.Masked, r.Purpose, r.Status, (int)r.Left, r.Created, r.Expires, r.Verified);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/mfa/challenges", async (HttpContext http, IClock clock, ChallengeRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (b.Channel is not ("SMS" or "EMAIL")) errors.Add(new FieldError("channel", "SMS o EMAIL"));
            if (string.IsNullOrWhiteSpace(b.Destination)) errors.Add(new FieldError("destination", "teléfono o email donde enviar el código"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Reto MFA no válido.", "Ejemplo: { \"channel\": \"SMS\", \"destination\": \"+46 70 123 45 67\" }", errors);
            var id = "mfa_" + Guid.NewGuid().ToString("N")[..16];
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var now = clock.UtcNow;
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO challenges VALUES (@id, @user, @Channel, @masked, @purpose, @hash, 'PENDING', 3, @created, @et, @ea, NULL)",
                new { id, user = p.Sub, b.Channel, masked = Mask(b.Channel!, b.Destination!), purpose = b.Purpose ?? "login", hash = Canonical.Sha256Hex(id + ":" + code),
                      created = Nordic.Iso(Nordic.ToStockholm(now)), et = now.AddMinutes(5).UtcTicks, ea = Nordic.Iso(Nordic.ToStockholm(now.AddMinutes(5))) });
            var sent = await Downstream.Post(http, "notification-service", "/api/v1/notifications", new
            {
                to = b.Destination, channel = b.Channel, templateKey = "mfa.code", language = b.Language ?? "sv", priority = "HIGH", reference = id,
                vars = new Dictionary<string, object> { ["code"] = code, ["minutes"] = 5 },
            }, forwardAuth: false);
            if (!sent.Ok)
            {
                db.Execute("UPDATE challenges SET status = 'FAILED' WHERE id = @id", new { id });
                return Problems.Of(http, 502, "CODE_NOT_SENT", "No se pudo enviar el código", "notification-service respondió " + sent.Status, null);
            }
            return Results.Created($"/api/v1/mfa/challenges/{id}", Load(db, id));
        }).WithTags("MFA").WithSummary("Enviar un código de un solo uso (llega al buzón de notification-service)").Produces<Challenge>(201);

        app.MapGet("/api/v1/mfa/challenges/{id}", (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var c = Load(db, id);
            if (c is null || c.UserId != p.Sub) return Problems.NotFound(http, "CHALLENGE_NOT_FOUND", $"No existe el reto {id}.", "Solo ves tus propios retos.");
            return Results.Ok(c);
        }).WithTags("MFA").WithSummary("Estado de un reto").Produces<Challenge>();

        app.MapPost("/api/v1/mfa/challenges/{id}/verify", (HttpContext http, IClock clock, string id, VerifyCodeRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var c = Load(db, id);
            if (c is null || c.UserId != p.Sub) return Problems.NotFound(http, "CHALLENGE_NOT_FOUND", $"No existe el reto {id}.", "Solo puedes verificar tus propios retos.");
            if (c.Status == "VERIFIED") return Problems.Conflict(http, "ALREADY_VERIFIED", "Este código ya se usó.", "Un código de un solo uso no vale dos veces: pide otro reto.");
            if (c.Status == "LOCKED") return Problems.Of(http, 423, "CHALLENGE_LOCKED", "Bloqueado", "Demasiados intentos fallidos.", "Pide un reto nuevo.");
            if (string.IsNullOrWhiteSpace(b.Code) || b.Code.Length != 6 || !b.Code.All(char.IsDigit))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "El código son 6 dígitos.", null, new[] { new FieldError("code", "6 dígitos") });
            var expires = db.ExecuteScalar<long>("SELECT expires_ticks FROM challenges WHERE id = @id", new { id });
            if (clock.UtcNow.UtcTicks > expires)
            {
                db.Execute("UPDATE challenges SET status = 'EXPIRED' WHERE id = @id", new { id });
                return Problems.Of(http, 410, "CHALLENGE_EXPIRED", "Caducado", "El código caducó a los 5 minutos.", "Pide un reto nuevo (o no adelantes tanto el reloj virtual).");
            }
            var hash = db.ExecuteScalar<string>("SELECT code_hash FROM challenges WHERE id = @id", new { id });
            if (Canonical.Sha256Hex(id + ":" + b.Code) != hash)
            {
                var left = c.AttemptsLeft - 1;
                db.Execute("UPDATE challenges SET attempts_left = @left, status = @s WHERE id = @id", new { left, s = left <= 0 ? "LOCKED" : "PENDING", id });
                return left <= 0
                    ? Problems.Of(http, 423, "CHALLENGE_LOCKED", "Bloqueado", "Tercer código incorrecto: el reto queda bloqueado.", "Pide un reto nuevo.")
                    : Problems.Unprocessable(http, "INVALID_CODE", $"Código incorrecto. Te quedan {left} intentos.", "El código está en GET notification-service /api/v1/mailbox?to=<destino>.");
            }
            db.Execute("UPDATE challenges SET status = 'VERIFIED', verified_at = @at, code_hash = 'usado' WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id });
            return Results.Ok(Load(db, id));
        }).WithTags("MFA").WithSummary("Comprobar el código").Produces<Challenge>();
    }
}
