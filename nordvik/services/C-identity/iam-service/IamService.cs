using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TokenResponse(string access_token, string token_type, int expires_in, string? refresh_token, string scope);
public sealed record IntrospectRequest(string? Token);
public sealed record Introspection(bool Active, string? Sub, string? Role, string? Scope, string? CustomerId, string? AdvisorId, string? Sid, long? Exp, string? Reason);
public sealed record UserInfo(string Username, string DisplayName, string Role, string? CustomerId, string? AdvisorId, string Scope);

/// <summary>
/// iam-service: identidad. Emite tokens JWT al estilo OAuth2 (password, client_credentials y refresh_token),
/// crea la sesión en session-service y responde a la introspección del gateway.
/// Ojo: este equipo sigue el formato de errores de OAuth2 ({ "error", "error_description" }) en /oauth/token, no RFC 9457.
/// </summary>
public sealed class IamService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "iam-service", "C-identity", 6200, 1, "Identidad",
        "Login OAuth2 (password, client_credentials, refresh_token), tokens JWT de 5 minutos, introspección, revocación y bloqueo tras 5 intentos fallidos.",
        DependsOn: new[] { "clock-service", "session-service", "config-service" });

    private sealed record UserRow(string Username, string DisplayName, string Role, string? CustomerId, string? AdvisorId, string Salt, string Hash, long FailedCount, long LockedUntil);

    public static readonly (string User, string Password, string Name, string Role, string? Customer, string? Advisor)[] SeedUsers =
    {
        ("cliente.ana", "Ana2026!", "Ana Lindqvist", "Customer", SeedIds.CustomerAna, null),
        ("cliente.lars", "Lars2026!", "Lars Berg", "Customer", SeedIds.CustomerLars, null),
        ("cliente.sofia", "Sofia2026!", "Sofia Nyström", "Customer", SeedIds.CustomerSofia, null),
        ("asesor.erik", "Erik2026!", "Erik Johansson", "Advisor", null, SeedIds.AdvisorErik),
        ("asesora.maja", "Maja2026!", "Maja Holm", "Advisor", null, SeedIds.AdvisorMaja),
        ("operaciones.olof", "Olof2026!", "Olof Sandberg", "Operations", null, null),
        ("compliance.karin", "Karin2026!", "Karin Ekström", "Compliance", null, null),
        ("admin", "Admin2026!", "Administración Nordvik", "Admin", null, null),
    };

    public static readonly Dictionary<string, string> RoleScopes = new()
    {
        ["Customer"] = "customers:read portfolios:read portfolios:write profile:read orders:read orders:write payments:read payments:write contracts:read contracts:write",
        ["Advisor"] = "customers:read customers:write portfolios:read portfolios:write profile:read orders:read orders:write payments:read contracts:read contracts:write",
        ["Operations"] = "customers:read customers:write portfolios:read portfolios:write profile:read orders:read orders:write payments:read payments:write contracts:read",
        ["Compliance"] = "customers:read portfolios:read profile:read orders:read payments:read contracts:read",
        ["Admin"] = "*",
        ["Service"] = "config:read",
    };

    private static readonly Dictionary<string, string> Clients = new() { ["nordvik-batch"] = "batch-secret-2026", ["nordvik-internal"] = "internal-secret-2026" };

    /// <summary>Scopes de cada cliente máquina (client_credentials). nordvik-internal lo usan las sagas que actúan sin usuario (rebalanceo, onboarding).</summary>
    private static readonly Dictionary<string, string> ClientScopes = new()
    {
        ["nordvik-batch"] = "config:read",
        ["nordvik-internal"] = "config:read customers:read portfolios:read portfolios:write orders:read orders:write payments:read contracts:read contracts:write",
    };

    private static string Hash(string salt, string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + password)));

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS users (username TEXT PRIMARY KEY, display_name TEXT, role TEXT, customer_id TEXT, advisor_id TEXT, salt TEXT, hash TEXT, failed_count INTEGER DEFAULT 0, locked_until INTEGER DEFAULT 0);
                     CREATE TABLE IF NOT EXISTS refresh_tokens (token TEXT PRIMARY KEY, username TEXT, scope TEXT, expires INTEGER, used INTEGER DEFAULT 0, revoked INTEGER DEFAULT 0);
                     CREATE TABLE IF NOT EXISTS revoked_jti (jti TEXT PRIMARY KEY, revoked_at TEXT)");
        foreach (var u in SeedUsers)
        {
            var salt = "salt-" + u.User;
            db.Execute("INSERT OR IGNORE INTO users (username, display_name, role, customer_id, advisor_id, salt, hash) VALUES (@u, @n, @r, @c, @a, @s, @h)",
                new { u = u.User, n = u.Name, r = u.Role, c = u.Customer, a = u.Advisor, s = salt, h = Hash(salt, u.Password) });
        }
    }

    private static IResult OAuthError(HttpContext http, int status, string error, string description, string? hint = null)
    {
        var body = new Dictionary<string, object?> { ["error"] = error, ["error_description"] = description };
        if (hint is not null && http.Items[NordvikMiddleware.LearningKey] is true) body["hint"] = hint;
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(body, statusCode: status);
    }

    private static async Task<int> Ttl(HttpContext http)
    {
        try
        {
            var r = await Downstream.Get(http, "config-service", "/api/v1/config/iam-service/accessTokenTtlSeconds", forwardAuth: false);
            if (r.Ok && int.TryParse(r.Str("value"), out var v)) return v;
        }
        catch (DependencyUnavailableException) { }
        return 300;
    }

    private static string? NarrowScope(string allowed, string? requested, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requested)) return allowed;
        if (allowed == "*") return requested;
        var allowedSet = allowed.Split(' ').ToHashSet();
        var extra = requested.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(s => !allowedSet.Contains(s)).ToList();
        if (extra.Count > 0) { error = string.Join(" ", extra); return null; }
        return requested;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/oauth/token", async (HttpContext http, IClock clock) =>
        {
            Dictionary<string, string?> f;
            if (http.Request.HasFormContentType)
                f = (await http.Request.ReadFormAsync()).ToDictionary(k => k.Key, k => (string?)k.Value.ToString());
            else if (http.Request.ContentType?.Contains("json") == true)
            {
                try { f = (await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(http.Request.Body))!.ToDictionary(k => k.Key, k => (string?)k.Value.ToString()); }
                catch { return OAuthError(http, 400, "invalid_request", "El cuerpo no es JSON válido.", "Envía form-urlencoded (lo estándar en OAuth2) o JSON."); }
            }
            else return OAuthError(http, 400, "invalid_request", "Falta el cuerpo de la petición.",
                "En Karate: form field grant_type = 'password', form field username = '...', form field password = '...' y method post.");
            string? V(string k) => f.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
            var grant = V("grant_type");
            var now = clock.UtcNow;
            var ttl = await Ttl(http);
            using var db = ctx.OpenDb();

            async Task<IResult> Issue(string sub, string role, string scope, string? customer, string? advisor, bool withRefresh, bool withSession)
            {
                string? sid = null;
                if (withSession)
                {
                    var s = await Downstream.Post(http, "session-service", "/api/v1/sessions", new { userId = sub, role, device = http.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(60, ua.Length)] : "api" }, forwardAuth: false);
                    if (!s.Ok) return OAuthError(http, 503, "temporarily_unavailable", "No se pudo crear la sesión.", "session-service tiene que estar encendido.");
                    sid = s.Str("id");
                }
                var p = new Principal(sub, role, scope, customer, advisor, sid, Guid.NewGuid().ToString("N"), now.ToUnixTimeSeconds(), now.AddSeconds(ttl).ToUnixTimeSeconds());
                string? refresh = null;
                if (withRefresh)
                {
                    refresh = "rt_" + Guid.NewGuid().ToString("N");
                    db.Execute("INSERT INTO refresh_tokens (token, username, scope, expires) VALUES (@refresh, @sub, @scope, @exp)", new { refresh, sub, scope, exp = now.AddHours(8).UtcTicks });
                }
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(new TokenResponse(Auth.Issue(p), "Bearer", ttl, refresh, scope));
            }

            switch (grant)
            {
                case "password":
                {
                    var username = V("username"); var password = V("password");
                    if (username is null || password is null)
                        return OAuthError(http, 400, "invalid_request", "Faltan username o password.", "Usuarios de prueba: cliente.ana / Ana2026!, asesor.erik / Erik2026!, admin / Admin2026! (lista completa en la ficha de iam-service).");
                    var u = db.QuerySingleOrDefault<UserRow>("SELECT username, display_name AS DisplayName, role, customer_id AS CustomerId, advisor_id AS AdvisorId, salt, hash, failed_count AS FailedCount, locked_until AS LockedUntil FROM users WHERE username = @username", new { username });
                    if (u is null) return OAuthError(http, 400, "invalid_grant", "Usuario o contraseña incorrectos.", "Revisa el nombre de usuario: lleva punto, por ejemplo asesor.erik.");
                    if (u.LockedUntil > now.UtcTicks)
                        return OAuthError(http, 400, "invalid_grant", "La cuenta está bloqueada temporalmente por demasiados intentos fallidos.",
                            "Se desbloquea a los 15 minutos (reloj virtual: puedes avanzarlo) o reinicia los datos con nordvik reset.");
                    if (Hash(u.Salt, password) != u.Hash)
                    {
                        var failed = u.FailedCount + 1;
                        var lockedUntil = failed >= 5 ? now.AddMinutes(15).UtcTicks : 0;
                        db.Execute("UPDATE users SET failed_count = @failed, locked_until = @lockedUntil WHERE username = @username", new { failed = failed >= 5 ? 0 : failed, lockedUntil, username });
                        return OAuthError(http, 400, "invalid_grant", "Usuario o contraseña incorrectos.", failed >= 5 ? "Era el quinto intento: la cuenta queda bloqueada 15 minutos." : $"Llevas {failed} de 5 intentos.");
                    }
                    db.Execute("UPDATE users SET failed_count = 0, locked_until = 0 WHERE username = @username", new { username });
                    var scope = NarrowScope(RoleScopes[u.Role], V("scope"), out var bad);
                    if (scope is null) return OAuthError(http, 400, "invalid_scope", $"El rol {u.Role} no puede pedir: {bad}.", "Puedes pedir un subconjunto de: " + RoleScopes[u.Role]);
                    return await Issue(u.Username, u.Role, scope, u.CustomerId, u.AdvisorId, withRefresh: true, withSession: true);
                }
                case "client_credentials":
                {
                    var id = V("client_id"); var secret = V("client_secret");
                    if (id is null || secret is null || !Clients.TryGetValue(id, out var expected) || expected != secret)
                    {
                        http.Response.Headers.WWWAuthenticate = "Basic";
                        return OAuthError(http, 401, "invalid_client", "Cliente o secreto incorrectos.", "Cliente de prueba: nordvik-batch / batch-secret-2026.");
                    }
                    return await Issue(id, "Service", ClientScopes[id], null, null, withRefresh: false, withSession: false);
                }
                case "refresh_token":
                {
                    var token = V("refresh_token");
                    if (token is null) return OAuthError(http, 400, "invalid_request", "Falta refresh_token.", null);
                    var row = db.QuerySingleOrDefault<(string Username, string Scope, long Expires, long Used, long Revoked)>("SELECT username, scope, expires, used, revoked FROM refresh_tokens WHERE token = @token", new { token });
                    if (row.Username is null) return OAuthError(http, 400, "invalid_grant", "refresh_token desconocido.", null);
                    if (row.Used == 1)
                    {
                        db.Execute("UPDATE refresh_tokens SET revoked = 1 WHERE username = @u", new { u = row.Username });
                        return OAuthError(http, 400, "invalid_grant", "Ese refresh_token ya se usó: por seguridad se han revocado todos los del usuario.",
                            "Los refresh_token son de un solo uso (rotación). Guarda siempre el nuevo que te devuelve cada refresco.");
                    }
                    if (row.Revoked == 1 || row.Expires < now.UtcTicks) return OAuthError(http, 400, "invalid_grant", "refresh_token revocado o caducado.", null);
                    db.Execute("UPDATE refresh_tokens SET used = 1 WHERE token = @token", new { token });
                    var u = db.QuerySingle<UserRow>("SELECT username, display_name AS DisplayName, role, customer_id AS CustomerId, advisor_id AS AdvisorId, salt, hash, failed_count AS FailedCount, locked_until AS LockedUntil FROM users WHERE username = @u", new { u = row.Username });
                    return await Issue(u.Username, u.Role, row.Scope, u.CustomerId, u.AdvisorId, withRefresh: true, withSession: true);
                }
                default:
                    return OAuthError(http, 400, "unsupported_grant_type", $"grant_type '{grant}' no soportado.", "Valores: password, client_credentials, refresh_token.");
            }
        }).WithTags("OAuth2").WithSummary("Pedir un token (form-urlencoded o JSON)").Produces<TokenResponse>().WithDescription("Acepta application/x-www-form-urlencoded (estándar OAuth2) o application/json con los campos grant_type, username, password, client_id, client_secret, refresh_token, scope.");

        app.MapPost("/api/v1/oauth/introspect", async (HttpContext http, IClock clock, IntrospectRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Token)) return Results.Ok(new Introspection(false, null, null, null, null, null, null, null, "sin token"));
            var reason = Auth.Validate(body.Token, clock.UtcNow, out var p);
            if (reason is not null) return Results.Ok(new Introspection(false, null, null, null, null, null, null, null, reason));
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM revoked_jti WHERE jti = @Jti", p!) > 0)
                return Results.Ok(new Introspection(false, p!.Sub, null, null, null, null, null, null, "revocado"));
            if (p!.SessionId is not null)
            {
                var s = await Downstream.Get(http, "session-service", $"/api/v1/sessions/{p.SessionId}?touch=true", forwardAuth: false);
                if (!s.Ok || s.Str("status") != "ACTIVE")
                    return Results.Ok(new Introspection(false, p.Sub, null, null, null, null, p.SessionId, null, "sesión " + (s.Str("status") ?? "desconocida").ToLowerInvariant()));
            }
            return Results.Ok(new Introspection(true, p.Sub, p.Role, p.Scope, p.CustomerId, p.AdvisorId, p.SessionId, p.Exp, null));
        }).WithTags("OAuth2").WithSummary("¿Sigue activo este token? (lo usa el gateway)").Produces<Introspection>();

        app.MapPost("/api/v1/oauth/revoke", (IClock clock, IntrospectRequest body) =>
        {
            if (!string.IsNullOrWhiteSpace(body.Token))
            {
                using var db = ctx.OpenDb();
                if (body.Token.StartsWith("rt_")) db.Execute("UPDATE refresh_tokens SET revoked = 1 WHERE token = @Token", body);
                else if (Auth.Validate(body.Token, clock.UtcNow, out var p) is null || p is not null)
                    db.Execute("INSERT OR IGNORE INTO revoked_jti VALUES (@jti, @at)", new { jti = p?.Jti, at = Nordic.Iso(clock.LocalNow) });
            }
            return Results.Ok(new { revoked = true });
        }).WithTags("OAuth2").WithSummary("Revocar un token o refresh_token (siempre 200, como dice RFC 7009)");

        app.MapGet("/api/v1/users/me", (HttpContext http, IClock clock) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var name = db.ExecuteScalar<string?>("SELECT display_name FROM users WHERE username = @Sub", p) ?? p.Sub;
            return Results.Ok(new UserInfo(p.Sub, name, p.Role, p.CustomerId, p.AdvisorId, p.Scope));
        }).WithTags("Usuarios").WithSummary("Quién soy según mi token").Produces<UserInfo>();

        app.MapGet("/api/v1/users", (HttpContext http, IClock clock) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Admin" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query("SELECT username, display_name AS displayName, role, customer_id AS customerId, advisor_id AS advisorId FROM users ORDER BY username").ToList());
        }).WithTags("Usuarios").WithSummary("Todos los usuarios (Admin)");
    }
}
