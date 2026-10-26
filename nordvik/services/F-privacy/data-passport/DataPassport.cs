using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record PassportRequest(string? Token, string? Purpose, string? Audience, int? MaxReads, int? TtlSeconds);
public sealed record Passport(string Id, string Token, string Purpose, string Audience, int MaxReads, int ReadsLeft, string Status, string IssuedBy, string IssuedAt, string ExpiresAt, string? LastReadAt);

/// <summary>
/// data-passport: el permiso para leer UN dato personal, para UN propósito, por UN servicio, N veces y durante un tiempo.
/// El servicio que lo usa se identifica con la cabecera X-Caller-Service; si no es el destinatario del pasaporte, 403.
/// Agotado o caducado, responde 410: el dato ya no está a su alcance.
/// </summary>
public sealed class DataPassport : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "data-passport", "F-privacy", 6503, 3, "Privacidad y protección de datos",
        "Pasaportes del dato: lectura de un dato personal limitada por propósito, destinatario, número de lecturas y caducidad (reloj virtual).",
        DependsOn: new[] { "pii-vault", "clock-service" }, Emits: new[] { "passport.issued", "passport.read", "passport.revoked" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS passports (id TEXT PRIMARY KEY, token TEXT, purpose TEXT, audience TEXT, max_reads INTEGER, reads_left INTEGER, status TEXT,
                        issued_by TEXT, issued_at TEXT, expires_ticks INTEGER, expires_at TEXT, last_read_at TEXT)");
    }

    private static Passport? Load(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Token, string Purpose, string Aud, long Max, long Left, string Status, string By, string At, long Ticks, string Exp, string? Last)>(
            "SELECT id, token, purpose, audience, max_reads, reads_left, status, issued_by, issued_at, expires_ticks, expires_at, last_read_at FROM passports WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var status = r.Status == "ACTIVE" && clock.UtcNow.UtcTicks >= r.Ticks ? "EXPIRED" : r.Status;
        if (status != r.Status) db.Execute("UPDATE passports SET status = @status WHERE id = @id", new { status, id });
        return new Passport(r.Id, r.Token, r.Purpose, r.Aud, (int)r.Max, (int)r.Left, status, r.By, r.At, r.Exp, r.Last);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] issuers = { "Operations", "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/passports", async (HttpContext http, PassportRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: issuers) is { } denied) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Token)) errors.Add(new FieldError("token", "obligatorio: un token de pii-vault"));
            if (string.IsNullOrWhiteSpace(b.Purpose)) errors.Add(new FieldError("purpose", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.Audience)) errors.Add(new FieldError("audience", "obligatorio: el servicio que leerá el dato"));
            else if (!ServiceDirectory.Knows(b.Audience)) errors.Add(new FieldError("audience", $"no existe el servicio {b.Audience}"));
            if (b.MaxReads is < 1 or > 10) errors.Add(new FieldError("maxReads", "entre 1 y 10"));
            if (b.TtlSeconds is < 10 or > 3600) errors.Add(new FieldError("ttlSeconds", "entre 10 y 3600"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Pasaporte no válido.",
                "Ejemplo: { \"token\": \"tok_iban_...\", \"purpose\": \"PAYMENT_EXECUTION\", \"audience\": \"payments-gateway\", \"maxReads\": 1, \"ttlSeconds\": 120 }", errors);
            var t = await Downstream.Get(http, "pii-vault", $"/api/v1/tokens/{b.Token}");
            if (t.Status == 404) return Problems.Unprocessable(http, "TOKEN_NOT_FOUND", $"pii-vault no conoce el token {b.Token}.", null);
            if (!t.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "pii-vault respondió " + t.Status, null);
            if (t.Str("status") == "ERASED") return Problems.Of(http, 410, "PII_ERASED", "Dato borrado", $"El dato del token {b.Token} ya se borró.", null);
            var id = "pas_" + Guid.NewGuid().ToString("N")[..16];
            var max = b.MaxReads ?? 1;
            var exp = clock.UtcNow.AddSeconds(b.TtlSeconds ?? 300);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO passports VALUES (@id, @Token, @purpose, @Audience, @max, @max, 'ACTIVE', @by, @at, @ticks, @exp, NULL)",
                new { id, b.Token, purpose = b.Purpose!.ToUpperInvariant(), b.Audience, max, by = p.Sub, at = Nordic.Iso(clock.LocalNow), ticks = exp.UtcTicks, exp = Nordic.Iso(Nordic.ToStockholm(exp)) }, tx);
            Outbox.Add(db, http, clock, "passport.issued", id, new { passportId = id, token = b.Token, purpose = b.Purpose, audience = b.Audience, maxReads = max }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/passports/{id}", Load(db, clock, id));
        }).WithTags("Pasaportes").WithSummary("Emitir un pasaporte: quién, para qué, cuántas veces y hasta cuándo").Produces<Passport>(201);

        app.MapGet("/api/v1/passports/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: issuers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, clock, id) is { } x ? Results.Ok(x) : Problems.NotFound(http, "PASSPORT_NOT_FOUND", $"No existe el pasaporte {id}.", null);
        }).WithTags("Pasaportes").WithSummary("Estado del pasaporte (nunca el dato)").Produces<Passport>();

        app.MapGet("/api/v1/passports", (HttpContext http, string? token, string? status) =>
        {
            if (Auth.Require(http, clock, out _, roles: issuers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM passports WHERE (@token IS NULL OR token = @token) ORDER BY issued_at, id", new { token })
                .Select(i => Load(db, clock, i)!).Where(x => status is null || x.Status == status.ToUpperInvariant()).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Pasaportes").WithSummary("Pasaportes de un token");

        app.MapPost("/api/v1/passports/{id}/read", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var caller = http.Request.Headers["X-Caller-Service"].FirstOrDefault();
            using var db = ctx.OpenDb();
            var x = Load(db, clock, id);
            if (x is null) return Problems.NotFound(http, "PASSPORT_NOT_FOUND", $"No existe el pasaporte {id}.", null);
            if (string.IsNullOrEmpty(caller))
                return Problems.BadRequest(http, "CALLER_REQUIRED", "Falta la cabecera X-Caller-Service.", "El servicio que lee el dato se identifica: X-Caller-Service: " + x.Audience);
            if (caller != x.Audience)
                return Problems.Of(http, 403, "AUDIENCE_MISMATCH", "Prohibido", $"Este pasaporte es para {x.Audience}, no para {caller}.", null);
            if (x.Status == "EXPIRED") return Problems.Of(http, 410, "PASSPORT_EXPIRED", "Pasaporte caducado", $"El pasaporte caducó el {x.ExpiresAt}.", "Los pasaportes caducan con el reloj virtual. Pide uno nuevo.");
            if (x.Status == "EXHAUSTED") return Problems.Of(http, 410, "PASSPORT_EXHAUSTED", "Pasaporte agotado", $"El pasaporte ya se usó {x.MaxReads} vez/veces.", "Un pasaporte de un solo uso no se puede volver a leer: es lo que se prueba.");
            if (x.Status == "REVOKED") return Problems.Of(http, 410, "PASSPORT_REVOKED", "Pasaporte revocado", "El pasaporte se revocó.", null);
            var n = db.Execute("UPDATE passports SET reads_left = reads_left - 1, last_read_at = @at, status = CASE WHEN reads_left - 1 = 0 THEN 'EXHAUSTED' ELSE status END WHERE id = @id AND reads_left > 0",
                new { id, at = Nordic.Iso(clock.LocalNow) });
            if (n == 0) return Problems.Of(http, 410, "PASSPORT_EXHAUSTED", "Pasaporte agotado", "El pasaporte ya no tiene lecturas.", null);
            var v = await Downstream.Send(http, "pii-vault", HttpMethod.Post, $"/api/v1/internal/tokens/{x.Token}/detokenize", new { purpose = x.Purpose }, forwardAuth: false,
                headers: new Dictionary<string, string> { ["X-Caller"] = caller });
            if (v.Status == 410) return Problems.Of(http, 410, "PII_ERASED", "Dato borrado", "El dato se borró después de emitir el pasaporte.", null);
            if (!v.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "pii-vault respondió " + v.Status, null);
            var readNo = x.MaxReads - x.ReadsLeft + 1;
            Outbox.Add(db, http, clock, "passport.read", id, new { passportId = id, token = x.Token, audience = caller, purpose = x.Purpose, readNo });
            return Results.Ok(new { passportId = id, token = x.Token, value = v.Str("value"), readNo, readsLeft = x.ReadsLeft - 1 });
        }).WithTags("Pasaportes").WithSummary("Usar el pasaporte (cabecera X-Caller-Service = destinatario): devuelve el dato y gasta una lectura");

        app.MapPost("/api/v1/passports/{id}/revoke", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: issuers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var x = Load(db, clock, id);
            if (x is null) return Problems.NotFound(http, "PASSPORT_NOT_FOUND", $"No existe el pasaporte {id}.", null);
            if (x.Status != "ACTIVE") return Problems.Conflict(http, "PASSPORT_NOT_ACTIVE", $"El pasaporte está {x.Status}.", null);
            db.Execute("UPDATE passports SET status = 'REVOKED' WHERE id = @id", new { id });
            Outbox.Add(db, http, clock, "passport.revoked", id, new { passportId = id, token = x.Token });
            return Results.Ok(Load(db, clock, id));
        }).WithTags("Pasaportes").WithSummary("Revocar un pasaporte antes de que caduque");
    }
}
