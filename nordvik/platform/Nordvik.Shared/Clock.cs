using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Nordvik.Shared;

/// <summary>La hora del sistema Nordvik. Nadie usa DateTime.Now: todos preguntan aquí (ADR-003).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    /// <summary>Hora local de Estocolmo, la zona de referencia del negocio.</summary>
    DateTimeOffset LocalNow { get; }
    DateOnly Today { get; }
}

public static class Nordic
{
    /// <summary>Zona horaria de negocio. Admite el nombre IANA y, si falla en Windows, el nombre de Windows.</summary>
    public static readonly TimeZoneInfo Stockholm = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Stockholm", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* siguiente */ }
        }
        return TimeZoneInfo.Utc;
    }

    public static DateTimeOffset ToStockholm(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Stockholm);

    /// <summary>ISO-8601 con desplazamiento, por ejemplo 2026-10-26T14:59:00+01:00.</summary>
    public static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");
}

/// <summary>Cliente del clock-service con caché de 1 segundo. Si el reloj no responde, usa la hora real.</summary>
public sealed class RemoteClock : IClock
{
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private DateTimeOffset _serverUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private bool _frozen;

    private readonly TimeSpan _cache;

    public RemoteClock(string clockUrl, int cacheMs = 1000)
    {
        _cache = TimeSpan.FromMilliseconds(Math.Max(0, cacheMs));
        _http = new HttpClient { BaseAddress = new Uri(clockUrl), Timeout = TimeSpan.FromMilliseconds(800) };
        // Las consultas de la hora no se registran en el log de clock-service (serían miles de líneas de ruido).
        _http.DefaultRequestHeaders.Add("X-Nordvik-Poll", "clock");
    }

    private sealed record NowDto([property: JsonPropertyName("utc")] DateTimeOffset Utc, [property: JsonPropertyName("mode")] string Mode);

    public DateTimeOffset UtcNow
    {
        get
        {
            var real = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                if (real - _fetchedAt > _cache)
                {
                    try
                    {
                        var dto = _http.GetFromJsonAsync<NowDto>("/api/v1/clock/now").GetAwaiter().GetResult();
                        if (dto is not null)
                        {
                            _serverUtc = dto.Utc;
                            _frozen = dto.Mode == "FROZEN";
                            _fetchedAt = real;
                        }
                    }
                    catch
                    {
                        _serverUtc = real;
                        _frozen = false;
                        _fetchedAt = real;
                    }
                }
                return _frozen ? _serverUtc : _serverUtc + (real - _fetchedAt);
            }
        }
    }

    public DateTimeOffset LocalNow => Nordic.ToStockholm(UtcNow);
    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);
}
