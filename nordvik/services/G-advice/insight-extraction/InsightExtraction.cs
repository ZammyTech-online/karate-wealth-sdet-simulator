using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record InternalConfirm(bool? Accept, JsonElement? Value);
public sealed record Extracted(string Field, object Value, int Turn, long StartMs, string Quote, double Confidence, bool NeedsReview);

/// <summary>
/// insight-extraction: de la transcripción REDACTADA a datos para las APIs, cada uno con su evidencia (turno, milisegundo y la frase).
/// Solo escucha al cliente. Reglas en español, sueco e inglés: aportación mensual, horizonte, tolerancia al riesgo, objetivo,
/// órdenes y cláusulas citadas. Si la confianza baja de 0,75 el dato espera al asesor; una orden SIEMPRE espera al asesor.
/// Nunca extrae nada de salud: el texto ya llega con [HEALTH].
/// </summary>
public sealed class InsightExtraction : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "insight-extraction", "G-advice", 6608, 3, "Asesoramiento y comunicación",
        "Datos estructurados desde la transcripción redactada (aportación, horizonte, riesgo, objetivo, orden, cláusula) con evidencia y revisión humana por debajo de 0,75 de confianza.",
        DependsOn: new[] { "transcription-service", "clock-service" }, Emits: new[] { "voice.insights.extracted" }, Consumes: new[] { "voice.transcript.redacted", "voice.call.shredded" });

    public const double ReviewThreshold = 0.75;

    private static readonly Dictionary<string, decimal> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uno"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10,
        ["quince"] = 15, ["veinte"] = 20, ["veinticinco"] = 25, ["treinta"] = 30, ["cuarenta"] = 40, ["cincuenta"] = 50, ["cien"] = 100, ["quinientos"] = 500, ["quinientas"] = 500, ["mil"] = 1000,
        ["en"] = 1, ["ett"] = 1, ["två"] = 2, ["tre"] = 3, ["fem"] = 5, ["tio"] = 10, ["femton"] = 15, ["tjugo"] = 20, ["trettio"] = 30, ["tusen"] = 1000,
        ["tvåtusen"] = 2000, ["tretusen"] = 3000, ["femtusen"] = 5000, ["tiotusen"] = 10000,
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["five"] = 5, ["ten"] = 10, ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30, ["thousand"] = 1000,
    };

    /// <summary>"dos mil" → 2000, "diez mil" → 10000, "1500" → 1500, "2.000" → 2000, "tretusen" → 3000. Devuelve también si venía en cifras.</summary>
    public static (decimal Value, bool Digits)? ParseAmount(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        var digits = Regex.Match(t, @"^\d{1,3}([\. ]\d{3})+$|^\d+([,\.]\d{1,2})?$");
        if (digits.Success)
        {
            var clean = Regex.IsMatch(t, @"[\. ]\d{3}$") && !Regex.IsMatch(t, @"[,\.]\d{1,2}$") ? t.Replace(".", "").Replace(" ", "") : t.Replace(",", ".");
            if (Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})+$")) clean = t.Replace(".", "");
            return (decimal.Parse(clean, CultureInfo.InvariantCulture), true);
        }
        decimal total = 0, current = 0; var any = false;
        foreach (var w in t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Words.TryGetValue(w, out var v)) return null;
            any = true;
            if (v == 1000) { current = (current == 0 ? 1 : current) * 1000; total += current; current = 0; }
            else if (v >= 2000) { total += v; }
            else current += v;
        }
        return any ? (total + current, false) : null;
    }

    private static readonly (string Pattern, string Isin)[] Funds =
    {
        ("global", "SE0000000001"), ("sverige|suecia|sweden", "SE0000000002"), ("small|peque", "SE0000000003"), ("emergentes|emerging", "SE0000000004"),
        ("esg|sostenible", "SE0000000005"), ("tech|tecnolog", "SE0000000006"), ("balanserad|equilibrado|mixto", "SE0000000201"), ("liquidez|penningmarknad|monetario", "SE0000000301"),
    };

    public static List<Extracted> Extract(IEnumerable<(int Turn, string Speaker, long StartMs, double Confidence, string Text)> turns)
    {
        var found = new List<Extracted>();
        void Add(string field, object value, (int Turn, string Speaker, long StartMs, double Confidence, string Text) t, double certainty, bool forceReview = false)
        {
            if (found.Any(f => f.Field == field)) return; // el primer dato de cada tipo gana
            var conf = Math.Round(t.Confidence * certainty, 2);
            found.Add(new Extracted(field, value, t.Turn, t.StartMs, t.Text, conf, forceReview || conf < ReviewThreshold));
        }
        foreach (var t in turns.Where(x => x.Speaker == "CUSTOMER"))
        {
            var s = t.Text;
            var m = Regex.Match(s, @"(?:aportar|ahorrar|invertir|ingresar|spara|save|invest)\s+(?<amt>[\p{L}\d\.\, ]+?)\s+(?:al mes|cada mes|mensuales|i månaden|per månad|a month|per month)", RegexOptions.IgnoreCase);
            if (m.Success && ParseAmount(m.Groups["amt"].Value) is { } a)
                Add("monthlyAmount", new { amount = Money.Format(a.Value, 2), currency = "SEK" }, t, a.Digits ? 1.0 : 0.95);
            m = Regex.Match(s, @"(?:dentro de|en|om|in)\s+(?<n>\p{L}+|\d+)\s+(?:años|år|years)", RegexOptions.IgnoreCase);
            if (m.Success && ParseAmount(m.Groups["n"].Value) is { } y && y.Value is > 0 and < 80)
                Add("horizonYears", (int)y.Value, t, y.Digits ? 1.0 : 0.95);
            if (Regex.IsMatch(s, @"sin riesgo|nada de riesgo|no quiero arriesgar|ingen risk|no risk", RegexOptions.IgnoreCase)) Add("riskTolerance", "LOW", t, 0.9);
            else if (Regex.IsMatch(s, @"algo de riesgo|un poco de riesgo|viss risk|some risk", RegexOptions.IgnoreCase)) Add("riskTolerance", "MEDIUM", t, 0.9);
            else if (Regex.IsMatch(s, @"mucho riesgo|arriesgar mucho|agresiv|hög risk|high risk", RegexOptions.IgnoreCase)) Add("riskTolerance", "HIGH", t, 0.9);
            if (Regex.IsMatch(s, @"jubilaci|pensi[oó]n|pensionen|retirement", RegexOptions.IgnoreCase)) Add("goal", "RETIREMENT", t, 0.9);
            else if (Regex.IsMatch(s, @"vivienda|\bcasa\b|\bpiso\b|bostad|house", RegexOptions.IgnoreCase)) Add("goal", "HOUSING", t, 0.9);
            else if (Regex.IsMatch(s, @"estudios|universidad|education|utbildning", RegexOptions.IgnoreCase)) Add("goal", "EDUCATION", t, 0.9);
            m = Regex.Match(s, @"(?<side>compra|comprar|vende|vender|köp|sälj|buy|sell)\s+(?<amt>[\p{L}\d\.\, ]+?)\s+(?:en el fondo|en|i fonden|in the|of)\s+(?<fund>[\p{L} ]+)", RegexOptions.IgnoreCase);
            if (m.Success && ParseAmount(m.Groups["amt"].Value) is { } o)
            {
                var fund = Funds.FirstOrDefault(f => Regex.IsMatch(m.Groups["fund"].Value, f.Pattern, RegexOptions.IgnoreCase));
                var side = Regex.IsMatch(m.Groups["side"].Value, "^(vend|sälj|sell)", RegexOptions.IgnoreCase) ? "SELL" : "BUY";
                Add("orderIntent", new { side, amount = Money.Format(o.Value, 2), currency = "SEK", isin = fund.Isin }, t, fund.Isin is null ? 0.6 : 0.95, forceReview: true);
            }
            m = Regex.Match(s, @"cl[aá]usula\s+(?<n>\d+)|klausul\s+(?<n>\d+)|clause\s+(?<n>\d+)", RegexOptions.IgnoreCase);
            if (m.Success) Add("contractClause", new { clauseNo = int.Parse(m.Groups["n"].Value) }, t, 1.0);
        }
        return found;
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS insights (call_id TEXT, field TEXT, value TEXT, turn INTEGER, start_ms INTEGER, quote TEXT, confidence REAL, needs_review INTEGER, status TEXT, confirmed_at TEXT,
                        PRIMARY KEY (call_id, field))");
    }

    private static List<object> Items(Microsoft.Data.Sqlite.SqliteConnection db, string callId) =>
        db.Query<(string Field, string Value, long Turn, long Start, string Quote, double Conf, long Review, string Status, string? At)>(
            "SELECT field, value, turn, start_ms, quote, confidence, needs_review, status, confirmed_at FROM insights WHERE call_id = @callId ORDER BY turn, field", new { callId })
          .Select(r => (object)new
          {
              field = r.Field, value = JsonDocument.Parse(r.Value).RootElement, evidence = new { turn = r.Turn, startMs = r.Start, quote = r.Quote },
              confidence = r.Conf, needsHumanReview = r.Review == 1, status = r.Status, confirmedAt = r.At,
          }).ToList();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var callId = ev.Payload.GetProperty("callId").GetString()!;
            using var db = ctx.OpenDb();
            // Los datos extraídos ya van redactados (sin PII): sobreviven a la destrucción para que advisor-desk y C9 los usen
            if (ev.Topic == "voice.call.shredded") return;
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM insights WHERE call_id = @callId", new { callId }) > 0) return;
            var t = await Downstream.Get(null, "transcription-service", $"/api/v1/transcripts/{callId}", forwardAuth: false);
            if (!t.Ok) throw new InvalidOperationException("transcription-service respondió " + t.Status);
            var turns = t.Json.GetProperty("turns").EnumerateArray()
                .Select(x => (x.GetProperty("turn").GetInt32(), x.GetProperty("speaker").GetString()!, x.GetProperty("startMs").GetInt64(), x.GetProperty("confidence").GetDouble(), x.GetProperty("text").GetString()!));
            var found = Extract(turns);
            using var tx = db.BeginTransaction();
            foreach (var f in found)
                db.Execute("INSERT INTO insights VALUES (@callId, @Field, @v, @Turn, @StartMs, @Quote, @Confidence, @r, 'EXTRACTED', NULL)",
                    new { callId, f.Field, v = JsonSerializer.Serialize(f.Value, Canonical.Web), f.Turn, f.StartMs, f.Quote, f.Confidence, r = f.NeedsReview ? 1 : 0 }, tx);
            Outbox.Add(db, null, clock, "voice.insights.extracted", callId, new { callId, fields = found.Count, needsReview = found.Count(f => f.NeedsReview) }, tx, correlationId: ev.CorrelationId);
            tx.Commit();
        });

        app.MapGet("/api/v1/insights/{callId}", (HttpContext http, string callId) =>
        {
            using var db = ctx.OpenDb();
            var items = Items(db, callId);
            return Results.Ok(new { callId, items, count = items.Count, reviewThreshold = ReviewThreshold });
        }).WithTags("Datos extraídos").WithSummary("Datos de una llamada con su evidencia (vía voice-service)");

        app.MapPost("/api/v1/internal/insights/{callId}/{field}/confirm", (HttpContext http, string callId, string field, InternalConfirm b) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Field, string Value, string Status)>("SELECT field, value, status FROM insights WHERE call_id = @callId AND field = @field", new { callId, field });
            if (r.Field is null) return Problems.NotFound(http, "INSIGHT_NOT_FOUND", $"La llamada {callId} no tiene el dato {field}.", null);
            if (r.Status != "EXTRACTED") return Problems.Conflict(http, "INSIGHT_ALREADY_REVIEWED", $"El dato ya está {r.Status}.", null);
            var value = r.Value;
            if (b.Value is { ValueKind: JsonValueKind.Object } v && JsonDocument.Parse(r.Value).RootElement.ValueKind == JsonValueKind.Object)
            {
                var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.Value)!;
                foreach (var p in v.EnumerateObject()) merged[p.Name] = p.Value.Clone();
                value = JsonSerializer.Serialize(merged);
            }
            else if (b.Value is { } other && other.ValueKind != JsonValueKind.Undefined && other.ValueKind != JsonValueKind.Null) value = other.GetRawText();
            var status = b.Accept == false ? "REJECTED" : "CONFIRMED";
            db.Execute("UPDATE insights SET status = @status, value = @value, confirmed_at = @at WHERE call_id = @callId AND field = @field", new { status, value, at = Nordic.Iso(clock.LocalNow), callId, field });
            return Results.Ok(Items(db, callId).First(i => JsonSerializer.SerializeToElement(i, Canonical.Web).GetProperty("field").GetString() == field));
        }).WithTags("Interno").WithSummary("(interno) Confirmar, corregir o descartar un dato");

        app.MapPost("/api/v1/extract/preview", (HttpContext http, JsonElement body) =>
        {
            if (!body.TryGetProperty("text", out var tx) || tx.ValueKind != JsonValueKind.String) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta text.", "Ejemplo: { \"text\": \"Quiero aportar dos mil al mes\" }");
            var found = Extract(new[] { (1, "CUSTOMER", 0L, body.TryGetProperty("confidence", out var c) ? c.GetDouble() : 1.0, tx.GetString()!) });
            return Results.Ok(new { items = found, count = found.Count });
        }).WithTags("Datos extraídos").WithSummary("Probar las reglas con una frase suelta (sin llamada)");
    }
}
