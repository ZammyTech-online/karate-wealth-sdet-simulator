using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FaultRequest(string? Service, string? Method, string? PathPrefix, int? DelayMs, int? Status, double? Probability, int? Times, int? TtlSeconds);
public sealed record TrapRequest(bool? Enabled);
public sealed record Trap(string Id, string Name, string Symptom, string HowToFind, string Nature, int Wave, bool Enabled);

/// <summary>
/// chaos-control: caos deliberado para aprender a depurar. Dos herramientas:
/// 1) faults: latencia o errores en cualquier servicio/ruta, con probabilidad, número de veces y caducidad (los aplica el middleware de todos los servicios);
/// 2) traps: el catálogo de trampas del simulador (T1-T20), las que ya son comportamiento del sistema y las activables.
/// Para una sola petición basta la cabecera X-Chaos-Scenario: latency=1500;status=503.
/// </summary>
public sealed class ChaosControl : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "chaos-control", "Q-quality", 7600, 3, "Calidad y test",
        "Inyecta latencia y errores en cualquier servicio (faults con probabilidad, veces y caducidad) y gestiona el catálogo de trampas T1-T20.");

    public static readonly Trap[] Catalog =
    {
        new("T1", "Redondeo bancario frente a half-up", "Diferencia de 0,0001 participaciones en una de cada 50 órdenes", "Recalcula las participaciones con tu propia función JS", "ACTIVABLE", 4, false),
        new("T2", "UTC frente a hora de Estocolmo", "Órdenes de las 14:30 que se van al día siguiente", "Revisa Z frente a +01:00 en el payload", "ACTIVABLE", 4, false),
        new("T3", "Cambio de horario de verano", "El plan de ahorro se ejecuta dos veces el último domingo de octubre", "Adelanta el reloj virtual a esa fecha", "ACTIVABLE", 4, false),
        new("T4", "Webhook del custodio duplicado", "Posiciones dobles", "Busca dos eventos iguales en event-spy (/duplicates)", "ACTIVABLE", 3, false),
        new("T5", "Eventos fuera de orden", "La orden pasa a SETTLED antes que a EXECUTED", "Lee la secuencia en event-spy", "ACTIVABLE", 4, false),
        new("T6", "Consistencia eventual", "El GET justo después del POST no ve el cambio durante unos cientos de ms", "retry until, no un fallo", "SIEMPRE", 2, true),
        new("T7", "Caché con ETag caducado", "El NAV corregido no aparece", "Revisa If-None-Match y 304", "ACTIVABLE", 4, false),
        new("T8", "Paginación inestable", "Un cliente aparece en dos páginas y otro en ninguna", "Pagina por cursor, no por offset", "ACTIVABLE", 4, false),
        new("T9", "Campo renombrado en v2 (isin → instrumentId)", "Tu matcher falla solo en un entorno", "Valida contra el OpenAPI de cada entorno", "ACTIVABLE", 4, false),
        new("T10", "null frente a campo ausente", "#notnull pasa y #present no", "Matchers difusos precisos", "SIEMPRE", 2, true),
        new("T11", "Número como string (\"50000.00\")", "Comparaciones numéricas que fallan en silencio", "#number frente a #string", "SIEMPRE", 1, true),
        new("T12", "Token que caduca a mitad de la regresión", "401 aleatorios tras 5 minutos", "Refresco automático en el ayudante de login", "SIEMPRE", 1, true),
        new("T13", "Rate limit compartido entre hilos", "429 solo al correr en paralelo", "Menos hilos o un token por hilo", "SIEMPRE", 1, true),
        new("T14", "Datos compartidos entre escenarios", "Tests que pasan solos y fallan juntos", "Datos únicos por escenario", "SIEMPRE", 1, true),
        new("T15", "PII que se filtra en un mensaje de error", "El detail de un 422 incluye el número nacional", "Validador global de PII (redaction-service /scan) sobre cada respuesta", "ACTIVABLE", 3, false),
        new("T16", "Lectura única consumida por un reintento", "Tu retry until lee el dato y luego recibe 410", "Entiende qué llamadas tienen efectos (data-passport)", "SIEMPRE", 3, true),
        new("T17", "Hora de corte con segundos", "La orden de las 15:00:00 entra y la de las 15:00:01 no", "Test de límite exacto con el reloj virtual", "SIEMPRE", 2, true),
        new("T18", "Firma sobre el JSON reformateado", "INVALID_SIGNATURE aunque la clave sea correcta", "Firma el texto exacto que se envía (hash-service /hmac)", "SIEMPRE", 2, true),
        new("T19", "Latencia del custodio de 8 s", "Timeouts intermitentes", "configure readTimeout y reintentos; actívala con mock-control", "ACTIVABLE", 3, false),
        new("T20", "Moneda implícita", "Un fondo en EUR se suma a saldos en SEK", "Valida currency en cada importe", "ACTIVABLE", 4, false),
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/faults", () => Results.Ok(new { items = Chaos.All, count = Chaos.All.Count }))
            .WithTags("Faults").WithSummary("Fallos activos");

        app.MapPost("/api/v1/faults", (HttpContext http, FaultRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Service)) errors.Add(new FieldError("service", "obligatorio (o * para todos)"));
            else if (b.Service != "*" && !ServiceDirectory.Knows(b.Service)) errors.Add(new FieldError("service", $"no existe el servicio {b.Service}"));
            if ((b.DelayMs ?? 0) == 0 && (b.Status ?? 0) == 0) errors.Add(new FieldError("delayMs", "pon delayMs, status o los dos"));
            if (b.DelayMs is < 0 or > 30000) errors.Add(new FieldError("delayMs", "entre 0 y 30000"));
            if (b.Status is not (null or 0) and (< 400 or > 599)) errors.Add(new FieldError("status", "entre 400 y 599"));
            if (b.Probability is < 0 or > 1) errors.Add(new FieldError("probability", "entre 0 y 1"));
            if (b.Times is < 1) errors.Add(new FieldError("times", "1 o más"));
            if (b.TtlSeconds is < 1 or > 86400) errors.Add(new FieldError("ttlSeconds", "entre 1 y 86400"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Fallo no válido.",
                "Ejemplo: { \"service\": \"market-data\", \"pathPrefix\": \"/api/v1/prices\", \"status\": 503, \"times\": 2 } o { \"service\": \"custody-adapter\", \"delayMs\": 8000 }", errors);
            var rule = new ChaosRule("flt_" + Guid.NewGuid().ToString("N")[..10], b.Service!, b.Method?.ToUpperInvariant(), b.PathPrefix, b.DelayMs ?? 0, b.Status ?? 0, b.Probability ?? 1,
                b.Times, b.TtlSeconds is { } t ? DateTimeOffset.UtcNow.AddSeconds(t).UtcTicks : null, DateTimeOffset.UtcNow.ToString("O"), 0);
            Chaos.Put(rule);
            return Results.Created($"/api/v1/faults/{rule.Id}", rule);
        }).WithTags("Faults").WithSummary("Inyectar un fallo: latencia y/o error en un servicio (y ruta), con probabilidad, veces y caducidad en tiempo real");

        app.MapGet("/api/v1/faults/{id}", (HttpContext http, string id) =>
            Chaos.All.FirstOrDefault(r => r.Id == id) is { } r ? Results.Ok(r) : Problems.NotFound(http, "FAULT_NOT_FOUND", $"No existe el fallo {id} (o ya se gastó).", null))
            .WithTags("Faults").WithSummary("Un fallo y cuántas veces ha saltado (hits)");

        app.MapDelete("/api/v1/faults/{id}", (HttpContext http, string id) =>
            Chaos.Remove(id) ? Results.NoContent() : Problems.NotFound(http, "FAULT_NOT_FOUND", $"No existe el fallo {id}.", null))
            .WithTags("Faults").WithSummary("Quitar un fallo");

        app.MapDelete("/api/v1/faults", () => { var n = Chaos.All.Count; Chaos.Clear(); return Results.Ok(new { removed = n }); })
            .WithTags("Faults").WithSummary("Quitar todos los fallos (hazlo en el afterScenario de tus tests de caos)");

        app.MapGet("/api/v1/traps", (string? nature) =>
        {
            var items = Catalog.Select(t => t with { Enabled = t.Nature == "SIEMPRE" || Chaos.TrapOn(t.Id) })
                .Where(t => nature is null || t.Nature == nature.ToUpperInvariant()).ToList();
            return Results.Ok(new { items, count = items.Count, note = "SIEMPRE: es comportamiento del sistema. ACTIVABLE: se enciende con PUT /api/v1/traps/{id} en su oleada." });
        }).WithTags("Trampas").WithSummary("Catálogo de trampas (síntoma y cómo se descubre; nunca dónde está)");

        app.MapPut("/api/v1/traps/{id}", (HttpContext http, string id, TrapRequest b) =>
        {
            var t = Catalog.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (t is null) return Problems.NotFound(http, "TRAP_NOT_FOUND", $"No existe la trampa {id}.", "Mira GET /api/v1/traps");
            if (t.Nature == "SIEMPRE") return Problems.Conflict(http, "TRAP_ALWAYS_ON", $"{t.Id} es comportamiento normal del sistema: no se apaga.", null);
            if (b.Enabled is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta enabled.", null);
            Chaos.SetTrap(t.Id, b.Enabled.Value);
            return Results.Ok(t with { Enabled = b.Enabled.Value });
        }).WithTags("Trampas").WithSummary("Encender o apagar una trampa activable");

        app.MapPost("/api/v1/reset", () => { var n = Chaos.All.Count; Chaos.Clear(); Chaos.ClearTraps(); return Results.Ok(new { faultsRemoved = n, trapsReset = true }); })
            .WithTags("Faults").WithSummary("Volver a la calma: sin fallos y trampas activables apagadas");

        app.MapGet("/api/v1/scenarios", () => Results.Ok(new
        {
            header = Chaos.ScenarioHeader,
            examples = new[] { "latency=1500", "status=503", "latency=800;status=500" },
            note = "La cabecera afecta solo a la petición que la lleva y al servicio que la recibe (no se propaga a sus dependencias).",
        })).WithTags("Faults").WithSummary("Caos para una sola petición con la cabecera X-Chaos-Scenario");
    }
}
