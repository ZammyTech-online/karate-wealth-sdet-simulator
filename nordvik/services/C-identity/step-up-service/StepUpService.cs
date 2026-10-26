using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record StepUpRequest(string? Method, string? Reason, string? Channel, string? Destination);
public sealed record StepUpCode(string? Code);
public sealed record StepUp(string Id, string UserId, string Method, string Status, string? Reason, string? OrderRef, string? ChallengeId, string CreatedAt, string? CompletedAt,
    string? AccessToken, int? ExpiresIn, string? Acr, string? FailureReason);

/// <summary>
/// step-up-service: re-autenticación para operaciones de riesgo (retiradas grandes, cambios de IBAN, firmas).
/// Con NordID (clientes) o con un código MFA, emite un token nuevo con acr=high que dura 5 minutos.
/// </summary>
public sealed class StepUpService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "step-up-service", "C-identity", 6205, 2, "Identidad",
        "Re-autenticación (acr=high) con NordID o MFA. El token resultante dura 5 minutos y lo exigen payments-gateway y bank-account-verification.",
        DependsOn: new[] { "eid-broker", "mfa-service", "customer-registry", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS stepups (id TEXT PRIMARY KEY, user_id TEXT, method TEXT, status TEXT, reason TEXT, order_ref TEXT, challenge_id TEXT,
                     created_at TEXT, completed_at TEXT, failure TEXT, expected_pn_hash TEXT)");
    }

    private static StepUp? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, string? token = null, int? expiresIn = null)
    {
        var r = db.QuerySingleOrDefault<(string Id, string User, string Method, string Status, string? Reason, string? OrderRef, string? Ch, string Created, string? Completed, string? Failure)>(
            "SELECT id, user_id, method, status, reason, order_ref, challenge_id, created_at, completed_at, failure FROM stepups WHERE id = @id", new { id });
        return r.Id is null ? null : new StepUp(r.Id, r.User, r.Method, r.Status, r.Reason, r.OrderRef, r.Ch, r.Created, r.Completed, token, expiresIn, token is null ? null : "high", r.Failure);
    }

    private static string HighToken(Principal p, IClock clock)
    {
        var now = clock.UtcNow;
        return Auth.Issue(p with { Jti = Guid.NewGuid().ToString("N"), Iat = now.ToUnixTimeSeconds(), Exp = now.AddMinutes(5).ToUnixTimeSeconds(), Acr = "high" });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/step-up", async (HttpContext http, IClock clock, StepUpRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var method = b.Method ?? (p.Role == "Customer" ? "EID" : "MFA");
            if (method is not ("EID" or "MFA"))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "method: EID o MFA.", null, new[] { new FieldError("method", "EID o MFA") });
            var id = "stp_" + Guid.NewGuid().ToString("N")[..16];
            using var db = ctx.OpenDb();
            if (method == "EID")
            {
                if (p.CustomerId is null)
                    return Problems.Unprocessable(http, "EID_NOT_AVAILABLE_FOR_ROLE", $"El rol {p.Role} no tiene NordID asociado en Nordvik.", "Los empleados usan MFA: { \"method\": \"MFA\", \"channel\": \"SMS\", \"destination\": \"...\" }");
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{p.CustomerId}", forwardAuth: false);
                if (!c.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "customer-registry respondió " + c.Status, null);
                var pn = c.Str("nationalId")!;
                var o = await Downstream.Post(http, "eid-broker", "/api/v1/eid/auth", new { personalNumber = pn, purpose = "step-up:" + (b.Reason ?? "operación de riesgo") }, forwardAuth: false);
                if (o.Status == 409) return Problems.Conflict(http, "EID_ALREADY_IN_PROGRESS", "Ya hay una operación NordID en curso para este cliente.", "Espera a que caduque (3 min) o complétala.");
                if (!o.Ok) return Problems.Of(http, o.Status >= 500 ? 503 : 502, "EID_PROVIDER_UNAVAILABLE", "NordID no disponible", "eid-broker respondió " + o.Status, null);
                db.Execute("INSERT INTO stepups VALUES (@id, @user, 'EID', 'PENDING', @reason, @ref, NULL, @at, NULL, NULL, @h)",
                    new { id, user = p.Sub, reason = b.Reason, @ref = o.Str("orderRef"), at = Nordic.Iso(clock.LocalNow), h = Canonical.Sha256Hex(pn) });
            }
            else
            {
                var ch = await Downstream.Post(http, "mfa-service", "/api/v1/mfa/challenges", new { channel = b.Channel ?? "SMS", destination = b.Destination, purpose = "step-up" });
                if (ch.Status == 400) return Problems.BadRequest(http, "VALIDATION_FAILED", "Para MFA hacen falta channel y destination.", "Ejemplo: { \"method\": \"MFA\", \"channel\": \"SMS\", \"destination\": \"+46 70 123 45 67\" }");
                if (!ch.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "mfa-service respondió " + ch.Status, null);
                db.Execute("INSERT INTO stepups VALUES (@id, @user, 'MFA', 'PENDING', @reason, NULL, @ch, @at, NULL, NULL, NULL)",
                    new { id, user = p.Sub, reason = b.Reason, ch = ch.Str("id"), at = Nordic.Iso(clock.LocalNow) });
            }
            return Results.Created($"/api/v1/step-up/{id}", Load(db, id));
        }).WithTags("Step-up").WithSummary("Empezar una re-autenticación (EID para clientes, MFA para cualquiera)").Produces<StepUp>(201);

        app.MapGet("/api/v1/step-up/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var s = Load(db, id);
            if (s is null || s.UserId != p.Sub) return Problems.NotFound(http, "STEP_UP_NOT_FOUND", $"No existe el step-up {id}.", null);
            if (s.Status == "PENDING" && s.Method == "EID")
            {
                var o = await Downstream.Get(http, "eid-broker", $"/api/v1/eid/orders/{s.OrderRef}", forwardAuth: false);
                var st = o.Ok ? o.Str("status") : "FAILED";
                if (st == "COMPLETE")
                {
                    var pn = o.Json.GetProperty("user").GetProperty("personalNumber").GetString()!;
                    var expected = db.ExecuteScalar<string>("SELECT expected_pn_hash FROM stepups WHERE id = @id", new { id });
                    if (Canonical.Sha256Hex(pn) != expected)
                    {
                        db.Execute("UPDATE stepups SET status = 'FAILED', failure = 'IDENTITY_MISMATCH' WHERE id = @id", new { id });
                        return Results.Ok(Load(db, id));
                    }
                    db.Execute("UPDATE stepups SET status = 'COMPLETED', completed_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id });
                    return Results.Ok(Load(db, id, HighToken(p, clock), 300));
                }
                if (st is "FAILED" or "EXPIRED" or "CANCELLED")
                    db.Execute("UPDATE stepups SET status = 'FAILED', failure = @f WHERE id = @id", new { f = o.Ok ? o.Str("failureReason") ?? st : "EID_ERROR", id });
            }
            return Results.Ok(Load(db, id));
        }).WithTags("Step-up").WithSummary("Estado; cuando está COMPLETED trae accessToken con acr=high (5 min)").Produces<StepUp>();

        app.MapPost("/api/v1/step-up/{id}/verify", async (HttpContext http, IClock clock, string id, StepUpCode b) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var s = Load(db, id);
            if (s is null || s.UserId != p.Sub) return Problems.NotFound(http, "STEP_UP_NOT_FOUND", $"No existe el step-up {id}.", null);
            if (s.Method != "MFA") return Problems.Conflict(http, "WRONG_METHOD", "Este step-up es con NordID: consulta su estado con GET.", null);
            if (s.Status != "PENDING") return Problems.Conflict(http, "STEP_UP_FINISHED", $"El step-up ya está {s.Status}.", null);
            var v = await Downstream.Post(http, "mfa-service", $"/api/v1/mfa/challenges/{s.ChallengeId}/verify", new { code = b.Code });
            if (!v.Ok)
            {
                if (v.Status is 410 or 423) db.Execute("UPDATE stepups SET status = 'FAILED', failure = @f WHERE id = @id", new { f = v.Str("code"), id });
                return Results.Content(v.Raw, "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, v.Status);
            }
            db.Execute("UPDATE stepups SET status = 'COMPLETED', completed_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id });
            return Results.Ok(Load(db, id, HighToken(p, clock), 300));
        }).WithTags("Step-up").WithSummary("Completar un step-up MFA con el código recibido").Produces<StepUp>();
    }
}
