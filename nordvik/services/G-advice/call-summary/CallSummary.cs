using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// call-summary: el resumen de 5 líneas que queda de una llamada (obligación de registro: 10 años). Se escribe desde la transcripción
/// redactada y los datos confirmados, se pasa por redaction-service para garantizar que no lleva PII, y al guardarlo avisa
/// (voice.call.summarised) para que voice-service destruya todo lo demás.
/// </summary>
public sealed class CallSummary : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "call-summary", "G-advice", 6609, 3, "Asesoramiento y comunicación",
        "Resumen de 5 líneas sin PII (comprobado con redaction-service) de cada llamada, conservado 10 años; su evento dispara la destrucción del resto.",
        DependsOn: new[] { "transcription-service", "insight-extraction", "voice-service", "redaction-service", "clock-service" },
        Emits: new[] { "voice.call.summarised" }, Consumes: new[] { "voice.review.completed" });

    private static readonly Dictionary<string, string> Goals = new() { ["RETIREMENT"] = "ahorro para la jubilación", ["HOUSING"] = "vivienda", ["EDUCATION"] = "estudios" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS summaries (call_id TEXT PRIMARY KEY, customer_id TEXT, lines TEXT, pii_check TEXT, created_at TEXT, keep_until TEXT)");
    }

    public static List<string> Write(JsonElement transcript, JsonElement insights, JsonElement? call)
    {
        var turns = transcript.GetProperty("turns").EnumerateArray().ToList();
        var ms = transcript.GetProperty("durationMs").GetInt64();
        var adv = turns.Count(t => t.GetProperty("speaker").GetString() == "ADVISOR");
        var items = insights.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("status").GetString() != "REJECTED").ToList();
        string V(JsonElement i)
        {
            var v = i.GetProperty("value");
            return i.GetProperty("field").GetString() switch
            {
                "monthlyAmount" => $"aportación mensual {v.GetProperty("amount").GetString()} SEK",
                "horizonYears" => $"horizonte {v.GetInt32()} años",
                "riskTolerance" => $"tolerancia al riesgo {v.GetString()}",
                "goal" => $"objetivo {v.GetString()}",
                "orderIntent" => $"orden {v.GetProperty("side").GetString()} de {v.GetProperty("amount").GetString()} SEK",
                "contractClause" => $"cita la cláusula {v.GetProperty("clauseNo").GetInt32()}",
                var f => f!,
            };
        }
        var goal = items.FirstOrDefault(i => i.GetProperty("field").GetString() == "goal");
        var placed = call?.TryGetProperty("orders", out var o) == true && o.ValueKind == JsonValueKind.Array ? o.EnumerateArray().Count(x => x.GetProperty("placed").GetBoolean()) : 0;
        var redactions = transcript.GetProperty("redactions").GetArrayLength();
        var health = transcript.GetProperty("healthMentioned").GetBoolean();
        return new List<string>
        {
            $"Llamada de {ms / 60000} min {ms / 1000 % 60} s con {turns.Count} turnos (asesor {adv}, cliente {turns.Count - adv}).",
            goal.ValueKind == JsonValueKind.Object ? $"Tema: {Goals.GetValueOrDefault(goal.GetProperty("value").GetString()!, "consulta")}." : "Tema: consulta general.",
            items.Count == 0 ? "Sin datos para el perfil del cliente." : "Datos: " + string.Join("; ", items.Select(V)) + ".",
            placed == 0 ? "Órdenes lanzadas en la llamada: ninguna." : $"Órdenes lanzadas en la llamada: {placed}.",
            $"Datos sensibles redactados: {redactions}{(health ? " (hubo una mención de salud, no registrada)" : "")}. Audio y transcripción destruidos tras este resumen.",
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var callId = ev.Payload.GetProperty("callId").GetString()!;
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM summaries WHERE call_id = @callId", new { callId }) > 0) return;
            var svc = await ServiceToken.Header(clock);
            var t = await Downstream.Get(null, "transcription-service", $"/api/v1/transcripts/{callId}", forwardAuth: false);
            if (!t.Ok) throw new InvalidOperationException("sin transcripción: " + t.Status);
            var i = await Downstream.Get(null, "insight-extraction", $"/api/v1/insights/{callId}", forwardAuth: false);
            var call = await Downstream.Send(null, "voice-service", HttpMethod.Get, $"/api/v2/calls/{callId}", null, forwardAuth: false, headers: svc);
            var lines = Write(t.Json, i.Json, call.Ok ? call.Json : null);
            var scan = await Downstream.Send(null, "redaction-service", HttpMethod.Post, "/api/v1/redact", new { text = string.Join("\n", lines), mode = "REMOVE" }, forwardAuth: false, headers: svc);
            var check = "CLEAN";
            if (scan.Ok && scan.Json.GetProperty("total").GetInt32() > 0) { lines = scan.Str("redacted")!.Split('\n').ToList(); check = "REDACTED_" + scan.Json.GetProperty("total").GetInt32(); }
            var customerId = call.Ok ? call.Str("customerId") : null;
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO summaries VALUES (@callId, @customerId, @lines, @check, @at, @keep)",
                new { callId, customerId, lines = JsonSerializer.Serialize(lines), check, at = Nordic.Iso(clock.LocalNow), keep = Money.Date(clock.Today.AddYears(10)) }, tx);
            Outbox.Add(db, null, clock, "voice.call.summarised", callId, new { callId, customerId }, tx, correlationId: ev.CorrelationId);
            tx.Commit();
        });

        app.MapGet("/api/v1/summaries/{callId}", (HttpContext http, string callId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string? Cust, string Lines, string Check, string At, string Keep)>("SELECT call_id, customer_id, lines, pii_check, created_at, keep_until FROM summaries WHERE call_id = @callId", new { callId });
            return r.Id is null
                ? Problems.Of(http, 404, "SUMMARY_NOT_READY", "Sin resumen", $"La llamada {callId} todavía no tiene resumen.", "Llega tras la revisión del asesor (o sola si no hubo datos que revisar).")
                : Results.Ok(new { callId, lines = JsonDocument.Parse(r.Lines).RootElement, piiCheck = r.Check, createdAt = r.At, keepUntil = r.Keep });
        }).WithTags("Resúmenes").WithSummary("Resumen de una llamada (vía voice-service)");
    }
}
