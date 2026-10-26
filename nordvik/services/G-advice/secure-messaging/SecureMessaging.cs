using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ConversationRequest(string? CustomerId, string? Subject);
public sealed record MessageRequest(string? Text, bool? ReadOnce, int? ExpiresInSeconds);

/// <summary>
/// secure-messaging: conversaciones cifradas entre el cliente y su asesor (una clave por conversación en key-management).
/// Un mensaje de lectura única no se ve en el listado: el destinatario lo abre una vez y se destruye (después, 410).
/// Los mensajes pueden caducar (reloj virtual). Un mensaje normal con datos personales se rechaza: para eso está la lectura única.
/// </summary>
public sealed class SecureMessaging : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "secure-messaging", "G-advice", 6605, 3, "Asesoramiento y comunicación",
        "Mensajería cifrada cliente-asesor: clave por conversación, mensajes de lectura única que se destruyen al abrirlos, caducidad y bloqueo de PII fuera de la lectura única.",
        DependsOn: new[] { "key-management", "redaction-service", "permission-service", "customer-registry", "notification-service", "clock-service" },
        Emits: new[] { "message.sent", "message.destroyed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS conversations (id TEXT PRIMARY KEY, customer_id TEXT, advisor_id TEXT, subject TEXT, key_id TEXT, created_by TEXT, created_at TEXT);
                     CREATE TABLE IF NOT EXISTS messages (id TEXT PRIMARY KEY, conversation_id TEXT, seq INTEGER, sender_sub TEXT, sender_role TEXT, cipher TEXT, read_once INTEGER, expires_ticks INTEGER,
                        expires_at TEXT, status TEXT, sent_at TEXT, opened_at TEXT)");
    }

    private static async Task<byte[]> Key(string conversationId)
    {
        var k = await Downstream.Post(null, "key-management", "/api/v1/internal/keys/resolve", new { subject = "conversation:" + conversationId, purpose = "MESSAGING" }, forwardAuth: false);
        if (!k.Ok) throw new DependencyUnavailableException("key-management", "sin clave para la conversación");
        return Convert.FromBase64String(k.Str("material")!);
    }

    private async Task<(IResult? Fail, (string Id, string Cust, string Adv) Conv, Principal P)> Member(HttpContext http, IClock clock, ServiceContext ctx, string id)
    {
        var denied = Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" });
        if (denied is not null) return (denied, default, p);
        using var db = ctx.OpenDb();
        var c = db.QuerySingleOrDefault<(string Id, string Cust, string Adv)>("SELECT id, customer_id, advisor_id FROM conversations WHERE id = @id", new { id });
        var ok = c.Id is not null && (p.Role == "Customer" ? p.CustomerId == c.Cust : p.AdvisorId == c.Adv);
        await Task.CompletedTask;
        return ok ? (null, c, p) : (Problems.NotFound(http, "CONVERSATION_NOT_FOUND", $"No existe la conversación {id}.", "Solo el cliente y su asesor ven una conversación."), c, p);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/conversations", async (HttpContext http, ConversationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            var customerId = b.CustomerId ?? p.CustomerId;
            if (customerId is null || string.IsNullOrWhiteSpace(b.Subject)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta customerId (si eres asesor) y subject.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            string? advisorId = p.AdvisorId;
            if (p.Role == "Customer")
            {
                var a = await Downstream.Get(http, "advisor-directory", $"/api/v1/customers/{customerId}/advisor");
                if (!a.Ok) return Problems.Unprocessable(http, "NO_ADVISOR_ASSIGNED", "No tienes asesor asignado: no hay con quién hablar.", null);
                advisorId = a.Str("id");
            }
            var id = "conv_" + Guid.NewGuid().ToString("N")[..12];
            await Key(id);
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO conversations VALUES (@id, @customerId, @advisorId, @Subject, NULL, @by, @at)", new { id, customerId, advisorId, b.Subject, by = p.Sub, at = Nordic.Iso(clock.LocalNow) });
            return Results.Created($"/api/v1/conversations/{id}", new { id, customerId, advisorId, subject = b.Subject, createdBy = p.Sub });
        }).WithTags("Conversaciones").WithSummary("Abrir una conversación cliente-asesor (el cliente habla siempre con SU asesor)");

        app.MapPost("/api/v1/conversations/{id}/messages", async (HttpContext http, string id, MessageRequest b) =>
        {
            var (fail, c, p) = await Member(http, clock, ctx, id);
            if (fail is not null) return fail;
            if (string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > 5000) return Problems.BadRequest(http, "VALIDATION_FAILED", "text entre 1 y 5000 caracteres.", null);
            if (b.ExpiresInSeconds is < 60 or > 30 * 86400) return Problems.BadRequest(http, "VALIDATION_FAILED", "expiresInSeconds entre 60 y 30 días.", null);
            var readOnce = b.ReadOnce ?? false;
            var scan = await Downstream.Post(http, "redaction-service", "/api/v1/scan", new { text = b.Text });
            if (scan.Ok && scan.Json.GetProperty("containsPii").GetBoolean() && !readOnce)
                return Problems.Unprocessable(http, "PII_NOT_ALLOWED", "El mensaje contiene datos personales (número nacional, IBAN, tarjeta...).",
                    "Mándalo como lectura única (readOnce: true): se destruye en cuanto el destinatario lo abre.");
            var key = await Key(id);
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var plain = Encoding.UTF8.GetBytes(b.Text); var cipher = new byte[plain.Length];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
            var exp = b.ExpiresInSeconds is { } s ? clock.UtcNow.AddSeconds(s) : (DateTimeOffset?)null;
            var mid = "msg_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            var seq = db.ExecuteScalar<long>("SELECT COALESCE(MAX(seq), 0) + 1 FROM messages WHERE conversation_id = @id", new { id });
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO messages VALUES (@mid, @id, @seq, @sub, @role, @cipher, @ro, @et, @ea, 'ACTIVE', @at, NULL)",
                new { mid, id, seq, sub = p.Sub, role = p.Role, cipher = Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray()), ro = readOnce ? 1 : 0,
                      et = exp?.UtcTicks, ea = exp is null ? null : Nordic.Iso(Nordic.ToStockholm(exp.Value)), at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "message.sent", mid, new { messageId = mid, conversationId = id, senderRole = p.Role, readOnce }, tx);
            tx.Commit();
            if (p.Role == "Advisor")
                try { await Downstream.Post(http, "notification-service", "/api/v1/notifications", new { customerId = c.Cust, templateKey = "message.new", category = "TRANSACTIONAL", reference = mid, vars = new { firstName = "" } }, forwardAuth: false); }
                catch (DependencyUnavailableException) { }
            return Results.Created($"/api/v1/conversations/{id}/messages", new { id = mid, seq, readOnce, expiresAt = exp is null ? null : Nordic.Iso(Nordic.ToStockholm(exp.Value)) });
        }).WithTags("Mensajes").WithSummary("Enviar un mensaje (con PII solo como lectura única)");

        app.MapGet("/api/v1/conversations/{id}/messages", async (HttpContext http, string id) =>
        {
            var (fail, _, p) = await Member(http, clock, ctx, id);
            if (fail is not null) return fail;
            var key = await Key(id);
            using var db = ctx.OpenDb();
            var now = clock.UtcNow.UtcTicks;
            var items = db.Query<(string Id, long Seq, string Sub, string Role, string? Cipher, long Ro, long? Et, string? Ea, string Status, string At, string? Opened)>(
                "SELECT id, seq, sender_sub, sender_role, cipher, read_once, expires_ticks, expires_at, status, sent_at, opened_at FROM messages WHERE conversation_id = @id ORDER BY seq", new { id })
                .Select(m =>
                {
                    var status = m.Status == "ACTIVE" && m.Et is { } e && now >= e ? "EXPIRED" : m.Status;
                    string? text = null;
                    if (status == "ACTIVE" && m.Ro == 0 && m.Cipher is not null)
                    {
                        var all = Convert.FromBase64String(m.Cipher); var plain = new byte[all.Length - 28];
                        using (var aes = new AesGcm(key, 16)) aes.Decrypt(all[..12], all[28..], all[12..28], plain);
                        text = Encoding.UTF8.GetString(plain);
                    }
                    return new { id = m.Id, seq = m.Seq, from = m.Role, sender = m.Sub, readOnce = m.Ro == 1, status, text, hidden = m.Ro == 1 && status == "ACTIVE", expiresAt = m.Ea, sentAt = m.At, openedAt = m.Opened };
                }).ToList();
            return Results.Ok(new { conversationId = id, items, count = items.Count });
        }).WithTags("Mensajes").WithSummary("Mensajes de la conversación (los de lectura única salen ocultos)");

        app.MapPost("/api/v1/conversations/{id}/messages/{messageId}/open", async (HttpContext http, string id, string messageId) =>
        {
            var (fail, _, p) = await Member(http, clock, ctx, id);
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            var m = db.QuerySingleOrDefault<(string Id, string Sub, string? Cipher, long Ro, long? Et, string Status, string? Opened)>(
                "SELECT id, sender_sub, cipher, read_once, expires_ticks, status, opened_at FROM messages WHERE id = @messageId AND conversation_id = @id", new { messageId, id });
            if (m.Id is null) return Problems.NotFound(http, "MESSAGE_NOT_FOUND", $"No existe el mensaje {messageId}.", null);
            if (m.Ro == 0) return Problems.BadRequest(http, "NOT_READ_ONCE", "Este mensaje no es de lectura única: se lee en el listado.", null);
            if (m.Sub == p.Sub) return Problems.Of(http, 403, "SENDER_CANNOT_OPEN", "Prohibido", "Quien envía un mensaje de lectura única no puede abrirlo.", null);
            if (m.Status == "DESTROYED") return Problems.Of(http, 410, "MESSAGE_DESTROYED", "Mensaje destruido", $"Se abrió el {m.Opened} y se destruyó.", "Era de lectura única: es lo esperado.");
            if (m.Et is { } e && clock.UtcNow.UtcTicks >= e) return Problems.Of(http, 410, "MESSAGE_EXPIRED", "Mensaje caducado", "Caducó sin que nadie lo abriera.", null);
            var key = await Key(id);
            var all = Convert.FromBase64String(m.Cipher!); var plain = new byte[all.Length - 28];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(all[..12], all[28..], all[12..28], plain);
            var at = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            if (db.Execute("UPDATE messages SET status = 'DESTROYED', cipher = NULL, opened_at = @at WHERE id = @messageId AND status = 'ACTIVE'", new { at, messageId }, tx) == 0)
            { tx.Rollback(); return Problems.Of(http, 410, "MESSAGE_DESTROYED", "Mensaje destruido", "Otra petición lo abrió antes.", null); }
            Outbox.Add(db, http, clock, "message.destroyed", messageId, new { messageId, conversationId = id }, tx);
            tx.Commit();
            return Results.Ok(new { id = messageId, text = Encoding.UTF8.GetString(plain), openedAt = at, destroyed = true, warning = "Lectura única: ya no se podrá volver a leer." });
        }).WithTags("Mensajes").WithSummary("Abrir un mensaje de lectura única (solo el destinatario, una sola vez)");

        app.MapGet("/api/v1/customers/{customerId}/conversations", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Advisor" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Id, string Subject, string At, long N)>(
                "SELECT c.id, c.subject, c.created_at, (SELECT COUNT(*) FROM messages m WHERE m.conversation_id = c.id) FROM conversations c WHERE c.customer_id = @customerId AND (@adv IS NULL OR c.advisor_id = @adv) ORDER BY c.created_at",
                new { customerId, adv = p.Role == "Advisor" ? p.AdvisorId : null }).Select(c => new { id = c.Id, subject = c.Subject, createdAt = c.At, messages = c.N }).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Conversaciones").WithSummary("Conversaciones de un cliente");
    }
}
