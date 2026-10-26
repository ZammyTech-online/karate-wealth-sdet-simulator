using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

public sealed record BehaviourRequest(string? Mode, int? DelayMs, int? FailNext);
public sealed record Behaviour(string Mode, int DelayMs, int FailNext, long Calls);

/// <summary>
/// Comportamiento de un sistema externo simulado, configurable en caliente con /__admin/behaviour:
/// normal · down (503) · error (500) · slow (espera delayMs) · failNext=N (las N siguientes fallan con 503).
/// Es lo que harías con un mock de Karate para probar timeouts y reintentos.
/// </summary>
public sealed class ExternalBehaviour
{
    public string Mode { get; private set; } = "normal";
    public int DelayMs { get; private set; }
    public int FailNext { get; private set; }
    public long Calls { get; private set; }
    private readonly object _gate = new();

    public Behaviour Snapshot() => new(Mode, DelayMs, FailNext, Calls);

    public void Map(WebApplication app)
    {
        app.MapGet("/__admin/behaviour", () => Results.Ok(Snapshot())).WithTags("Administración del simulador");
        app.MapPost("/__admin/behaviour", (HttpContext http, BehaviourRequest body) =>
        {
            if (body.Mode is not (null or "normal" or "down" or "error" or "slow"))
                return Problems.BadRequest(http, "INVALID_MODE", "mode: normal, down, error o slow.", "Ejemplo: { \"mode\": \"slow\", \"delayMs\": 3000 } o { \"failNext\": 2 }");
            lock (_gate)
            {
                Mode = body.Mode ?? Mode;
                DelayMs = Math.Clamp(body.DelayMs ?? (Mode == "slow" ? 3000 : 0), 0, 30000);
                FailNext = Math.Max(0, body.FailNext ?? 0);
            }
            return Results.Ok(Snapshot());
        }).WithTags("Administración del simulador").WithSummary("Cambiar el comportamiento: normal, down, error, slow o failNext");
        app.MapPost("/__admin/behaviour/reset", () => { lock (_gate) { Mode = "normal"; DelayMs = 0; FailNext = 0; } return Results.Ok(Snapshot()); })
            .WithTags("Administración del simulador").WithSummary("Volver al comportamiento normal");
    }

    /// <summary>Llamar al principio de cada endpoint del simulador: devuelve un fallo simulado o null.</summary>
    public async Task<IResult?> Apply()
    {
        string mode; int delay; bool fail;
        lock (_gate)
        {
            Calls++;
            mode = Mode; delay = DelayMs;
            fail = FailNext > 0;
            if (fail) FailNext--;
        }
        if (delay > 0) await Task.Delay(delay);
        if (fail || mode == "down") return Results.Json(new { error = "SERVICE_UNAVAILABLE", message = "Sistema externo no disponible (simulado)." }, statusCode: 503);
        if (mode == "error") return Results.Json(new { error = "INTERNAL_ERROR", message = "Error interno del sistema externo (simulado)." }, statusCode: 500);
        return null;
    }
}
