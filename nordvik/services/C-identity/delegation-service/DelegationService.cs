using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record DelegationRequest(string? GrantorId, string? AttorneyId, string? Type, string[]? Scopes, string? ValidFrom, string? ValidTo, string? DocumentRef);

/// <summary>
/// delegation-service: apoderados. Un cliente (grantor) autoriza a otro (attorney) a actuar por él con unos permisos (READ, TRADE, PAYMENTS)
/// y unas fechas. Una tutela (GUARDIANSHIP) exige la resolución judicial (documentRef). Toda delegación nace PENDING_VERIFICATION y la
/// verifica Operaciones o Cumplimiento, nunca quien la creó (cuatro ojos). El estado efectivo depende del reloj: NOT_YET_VALID, ACTIVE, EXPIRED.
/// </summary>
public sealed class DelegationService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "delegation-service", "C-identity", 6207, 4, "Identidad",
        "Apoderados y tutelas: permisos por ámbito (READ, TRADE, PAYMENTS) con fechas, verificación a cuatro ojos, revocación y comprobación de si alguien puede actuar por otro.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "delegation.created", "delegation.verified", "delegation.revoked" });

    public static readonly string[] Scopes = { "READ", "TRADE", "PAYMENTS" };

    public static string Effective(string stored, DateOnly from, DateOnly to, DateOnly today) =>
        stored != "ACTIVE" ? stored : today < from ? "NOT_YET_VALID" : today > to ? "EXPIRED" : "ACTIVE";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS delegations (id TEXT PRIMARY KEY, grantor_id TEXT, attorney_id TEXT, type TEXT, scopes TEXT, valid_from TEXT, valid_to TEXT, document_ref TEXT,
                        status TEXT, created_by TEXT, verified_by TEXT, created_at TEXT, updated_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string G, string A, string Type, string Scopes, string From, string To, string? Doc, string Status, string By, string? Ver, string At, string Up)>(
            "SELECT id, grantor_id, attorney_id, type, scopes, valid_from, valid_to, document_ref, status, created_by, verified_by, created_at, updated_at FROM delegations WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, grantorId = r.G, attorneyId = r.A, type = r.Type, scopes = JsonSerializer.Deserialize<string[]>(r.Scopes), validFrom = r.From, validTo = r.To, documentRef = r.Doc,
            status = Effective(r.Status, DateOnly.Parse(r.From), DateOnly.Parse(r.To), clock.Today), createdBy = r.By, verifiedBy = r.Ver, createdAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        IResult Move(HttpContext http, Microsoft.Data.Sqlite.SqliteConnection db, string id, string to, string by, string topic)
        {
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE delegations SET status = @to, verified_by = CASE WHEN @to = 'ACTIVE' THEN @by ELSE verified_by END, updated_at = @at WHERE id = @id", new { id, to, by, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, topic, id, new { delegationId = id, status = to, by }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id, clock));
        }

        app.MapPost("/api/v1/delegations", async (HttpContext http, DelegationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Admin" }) is { } denied) return denied;
            var type = (b.Type ?? "POWER_OF_ATTORNEY").ToUpperInvariant();
            var scopes = (b.Scopes ?? Array.Empty<string>()).Select(s => s.ToUpperInvariant()).Distinct().ToArray();
            var errors = new List<FieldError>();
            if (b.GrantorId is null) errors.Add(new FieldError("grantorId", "obligatorio"));
            if (b.AttorneyId is null) errors.Add(new FieldError("attorneyId", "obligatorio"));
            if (type is not ("POWER_OF_ATTORNEY" or "GUARDIANSHIP")) errors.Add(new FieldError("type", "POWER_OF_ATTORNEY o GUARDIANSHIP"));
            if (scopes.Length == 0 || scopes.Any(s => !Scopes.Contains(s))) errors.Add(new FieldError("scopes", "uno o más de " + string.Join(", ", Scopes)));
            if (!Money.TryParseDate(b.ValidFrom, out var from)) errors.Add(new FieldError("validFrom", "AAAA-MM-DD"));
            if (!Money.TryParseDate(b.ValidTo, out var to)) errors.Add(new FieldError("validTo", "AAAA-MM-DD"));
            else if (to <= from) errors.Add(new FieldError("validTo", "posterior a validFrom"));
            else if (to > from.AddYears(10)) errors.Add(new FieldError("validTo", "como mucho 10 años"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Delegación no válida.", null, errors);
            if (b.GrantorId == b.AttorneyId) return Problems.Unprocessable(http, "SELF_DELEGATION", "Nadie puede ser su propio apoderado.", null);
            if (type == "GUARDIANSHIP" && string.IsNullOrWhiteSpace(b.DocumentRef)) return Problems.Unprocessable(http, "DOCUMENT_REQUIRED", "Una tutela necesita la resolución judicial (documentRef).", null);
            if (await CustomerGuard.Check(http, p, "customer:update", b.GrantorId!) is { } g) return g;
            var att = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.AttorneyId}", forwardAuth: false);
            if (!att.Ok) return Problems.Unprocessable(http, "ATTORNEY_NOT_CUSTOMER", "El apoderado tiene que estar identificado como cliente.", null);
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT id FROM delegations WHERE grantor_id = @GrantorId AND attorney_id = @AttorneyId AND status IN ('PENDING_VERIFICATION','ACTIVE') AND valid_to >= @today",
                    new { b.GrantorId, b.AttorneyId, today = Money.Date(clock.Today) }) is { } existing)
                return Problems.Conflict(http, "DELEGATION_EXISTS", $"Ya hay una delegación viva entre esas personas: {existing}.", "Revócala antes de crear otra.");
            var id = "dlg_" + Guid.NewGuid().ToString("N")[..12];
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO delegations VALUES (@id, @GrantorId, @AttorneyId, @type, @s, @f, @t, @DocumentRef, 'PENDING_VERIFICATION', @by, NULL, @at, @at)",
                    new { id, b.GrantorId, b.AttorneyId, type, s = JsonSerializer.Serialize(scopes), f = Money.Date(from), t = Money.Date(to), b.DocumentRef, by = p.Sub, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "delegation.created", id, new { delegationId = id, grantorId = b.GrantorId, attorneyId = b.AttorneyId, type }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/delegations/{id}", Load(db, id, clock));
        }).WithTags("Delegaciones").WithSummary("Crear una delegación (queda pendiente de verificar)");

        app.MapGet("/api/v1/delegations/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? grantor;
            using (var db0 = ctx.OpenDb()) grantor = db0.ExecuteScalar<string?>("SELECT grantor_id FROM delegations WHERE id = @id", new { id });
            if (grantor is null) return Problems.NotFound(http, "DELEGATION_NOT_FOUND", $"No existe la delegación {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", grantor) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Delegaciones").WithSummary("Una delegación con su estado efectivo");

        app.MapPost("/api/v1/delegations/{id}/verify", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Compliance", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Status, string By)>("SELECT status, created_by FROM delegations WHERE id = @id", new { id });
            if (r.Status is null) return Problems.NotFound(http, "DELEGATION_NOT_FOUND", $"No existe la delegación {id}.", null);
            if (r.Status != "PENDING_VERIFICATION") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Solo se verifica una delegación pendiente (está {r.Status}).", null);
            if (r.By == p.Sub) return Problems.Conflict(http, "FOUR_EYES_REQUIRED", "Quien crea la delegación no puede verificarla.", null);
            return Move(http, db, id, "ACTIVE", p.Sub, "delegation.verified");
        }).WithTags("Delegaciones").WithSummary("Verificar (cuatro ojos: no puede hacerlo quien la creó)");

        app.MapPost("/api/v1/delegations/{id}/revoke", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? grantor, status;
            using (var db0 = ctx.OpenDb()) (grantor, status) = db0.QuerySingleOrDefault<(string?, string?)>("SELECT grantor_id, status FROM delegations WHERE id = @id", new { id });
            if (grantor is null) return Problems.NotFound(http, "DELEGATION_NOT_FOUND", $"No existe la delegación {id}.", null);
            if (await CustomerGuard.Check(http, p, "customer:update", grantor) is { } g) return g;
            if (status is not ("PENDING_VERIFICATION" or "ACTIVE")) return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"La delegación ya está {status}.", null);
            using var db = ctx.OpenDb();
            return Move(http, db, id, "REVOKED", p.Sub, "delegation.revoked");
        }).WithTags("Delegaciones").WithSummary("Revocar (el poderdante, su asesor u Operaciones)");

        app.MapGet("/api/v1/customers/{customerId}/delegations", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var granted = db.Query<string>("SELECT id FROM delegations WHERE grantor_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i, clock)).ToList();
            var received = db.Query<string>("SELECT id FROM delegations WHERE attorney_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i, clock)).ToList();
            return Results.Ok(new { customerId, granted, received });
        }).WithTags("Delegaciones").WithSummary("Delegaciones que el cliente ha dado y las que tiene");

        app.MapGet("/api/v1/delegations/check", (string? attorneyId, string? grantorId, string? scope) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<(string Id, string Scopes, string Status, string From, string To)>("SELECT id, scopes, status, valid_from, valid_to FROM delegations WHERE attorney_id = @attorneyId AND grantor_id = @grantorId",
                new { attorneyId, grantorId }).ToList();
            var s = (scope ?? "READ").ToUpperInvariant();
            foreach (var r in rows)
            {
                var eff = Effective(r.Status, DateOnly.Parse(r.From), DateOnly.Parse(r.To), clock.Today);
                if (eff == "ACTIVE" && JsonSerializer.Deserialize<string[]>(r.Scopes)!.Contains(s)) return Results.Ok(new { allowed = true, delegationId = r.Id, scope = s, reason = "ACTIVE_DELEGATION" });
            }
            var why = rows.Count == 0 ? "NO_DELEGATION" : rows.Select(r => Effective(r.Status, DateOnly.Parse(r.From), DateOnly.Parse(r.To), clock.Today)).Any(e => e == "ACTIVE") ? "SCOPE_NOT_GRANTED"
                : rows.Select(r => Effective(r.Status, DateOnly.Parse(r.From), DateOnly.Parse(r.To), clock.Today)).First();
            return Results.Ok(new { allowed = false, delegationId = (string?)null, scope = s, reason = why });
        }).WithTags("Delegaciones").WithSummary("¿Puede el apoderado hacer esto por el poderdante? (con el motivo)");
    }
}
