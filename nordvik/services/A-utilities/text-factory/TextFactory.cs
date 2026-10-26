using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record MessageRequest(string? Scenario, string? Language, bool? IncludePii, int? Seed);
public sealed record ScriptTurn(int Turn, string Speaker, string Text, int OffsetMs);
public sealed record CallScript(string Scenario, string Language, int DurationMs, IReadOnlyList<ScriptTurn> Turns, IReadOnlyList<string> PiiPresent);

/// <summary>
/// text-factory: textos sintéticos para mensajería y voz: mensajes de clientes (queja, pregunta, cambio de datos) con o sin datos personales
/// para practicar enmascarado, y guiones de llamada cliente–asesor (V1-V5) que usará voice-service en la oleada 3.
/// </summary>
public sealed class TextFactory : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "text-factory", "A-utilities", 6012, 2, "Plataforma de datos de prueba",
        "Mensajes de cliente con PII opcional (personnummer, IBAN, teléfono) y guiones de llamada V1-V5 con marcas de tiempo.");

    private static readonly string[] Scenarios = { "V1", "V2", "V3", "V4", "V5" };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/texts/customer-message", (HttpContext http, MessageRequest b) =>
        {
            var rnd = new Random(b.Seed ?? Random.Shared.Next());
            var pn = NationalId.Create(new DateOnly(1980, 1, 1).AddDays(rnd.Next(8000)), rnd.Next(100), rnd.Next(2) == 0);
            var iban = IbanMath.Print(IbanMath.Make("SE", "300" + rnd.NextInt64(10_000_000_000_000_000, 99_999_999_999_999_999).ToString()));
            var phone = $"+46 70 {rnd.Next(100, 999)} {rnd.Next(10, 99)} {rnd.Next(10, 99)}";
            var pii = b.IncludePii ?? true;
            var text = (b.Scenario ?? "question") switch
            {
                "complaint" => "Hola, me han cobrado dos veces la comisión de este mes." + (pii ? $" Mi personnummer es {pn} y mi cuenta {iban}." : "") + " ¿Me lo podéis devolver?",
                "change" => "Quiero cambiar mi teléfono." + (pii ? $" El nuevo es {phone}." : "") + " Gracias.",
                "question" => "¿Cuándo se ejecuta mi orden si la doy después de las tres de la tarde?",
                _ => null,
            };
            if (text is null) return Problems.BadRequest(http, "UNKNOWN_SCENARIO", "Escenarios: complaint, change, question.", null);
            var present = new List<string>();
            if (text.Contains(pn)) present.Add("NATIONAL_ID");
            if (text.Contains(iban)) present.Add("IBAN");
            if (text.Contains(phone)) present.Add("PHONE");
            return Results.Ok(new { scenario = b.Scenario ?? "question", language = "es", text, piiPresent = present, expected = new { nationalId = present.Contains("NATIONAL_ID") ? pn : null, iban = present.Contains("IBAN") ? iban : null, phone = present.Contains("PHONE") ? phone : null } });
        }).WithTags("Textos").WithSummary("Mensaje de cliente (complaint, change, question), con PII o sin ella");

        app.MapPost("/api/v1/texts/call-script", (HttpContext http, string? scenario, int? seed) =>
        {
            var sc = scenario ?? "V1";
            if (!Scenarios.Contains(sc)) return Problems.BadRequest(http, "UNKNOWN_SCENARIO", "Escenarios: V1-V5.", "V1 consulta · V2 orden de compra · V3 cambio de IBAN con PII · V4 queja · V5 consentimiento de grabación");
            var rnd = new Random(seed ?? 1);
            var pn = NationalId.Create(new DateOnly(1975, 1, 1).AddDays(rnd.Next(9000)), rnd.Next(100), false);
            var lines = sc switch
            {
                "V1" => new[] { ("ADVISOR", "Hola, soy Erik de Nordvik. ¿En qué puedo ayudarte?"), ("CUSTOMER", "Quería saber cuánto vale hoy mi cartera."), ("ADVISOR", "Ahora mismo 152 300 coronas.") },
                "V2" => new[] { ("ADVISOR", "Confirmo: compra de 10 000 coronas en Nordvik Global Equity."), ("CUSTOMER", "Sí, confirmo la orden."), ("ADVISOR", "Recuerda que según la cláusula 2 se aplican las comisiones del folleto.") },
                "V3" => new[] { ("CUSTOMER", $"Quiero cambiar la cuenta de retiradas. Mi personnummer es {pn}."), ("ADVISOR", "Por seguridad te enviaré una solicitud de NordID."), ("CUSTOMER", "La cuenta nueva es SE45 3000 0000 0123 4567 8901.") },
                "V4" => new[] { ("CUSTOMER", "Estoy muy molesto, la retirada no ha llegado."), ("ADVISOR", "Lo siento, el banco la devolvió con el código AC06: cuenta bloqueada."), ("CUSTOMER", "Quiero poner una reclamación.") },
                _ => new[] { ("ADVISOR", "Esta llamada se graba según el acuerdo de grabación, cláusula 1."), ("CUSTOMER", "De acuerdo, pero quiero poder revocarlo."), ("ADVISOR", "Claro, la cláusula 2 te permite revocarlo cuando quieras.") },
            };
            var t = 0;
            var turns = lines.Select((l, i) => { var turn = new ScriptTurn(i + 1, l.Item1, l.Item2, t); t += 2500 + l.Item2.Length * 45; return turn; }).ToList();
            var pii = new List<string>();
            if (sc == "V3") { pii.Add("NATIONAL_ID"); pii.Add("IBAN"); }
            return Results.Ok(new CallScript(sc, "es", t, turns, pii));
        }).WithTags("Textos").WithSummary("Guion de llamada V1-V5 con marcas de tiempo (oleada 3: voz)").Produces<CallScript>();

        app.MapGet("/api/v1/texts/lorem", (int? words) =>
        {
            var w = new[] { "fondo", "cartera", "riesgo", "pensión", "ahorro", "comisión", "rentabilidad", "contrato", "cliente", "asesor", "plazo", "índice" };
            var n = Math.Clamp(words ?? 20, 1, 2000);
            return Results.Ok(new { words = n, text = string.Join(" ", Enumerable.Range(0, n).Select(i => w[i * 7 % w.Length])) + "." });
        }).WithTags("Textos").WithSummary("Texto de relleno con vocabulario financiero");
    }
}
