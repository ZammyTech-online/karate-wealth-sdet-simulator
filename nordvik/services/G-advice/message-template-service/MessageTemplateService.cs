using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Template(string Key, string Language, int Version, string Channel, string? Subject, string Body, IReadOnlyList<string> Variables, bool Current);
public sealed record RenderRequest(string? Language, int? Version, Dictionary<string, JsonElement>? Vars);
public sealed record Rendered(string Key, string Language, int Version, bool FallbackLanguage, string? Subject, string Body);

/// <summary>message-template-service: plantillas de notificación por idioma y versión, con variables {{nombre}}.</summary>
public sealed class MessageTemplateService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "message-template-service", "G-advice", 6601, 2, "Comunicación con clientes",
        "Plantillas de email, SMS y push en sv, en y es, versionadas. Renderiza {{variables}}; si falta alguna, 422 con la lista. Idioma desconocido → en.");

    private static readonly Regex Var = new(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*)\s*\}\}");

    private static readonly List<(string Key, string Lang, int Version, string Channel, string? Subject, string Body)> Seed = new()
    {
        ("mfa.code", "sv", 1, "SMS", null, "Din Nordvik-kod är {{code}}. Den gäller i {{minutes}} minuter. Dela den aldrig med någon."),
        ("mfa.code", "en", 1, "SMS", null, "Your Nordvik code is {{code}}. It is valid for {{minutes}} minutes. Never share it."),
        ("mfa.code", "es", 1, "SMS", null, "Tu código de Nordvik es {{code}}. Vale {{minutes}} minutos. No lo compartas nunca."),
        ("order.executed", "sv", 1, "EMAIL", "Din order är genomförd", "Hej {{firstName}}! Din {{side}}-order i {{fundName}} är genomförd: {{units}} andelar till NAV {{nav}}."),
        ("order.executed", "en", 1, "EMAIL", "Your order has been executed", "Hi {{firstName}}! Your {{side}} order in {{fundName}} was executed: {{units}} units at NAV {{nav}}."),
        ("order.executed", "es", 1, "EMAIL", "Tu orden se ha ejecutado", "¡Hola, {{firstName}}! Tu orden de {{side}} en {{fundName}} se ha ejecutado: {{units}} participaciones a un NAV de {{nav}}."),
        ("order.executed", "es", 2, "EMAIL", "Orden ejecutada · {{fundName}}", "Hola, {{firstName}}. Orden {{orderId}} ({{side}}) ejecutada: {{units}} participaciones de {{fundName}} a {{nav}}. Liquidación: {{settlementDate}}."),
        ("payment.settled", "sv", 1, "EMAIL", "Ditt uttag är klart", "Hej {{firstName}}! {{amount}} SEK har skickats till {{iban}}."),
        ("payment.settled", "en", 1, "EMAIL", "Your withdrawal is complete", "Hi {{firstName}}! {{amount}} SEK has been sent to {{iban}}."),
        ("payment.settled", "es", 1, "EMAIL", "Tu retirada está completada", "¡Hola, {{firstName}}! Hemos enviado {{amount}} SEK a {{iban}}."),
        ("payment.rejected", "sv", 1, "EMAIL", "Ditt uttag kunde inte genomföras", "Hej {{firstName}}! Banken avvisade uttaget på {{amount}} SEK ({{reason}}). Pengarna finns kvar på ditt konto."),
        ("payment.rejected", "en", 1, "EMAIL", "Your withdrawal could not be completed", "Hi {{firstName}}! The bank rejected the {{amount}} SEK withdrawal ({{reason}}). The money is still in your account."),
        ("payment.rejected", "es", 1, "EMAIL", "No se pudo completar tu retirada", "¡Hola, {{firstName}}! El banco rechazó la retirada de {{amount}} SEK ({{reason}}). El dinero sigue en tu cuenta."),
        ("deposit.received", "sv", 1, "PUSH", "Insättning mottagen", "{{amount}} SEK har kommit in på ditt Nordvik-konto."),
        ("deposit.received", "en", 1, "PUSH", "Deposit received", "{{amount}} SEK has arrived in your Nordvik account."),
        ("deposit.received", "es", 1, "PUSH", "Ingreso recibido", "Han llegado {{amount}} SEK a tu cuenta de Nordvik."),
        ("contract.ready-to-sign", "sv", 1, "EMAIL", "Ditt avtal väntar på din signatur", "Hej {{firstName}}! Avtalet {{contractType}} är klart. Signera med NordID före {{deadline}}."),
        ("contract.ready-to-sign", "en", 1, "EMAIL", "Your contract is waiting for your signature", "Hi {{firstName}}! The {{contractType}} contract is ready. Sign with NordID before {{deadline}}."),
        ("contract.ready-to-sign", "es", 1, "EMAIL", "Tu contrato espera tu firma", "¡Hola, {{firstName}}! El contrato {{contractType}} está listo. Fírmalo con NordID antes del {{deadline}}."),
        ("welcome", "sv", 1, "EMAIL", "Välkommen till Nordvik", "Hej {{firstName}}! Ditt kundnummer är {{customerNumber}}."),
        ("welcome", "en", 1, "EMAIL", "Welcome to Nordvik", "Hi {{firstName}}! Your customer number is {{customerNumber}}."),
        ("welcome", "es", 1, "EMAIL", "Bienvenido a Nordvik", "¡Hola, {{firstName}}! Tu número de cliente es {{customerNumber}}."),
        ("marketing.newsletter", "sv", 1, "EMAIL", "Nyheter från Nordvik", "Hej {{firstName}}! Upptäck våra nya fonder."),
        ("marketing.newsletter", "en", 1, "EMAIL", "News from Nordvik", "Hi {{firstName}}! Discover our new funds."),
        ("marketing.newsletter", "es", 1, "EMAIL", "Novedades de Nordvik", "¡Hola, {{firstName}}! Descubre nuestros nuevos fondos."),
        ("meeting.reminder", "sv", 1, "EMAIL", "Påminnelse: möte med din rådgivare", "Hej {{firstName}}! Du har ett möte ({{channel}}) {{start}}."),
        ("meeting.reminder", "en", 1, "EMAIL", "Reminder: meeting with your advisor", "Hi {{firstName}}! You have a {{channel}} meeting on {{start}}."),
        ("meeting.reminder", "es", 1, "EMAIL", "Recordatorio: reunión con tu asesor", "¡Hola, {{firstName}}! Tienes una reunión ({{channel}}) el {{start}}."),
        ("message.new", "sv", 1, "EMAIL", "Nytt säkert meddelande", "Hej {{firstName}}! Du har ett nytt meddelande i Nordvik. Logga in för att läsa det."),
        ("message.new", "en", 1, "EMAIL", "New secure message", "Hi {{firstName}}! You have a new message in Nordvik. Log in to read it."),
        ("message.new", "es", 1, "EMAIL", "Nuevo mensaje seguro", "¡Hola, {{firstName}}! Tienes un mensaje nuevo en Nordvik. Entra para leerlo."),
    };

    private static List<string> VarsOf(string? subject, string body) =>
        Var.Matches((subject ?? "") + " " + body).Select(m => m.Groups[1].Value).Distinct().ToList();

    private static Template ToTemplate((string Key, string Lang, int Version, string Channel, string? Subject, string Body) t) =>
        new(t.Key, t.Lang, t.Version, t.Channel, t.Subject, t.Body, VarsOf(t.Subject, t.Body),
            t.Version == Seed.Where(s => s.Key == t.Key && s.Lang == t.Lang).Max(s => s.Version));

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/templates", (string? channel) =>
            Results.Ok(Seed.Where(s => channel is null || s.Channel == channel).Select(ToTemplate).ToList()))
            .WithTags("Plantillas").WithSummary("Todas las plantillas").Produces<List<Template>>();

        app.MapGet("/api/v1/templates/{key}", (HttpContext http, string key, string? lang, int? version) =>
        {
            var all = Seed.Where(s => s.Key == key).ToList();
            if (all.Count == 0) return Problems.NotFound(http, "TEMPLATE_NOT_FOUND", $"No existe la plantilla {key}.", "Lista: GET /api/v1/templates");
            var l = lang ?? "sv";
            var byLang = all.Where(s => s.Lang == l).ToList();
            if (byLang.Count == 0) byLang = all.Where(s => s.Lang == "en").ToList();
            var t = version is null ? byLang.MaxBy(s => s.Version) : byLang.FirstOrDefault(s => s.Version == version);
            if (t.Key is null) return Problems.NotFound(http, "TEMPLATE_VERSION_NOT_FOUND", $"{key} no tiene versión {version} en {l}.", "Versiones: " + string.Join(", ", byLang.Select(s => s.Version)));
            return Results.Ok(ToTemplate(t));
        }).WithTags("Plantillas").WithSummary("Una plantilla (la versión más reciente si no pides una)").Produces<Template>();

        app.MapPost("/api/v1/templates/{key}/render", (HttpContext http, string key, RenderRequest body) =>
        {
            var all = Seed.Where(s => s.Key == key).ToList();
            if (all.Count == 0) return Problems.NotFound(http, "TEMPLATE_NOT_FOUND", $"No existe la plantilla {key}.", "Lista: GET /api/v1/templates");
            var lang = body.Language ?? "sv";
            var byLang = all.Where(s => s.Lang == lang).ToList();
            var fallback = byLang.Count == 0;
            if (fallback) byLang = all.Where(s => s.Lang == "en").ToList();
            var t = body.Version is null ? byLang.MaxBy(s => s.Version) : byLang.FirstOrDefault(s => s.Version == body.Version);
            if (t.Key is null) return Problems.NotFound(http, "TEMPLATE_VERSION_NOT_FOUND", $"{key} no tiene versión {body.Version}.", null);
            var vars = body.Vars ?? new();
            var missing = VarsOf(t.Subject, t.Body).Where(v => !vars.ContainsKey(v)).ToList();
            if (missing.Count > 0)
                return Problems.Unprocessable(http, "MISSING_VARIABLES", "Faltan variables: " + string.Join(", ", missing) + ".",
                    "Mira la lista 'variables' de la plantilla con GET /api/v1/templates/" + key, missing.Select(m => new FieldError("vars." + m, "obligatoria")));
            string Fill(string s) => Var.Replace(s, m => vars[m.Groups[1].Value] is var v && v.ValueKind == JsonValueKind.String ? v.GetString()! : vars[m.Groups[1].Value].GetRawText());
            return Results.Ok(new Rendered(key, t.Lang, t.Version, fallback, t.Subject is null ? null : Fill(t.Subject), Fill(t.Body)));
        }).WithTags("Plantillas").WithSummary("Renderizar una plantilla con variables").Produces<Rendered>();
    }
}
