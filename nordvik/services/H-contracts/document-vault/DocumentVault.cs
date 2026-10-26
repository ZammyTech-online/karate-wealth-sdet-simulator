using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record StoreDocumentRequest(string? Kind, string? MimeType, string? FileName, string? ContentBase64, JsonElement? Content, string? ContractId, string? CustomerId,
    int? ExpiresInSeconds, string? Purpose);
public sealed record LinkRequest(int? TtlSeconds);
public sealed record VaultDocument(string Id, string Kind, string MimeType, string? FileName, long Size, string ContentHash, string? ContractId, string? CustomerRef, int Version,
    string Status, string CreatedAt, string? ExpiresAt, string? DestroyedAt, JsonElement? Content);
public sealed record OneTimeLink(string Token, string Url, string ExpiresAt, int TtlSeconds);

/// <summary>
/// document-vault: documentos cifrados en reposo (AES-GCM, una clave por documento). Si un documento tiene caducidad,
/// se autodestruye: se borra su clave (crypto-shredding) y queda una lápida que responde 410.
/// Los binarios se descargan con enlaces de un solo uso que caducan (por defecto a los 60 s).
/// </summary>
public sealed class DocumentVault : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "document-vault", "H-contracts", 6704, 2, "Contratos y documentos",
        "Documentos cifrados con caducidad y autodestrucción (crypto-shredding), contentHash, y enlaces de descarga de un solo uso.",
        DependsOn: new[] { "permission-service", "clock-service" }, Emits: new[] { "document.stored", "document.destroyed" });

    private Timer? _timer;
    private static readonly string[] JsonKinds = { "FEE_SCHEDULE", "KYC_SUMMARY", "SUITABILITY", "STATEMENT_DATA", "JSON" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS documents (id TEXT PRIMARY KEY, kind TEXT, mime TEXT, file_name TEXT, size INTEGER, content_hash TEXT, contract_id TEXT, customer_id TEXT,
                        version INTEGER, status TEXT, created_at TEXT, expires_ticks INTEGER, expires_at TEXT, destroyed_at TEXT, purpose TEXT, key BLOB, nonce BLOB, tag BLOB, cipher BLOB);
                     CREATE TABLE IF NOT EXISTS links (token TEXT PRIMARY KEY, document_id TEXT, expires_ticks INTEGER, used_at TEXT)");
    }

    public static string CustomerRef(string? customerId) => customerId is null ? null! : "tok_cu_" + Canonical.Sha256Hex(customerId)[..6];

    private static VaultDocument? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, bool withContent)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Kind, string Mime, string? File, long Size, string Hash, string? Contract, string? Cust, long Version, string Status, string Created,
            string? Expires, string? Destroyed, byte[]? Key, byte[]? Nonce, byte[]? Tag, byte[]? Cipher)>(
            "SELECT id, kind, mime, file_name, size, content_hash, contract_id, customer_id, version, status, created_at, expires_at, destroyed_at, key, nonce, tag, cipher FROM documents WHERE id = @id", new { id });
        if (r.Id is null) return null;
        JsonElement? content = null;
        if (withContent && r.Status == "ACTIVE" && r.Mime == "application/json" && r.Key is not null)
            content = JsonDocument.Parse(Decrypt(r.Key, r.Nonce!, r.Tag!, r.Cipher!)).RootElement;
        return new VaultDocument(r.Id, r.Kind, r.Mime, r.File, r.Size, r.Hash, r.Contract, r.Cust is null ? null : CustomerRef(r.Cust), (int)r.Version, r.Status, r.Created, r.Expires, r.Destroyed, content);
    }

    private static (byte[] Key, byte[] Nonce, byte[] Tag, byte[] Cipher) Encrypt(byte[] plain)
    {
        var key = RandomNumberGenerator.GetBytes(32); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return (key, nonce, tag, cipher);
    }

    private static byte[] Decrypt(byte[] key, byte[] nonce, byte[] tag, byte[] cipher)
    {
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static int Destroy(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock, HttpContext? http, string where, object args)
    {
        var ids = db.Query<string>("SELECT id FROM documents WHERE status = 'ACTIVE' AND " + where, args).ToList();
        foreach (var id in ids)
        {
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE documents SET status = 'DESTROYED', destroyed_at = @at, key = NULL, nonce = NULL, tag = NULL, cipher = NULL WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, http, clock, "document.destroyed", id, new { documentId = id }, tx);
            tx.Commit();
        }
        return ids.Count;
    }

    private static IResult Gone(HttpContext http, VaultDocument d) =>
        Problems.Of(http, 410, "DOCUMENT_DESTROYED", "Documento destruido", $"El documento {d.Id} se destruyó el {d.DestroyedAt} (su clave se borró: ya no se puede descifrar).",
            "Es lo esperado en datos efímeros: comprueba el 410 y que el contenido no vuelve nunca.");

    public static async Task<IResult?> Guard(HttpContext http, IClock clock, ServiceContext ctx, string id)
    {
        var denied = Auth.Require(http, clock, out var p);
        if (denied is not null) return denied;
        using var db = ctx.OpenDb();
        var cust = db.ExecuteScalar<string?>("SELECT customer_id FROM documents WHERE id = @id", new { id });
        if (cust is null) return null;
        var d = await Permissions.Check(http, p, "contract:read", cust);
        return d.Allowed ? null : Permissions.Denied(http, p, d, "DOCUMENT_NOT_FOUND", $"el documento {id}");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/documents", (HttpContext http, StoreDocumentRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Kind)) errors.Add(new FieldError("kind", "obligatorio (FEE_SCHEDULE, CONTRACT_PDF, EXTERNAL_STATEMENT...)"));
            if ((b.ContentBase64 is null) == (b.Content is null)) errors.Add(new FieldError("content", "envía content (JSON) o contentBase64 (binario), uno de los dos"));
            if (b.ExpiresInSeconds is < 1 or > 400 * 86400) errors.Add(new FieldError("expiresInSeconds", "entre 1 y 400 días"));
            byte[] data = Array.Empty<byte>(); string mime;
            if (b.Content is not null) { data = Encoding.UTF8.GetBytes(Canonical.Of(b.Content.Value)); mime = "application/json"; }
            else
            {
                if (b.ContentBase64 is not null)
                    try { data = Convert.FromBase64String(b.ContentBase64); } catch (FormatException) { errors.Add(new FieldError("contentBase64", "base64 no válido")); }
                mime = b.MimeType ?? "application/octet-stream";
            }
            if (data.Length > 5 * 1024 * 1024) return Problems.Of(http, 413, "FILE_TOO_LARGE", "Fichero demasiado grande", "El máximo es 5 MB.", null);
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Documento no válido.", "Ejemplo JSON: { \"kind\": \"FEE_SCHEDULE\", \"content\": { \"managementFee\": \"0.45\" }, \"contractId\": \"...\" }", errors);
            var id = "doc_" + Guid.NewGuid().ToString("N")[..16];
            var hash = b.Content is not null ? Canonical.Hash(b.Content.Value) : Canonical.Sha256Hex(data);
            var (key, nonce, tag, cipher) = Encrypt(data);
            var now = clock.UtcNow;
            DateTimeOffset? exp = b.ExpiresInSeconds is { } s ? now.AddSeconds(s) : null;
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute(@"INSERT INTO documents VALUES (@id, @kind, @mime, @FileName, @size, @hash, @ContractId, @CustomerId, 1, 'ACTIVE', @created, @et, @ea, NULL, @Purpose, @key, @nonce, @tag, @cipher)",
                new { id, kind = b.Kind, mime, b.FileName, size = data.LongLength, hash, b.ContractId, b.CustomerId, created = Nordic.Iso(Nordic.ToStockholm(now)), et = exp?.UtcTicks,
                      ea = exp is null ? null : Nordic.Iso(Nordic.ToStockholm(exp.Value)), b.Purpose, key, nonce, tag, cipher }, tx);
            Outbox.Add(db, http, clock, "document.stored", id, new { documentId = id, kind = b.Kind, contractId = b.ContractId, expiresAt = exp is null ? null : Nordic.Iso(Nordic.ToStockholm(exp.Value)) }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/documents/{id}", Load(db, id, true));
        }).WithTags("Documentos").WithSummary("Guardar un documento cifrado (JSON o binario en base64), con caducidad opcional").Produces<VaultDocument>(201);

        app.MapGet("/api/v1/documents/{id}", async (HttpContext http, string id) =>
        {
            if (await Guard(http, clock, ctx, id) is { } g) return g;
            using var db = ctx.OpenDb();
            Destroy(db, clock, http, "expires_ticks IS NOT NULL AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
            var d = Load(db, id, true);
            if (d is null) return Problems.NotFound(http, "DOCUMENT_NOT_FOUND", $"No existe el documento {id}.", null);
            return d.Status == "DESTROYED" ? Gone(http, d) : Results.Ok(d);
        }).WithTags("Documentos").WithSummary("Un documento (los JSON incluyen content; los binarios se descargan con un enlace)").Produces<VaultDocument>();

        app.MapGet("/api/v1/internal/documents/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            Destroy(db, clock, http, "expires_ticks IS NOT NULL AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
            var d = Load(db, id, true);
            if (d is null) return Problems.NotFound(http, "DOCUMENT_NOT_FOUND", $"No existe el documento {id}.", null);
            return d.Status == "DESTROYED" ? Gone(http, d) : Results.Ok(d);
        }).WithTags("Interno").WithSummary("Documento para otros servicios (red interna)").Produces<VaultDocument>();

        app.MapPost("/api/v1/documents/{id}/links", async (HttpContext http, string id, LinkRequest? b) =>
        {
            if (http.Request.Headers.Authorization.Count > 0 && await Guard(http, clock, ctx, id) is { } g) return g;
            using var db = ctx.OpenDb();
            Destroy(db, clock, http, "expires_ticks IS NOT NULL AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks });
            var d = Load(db, id, false);
            if (d is null) return Problems.NotFound(http, "DOCUMENT_NOT_FOUND", $"No existe el documento {id}.", null);
            if (d.Status == "DESTROYED") return Gone(http, d);
            var ttl = Math.Clamp(b?.TtlSeconds ?? 60, 5, 300);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var exp = clock.UtcNow.AddSeconds(ttl);
            db.Execute("INSERT INTO links VALUES (@token, @id, @t, NULL)", new { token, id, t = exp.UtcTicks });
            var link = new OneTimeLink(token, $"{ServiceDirectory.UrlOf("document-vault")}/api/v1/links/{token}", Nordic.Iso(Nordic.ToStockholm(exp)), ttl);
            return Results.Created(link.Url, link);
        }).WithTags("Enlaces").WithSummary("Crear un enlace de descarga de un solo uso (60 s por defecto, máximo 300)").Produces<OneTimeLink>(201);

        app.MapGet("/api/v1/links/{token}", (HttpContext http, string token) =>
        {
            using var db = ctx.OpenDb();
            var l = db.QuerySingleOrDefault<(string Token, string Doc, long Exp, string? Used)>("SELECT token, document_id, expires_ticks, used_at FROM links WHERE token = @token", new { token });
            if (l.Token is null) return Problems.NotFound(http, "LINK_NOT_FOUND", "Ese enlace no existe.", null);
            if (l.Used is not null) return Problems.Of(http, 410, "LINK_ALREADY_USED", "Enlace usado", $"Este enlace de un solo uso ya se descargó el {l.Used}.", "Pide un enlace nuevo: cada descarga necesita el suyo.");
            if (clock.UtcNow.UtcTicks > l.Exp) return Problems.Of(http, 410, "LINK_EXPIRED", "Enlace caducado", "El enlace caducó.", "Los enlaces duran 60 s del reloj virtual.");
            var r = db.QuerySingle<(string Status, string Mime, string? File, byte[]? Key, byte[]? Nonce, byte[]? Tag, byte[]? Cipher)>(
                "SELECT status, mime, file_name, key, nonce, tag, cipher FROM documents WHERE id = @d", new { d = l.Doc });
            if (r.Status != "ACTIVE") return Problems.Of(http, 410, "DOCUMENT_DESTROYED", "Documento destruido", "El documento se destruyó.", null);
            db.Execute("UPDATE links SET used_at = @at WHERE token = @token", new { at = Nordic.Iso(clock.LocalNow), token });
            return Results.File(Decrypt(r.Key!, r.Nonce!, r.Tag!, r.Cipher!), r.Mime, r.File ?? l.Doc + (r.Mime == "application/pdf" ? ".pdf" : r.Mime == "application/json" ? ".json" : ".bin"));
        }).WithTags("Enlaces").WithSummary("Descargar con un enlace de un solo uso (el segundo intento da 410)");

        app.MapDelete("/api/v1/documents/{id}", (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Admin", "Operations", "Compliance" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var d = Load(db, id, false);
            if (d is null) return Problems.NotFound(http, "DOCUMENT_NOT_FOUND", $"No existe el documento {id}.", null);
            if (d.Status == "DESTROYED") return Gone(http, d);
            Destroy(db, clock, http, "id = @id", new { id });
            return Results.Ok(Load(db, id, false));
        }).WithTags("Documentos").WithSummary("Destruir un documento ya (crypto-shredding)").Produces<VaultDocument>();

        app.MapGet("/api/v1/documents", (string? contractId) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM documents WHERE (@contractId IS NULL OR contract_id = @contractId) ORDER BY created_at", new { contractId }).Select(i => Load(db, i, false)!).ToList());
        }).WithTags("Documentos").WithSummary("Documentos (sin contenido) de un contrato").Produces<List<VaultDocument>>();

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ =>
        {
            try { using var db = ctx.OpenDb(); Destroy(db, clock, null, "expires_ticks IS NOT NULL AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks }); } catch { }
        }, null, 1000, 1000));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }
}
