using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RedactedTurn(int Turn, string Speaker, long StartMs, long EndMs, double Confidence, string Language, string Text);
public sealed record Redaction(int Index, int Turn, string Type, string? VaultRef, bool Revealed);

/// <summary>
/// transcription-service: de turnos a transcripción. Al colgar (voice.call.ended) pide los turnos a voice-service, monta la transcripción
/// con hablantes y tiempos y la redacta: los identificadores van a pii-vault y en el texto queda [NATIONAL_ID], [IBAN]...; las menciones de salud
/// se sustituyen por [HEALTH] y no se guardan en ningún sitio. El original se guarda cifrado con la clave de la llamada (muere con ella),
/// se lee una sola vez y solo Cumplimiento con un propósito.
/// </summary>
public sealed class TranscriptionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "transcription-service", "G-advice", 6607, 3, "Asesoramiento y comunicación",
        "Transcripción con hablantes y tiempos, redactada ([NATIONAL_ID], [IBAN], [HEALTH]...) con originales en pii-vault; el original completo se lee una sola vez y muere con la clave de la llamada.",
        DependsOn: new[] { "voice-service", "redaction-service", "pii-vault", "key-management", "clock-service" },
        Emits: new[] { "voice.transcript.redacted" }, Consumes: new[] { "voice.call.ended", "voice.call.shredded" });

    private static readonly Regex Health = new(@"\b(diabetes|c[aá]ncer|cancer|depresi[oó]n|depression|ansiedad|anxiety|embarazada|pregnant|gravid|infarto|VIH|HIV|esclerosis|epilepsia|epilepsy)\b", RegexOptions.IgnoreCase);
    public static readonly string[] Purposes = { "COMPLAINT_INVESTIGATION", "REGULATORY_REQUEST", "LEGAL_HOLD", "QUALITY_REVIEW" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS transcripts (call_id TEXT PRIMARY KEY, customer_id TEXT, language TEXT, duration_ms INTEGER, speakers TEXT, turns TEXT, raw_cipher TEXT, key_id TEXT, key_version INTEGER,
                        raw_read_at TEXT, raw_read_by TEXT, health INTEGER, created_at TEXT);
                     CREATE TABLE IF NOT EXISTS redactions (call_id TEXT, idx INTEGER, turn INTEGER, type TEXT, token TEXT, revealed_at TEXT, revealed_by TEXT, PRIMARY KEY (call_id, idx))");
    }

    private static string Marker(string type) => "[" + type + "]";

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var callId = ev.Payload.GetProperty("callId").GetString()!;
            using var db = ctx.OpenDb();
            if (ev.Topic == "voice.call.shredded")
            {
                db.Execute("DELETE FROM transcripts WHERE call_id = @callId; DELETE FROM redactions WHERE call_id = @callId", new { callId });
                return;
            }
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM transcripts WHERE call_id = @callId", new { callId }) > 0) return;
            var src = await Downstream.Get(null, "voice-service", $"/api/v2/internal/calls/{callId}/turns", forwardAuth: false);
            if (!src.Ok) throw new InvalidOperationException("voice-service no dio los turnos: " + src.Status);
            var customerId = src.Str("customerId");
            var svc = await ServiceToken.Header(clock);
            var turns = new List<RedactedTurn>(); var redactions = new List<Redaction>(); var raw = new List<object>(); var health = false;
            foreach (var t in src.Json.GetProperty("turns").EnumerateArray())
            {
                var text = t.GetProperty("text").GetString()!;
                raw.Add(new { turn = t.GetProperty("turn").GetInt32(), speaker = t.GetProperty("speaker").GetString(), text });
                var red = await Downstream.Send(null, "redaction-service", HttpMethod.Post, "/api/v1/redact", new { text, mode = "TOKENIZE", customerId }, forwardAuth: false, headers: svc);
                if (!red.Ok) throw new InvalidOperationException("redaction-service respondió " + red.Status);
                var redacted = red.Str("redacted")!;
                foreach (var f in red.Json.GetProperty("findings").EnumerateArray())
                {
                    var type = f.GetProperty("type").GetString()!;
                    var token = f.GetProperty("replacement").GetString()!;
                    redacted = redacted.Replace(token, Marker(type));
                    redactions.Add(new Redaction(redactions.Count, t.GetProperty("turn").GetInt32(), type, token.StartsWith("tok_") ? token : null, false));
                }
                if (Health.IsMatch(redacted))
                {
                    health = true;
                    foreach (Match _ in Health.Matches(redacted)) redactions.Add(new Redaction(redactions.Count, t.GetProperty("turn").GetInt32(), "HEALTH", null, false));
                    redacted = Health.Replace(redacted, Marker("HEALTH"));
                }
                turns.Add(new RedactedTurn(t.GetProperty("turn").GetInt32(), t.GetProperty("speaker").GetString()!, t.GetProperty("startMs").GetInt64(), t.GetProperty("endMs").GetInt64(),
                    t.GetProperty("confidence").GetDouble(), t.GetProperty("language").GetString()!, redacted));
            }
            var key = await Downstream.Post(null, "key-management", "/api/v1/internal/keys/resolve", new { subject = "call:" + callId, purpose = "VOICE" }, forwardAuth: false);
            string? cipher = null;
            if (key.Ok)
            {
                var k = Convert.FromBase64String(key.Str("material")!);
                var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(raw)); var c = new byte[plain.Length];
                using (var aes = new AesGcm(k, 16)) aes.Encrypt(nonce, plain, c, tag);
                cipher = Convert.ToBase64String(nonce.Concat(tag).Concat(c).ToArray());
            }
            var speakers = turns.GroupBy(t => t.Speaker).ToDictionary(gr => gr.Key, gr => new { turns = gr.Count(), talkMs = gr.Sum(t => t.EndMs - t.StartMs) });
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO transcripts VALUES (@callId, @customerId, @lang, @d, @sp, @turns, @cipher, @kid, @kv, NULL, NULL, @h, @at)",
                new { callId, customerId, lang = turns.FirstOrDefault()?.Language ?? "es", d = turns.Count == 0 ? 0 : turns.Max(t => t.EndMs), sp = JsonSerializer.Serialize(speakers),
                      turns = JsonSerializer.Serialize(turns, Canonical.Web), cipher, kid = key.Ok ? key.Str("keyId") : null, kv = key.Ok ? key.Json.GetProperty("version").GetInt32() : 0, h = health ? 1 : 0, at = Nordic.Iso(clock.LocalNow) }, tx);
            foreach (var r in redactions) db.Execute("INSERT INTO redactions VALUES (@callId, @Index, @Turn, @Type, @VaultRef, NULL, NULL)", new { callId, r.Index, r.Turn, r.Type, r.VaultRef }, tx);
            Outbox.Add(db, null, clock, "voice.transcript.redacted", callId, new { callId, turns = turns.Count, redactions = redactions.Count, healthMentioned = health }, tx, correlationId: ev.CorrelationId);
            tx.Commit();
        });

        app.MapDelete("/api/v1/internal/transcripts/{callId}", (string callId) =>
        {
            using var db = ctx.OpenDb();
            var n = db.Execute("DELETE FROM transcripts WHERE call_id = @callId", new { callId });
            db.Execute("DELETE FROM redactions WHERE call_id = @callId", new { callId });
            return Results.Ok(new { callId, deleted = n });
        }).WithTags("Interno").WithSummary("(interno) Borrar la transcripción de una llamada (lo hace voice-service al destruirla)");

        app.MapGet("/api/v1/transcripts/{callId}", (HttpContext http, string callId) =>
        {
            using var db = ctx.OpenDb();
            var t = db.QuerySingleOrDefault<(string Id, string Lang, long Dur, string Sp, string Turns, long Health, string At)>(
                "SELECT call_id, language, duration_ms, speakers, turns, health, created_at FROM transcripts WHERE call_id = @callId", new { callId });
            if (t.Id is null) return Problems.Of(http, 404, "TRANSCRIPT_NOT_AVAILABLE", "Sin transcripción", $"No hay transcripción de {callId}: todavía no está lista o ya se destruyó.", "Espera con retry until a que la llamada pase de TRANSCRIBING; tras SHREDDED ya no existe.");
            var red = db.Query<(long Idx, long Turn, string Type, string? Token, string? Rev)>("SELECT idx, turn, type, token, revealed_at FROM redactions WHERE call_id = @callId ORDER BY idx", new { callId })
                .Select(r => new { index = r.Idx, turn = r.Turn, type = r.Type, vaultRef = r.Token, revealed = r.Rev is not null }).ToList();
            return Results.Ok(new { callId, language = t.Lang, durationMs = t.Dur, speakers = JsonDocument.Parse(t.Sp).RootElement, turns = JsonDocument.Parse(t.Turns).RootElement,
                                    redactions = red, healthMentioned = t.Health == 1, createdAt = t.At, lifetime = "EPHEMERAL: se destruye con la llamada (como mucho 24 h)" });
        }).WithTags("Transcripciones").WithSummary("Transcripción redactada (la usan voice-service, insight-extraction y call-summary)");

        app.MapGet("/api/v1/transcripts/{callId}/raw", async (HttpContext http, string callId, string? purpose, string? by) =>
        {
            if (purpose is null || !Purposes.Contains(purpose)) return Problems.BadRequest(http, "PURPOSE_REQUIRED", "Leer el original exige un propósito.", "Propósitos: " + string.Join(", ", Purposes));
            using var db = ctx.OpenDb();
            var t = db.QuerySingleOrDefault<(string Id, string? Cipher, string? Kid, long Kv, string? ReadAt)>("SELECT call_id, raw_cipher, key_id, key_version, raw_read_at FROM transcripts WHERE call_id = @callId", new { callId });
            if (t.Id is null) return Problems.Of(http, 404, "TRANSCRIPT_NOT_AVAILABLE", "Sin transcripción", "No existe o ya se destruyó.", null);
            if (t.ReadAt is not null) return Problems.Of(http, 410, "RAW_ALREADY_READ", "Original ya leído", $"El original se leyó el {t.ReadAt}: solo admite una lectura.", null);
            var m = t.Kid is null ? null : await Downstream.Get(http, "key-management", $"/api/v1/internal/keys/{t.Kid}/versions/{t.Kv}", forwardAuth: false);
            if (m is not { Ok: true }) return Problems.Of(http, 410, "CALL_KEY_SHREDDED", "Clave destruida", "La clave de la llamada ya no existe: el original es ilegible.", null);
            if (db.Execute("UPDATE transcripts SET raw_read_at = @at, raw_read_by = @by WHERE call_id = @callId AND raw_read_at IS NULL", new { at = Nordic.Iso(clock.LocalNow), by, callId }) == 0)
                return Problems.Of(http, 410, "RAW_ALREADY_READ", "Original ya leído", "Otra petición lo leyó antes.", null);
            var all = Convert.FromBase64String(t.Cipher!); var plain = new byte[all.Length - 28];
            using (var aes = new AesGcm(Convert.FromBase64String(m.Str("material")!), 16)) aes.Decrypt(all[..12], all[28..], all[12..28], plain);
            return Results.Ok(new { callId, purpose, readBy = by, turns = JsonDocument.Parse(plain).RootElement, warning = "Lectura única consumida: una segunda petición recibirá 410." });
        }).WithTags("Transcripciones").WithSummary("(vía voice-service) El original, una sola vez");

        app.MapGet("/api/v1/transcripts/{callId}/redactions/{index:int}/reveal", async (HttpContext http, string callId, int index, string? purpose, string? by) =>
        {
            if (purpose is null || !Purposes.Contains(purpose)) return Problems.BadRequest(http, "PURPOSE_REQUIRED", "Ver un dato redactado exige un propósito.", "Propósitos: " + string.Join(", ", Purposes));
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Type, string? Token, string? Rev)>("SELECT call_id, type, token, revealed_at FROM redactions WHERE call_id = @callId AND idx = @index", new { callId, index });
            if (r.Id is null) return Problems.NotFound(http, "REDACTION_NOT_FOUND", $"No existe la redacción {index} de {callId}.", null);
            if (r.Token is null) return Problems.Of(http, 410, "NOT_STORED", "No se guardó", $"Los datos {r.Type} no se guardan en ningún sitio: solo queda el marcador.", null);
            if (r.Rev is not null) return Problems.Of(http, 410, "ALREADY_REVEALED", "Ya se leyó", $"Este dato se leyó el {r.Rev}: una sola lectura.", null);
            var v = await Downstream.Send(http, "pii-vault", HttpMethod.Post, $"/api/v1/internal/tokens/{r.Token}/detokenize", new { purpose = "LEGAL_HOLD" }, forwardAuth: false,
                headers: new Dictionary<string, string> { ["X-Caller"] = "transcription-service" });
            if (!v.Ok) return Problems.Of(http, v.Status == 410 ? 410 : 502, v.Status == 410 ? "PII_ERASED" : "DEPENDENCY_ERROR", "No disponible", "pii-vault respondió " + v.Status, null);
            db.Execute("UPDATE redactions SET revealed_at = @at, revealed_by = @by WHERE call_id = @callId AND idx = @index", new { at = Nordic.Iso(clock.LocalNow), by, callId, index });
            return Results.Ok(new { callId, index, type = r.Type, value = v.Str("value"), purpose });
        }).WithTags("Transcripciones").WithSummary("(vía voice-service) El valor original de una redacción, una sola vez");
    }
}
