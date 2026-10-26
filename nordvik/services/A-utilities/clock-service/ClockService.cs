using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public enum ClockMode { REAL, OFFSET, FROZEN }

public sealed record ClockNow(string Now, DateTimeOffset Utc, long EpochMillis, string Timezone, string Date, string Time, ClockMode Mode, long OffsetSeconds);
public sealed record TravelRequest(DateTimeOffset? To, long? OffsetSeconds);
public sealed record AdvanceRequest(long? Seconds, long? Minutes, long? Hours, long? Days);
public sealed record FreezeRequest(DateTimeOffset? At);

/// <summary>
/// clock-service: el reloj virtual de todo Nordvik (ADR-003). Permite viajar en el tiempo, avanzar y congelar.
/// El estado vive en memoria: al reiniciar el anfitrión, el reloj vuelve a la hora real.
/// </summary>
public sealed class ClockService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "clock-service", "A-utilities", 6003, 0, "Plataforma de datos de prueba",
        "Reloj virtual del sistema. Todos los servicios preguntan aquí la hora; tus tests pueden viajar en el tiempo.");

    private readonly object _gate = new();
    private ClockMode _mode = ClockMode.REAL;
    private TimeSpan _offset = TimeSpan.Zero;
    private DateTimeOffset _frozenAt;

    private DateTimeOffset Current()
    {
        lock (_gate)
        {
            return _mode switch
            {
                ClockMode.FROZEN => _frozenAt,
                _ => DateTimeOffset.UtcNow + _offset,
            };
        }
    }

    private ClockNow Snapshot()
    {
        var utc = Current();
        var local = Nordic.ToStockholm(utc);
        lock (_gate)
        {
            var offset = _mode == ClockMode.FROZEN ? (long)(_frozenAt - DateTimeOffset.UtcNow).TotalSeconds : (long)_offset.TotalSeconds;
            return new ClockNow(Nordic.Iso(local), utc, utc.ToUnixTimeMilliseconds(), "Europe/Stockholm",
                local.ToString("yyyy-MM-dd"), local.ToString("HH:mm:ss"), _mode, offset);
        }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/clock").WithTags("Reloj");

        g.MapGet("/now", () => Results.Ok(Snapshot())).WithSummary("Hora actual del sistema Nordvik").Produces<ClockNow>();

        g.MapPost("/travel", (HttpContext http, TravelRequest body) =>
        {
            if (body.To is null && body.OffsetSeconds is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Indica 'to' o 'offsetSeconds'.",
                    "Ejemplo: { \"to\": \"2026-10-26T14:59:00+01:00\" } o { \"offsetSeconds\": 3600 }. Incluye siempre la zona horaria en 'to'.",
                    new[] { new FieldError("to", "o bien offsetSeconds") });
            lock (_gate)
            {
                _offset = body.To is { } to ? to.ToUniversalTime() - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(body.OffsetSeconds!.Value);
                _mode = _offset == TimeSpan.Zero ? ClockMode.REAL : ClockMode.OFFSET;
            }
            return Results.Ok(Snapshot());
        }).WithSummary("Viajar a una fecha y hora (el reloj sigue corriendo desde ahí)").Produces<ClockNow>();

        g.MapPost("/advance", (HttpContext http, AdvanceRequest body) =>
        {
            var delta = TimeSpan.FromSeconds(body.Seconds ?? 0) + TimeSpan.FromMinutes(body.Minutes ?? 0)
                        + TimeSpan.FromHours(body.Hours ?? 0) + TimeSpan.FromDays(body.Days ?? 0);
            if (delta == TimeSpan.Zero)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "No has indicado cuánto avanzar.",
                    "Ejemplo: { \"minutes\": 15 } o { \"days\": 1, \"hours\": 2 }.");
            lock (_gate)
            {
                if (_mode == ClockMode.FROZEN) _frozenAt += delta;
                else { _offset += delta; _mode = ClockMode.OFFSET; }
            }
            return Results.Ok(Snapshot());
        }).WithSummary("Avanzar (o retroceder con valores negativos) el reloj").Produces<ClockNow>();

        g.MapPost("/freeze", (FreezeRequest? body) =>
        {
            lock (_gate)
            {
                _frozenAt = body?.At?.ToUniversalTime() ?? (DateTimeOffset.UtcNow + _offset);
                _mode = ClockMode.FROZEN;
            }
            return Results.Ok(Snapshot());
        }).WithSummary("Congelar el reloj (en la hora actual o en 'at')").Produces<ClockNow>();

        g.MapPost("/reset", () =>
        {
            lock (_gate) { _mode = ClockMode.REAL; _offset = TimeSpan.Zero; }
            return Results.Ok(Snapshot());
        }).WithSummary("Volver a la hora real").Produces<ClockNow>();
    }
}
