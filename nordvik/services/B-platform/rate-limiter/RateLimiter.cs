using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CheckRequest(string? Key, int? Cost);
public sealed record LimitStatus(string Key, bool Allowed, int Limit, int Used, int Remaining, string WindowStart, string ResetAt, int RetryAfterSeconds);

/// <summary>rate-limiter: ventana fija de un minuto por clave (usuario o IP). El límite viene de config-service.</summary>
public sealed class RateLimiter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "rate-limiter", "B-platform", 6101, 1, "Plataforma",
        "Cuotas por minuto por clave. El api-gateway le pregunta en cada petición; el límite se configura en config-service.",
        DependsOn: new[] { "clock-service", "config-service" });

    private readonly ConcurrentDictionary<string, (long Window, int Used)> _counters = new();
    private (int Limit, DateTimeOffset At) _cached = (100, DateTimeOffset.MinValue);

    private async Task<int> Limit(HttpContext http)
    {
        if (DateTimeOffset.UtcNow - _cached.At < TimeSpan.FromSeconds(2)) return _cached.Limit;
        try
        {
            var r = await Downstream.Get(http, "config-service", "/api/v1/config/rate-limiter/limitPerMinute", forwardAuth: false);
            if (r.Ok && int.TryParse(r.Str("value"), out var v)) _cached = (v, DateTimeOffset.UtcNow);
        }
        catch (DependencyUnavailableException) { _cached = (_cached.Limit, DateTimeOffset.UtcNow); }
        return _cached.Limit;
    }

    private LimitStatus Status(string key, long window, int used, int limit, bool allowed, DateTimeOffset now)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(window * 60);
        var reset = start.AddMinutes(1);
        return new LimitStatus(key, allowed, limit, used, Math.Max(0, limit - used), Nordic.Iso(Nordic.ToStockholm(start)), Nordic.Iso(Nordic.ToStockholm(reset)),
            allowed ? 0 : (int)Math.Ceiling((reset - now).TotalSeconds));
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/check", async (HttpContext http, IClock clock, CheckRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Key))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta key.", "Ejemplo: { \"key\": \"user:asesor.erik\", \"cost\": 1 }", new[] { new FieldError("key", "obligatorio") });
            var cost = body.Cost ?? 1;
            if (cost is < 1 or > 100) return Problems.BadRequest(http, "INVALID_COST", "cost entre 1 y 100.", "Normalmente cada petición cuesta 1.");
            var limit = await Limit(http);
            var now = clock.UtcNow;
            var window = now.ToUnixTimeSeconds() / 60;
            var allowed = false;
            var entry = _counters.AddOrUpdate(body.Key, _ => { allowed = cost <= limit; return (window, allowed ? cost : 0); }, (_, old) =>
            {
                var used = old.Window == window ? old.Used : 0;
                allowed = used + cost <= limit;
                return (window, allowed ? used + cost : used);
            });
            return Results.Ok(Status(body.Key, window, entry.Used, limit, allowed, now));
        }).WithTags("Cuotas").WithSummary("Consumir cuota (siempre 200; mira allowed)").Produces<LimitStatus>();

        app.MapGet("/api/v1/limits/{key}", async (HttpContext http, IClock clock, string key) =>
        {
            var limit = await Limit(http);
            var now = clock.UtcNow;
            var window = now.ToUnixTimeSeconds() / 60;
            var used = _counters.TryGetValue(key, out var e) && e.Window == window ? e.Used : 0;
            return Results.Ok(Status(key, window, used, limit, used < limit, now));
        }).WithTags("Cuotas").WithSummary("Estado de la cuota de una clave").Produces<LimitStatus>();

        app.MapDelete("/api/v1/limits/{key}", (string key) => { _counters.TryRemove(key, out _); return Results.NoContent(); })
            .WithTags("Cuotas").WithSummary("Reiniciar la cuota de una clave");
    }
}
