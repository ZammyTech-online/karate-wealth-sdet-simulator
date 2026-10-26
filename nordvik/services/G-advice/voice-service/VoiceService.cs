using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record StartCallRequest(string? CustomerId, string? Channel);
public sealed record ChunkRequest(int? Turn, string? Speaker, int? StartMs, int? EndMs, string? Text, double? Confidence, string? Language);
public sealed record PlayScriptRequest(string? ScriptId, Dictionary<string, string>? Variables);
public sealed record ConfirmInsightRequest(bool? Accept, JsonElement? Value);
public sealed record ReceiptVerifyRequest(string? Receipt, string? Signature);

/// <summary>
/// voice-service: la llamada asesor-cliente de principio a fin. Sin consentimiento de grabación no hay llamada (409).
/// Los turnos se guardan cifrados con una clave por llamada; al colgar arranca la cadena por eventos:
/// transcripción y redacción → extracción de datos con evidencia → revisión del asesor → resumen sin PII → destrucción con recibo firmado.
/// RECORDING → TRANSCRIBING → EXTRACTING → AWAITING_REVIEW → SUMMARISING → SHREDDED (o ENDED_NOT_RECORDED si se revoca la grabación).
/// </summary>
public sealed class VoiceService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "voice-service", "G-advice", 6606, 3, "Asesoramiento y comunicación",
        "Llamadas grabadas con consentimiento: turnos cifrados por llamada, cadena de eventos (transcripción, redacción, extracción, revisión, resumen) y destrucción verificable con recibo firmado.",
        DependsOn: new[] { "consent-ledger", "key-management", "telephony-sim", "transcription-service", "insight-extraction", "call-summary", "retention-scheduler", "customer-registry", "clock-service" },
        Emits: new[] { "voice.call.started", "voice.call.ended", "voice.recording.revoked", "voice.review.completed", "voice.call.shredded" },
        Consumes: new[] { "voice.transcript.redacted", "voice.insights.extracted", "voice.call.summarised" });

    private const string ReceiptKey = "nordvik-voice-receipts-2026";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS calls (id TEXT PRIMARY KEY, customer_id TEXT, advisor_sub TEXT, advisor_id TEXT, channel TEXT, status TEXT, recording INTEGER, provider_call_id TEXT,
                        key_id TEXT, key_version INTEGER, retention_item TEXT, started_at TEXT, ended_at TEXT, duration_ms INTEGER, turns INTEGER, shredded_at TEXT, receipt TEXT, receipt_sig TEXT, orders TEXT, health INTEGER);
                     CREATE TABLE IF NOT EXISTS chunks (call_id TEXT, turn INTEGER, speaker TEXT, start_ms INTEGER, end_ms INTEGER, confidence REAL, language TEXT, cipher TEXT, PRIMARY KEY (call_id, turn))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Adv, string Channel, string Status, long Rec, string? Prov, string? Ret, string Started, string? Ended, long? Dur, long? Turns, string? Shredded, string? Orders, long? Health)>(
            "SELECT id, customer_id, advisor_sub, channel, status, recording, provider_call_id, retention_item, started_at, ended_at, duration_ms, turns, shredded_at, orders, health FROM calls WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var chunks = db.ExecuteScalar<long>("SELECT COUNT(*) FROM chunks WHERE call_id = @id", new { id });
        return new
        {
            id = r.Id, customerId = r.Cust, advisor = r.Adv, channel = r.Channel, status = r.Status, recording = r.Rec == 1, providerCallId = r.Prov, retentionItemId = r.Ret,
            startedAt = r.Started, endedAt = r.Ended, durationMs = r.Dur, turns = r.Turns ?? chunks, storedChunks = chunks, healthMentioned = r.Health == 1,
            orders = r.Orders is null ? (JsonElement?)null : JsonDocument.Parse(r.Orders).RootElement, shreddedAt = r.Shredded,
        };
    }

    private static (string Id, string Cust, string Adv, string Status, long Rec, string? KeyId, long? KeyVer) Raw(Microsoft.Data.Sqlite.SqliteConnection db, string id) =>
        db.QuerySingleOrDefault<(string, string, string, string, long, string?, long?)>("SELECT id, customer_id, advisor_sub, status, recording, key_id, key_version FROM calls WHERE id = @id", new { id });

    private static async Task<byte[]?> KeyOf(string keyId, long version)
    {
        var m = await Downstream.Get(null, "key-management", $"/api/v1/internal/keys/{keyId}/versions/{version}", forwardAuth: false);
        return m.Ok ? Convert.FromBase64String(m.Str("material")!) : null;
    }

    private static string Seal(byte[] key, string text)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var plain = Encoding.UTF8.GetBytes(text); var c = new byte[plain.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, c, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(c).ToArray());
    }

    private static string Open(byte[] key, string b64)
    {
        var all = Convert.FromBase64String(b64); var plain = new byte[all.Length - 28];
        using (var aes = new AesGcm(key, 16)) aes.Decrypt(all[..12], all[28..], all[12..28], plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Destruye todo lo grabado de una llamada: turnos, clave (crypto-shredding) y deja un recibo firmado.</summary>
    private static async Task Shred(ServiceContext ctx, HttpContext? http, IClock clock, string id, string reason, string finalStatus)
    {
        using var db = ctx.OpenDb();
        var chunks = db.ExecuteScalar<long>("SELECT COUNT(*) FROM chunks WHERE call_id = @id", new { id });
        try { await Downstream.Post(http, "key-management", "/api/v1/internal/subjects/shred", new { subject = "call:" + id, purpose = reason }, forwardAuth: false); }
        catch (DependencyUnavailableException) { /* la clave caduca igualmente por retención */ }
        try { await Downstream.Send(http, "transcription-service", HttpMethod.Delete, $"/api/v1/internal/transcripts/{id}", null, forwardAuth: false); }
        catch (DependencyUnavailableException) { /* también se borra al recibir voice.call.shredded */ }
        var at = Nordic.Iso(clock.LocalNow);
        var receipt = Canonical.Of(new { callId = id, reason, destroyed = new { audioChunks = chunks, transcripts = "deleted by transcription-service on voice.call.shredded", keys = "call:" + id + " shredded in key-management" }, at });
        var sig = Jws.SignDetached(receipt, ReceiptKey, "voice-receipts-1");
        using var tx = db.BeginTransaction();
        db.Execute("DELETE FROM chunks WHERE call_id = @id", new { id }, tx);
        db.Execute("UPDATE calls SET status = @finalStatus, recording = 0, shredded_at = @at, receipt = @receipt, receipt_sig = @sig WHERE id = @id", new { finalStatus, at, receipt, sig, id }, tx);
        Outbox.Add(db, http, clock, "voice.call.shredded", id, new { callId = id, reason, chunksDestroyed = chunks }, tx);
        tx.Commit();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        var g = app.MapGroup("/api/v2").WithTags("Llamadas");

        async Task<(IResult? Fail, (string Id, string Cust, string Adv, string Status, long Rec, string? KeyId, long? KeyVer) Call, Principal P)> Access(HttpContext http, string id, bool advisorOnly = false)
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return (denied, default, p);
            using var db = ctx.OpenDb();
            var c = Raw(db, id);
            if (c.Id is null) return (Problems.NotFound(http, "CALL_NOT_FOUND", $"No existe la llamada {id}.", null), c, p);
            var ok = p.Role is "Compliance" or "Admin" or "Service" && !advisorOnly || p.Sub == c.Adv || (!advisorOnly && p.Role == "Customer" && p.CustomerId == c.Cust);
            if (!ok) return (p.Role == "Customer" ? Problems.NotFound(http, "CALL_NOT_FOUND", $"No existe la llamada {id}.", null)
                                                  : Problems.Of(http, 403, "NOT_YOUR_CALL", "Prohibido", "Solo el asesor de la llamada (o Cumplimiento) puede hacer esto.", null), c, p);
            await Task.CompletedTask;
            return (null, c, p);
        }

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var callId = ev.Payload.GetProperty("callId").GetString()!;
            using var db = ctx.OpenDb();
            switch (ev.Topic)
            {
                case "voice.transcript.redacted":
                    db.Execute("UPDATE calls SET status = 'EXTRACTING', health = @h WHERE id = @callId AND status = 'TRANSCRIBING'",
                        new { callId, h = ev.Payload.TryGetProperty("healthMentioned", out var hm) && hm.GetBoolean() ? 1 : 0 });
                    break;
                case "voice.insights.extracted":
                    var fields = ev.Payload.GetProperty("fields").GetInt32();
                    if (fields > 0) db.Execute("UPDATE calls SET status = 'AWAITING_REVIEW' WHERE id = @callId AND status = 'EXTRACTING'", new { callId });
                    else
                    {
                        using var tx = db.BeginTransaction();
                        if (db.Execute("UPDATE calls SET status = 'SUMMARISING' WHERE id = @callId AND status = 'EXTRACTING'", new { callId }, tx) == 1)
                            Outbox.Add(db, null, clock, "voice.review.completed", callId, new { callId, confirmedFields = 0, automatic = true }, tx);
                        tx.Commit();
                    }
                    break;
                case "voice.call.summarised":
                    await Shred(ctx, null, clock, callId, "ciclo de vida completo: resumen guardado", "SHREDDED");
                    break;
            }
        });

        g.MapPost("/calls", async (HttpContext http, StartCallRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor" }) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.CustomerId)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta customerId.", null);
            if (await CustomerGuard.Check(http, p, "customer:read", b.CustomerId) is { } gd) return gd;
            var consent = await Downstream.Get(http, "consent-ledger", $"/api/v1/internal/consents/check?customerId={b.CustomerId}&purpose=VOICE_RECORDING", forwardAuth: false);
            if (!consent.Ok || !consent.Json.GetProperty("granted").GetBoolean())
                return Problems.Conflict(http, "RECORDING_CONSENT_MISSING", "El cliente no ha dado su consentimiento para grabar llamadas.",
                    "Sin consentimiento VOICE_RECORDING en consent-ledger no se graba nada. Regístralo primero o atiende sin grabar.");
            var id = "call_" + Guid.NewGuid().ToString("N")[..12];
            var key = await Downstream.Post(http, "key-management", "/api/v1/internal/keys/resolve", new { subject = "call:" + id, purpose = "VOICE" }, forwardAuth: false);
            var prov = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("telephony-sim") + "/v1/calls", new { from = p.Sub, to = "customer" }, null, TimeSpan.FromSeconds(5));
            if (!prov.Ok) return Problems.Of(http, 503, "TELEPHONY_UNAVAILABLE", "Centralita no disponible", $"La telefonía respondió {prov.Status}.", null);
            string? retention = null;
            try
            {
                var ret = await Downstream.Send(http, "retention-scheduler", HttpMethod.Post, "/api/v1/items", new { dataClass = "VOICE_AUDIO", ownerService = "voice-service", @ref = id, subject = "call:" + id },
                    forwardAuth: false, headers: await ServiceToken.Header(clock));
                retention = ret.Ok ? ret.Str("id") : null;
            }
            catch (DependencyUnavailableException) { }
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO calls VALUES (@id, @c, @adv, @advId, @ch, 'RECORDING', 1, @prov, @kid, @kv, @ret, @at, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0)",
                new { id, c = b.CustomerId, adv = p.Sub, advId = p.AdvisorId, ch = (b.Channel ?? "PHONE").ToUpperInvariant(), prov = prov.Str("providerCallId"), kid = key.Str("keyId"),
                      kv = key.Json.GetProperty("version").GetInt32(), ret = retention, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "voice.call.started", id, new { callId = id, customerId = b.CustomerId, advisor = p.Sub }, tx);
            tx.Commit();
            return Results.Created($"/api/v2/calls/{id}", Load(db, id));
        }).WithSummary("Iniciar una llamada grabada (asesor; exige consentimiento VOICE_RECORDING)");

        g.MapPost("/calls/{id}/chunks", async (HttpContext http, string id, ChunkRequest[] chunks) =>
        {
            var (fail, c, _) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (c.Status != "RECORDING") return Problems.Conflict(http, "CALL_NOT_RECORDING", $"La llamada está {c.Status}.", null);
            var errors = new List<FieldError>();
            for (var i = 0; i < chunks.Length; i++)
            {
                var t = chunks[i];
                if (t.Turn is null or < 1) errors.Add(new FieldError($"[{i}].turn", "entero desde 1"));
                if (t.Speaker is not ("ADVISOR" or "CUSTOMER")) errors.Add(new FieldError($"[{i}].speaker", "ADVISOR o CUSTOMER"));
                if (string.IsNullOrWhiteSpace(t.Text)) errors.Add(new FieldError($"[{i}].text", "obligatorio"));
                if (t.Confidence is < 0 or > 1) errors.Add(new FieldError($"[{i}].confidence", "entre 0 y 1"));
            }
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Turnos no válidos.", "Ejemplo: [ { \"turn\": 1, \"speaker\": \"CUSTOMER\", \"startMs\": 0, \"endMs\": 3000, \"text\": \"Hola\", \"confidence\": 0.95 } ]", errors);
            if (c.Rec == 0) return Results.Json(new { accepted = 0, duplicates = 0, recorded = false, note = "La grabación se revocó: los turnos no se guardan." }, statusCode: 202);
            var key = await KeyOf(c.KeyId!, c.KeyVer ?? 1);
            if (key is null) return Problems.Of(http, 410, "CALL_KEY_SHREDDED", "Clave destruida", "La clave de la llamada ya no existe.", null);
            using var db = ctx.OpenDb();
            int accepted = 0, dups = 0;
            foreach (var t in chunks.OrderBy(x => x.Turn))
            {
                var n = db.Execute("INSERT OR IGNORE INTO chunks VALUES (@id, @Turn, @Speaker, @s, @e, @conf, @lang, @cipher)",
                    new { id, t.Turn, t.Speaker, s = t.StartMs ?? 0, e = t.EndMs ?? t.StartMs ?? 0, conf = t.Confidence ?? 0.9, lang = t.Language ?? "es", cipher = Seal(key, t.Text!) });
                if (n == 1) accepted++; else dups++;
            }
            return Results.Json(new { accepted, duplicates = dups, recorded = true }, statusCode: 202);
        }).WithSummary("Enviar turnos (orden por turn; los turnos repetidos se ignoran)");

        g.MapPost("/calls/{id}/play-script", async (HttpContext http, string id, PlayScriptRequest b) =>
        {
            var (fail, c, _) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (string.IsNullOrWhiteSpace(b.ScriptId)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta scriptId.", "Lista: GET http://localhost:7712/v1/scripts");
            var r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("telephony-sim") + $"/v1/scripts/{b.ScriptId}/render", new { variables = b.Variables }, null, TimeSpan.FromSeconds(5));
            if (r.Status == 404) return Problems.NotFound(http, "SCRIPT_NOT_FOUND", $"La centralita no tiene el guion {b.ScriptId}.", null);
            if (!r.Ok) return Problems.Of(http, 503, "TELEPHONY_UNAVAILABLE", "Centralita no disponible", $"La telefonía respondió {r.Status}.", null);
            var turns = JsonSerializer.Deserialize<ChunkRequest[]>(r.Json.GetProperty("turns").GetRawText(), Canonical.Web)!;
            var res = await Downstream.Send(http, "voice-service", HttpMethod.Post, $"/api/v2/calls/{id}/chunks", turns);
            return Results.Content(res.Raw, "application/json", Encoding.UTF8, res.Status);
        }).WithSummary("Reproducir un guion de la centralita (E12) como turnos de esta llamada");

        g.MapPost("/calls/{id}/end", async (HttpContext http, string id) =>
        {
            var (fail, c, _) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (c.Status != "RECORDING") return Problems.Conflict(http, "CALL_NOT_RECORDING", $"La llamada ya está {c.Status}.", null);
            using var db = ctx.OpenDb();
            var stats = db.QuerySingle<(long N, long? MaxEnd)>("SELECT COUNT(*), MAX(end_ms) FROM chunks WHERE call_id = @id", new { id });
            var status = c.Rec == 1 && stats.N > 0 ? "TRANSCRIBING" : "ENDED_NOT_RECORDED";
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE calls SET status = @status, ended_at = @at, duration_ms = @d, turns = @n WHERE id = @id", new { status, at = Nordic.Iso(clock.LocalNow), d = stats.MaxEnd ?? 0, n = stats.N, id }, tx);
            if (status == "TRANSCRIBING") Outbox.Add(db, http, clock, "voice.call.ended", id, new { callId = id, customerId = c.Cust, turns = stats.N }, tx);
            tx.Commit();
            return Results.Json(Load(db, id), statusCode: 202);
        }).WithSummary("Colgar: 202 y arranca la cadena de eventos");

        g.MapGet("/calls/{id}", async (HttpContext http, string id) =>
        {
            var (fail, _, _) = await Access(http, id);
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithSummary("Estado de la llamada (RECORDING → TRANSCRIBING → EXTRACTING → AWAITING_REVIEW → SUMMARISING → SHREDDED)");

        g.MapGet("/calls/{id}/transcript", async (HttpContext http, string id) =>
        {
            var (fail, c, p) = await Access(http, id);
            if (fail is not null) return fail;
            if (p.Role == "Customer") return Problems.Of(http, 403, "ROLE_NOT_ALLOWED", "Prohibido", "El cliente recibe el resumen, no la transcripción.", null);
            var r = await Downstream.Get(http, "transcription-service", $"/api/v1/transcripts/{id}", forwardAuth: false, timeout: null);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("Transcripción redactada (sin PII; solo antes de la destrucción)");

        g.MapGet("/calls/{id}/transcript/raw", async (HttpContext http, string id, string? purpose) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Compliance" }) is { } denied) return denied;
            var r = await Downstream.Get(http, "transcription-service", $"/api/v1/transcripts/{id}/raw?purpose={Uri.EscapeDataString(purpose ?? "")}&by={Uri.EscapeDataString(p.Sub)}", forwardAuth: false);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("Transcripción original (Cumplimiento, con purpose, UNA sola lectura)");

        g.MapGet("/calls/{id}/redactions/{index:int}/reveal", async (HttpContext http, string id, int index, string? purpose) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Compliance" }) is { } denied) return denied;
            var r = await Downstream.Get(http, "transcription-service", $"/api/v1/transcripts/{id}/redactions/{index}/reveal?purpose={Uri.EscapeDataString(purpose ?? "")}&by={Uri.EscapeDataString(p.Sub)}", forwardAuth: false);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("Ver el valor original de una redacción (Cumplimiento, una sola lectura por redacción)");

        g.MapGet("/calls/{id}/insights", async (HttpContext http, string id) =>
        {
            var (fail, _, p) = await Access(http, id);
            if (fail is not null) return fail;
            if (p.Role == "Customer") return Problems.Of(http, 403, "ROLE_NOT_ALLOWED", "Prohibido", "Los datos extraídos son para el asesor.", null);
            var r = await Downstream.Get(http, "insight-extraction", $"/api/v1/insights/{id}", forwardAuth: false);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("Datos extraídos con su evidencia (campo, valor, turno, cita, confianza, needsHumanReview)");

        g.MapPost("/calls/{id}/insights/{field}/confirm", async (HttpContext http, string id, string field, ConfirmInsightRequest b) =>
        {
            var (fail, c, _) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (c.Status != "AWAITING_REVIEW") return Problems.Conflict(http, "CALL_NOT_IN_REVIEW", $"La llamada está {c.Status}.", null);
            var r = await Downstream.Post(http, "insight-extraction", $"/api/v1/internal/insights/{id}/{field}/confirm", new { accept = b.Accept ?? true, value = b.Value }, forwardAuth: false);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("El asesor de la llamada confirma (o descarta) un dato extraído; puede corregir el valor");

        g.MapPost("/calls/{id}/review/complete", async (HttpContext http, string id) =>
        {
            var (fail, c, p) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (c.Status != "AWAITING_REVIEW") return Problems.Conflict(http, "CALL_NOT_IN_REVIEW", $"La llamada está {c.Status}.", null);
            var ins = await Downstream.Get(http, "insight-extraction", $"/api/v1/insights/{id}", forwardAuth: false);
            var items = ins.Json.GetProperty("items").EnumerateArray().ToList();
            var pending = items.Where(i => i.GetProperty("needsHumanReview").GetBoolean() && i.GetProperty("status").GetString() == "EXTRACTED").Select(i => i.GetProperty("field").GetString()).ToList();
            if (pending.Count > 0) return Problems.Conflict(http, "REVIEW_PENDING", "Quedan datos por confirmar: " + string.Join(", ", pending), "Confírmalos o descártalos con POST /insights/{field}/confirm.");
            var orders = new List<object>();
            foreach (var o in items.Where(i => i.GetProperty("field").GetString() == "orderIntent" && i.GetProperty("status").GetString() == "CONFIRMED"))
            {
                var v = o.GetProperty("value");
                if (!v.TryGetProperty("portfolioId", out var pf)) { orders.Add(new { turn = o.GetProperty("evidence").GetProperty("turn").GetInt32(), placed = false, reason = "sin portfolioId en la confirmación" }); continue; }
                var turn = o.GetProperty("evidence").GetProperty("turn").GetInt32();
                var key = $"call-{id}-turn-{turn}";
                var order = await Downstream.Send(http, "order-management", HttpMethod.Post, "/api/v1/orders",
                    new { portfolioId = pf.GetString(), isin = v.GetProperty("isin").GetString(), side = v.GetProperty("side").GetString(), amount = v.GetProperty("amount").GetString() },
                    forwardAuth: true, headers: new Dictionary<string, string> { ["Idempotency-Key"] = key });
                orders.Add(new { turn, placed = order.Ok, orderId = order.Ok ? order.Str("id") : null, status = order.Status, idempotencyKey = key });
            }
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE calls SET status = 'SUMMARISING', orders = @o WHERE id = @id", new { o = JsonSerializer.Serialize(orders), id }, tx);
            Outbox.Add(db, http, clock, "voice.review.completed", id, new { callId = id, confirmedFields = items.Count(i => i.GetProperty("status").GetString() == "CONFIRMED"), automatic = false }, tx);
            tx.Commit();
            return Results.Json(Load(db, id), statusCode: 202);
        }).WithSummary("Terminar la revisión: lanza las órdenes confirmadas (Idempotency-Key derivada del callId) y pasa a resumen");

        g.MapGet("/calls/{id}/summary", async (HttpContext http, string id) =>
        {
            var (fail, _, _) = await Access(http, id);
            if (fail is not null) return fail;
            var r = await Downstream.Get(http, "call-summary", $"/api/v1/summaries/{id}", forwardAuth: false);
            return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, r.Status);
        }).WithSummary("Resumen de 5 líneas sin PII (se conserva 10 años; disponible tras la destrucción)");

        g.MapPost("/calls/{id}/revoke-recording", async (HttpContext http, string id) =>
        {
            var (fail, c, _) = await Access(http, id, advisorOnly: true);
            if (fail is not null) return fail;
            if (c.Status != "RECORDING" || c.Rec == 0) return Problems.Conflict(http, "NOT_RECORDING", "La llamada ya no se está grabando.", null);
            await Shred(ctx, http, clock, id, "el cliente revocó la grabación durante la llamada", "RECORDING");
            using var db = ctx.OpenDb();
            Outbox.Add(db, http, clock, "voice.recording.revoked", id, new { callId = id, customerId = c.Cust });
            return Results.Ok(Load(db, id));
        }).WithSummary("El cliente revoca la grabación: lo grabado se destruye ya y la llamada sigue sin grabar");

        g.MapGet("/calls/{id}/destruction-receipt", async (HttpContext http, string id) =>
        {
            var (fail, _, _) = await Access(http, id);
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            var r = db.QuerySingle<(string? Receipt, string? Sig)>("SELECT receipt, receipt_sig FROM calls WHERE id = @id", new { id });
            if (r.Receipt is null) return Problems.Conflict(http, "NOT_DESTROYED_YET", "Esta llamada todavía no se ha destruido.", "Espera al estado SHREDDED (retry until).");
            return Results.Ok(new { receipt = r.Receipt, signature = r.Sig, algorithm = "HS256 (JWS detached)", verify = "POST /api/v2/receipts/verify con receipt y signature exactos" });
        }).WithSummary("Recibo de destrucción firmado (JWS con payload separado)");

        g.MapPost("/receipts/verify", (ReceiptVerifyRequest b) =>
            Results.Ok(new { valid = b.Receipt is not null && Jws.VerifyDetached(b.Receipt, b.Signature, ReceiptKey) }))
            .WithSummary("Verificar un recibo: cambia un solo carácter del receipt y deja de ser válido");

        g.MapGet("/calls", (HttpContext http, string? advisor, string? from, string? to, string? status) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Compliance", "Admin" }) is { } denied) return denied;
            var adv = p.Role == "Advisor" ? p.Sub : advisor;
            using var db = ctx.OpenDb();
            var items = db.Query<string>(@"SELECT id FROM calls WHERE (@adv IS NULL OR advisor_sub = @adv) AND (@status IS NULL OR status = @status)
                                           AND (@from IS NULL OR started_at >= @from) AND (@to IS NULL OR started_at < @to) ORDER BY started_at, id",
                new { adv, status = status?.ToUpperInvariant(), from, to }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithSummary("Listado de llamadas: solo metadatos (un asesor ve las suyas)");

        g.MapGet("/customers/{customerId}/calls", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } gd) return gd;
            using var db = ctx.OpenDb();
            var items = new List<object>();
            foreach (var id in db.Query<string>("SELECT id FROM calls WHERE customer_id = @customerId ORDER BY started_at", new { customerId }).ToList())
            {
                var s = await Downstream.Get(http, "call-summary", $"/api/v1/summaries/{id}", forwardAuth: false);
                var r = db.QuerySingle<(string Status, string Started, string? Shredded, string? Receipt)>("SELECT status, started_at, shredded_at, receipt FROM calls WHERE id = @id", new { id });
                items.Add(new { callId = id, status = r.Status, startedAt = r.Started, audioAvailable = false, summary = s.Ok ? s.Json.GetProperty("lines") : (JsonElement?)null,
                                destroyedAt = r.Shredded, destructionReceipt = r.Receipt is null ? null : $"/api/v2/calls/{id}/destruction-receipt" });
            }
            return Results.Ok(new { customerId, items, count = items.Count, note = "Derecho de acceso: el audio y la transcripción no se conservan; se entregan el resumen, los metadatos y el recibo de destrucción." });
        }).WithSummary("Acceso del cliente a sus llamadas (V10): resumen, metadatos y recibo; nunca audio");

        app.MapGet("/api/v2/internal/calls/{id}/turns", async (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var c = Raw(db, id);
            if (c.Id is null) return Problems.NotFound(http, "CALL_NOT_FOUND", $"No existe la llamada {id}.", null);
            var key = await KeyOf(c.KeyId!, c.KeyVer ?? 1);
            if (key is null) return Problems.Of(http, 410, "CALL_KEY_SHREDDED", "Clave destruida", "La clave de la llamada ya no existe.", null);
            var turns = db.Query<(long Turn, string Speaker, long S, long E, double Conf, string Lang, string Cipher)>("SELECT turn, speaker, start_ms, end_ms, confidence, language, cipher FROM chunks WHERE call_id = @id ORDER BY turn", new { id })
                .Select(t => new { turn = t.Turn, speaker = t.Speaker, startMs = t.S, endMs = t.E, confidence = t.Conf, language = t.Lang, text = Open(key, t.Cipher) }).ToList();
            return Results.Ok(new { callId = id, customerId = c.Cust, advisor = c.Adv, turns });
        }).WithTags("Interno").WithSummary("(interno) Turnos descifrados para transcription-service");
    }
}
