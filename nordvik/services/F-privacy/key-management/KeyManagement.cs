using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateKeyRequest(string? Subject, string? Purpose);
public sealed record ResolveKeyRequest(string? Subject, string? Purpose);
public sealed record ShredRequest(string? Reason);
public sealed record CryptRequest(string? Plaintext, string? Ciphertext);
public sealed record KeyInfo(string Id, string Subject, string Purpose, int CurrentVersion, string Status, string CreatedAt, string? RotatedAt, string? ShreddedAt, string? ShredReason, int Versions);
public sealed record KeyMaterial(string KeyId, int Version, string Material, string Algorithm);

/// <summary>
/// key-management: una clave AES-256 por sujeto (un cliente, un documento, una llamada). Las claves rotan (las versiones viejas
/// siguen descifrando) y se pueden triturar (crypto-shredding): se borra el material de TODAS las versiones y lo cifrado con ellas
/// deja de poder leerse para siempre, sin tener que buscar cada copia del dato.
/// </summary>
public sealed class KeyManagement : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "key-management", "F-privacy", 6501, 3, "Privacidad y protección de datos",
        "Claves AES-256 por sujeto con versiones, rotación, cifrado/descifrado y crypto-shredding (triturar la clave hace ilegible el dato en todas partes).",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "key.created", "key.rotated", "key.shredded" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS keys (id TEXT PRIMARY KEY, subject TEXT, purpose TEXT, current_version INTEGER, status TEXT, created_at TEXT, rotated_at TEXT, shredded_at TEXT, shred_reason TEXT);
                     CREATE UNIQUE INDEX IF NOT EXISTS ux_keys_subject ON keys(subject, purpose);
                     CREATE TABLE IF NOT EXISTS key_versions (key_id TEXT, version INTEGER, material BLOB, created_at TEXT, PRIMARY KEY (key_id, version))");
    }

    private static KeyInfo? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Subject, string Purpose, long Ver, string Status, string Created, string? Rotated, string? Shredded, string? Reason)>(
            "SELECT id, subject, purpose, current_version, status, created_at, rotated_at, shredded_at, shred_reason FROM keys WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var versions = db.ExecuteScalar<long>("SELECT COUNT(*) FROM key_versions WHERE key_id = @id", new { id });
        return new KeyInfo(r.Id, r.Subject, r.Purpose, (int)r.Ver, r.Status, r.Created, r.Rotated, r.Shredded, r.Reason, (int)versions);
    }

    private static string Create(Microsoft.Data.Sqlite.SqliteConnection db, HttpContext? http, IClock clock, string subject, string purpose)
    {
        var id = "key_" + Guid.NewGuid().ToString("N")[..16];
        var now = Nordic.Iso(clock.LocalNow);
        using var tx = db.BeginTransaction();
        db.Execute("INSERT INTO keys VALUES (@id, @subject, @purpose, 1, 'ACTIVE', @now, NULL, NULL, NULL)", new { id, subject, purpose, now }, tx);
        db.Execute("INSERT INTO key_versions VALUES (@id, 1, @m, @now)", new { id, m = RandomNumberGenerator.GetBytes(32), now }, tx);
        Outbox.Add(db, http, clock, "key.created", id, new { keyId = id, subject, purpose }, tx);
        tx.Commit();
        return id;
    }

    /// <summary>Formato del texto cifrado: nvk1.{keyId}.{version}.{base64(nonce|tag|cipher)}</summary>
    public static string Seal(string keyId, int version, byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return $"nvk1.{keyId}.{version}.{Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray())}";
    }

    public static byte[] Open(byte[] key, string payloadB64)
    {
        var all = Convert.FromBase64String(payloadB64);
        var plain = new byte[all.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(all[..12], all[28..], all[12..28], plain);
        return plain;
    }

    /// <summary>Cuerpo opcional: sin cuerpo (o sin Content-Type) la petición también vale.</summary>
    public static async Task<T?> OptionalBody<T>(HttpContext http) where T : class
    {
        if (http.Request.ContentLength is 0 || (http.Request.ContentLength is null && !http.Request.Headers.ContainsKey("Transfer-Encoding"))) return null;
        try { return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(http.Request.Body, Canonical.Web); } catch (System.Text.Json.JsonException) { return null; }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/keys", (HttpContext http, CreateKeyRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Subject))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta subject.", "Ejemplo: { \"subject\": \"customer:c000...\", \"purpose\": \"PII\" }", new[] { new FieldError("subject", "obligatorio") });
            var purpose = (b.Purpose ?? "GENERAL").ToUpperInvariant();
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT id FROM keys WHERE subject = @s AND purpose = @purpose", new { s = b.Subject, purpose }) is { } existing)
                return Problems.Conflict(http, "KEY_ALREADY_EXISTS", $"Ya hay una clave para {b.Subject} ({purpose}): {existing}.", "Usa esa clave o rótala con POST /api/v1/keys/{id}/rotate.");
            var id = Create(db, http, clock, b.Subject, purpose);
            return Results.Created($"/api/v1/keys/{id}", Load(db, id));
        }).WithTags("Claves").WithSummary("Crear la clave de un sujeto (Compliance, Admin o servicio)").Produces<KeyInfo>(201);

        app.MapGet("/api/v1/keys", (HttpContext http, string? subject, string? status) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM keys WHERE (@subject IS NULL OR subject = @subject) AND (@status IS NULL OR status = @status) ORDER BY created_at, id", new { subject, status });
            var items = ids.Select(i => Load(db, i)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Claves").WithSummary("Listar claves (sin material)");

        app.MapGet("/api/v1/keys/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } k ? Results.Ok(k) : Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {id}.", null);
        }).WithTags("Claves").WithSummary("Metadatos de una clave (el material nunca sale por la API pública)").Produces<KeyInfo>();

        app.MapPost("/api/v1/keys/{id}/rotate", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var k = Load(db, id);
            if (k is null) return Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {id}.", null);
            if (k.Status == "SHREDDED") return Problems.Of(http, 410, "KEY_SHREDDED", "Clave triturada", $"La clave {id} se trituró el {k.ShreddedAt}.", "Una clave triturada no vuelve: crea una nueva para datos nuevos.");
            var now = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO key_versions VALUES (@id, @v, @m, @now)", new { id, v = k.CurrentVersion + 1, m = RandomNumberGenerator.GetBytes(32), now }, tx);
            db.Execute("UPDATE keys SET current_version = current_version + 1, rotated_at = @now WHERE id = @id", new { id, now }, tx);
            Outbox.Add(db, http, clock, "key.rotated", id, new { keyId = id, version = k.CurrentVersion + 1 }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Claves").WithSummary("Rotar: nueva versión para cifrar; las anteriores siguen descifrando").Produces<KeyInfo>();

        app.MapPost("/api/v1/keys/{id}/shred", async (HttpContext http, string id) =>
        {
            var b = await OptionalBody<ShredRequest>(http);
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var k = Load(db, id);
            if (k is null) return Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {id}.", null);
            if (k.Status == "SHREDDED") return Problems.Conflict(http, "KEY_ALREADY_SHREDDED", $"La clave {id} ya se trituró el {k.ShreddedAt}.", null);
            var now = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE key_versions SET material = NULL WHERE key_id = @id", new { id }, tx);
            db.Execute("UPDATE keys SET status = 'SHREDDED', shredded_at = @now, shred_reason = @r WHERE id = @id", new { id, now, r = b?.Reason ?? "sin motivo" }, tx);
            Outbox.Add(db, http, clock, "key.shredded", id, new { keyId = id, subject = k.Subject, reason = b?.Reason }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Claves").WithSummary("Triturar (crypto-shredding): borra el material de todas las versiones, sin vuelta atrás").Produces<KeyInfo>();

        app.MapPost("/api/v1/keys/{id}/encrypt", (HttpContext http, string id, CryptRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            if (b.Plaintext is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta plaintext.", null, new[] { new FieldError("plaintext", "obligatorio") });
            using var db = ctx.OpenDb();
            var k = Load(db, id);
            if (k is null) return Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {id}.", null);
            if (k.Status == "SHREDDED") return Problems.Of(http, 410, "KEY_SHREDDED", "Clave triturada", $"La clave {id} se trituró el {k.ShreddedAt}.", null);
            var m = db.ExecuteScalar<byte[]>("SELECT material FROM key_versions WHERE key_id = @id AND version = @v", new { id, v = k.CurrentVersion });
            return Results.Ok(new { keyId = id, version = k.CurrentVersion, ciphertext = Seal(id, k.CurrentVersion, m, Encoding.UTF8.GetBytes(b.Plaintext)) });
        }).WithTags("Cifrado").WithSummary("Cifrar un texto con la versión actual (AES-256-GCM)");

        app.MapPost("/api/v1/decrypt", (HttpContext http, CryptRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            var parts = (b.Ciphertext ?? "").Split('.');
            if (parts.Length != 4 || parts[0] != "nvk1" || !int.TryParse(parts[2], out var v))
                return Problems.BadRequest(http, "INVALID_CIPHERTEXT", "El texto cifrado no tiene el formato nvk1.{keyId}.{version}.{datos}.", null);
            using var db = ctx.OpenDb();
            var k = Load(db, parts[1]);
            if (k is null) return Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {parts[1]}.", null);
            if (k.Status == "SHREDDED") return Problems.Of(http, 410, "KEY_SHREDDED", "Clave triturada", $"La clave {k.Id} se trituró el {k.ShreddedAt}: este dato ya no se puede leer.", "Es el resultado esperado tras un borrado GDPR.");
            var m = db.ExecuteScalar<byte[]?>("SELECT material FROM key_versions WHERE key_id = @id AND version = @v", new { id = k.Id, v });
            if (m is null) return Problems.NotFound(http, "KEY_VERSION_NOT_FOUND", $"La clave {k.Id} no tiene versión {v}.", null);
            try { return Results.Ok(new { keyId = k.Id, version = v, plaintext = Encoding.UTF8.GetString(Open(m, parts[3])) }); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
            { return Problems.Unprocessable(http, "DECRYPTION_FAILED", "No se pudo descifrar: el texto se ha modificado o no es de esta clave.", null); }
        }).WithTags("Cifrado").WithSummary("Descifrar (con la versión que indica el propio texto cifrado)");

        // ---- Red interna: otros servicios (pii-vault, document-vault, voice) piden aquí su material ----
        app.MapPost("/api/v1/internal/keys/resolve", (HttpContext http, ResolveKeyRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Subject)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta subject.", null);
            var purpose = (b.Purpose ?? "GENERAL").ToUpperInvariant();
            using var db = ctx.OpenDb();
            var id = db.ExecuteScalar<string?>("SELECT id FROM keys WHERE subject = @s AND purpose = @purpose", new { s = b.Subject, purpose }) ?? Create(db, http, clock, b.Subject, purpose);
            var k = Load(db, id)!;
            if (k.Status == "SHREDDED") return Problems.Of(http, 410, "KEY_SHREDDED", "Clave triturada", $"La clave de {b.Subject} se trituró el {k.ShreddedAt}.", null);
            var m = db.ExecuteScalar<byte[]>("SELECT material FROM key_versions WHERE key_id = @id AND version = @v", new { id, v = k.CurrentVersion });
            return Results.Ok(new KeyMaterial(id, k.CurrentVersion, Convert.ToBase64String(m), "AES-256-GCM"));
        }).WithTags("Interno").WithSummary("(interno) Clave actual de un sujeto; la crea si no existe");

        app.MapGet("/api/v1/internal/keys/{id}/versions/{version:int}", (HttpContext http, string id, int version) =>
        {
            using var db = ctx.OpenDb();
            var k = Load(db, id);
            if (k is null) return Problems.NotFound(http, "KEY_NOT_FOUND", $"No existe la clave {id}.", null);
            if (k.Status == "SHREDDED") return Problems.Of(http, 410, "KEY_SHREDDED", "Clave triturada", $"La clave {id} se trituró el {k.ShreddedAt}.", null);
            var m = db.ExecuteScalar<byte[]?>("SELECT material FROM key_versions WHERE key_id = @id AND version = @version", new { id, version });
            return m is null ? Problems.NotFound(http, "KEY_VERSION_NOT_FOUND", $"La clave {id} no tiene versión {version}.", null)
                             : Results.Ok(new KeyMaterial(id, version, Convert.ToBase64String(m), "AES-256-GCM"));
        }).WithTags("Interno").WithSummary("(interno) Material de una versión concreta");

        app.MapPost("/api/v1/internal/subjects/shred", (HttpContext http, ResolveKeyRequest b) =>
        {
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM keys WHERE subject = @s AND status = 'ACTIVE'", new { s = b.Subject }).ToList();
            var now = Nordic.Iso(clock.LocalNow);
            foreach (var id in ids)
            {
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE key_versions SET material = NULL WHERE key_id = @id", new { id }, tx);
                db.Execute("UPDATE keys SET status = 'SHREDDED', shredded_at = @now, shred_reason = @r WHERE id = @id", new { id, now, r = b.Purpose ?? "borrado del sujeto" }, tx);
                Outbox.Add(db, http, clock, "key.shredded", id, new { keyId = id, subject = b.Subject, reason = b.Purpose }, tx);
                tx.Commit();
            }
            return Results.Ok(new { subject = b.Subject, shredded = ids });
        }).WithTags("Interno").WithSummary("(interno) Triturar todas las claves de un sujeto (lo usa el borrado GDPR)");
    }
}
