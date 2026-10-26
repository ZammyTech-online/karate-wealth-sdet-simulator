using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>
/// Se ejecuta en cada petición de cada servicio:
/// 1) asigna o propaga X-Correlation-Id, 2) decide el modo didáctico,
/// 3) convierte excepciones en errores RFC 9457 y 4) escribe una línea de log JSON.
/// </summary>
public sealed class NordvikMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string LearningHeader = "X-Learning-Mode";
    public const string PollHeader = "X-Nordvik-Poll";
    public const string CorrelationKey = "nordvik.correlation";
    public const string LearningKey = "nordvik.learning";
    public const string ServiceKey = "nordvik.service";

    private static readonly ConcurrentDictionary<string, object> FileLocks = new();
    private readonly RequestDelegate _next;
    private readonly ServiceContext _ctx;

    public NordvikMiddleware(RequestDelegate next, ServiceContext ctx)
    {
        _next = next;
        _ctx = ctx;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        var sw = Stopwatch.StartNew();
        var correlation = http.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlation)) correlation = "corr-" + Guid.NewGuid().ToString("N")[..16];
        http.Items[CorrelationKey] = correlation;
        http.Items[ServiceKey] = _ctx.Info.Name;
        var learningHeader = http.Request.Headers[LearningHeader].FirstOrDefault();
        http.Items[LearningKey] = learningHeader is null ? _ctx.LearningMode : learningHeader.Equals("true", StringComparison.OrdinalIgnoreCase);

        http.Response.OnStarting(() =>
        {
            http.Response.Headers[CorrelationHeader] = correlation;
            http.Response.Headers["X-Service"] = _ctx.Info.Name;
            http.Response.Headers["X-Service-Version"] = _ctx.Info.Version;
            return Task.CompletedTask;
        });

        try
        {
            if (await InjectChaos(http)) return;
            await _next(http);
        }
        catch (BadHttpRequestException ex)
        {
            await Problems.BadRequest(http, "INVALID_REQUEST", ex.Message,
                "El cuerpo de la petición no se pudo leer. ¿Es JSON válido? ¿Enviaste la cabecera Content-Type: application/json? ¿Los tipos coinciden con el esquema de /swagger?")
                .ExecuteAsync(http);
        }
        catch (DependencyUnavailableException dep)
        {
            await Problems.Of(http, 503, "DEPENDENCY_UNAVAILABLE", "Dependencia no disponible", dep.Message,
                $"Este servicio necesita {dep.Service}. ¿Está encendido en tu perfil? Compruébalo con: nordvik status").ExecuteAsync(http);
        }
        catch (Exception ex)
        {
            await Problems.Of(http, 500, "INTERNAL_ERROR", "Error interno", "Error inesperado en " + _ctx.Info.Name + ": " + ex.GetType().Name,
                "Esto no es culpa de tu test: guarda el correlationId y revisa logs/" + _ctx.Info.Name + ".log").ExecuteAsync(http);
        }
        finally
        {
            sw.Stop();
            Log(http, correlation, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>Caos deliberado (chaos-control o cabecera X-Chaos-Scenario). true si la petición ya se ha respondido con el fallo.</summary>
    private async Task<bool> InjectChaos(HttpContext http)
    {
        var path = http.Request.Path.Value ?? "/";
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) return false;
        int delay = 0, status = 0; string? source = null;
        if (http.Request.Headers.TryGetValue(Chaos.ScenarioHeader, out var sc))
        {
            var parsed = Chaos.Parse(sc.ToString());
            if (parsed is null)
            {
                await Problems.BadRequest(http, "INVALID_CHAOS_SCENARIO", $"X-Chaos-Scenario no válido: '{sc}'.", "Formato: latency=1500 · status=503 · latency=800;status=500").ExecuteAsync(http);
                return true;
            }
            (delay, status) = parsed.Value; source = "header";
        }
        else if (Chaos.Match(_ctx.Info.Name, http.Request.Method, path, DateTimeOffset.UtcNow.UtcTicks) is { } rule)
        {
            delay = rule.DelayMs; status = rule.Status; source = rule.Id;
        }
        if (source is null) return false;
        http.Response.OnStarting(() => { http.Response.Headers["X-Chaos-Injected"] = source; return Task.CompletedTask; });
        if (delay > 0) await Task.Delay(delay, http.RequestAborted);
        if (status == 0) return false;
        await Problems.Of(http, status, "CHAOS_INJECTED", "Fallo inyectado", $"Fallo provocado a propósito en {_ctx.Info.Name} ({source}).",
            "Lo ha puesto chaos-control (puerto 7600) o tu cabecera X-Chaos-Scenario. Quítalo con DELETE /api/v1/faults en chaos-control.").ExecuteAsync(http);
        return true;
    }

    private void Log(HttpContext http, string correlation, long ms)
    {
        try
        {
            // Los sondeos internos (outbox-relay, consumidores de eventos) no se registran si van bien: serían miles de líneas de ruido.
            if (http.Request.Headers.ContainsKey(PollHeader) && http.Response.StatusCode < 400) return;
            var line = JsonSerializer.Serialize(new
            {
                ts = DateTimeOffset.UtcNow.ToString("O"),
                service = _ctx.Info.Name,
                method = http.Request.Method,
                path = http.Request.Path.Value + http.Request.QueryString.Value,
                status = http.Response.StatusCode,
                ms,
                correlationId = correlation
            });
            var file = Path.Combine(_ctx.LogDir, _ctx.Info.Name + ".log");
            var gate = FileLocks.GetOrAdd(file, _ => new object());
            lock (gate) File.AppendAllText(file, line + Environment.NewLine);
        }
        catch
        {
            // El log nunca debe tumbar una petición.
        }
    }
}
