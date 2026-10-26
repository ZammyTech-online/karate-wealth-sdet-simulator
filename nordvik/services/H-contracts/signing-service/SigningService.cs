using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record StartSigningRequest(string? ContractId, string? Party, string? PersonalNumber, string? ContentHash, int? TemplateVersion, string? Title, string? Signer);
public sealed record SigningSession(string Id, string ContractId, string Party, string Status, string? OrderRef, string? HintCode, string? FailureReason, string SignedHash,
    int TemplateVersion, string CreatedAt, string ExpiresAt, string? CompletedAt);

/// <summary>
/// signing-service: orquesta la firma de cada parte de un contrato con NordID (vía eid-broker). Lo que se firma es el título más
/// el contentHash del contrato; cuando NordID completa, registra la firma en contract-service. Las sesiones caducan a los 3 minutos.
/// </summary>
public sealed class SigningService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "signing-service", "H-contracts", 6703, 2, "Contratos y documentos",
        "Sesiones de firma por parte: NordID firma el contentHash; al completarse se registra en contract-service (que comprueba hash y versión de plantilla).",
        DependsOn: new[] { "eid-broker", "contract-service", "clock-service" }, Emits: new[] { "signing.completed", "signing.failed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, contract_id TEXT, party TEXT, status TEXT, order_ref TEXT, hint TEXT, failure TEXT, signed_hash TEXT, template_version INTEGER,
                     signer TEXT, created_at TEXT, expires_at TEXT, completed_at TEXT)");
    }

    private static SigningSession? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string C, string Party, string Status, string? Ref, string? Hint, string? Fail, string Hash, long Tv, string Created, string Expires, string? Done)>(
            "SELECT id, contract_id, party, status, order_ref, hint, failure, signed_hash, template_version, created_at, expires_at, completed_at FROM sessions WHERE id = @id", new { id });
        return r.Id is null ? null : new SigningSession(r.Id, r.C, r.Party, r.Status, r.Ref, r.Hint, r.Fail, r.Hash, (int)r.Tv, r.Created, r.Expires, r.Done);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/signing-sessions", async (HttpContext http, StartSigningRequest b) =>
        {
            if (b.ContractId is null || b.Party is null || b.PersonalNumber is null || b.ContentHash is null || b.TemplateVersion is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Faltan contractId, party, personalNumber, contentHash o templateVersion.", "Normalmente lo llama contract-service.");
            var o = await Downstream.Post(http, "eid-broker", "/api/v1/eid/sign", new
            {
                personalNumber = b.PersonalNumber, visibleText = $"Firmo el contrato \"{b.Title}\" como {b.Party}.", dataHash = b.ContentHash, purpose = "contract:" + b.ContractId,
            }, forwardAuth: false);
            if (o.Status == 409) return Problems.Conflict(http, "EID_ALREADY_IN_PROGRESS", "Ya hay una operación NordID en curso para esta persona.", null);
            if (!o.Ok) return Problems.Of(http, 503, "EID_PROVIDER_UNAVAILABLE", "NordID no disponible", "eid-broker respondió " + o.Status, null);
            var id = "sig_" + Guid.NewGuid().ToString("N")[..16];
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO sessions VALUES (@id, @ContractId, @Party, 'PENDING', @ref, NULL, NULL, @ContentHash, @TemplateVersion, @Signer, @at, @exp, NULL)",
                new { id, b.ContractId, b.Party, @ref = o.Str("orderRef"), b.ContentHash, b.TemplateVersion, b.Signer, at = Nordic.Iso(clock.LocalNow), exp = o.Str("expiresAt") });
            return Results.Created($"/api/v1/signing-sessions/{id}", Load(db, id));
        }).WithTags("Firmas").WithSummary("Empezar la firma de una parte (lo usa contract-service)").Produces<SigningSession>(201);

        app.MapGet("/api/v1/signing-sessions/{id}", async (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var s = Load(db, id);
            if (s is null) return Problems.NotFound(http, "SIGNING_SESSION_NOT_FOUND", $"No existe la sesión {id}.", null);
            if (s.Status != "PENDING") return Results.Ok(s);
            var o = await Downstream.Get(http, "eid-broker", $"/api/v1/eid/orders/{s.OrderRef}", forwardAuth: false);
            var st = o.Ok ? o.Str("status") : "FAILED";
            if (st == "PENDING") { db.Execute("UPDATE sessions SET hint = @h WHERE id = @id", new { h = o.Str("hintCode"), id }); return Results.Ok(Load(db, id)); }
            if (st != "COMPLETE")
            {
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE sessions SET status = @s, failure = @f, hint = NULL WHERE id = @id", new { s = st == "EXPIRED" ? "EXPIRED" : "FAILED", f = o.Ok ? o.Str("failureReason") ?? st : "EID_ERROR", id }, tx);
                Outbox.Add(db, http, clock, "signing.failed", s.ContractId, new { sessionId = id, contractId = s.ContractId, party = s.Party, reason = o.Ok ? o.Str("failureReason") : "EID_ERROR" }, tx);
                tx.Commit();
                return Results.Ok(Load(db, id));
            }
            var signer = db.ExecuteScalar<string>("SELECT signer FROM sessions WHERE id = @id", new { id });
            var reg = await Downstream.Post(http, "contract-service", $"/api/v2/internal/contracts/{s.ContractId}/signatures", new
            {
                party = s.Party, sessionId = id, signedHash = s.SignedHash, templateVersion = s.TemplateVersion, signature = o.Str("signature"), signedAt = Nordic.Iso(clock.LocalNow), signer,
            }, forwardAuth: false);
            using (var tx = db.BeginTransaction())
            {
                var ok = reg.Ok && reg.Str("status") != "INVALID_SIGNATURE";
                db.Execute("UPDATE sessions SET status = @s, failure = @f, hint = NULL, completed_at = @at WHERE id = @id",
                    new { s = ok ? "COMPLETED" : "REJECTED", f = ok ? null : reg.Ok ? "INVALID_SIGNATURE" : reg.Str("code"), at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, http, clock, ok ? "signing.completed" : "signing.failed", s.ContractId, new { sessionId = id, contractId = s.ContractId, party = s.Party, contractStatus = reg.Str("status") }, tx);
                tx.Commit();
            }
            return Results.Ok(Load(db, id));
        }).WithTags("Firmas").WithSummary("Estado de una sesión (consulta NordID y, si completó, registra la firma)").Produces<SigningSession>();
    }
}
