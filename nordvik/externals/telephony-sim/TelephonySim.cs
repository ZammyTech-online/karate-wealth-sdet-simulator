using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ScriptTurn(string Speaker, string Text, double Confidence, int DurationMs);
public sealed record CallScript(string Id, string Case, string Language, string Title, ScriptTurn[] Turns);
public sealed record ProviderCallRequest(string? From, string? To);
public sealed record RenderRequest(Dictionary<string, string>? Variables);

/// <summary>
/// telephony-sim · sistema externo E12 (simulado): la centralita. No hay audio: una llamada es una secuencia de turnos ya reconocidos
/// (hablante, texto, confianza, tiempos). Trae guiones para los casos V2-V9 en español, sueco e inglés, con huecos como {PNR} o {IBAN}
/// que se rellenan al pedir el guion. Puedes añadir los tuyos en /__admin/scripts.
/// </summary>
public sealed class TelephonySim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "telephony-sim", "X-external", 7712, 3, "Sistema externo (simulado)",
        "E12 Telefonía: llamadas del proveedor y guiones de turnos ya reconocidos (V2-V9, es/sv/en) con variables {PNR} e {IBAN}; guiones propios en /__admin/scripts.",
        DependsOn: new[] { "clock-service" });

    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, CallScript> _scripts = new(new[]
    {
        new CallScript("v2-consulta-corta", "V2", "es", "Consulta corta sobre comisiones", new[]
        {
            new ScriptTurn("ADVISOR", "Hola, soy tu asesor de Nordvik, ¿en qué te puedo ayudar?", 0.97, 3000),
            new ScriptTurn("CUSTOMER", "Hola, quería saber qué comisión tiene el fondo global.", 0.95, 3500),
            new ScriptTurn("ADVISOR", "La comisión de gestión es del cero coma cuarenta y cinco por ciento al año.", 0.96, 4200),
            new ScriptTurn("CUSTOMER", "¿Y hay comisión de entrada?", 0.94, 2000),
            new ScriptTurn("ADVISOR", "No, ese fondo no tiene comisión de entrada.", 0.97, 2500),
            new ScriptTurn("CUSTOMER", "Perfecto, gracias. Hasta luego.", 0.96, 1800),
        }),
        new CallScript("v3-dicta-numero", "V3", "es", "El cliente dicta su número nacional", new[]
        {
            new ScriptTurn("ADVISOR", "Para identificarte, ¿me confirmas tu número?", 0.96, 2800),
            new ScriptTurn("CUSTOMER", "Claro, mi número es {PNR}.", 0.93, 4100),
            new ScriptTurn("ADVISOR", "Gracias. ¿Qué querías hacer hoy?", 0.97, 2200),
            new ScriptTurn("CUSTOMER", "Quiero aportar dos mil al mes a mi plan.", 0.93, 3600),
        }),
        new CallScript("v3-dicta-iban", "V3", "es", "El cliente dicta su IBAN", new[]
        {
            new ScriptTurn("ADVISOR", "¿A qué cuenta quieres que enviemos el dinero?", 0.96, 2600),
            new ScriptTurn("CUSTOMER", "A mi cuenta {IBAN}, la de siempre.", 0.92, 5200),
        }),
        new CallScript("v4-idoneidad", "V4", "es", "Datos para el test de idoneidad", new[]
        {
            new ScriptTurn("ADVISOR", "Cuéntame un poco tus objetivos.", 0.97, 2000),
            new ScriptTurn("CUSTOMER", "Ahorro para la jubilación, la necesitaré dentro de quince años.", 0.94, 4800),
            new ScriptTurn("ADVISOR", "¿Cómo llevas las caídas de la bolsa?", 0.96, 2400),
            new ScriptTurn("CUSTOMER", "Puedo asumir algo de riesgo, pero sin locuras.", 0.92, 3300),
            new ScriptTurn("CUSTOMER", "Podría aportar tres mil al mes.", 0.95, 2500),
        }),
        new CallScript("v5-salud", "V5", "es", "Mención de salud", new[]
        {
            new ScriptTurn("CUSTOMER", "Tengo diabetes y quiero contratar el seguro de vida.", 0.94, 4200),
            new ScriptTurn("ADVISOR", "Te explico las opciones de seguro, no hace falta que me des más detalles médicos.", 0.96, 5000),
        }),
        new CallScript("v6-orden", "V6", "es", "Orden dada por teléfono", new[]
        {
            new ScriptTurn("ADVISOR", "¿Quieres hacer alguna operación?", 0.97, 2000),
            new ScriptTurn("CUSTOMER", "Sí, compra diez mil en el fondo global.", 0.95, 3000),
            new ScriptTurn("ADVISOR", "De acuerdo, diez mil coronas en Nordvik Global Equity. Te lo confirmo ahora.", 0.97, 4500),
        }),
        new CallScript("v7-ruido", "V7", "es", "Mucho ruido: baja confianza", new[]
        {
            new ScriptTurn("ADVISOR", "Perdona, se te oye entrecortado.", 0.88, 2000),
            new ScriptTurn("CUSTOMER", "Que quiero aportar mil al mes, creo.", 0.6, 3000),
        }),
        new CallScript("v9-clausula", "V9", "es", "La llamada cita el contrato", new[]
        {
            new ScriptTurn("CUSTOMER", "Según la cláusula 4 del mandato, ¿puedo cambiar el perfil de riesgo?", 0.94, 4600),
            new ScriptTurn("ADVISOR", "Sí, la cláusula 4 permite revisarlo una vez al año.", 0.96, 3500),
        }),
        new CallScript("sv-pension", "V4", "sv", "Sparande till pensionen (sueco)", new[]
        {
            new ScriptTurn("CUSTOMER", "Jag vill spara tvåtusen i månaden till pensionen.", 0.94, 3800),
            new ScriptTurn("CUSTOMER", "Jag behöver pengarna om tjugo år.", 0.93, 2600),
        }),
        new CallScript("en-education", "V4", "en", "Saving for education (inglés)", new[]
        {
            new ScriptTurn("CUSTOMER", "I want to save 1500 a month for my children's education.", 0.95, 3900),
            new ScriptTurn("CUSTOMER", "I can take some risk.", 0.93, 1800),
        }),
    }.Select(s => new KeyValuePair<string, CallScript>(s.Id, s)));
    private readonly ConcurrentDictionary<string, (string From, string To, string At)> _calls = new();

    public static object Render(CallScript s, Dictionary<string, string>? vars)
    {
        var start = 0;
        var turns = new List<object>();
        for (var i = 0; i < s.Turns.Length; i++)
        {
            var t = s.Turns[i];
            var text = t.Text;
            foreach (var (k, v) in vars ?? new()) text = text.Replace("{" + k + "}", v);
            turns.Add(new { turn = i + 1, speaker = t.Speaker, startMs = start, endMs = start + t.DurationMs, text, confidence = t.Confidence, language = s.Language });
            start += t.DurationMs + 400;
        }
        return new { scriptId = s.Id, @case = s.Case, language = s.Language, title = s.Title, turns };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);

        app.MapPost("/v1/calls", async (ProviderCallRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var id = "pc_" + Guid.NewGuid().ToString("N")[..12];
            _calls[id] = (b.From ?? "advisor", b.To ?? "customer", DateTimeOffset.UtcNow.ToString("O"));
            return Results.Json(new { providerCallId = id, state = "CONNECTED" }, statusCode: 201);
        }).WithTags("Telefonía v1").WithSummary("Abrir una llamada en la centralita");

        app.MapGet("/v1/scripts", (string? @case, string? language) => Results.Ok(new
        {
            items = _scripts.Values.Where(s => (@case is null || s.Case == @case) && (language is null || s.Language == language))
                .OrderBy(s => s.Id).Select(s => new { id = s.Id, @case = s.Case, language = s.Language, title = s.Title, turns = s.Turns.Length }),
        })).WithTags("Telefonía v1").WithSummary("Guiones de llamada disponibles");

        app.MapPost("/v1/scripts/{id}/render", async (string id, RenderRequest? b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            return _scripts.TryGetValue(id, out var s) ? Results.Ok(Render(s, b?.Variables)) : Results.Json(new { error = "SCRIPT_NOT_FOUND", message = "unknown script" }, statusCode: 404);
        }).WithTags("Telefonía v1").WithSummary("Los turnos de un guion con las variables rellenas ({PNR}, {IBAN}...)");

        app.MapPost("/__admin/scripts", (CallScript s) =>
        {
            if (string.IsNullOrWhiteSpace(s.Id) || s.Turns is not { Length: > 0 }) return Results.Json(new { error = "BAD_REQUEST", message = "id and turns are required" }, statusCode: 400);
            _scripts[s.Id] = s with { Case = s.Case ?? "CUSTOM", Language = s.Language ?? "es", Title = s.Title ?? s.Id };
            return Results.Created($"/v1/scripts/{s.Id}", new { id = s.Id, turns = s.Turns.Length });
        }).WithTags("Administración del simulador").WithSummary("Añadir un guion a medida");
    }
}
