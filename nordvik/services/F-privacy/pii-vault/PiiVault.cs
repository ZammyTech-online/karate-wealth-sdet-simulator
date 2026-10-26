using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TokenizeRequest(string? Type, string? Value, string? CustomerId);
public sealed record DetokenizeRequest(string? Purpose);
public sealed record PiiToken(string Token, string Type, string Masked, string? CustomerRef, string KeyId, int KeyVersion, string Status, string CreatedAt);

/// <summary>
/// pii-vault: guarda los datos personales (número nacional, IBAN, email, teléfono, nombre, dirección) cifrados con la clave del cliente
/// (key-management) y devuelve un token. El resto de servicios guarda el token, nunca el dato. Leer el dato en claro
/// (detokenizar) exige un propósito y deja rastro (evento pii.detokenized → lineage-service y audit-service).
/// Si se tritura la clave del cliente, todos sus tokens responden 410 PII_ERASED.
/// </summary>
public sealed class PiiVault : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "pii-vault", "F-privacy", 6500, 3, "Privacidad y protección de datos",
        "Tokenización de datos personales: cifrados con la clave del cliente, detokenización con propósito y rastro, y borrado por crypto-shredding.",
        DependsOn: new[] { "key-management", "clock-service" }, Emits: new[] { "pii.tokenized", "pii.detokenized" }, Consumes: new[] { "key.shredded" });

    public static readonly string[] Types = { "NATIONAL_ID", "IBAN", "EMAIL", "PHONE", "NAME", "ADDRESS" };
    public static readonly string[] Purposes = { "KYC_REVIEW", "PAYMENT_EXECUTION", "LEGAL_HOLD", "CUSTOMER_REQUEST", "FRAUD_INVESTIGATION", "TAX_REPORTING" };
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase);
    private static readonly Regex Phone = new(@"^\+(46|47|45|358)\d{6,10}$");
    private static readonly byte[] Pepper = Encoding.UTF8.GetBytes("nordvik-pii-fingerprint-2026");

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS tokens (token TEXT PRIMARY KEY, type TEXT, fingerprint TEXT UNIQUE, masked TEXT, customer_id TEXT, key_id TEXT, key_version INTEGER,
                        status TEXT, created_at TEXT, cipher TEXT)");
    }

    public static string? Normalize(string type, string value, out string? error)
    {
        error = null;
        var v = value.Trim();
        switch (type)
        {
            case "NATIONAL_ID":
                var c = NationalId.Check(v);
                if (!c.Valid) { error = "número nacional no válido: " + c.Reason; return null; }
                return c.Normalized;
            case "IBAN":
                var i = IbanMath.Normalize(v);
                if (!IbanMath.IsValid(i)) { error = "IBAN no válido (longitud o dígitos de control)"; return null; }
                return i;
            case "EMAIL":
                if (!Email.IsMatch(v)) { error = "email no válido"; return null; }
                return v.ToLowerInvariant();
            case "PHONE":
                var p = v.Replace(" ", "").Replace("-", "");
                if (!Phone.IsMatch(p)) { error = "teléfono en formato internacional nórdico: +46..., +47..., +45..., +358..."; return null; }
                return p;
            default:
                if (v.Length is < 2 or > 200) { error = "entre 2 y 200 caracteres"; return null; }
                return v;
        }
    }

    public static string Mask(string type, string v) => type switch
    {
        "NATIONAL_ID" => NationalId.Mask(v),
        "IBAN" => IbanMath.Mask(v),
        "EMAIL" => v[..1] + "***@" + v.Split('@')[1],
        "PHONE" => v[..3] + new string('*', v.Length - 5) + v[^2..],
        "NAME" => string.Join(" ", v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[..1] + ".")),
        _ => v[..Math.Min(3, v.Length)] + "***",
    };

    private static string Prefix(string type) => type switch { "NATIONAL_ID" => "nid", "IBAN" => "iban", "EMAIL" => "mail", "PHONE" => "tel", "NAME" => "name", _ => "addr" };

    private static PiiToken? Load(Microsoft.Data.Sqlite.SqliteConnection db, string token)
    {
        var r = db.QuerySingleOrDefault<(string Token, string Type, string Masked, string? Cust, string Key, long Ver, string Status, string Created)>(
            "SELECT token, type, masked, customer_id, key_id, key_version, status, created_at FROM tokens WHERE token = @token", new { token });
        return r.Token is null ? null : new PiiToken(r.Token, r.Type, r.Masked, r.Cust is null ? null : "tok_cu_" + Canonical.Sha256Hex(r.Cust)[..6], r.Key, (int)r.Ver, r.Status, r.Created);
    }

    private static async Task<IResult?> Reveal(HttpContext http, ServiceContext ctx, IClock clock, string token, string? purpose, string by, Action<string> onValue)
    {
        if (purpose is null || !Purposes.Contains(purpose))
            return Problems.BadRequest(http, "PURPOSE_REQUIRED", "Leer un dato personal exige un propósito válido.", "Propósitos: " + string.Join(", ", Purposes));
        using var db = ctx.OpenDb();
        var r = db.QuerySingleOrDefault<(string Token, string Key, long Ver, string Status, string Cipher, string? Cust)>(
            "SELECT token, key_id, key_version, status, cipher, customer_id FROM tokens WHERE token = @token", new { token });
        if (r.Token is null) return Problems.NotFound(http, "TOKEN_NOT_FOUND", $"No existe el token {token}.", null);
        if (r.Status == "ERASED") return Erased(http, token);
        var m = await Downstream.Get(http, "key-management", $"/api/v1/internal/keys/{r.Key}/versions/{r.Ver}", forwardAuth: false);
        if (m.Status == 410)
        {
            db.Execute("UPDATE tokens SET status = 'ERASED', cipher = '' WHERE key_id = @k", new { k = r.Key });
            return Erased(http, token);
        }
        if (!m.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "key-management respondió " + m.Status, null);
        var all = Convert.FromBase64String(r.Cipher);
        var plain = new byte[all.Length - 28];
        using (var aes = new AesGcm(Convert.FromBase64String(m.Str("material")!), 16)) aes.Decrypt(all[..12], all[28..], all[12..28], plain);
        Outbox.Add(db, http, clock, "pii.detokenized", token, new { token, purpose, by, customerId = r.Cust });
        onValue(Encoding.UTF8.GetString(plain));
        return null;
    }

    private static IResult Erased(HttpContext http, string token) =>
        Problems.Of(http, 410, "PII_ERASED", "Dato borrado", $"El dato del token {token} se borró (su clave se trituró).", "Es lo esperado tras un borrado GDPR: el token existe, el dato no volverá.");

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Advisor", "Operations", "Compliance", "Admin", "Service" };

        EventConsumer.Start(app, ctx, new[] { "key.shredded" }, ev =>
        {
            using var db = ctx.OpenDb();
            db.Execute("UPDATE tokens SET status = 'ERASED', cipher = '' WHERE key_id = @k AND status = 'ACTIVE'", new { k = ev.Payload.GetProperty("keyId").GetString() });
            return Task.CompletedTask;
        });

        app.MapPost("/api/v1/tokens", async (HttpContext http, TokenizeRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            var type = b.Type?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (type is null || !Types.Contains(type)) errors.Add(new FieldError("type", "uno de " + string.Join(", ", Types)));
            if (string.IsNullOrWhiteSpace(b.Value)) errors.Add(new FieldError("value", "obligatorio"));
            string? norm = null;
            if (errors.Count == 0 && (norm = Normalize(type!, b.Value!, out var err)) is null) errors.Add(new FieldError("value", err!));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Dato personal no válido.", "Ejemplo: { \"type\": \"IBAN\", \"value\": \"SE45...\", \"customerId\": \"c000...\" }", errors);
            var fingerprint = Convert.ToHexString(HMACSHA256.HashData(Pepper, Encoding.UTF8.GetBytes($"{type}|{norm}|{b.CustomerId}"))).ToLowerInvariant();
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT token FROM tokens WHERE fingerprint = @fingerprint AND status = 'ACTIVE'", new { fingerprint }) is { } existing)
                return Results.Ok(Load(db, existing));
            var k = await Downstream.Post(http, "key-management", "/api/v1/internal/keys/resolve", new { subject = b.CustomerId is null ? "pii:unbound" : "customer:" + b.CustomerId, purpose = "PII" }, forwardAuth: false);
            if (k.Status == 410) return Problems.Conflict(http, "CUSTOMER_ERASED", "La clave de este cliente se trituró: no se pueden guardar datos nuevos con ella.", null);
            if (!k.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "key-management respondió " + k.Status, null);
            var key = Convert.FromBase64String(k.Str("material")!);
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var data = Encoding.UTF8.GetBytes(norm!); var cipher = new byte[data.Length];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, data, cipher, tag);
            var token = $"tok_{Prefix(type!)}_{Guid.NewGuid().ToString("N")[..12]}";
            db.Execute("DELETE FROM tokens WHERE fingerprint = @fingerprint", new { fingerprint });
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO tokens VALUES (@token, @type, @fingerprint, @masked, @cust, @kid, @ver, 'ACTIVE', @now, @cipher)",
                new { token, type, fingerprint, masked = Mask(type!, norm!), cust = b.CustomerId, kid = k.Str("keyId"), ver = k.Json.GetProperty("version").GetInt32(),
                      now = Nordic.Iso(clock.LocalNow), cipher = Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray()) }, tx);
            Outbox.Add(db, http, clock, "pii.tokenized", token, new { token, type, customerId = b.CustomerId }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/tokens/{token}", Load(db, token));
        }).WithTags("Tokens").WithSummary("Tokenizar un dato personal (el mismo dato del mismo cliente devuelve el mismo token)").Produces<PiiToken>(201);

        app.MapGet("/api/v1/tokens/{token}", (HttpContext http, string token) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, token) is { } t ? Results.Ok(t) : Problems.NotFound(http, "TOKEN_NOT_FOUND", $"No existe el token {token}.", null);
        }).WithTags("Tokens").WithSummary("Ver un token: tipo, máscara y estado (nunca el dato)").Produces<PiiToken>();

        app.MapGet("/api/v1/customers/{customerId}/tokens", (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Compliance", "Admin", "Service" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT token FROM tokens WHERE customer_id = @customerId ORDER BY created_at, token", new { customerId }).Select(t => Load(db, t)!).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Tokens").WithSummary("Todos los tokens de un cliente (inventario para un borrado o un acceso GDPR)");

        app.MapPost("/api/v1/tokens/{token}/detokenize", async (HttpContext http, string token, DetokenizeRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            using (var db = ctx.OpenDb())
            {
                var owner = db.ExecuteScalar<string?>("SELECT customer_id FROM tokens WHERE token = @token", new { token });
                var allowed = p.Role is "Compliance" or "Admin" or "Service" || (p.Role == "Customer" && owner is not null && owner == p.CustomerId && b.Purpose == "CUSTOMER_REQUEST");
                if (!allowed)
                    return p.Role == "Customer"
                        ? Problems.NotFound(http, "TOKEN_NOT_FOUND", $"No existe el token {token}.", null)
                        : Problems.Of(http, 403, "DETOKENIZE_NOT_ALLOWED", "Prohibido", $"El rol {p.Role} no puede leer datos personales en claro.",
                            "Los asesores y operaciones trabajan con la máscara. Si un proceso necesita el dato, pide un pasaporte en data-passport (puerto 6503).");
            }
            string? value = null;
            if (await Reveal(http, ctx, clock, token, b.Purpose, p.Sub, v => value = v) is { } fail) return fail;
            return Results.Ok(new { token, value, purpose = b.Purpose });
        }).WithTags("Tokens").WithSummary("Leer el dato en claro con un propósito (Compliance, Admin, servicios, o el propio cliente con CUSTOMER_REQUEST)");

        app.MapPost("/api/v1/internal/tokens/{token}/detokenize", async (HttpContext http, string token, DetokenizeRequest b) =>
        {
            string? value = null;
            if (await Reveal(http, ctx, clock, token, b.Purpose, http.Request.Headers["X-Caller"].FirstOrDefault() ?? "internal", v => value = v) is { } fail) return fail;
            return Results.Ok(new { token, value, purpose = b.Purpose });
        }).WithTags("Interno").WithSummary("(interno) Detokenizar desde otro servicio (data-passport)");

        app.MapGet("/api/v1/purposes", () => Results.Ok(new { types = Types, purposes = Purposes })).WithTags("Tokens").WithSummary("Tipos de dato y propósitos admitidos");
    }
}
