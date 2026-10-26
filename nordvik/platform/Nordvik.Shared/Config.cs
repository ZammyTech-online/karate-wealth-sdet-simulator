using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Lee un valor de config-service; si no está encendido o no existe la clave, usa el valor por defecto.</summary>
public static class Config
{
    public static async Task<string> Get(HttpContext? http, string service, string key, string fallback)
    {
        try
        {
            var r = await Downstream.Get(http, "config-service", $"/api/v1/config/{service}/{key}", forwardAuth: false, timeout: TimeSpan.FromSeconds(2));
            return r.Ok && r.Str("value") is { } v ? v : fallback;
        }
        catch (DependencyUnavailableException) { return fallback; }
    }

    public static async Task<int> GetInt(HttpContext? http, string service, string key, int fallback) =>
        int.TryParse(await Get(http, service, key, fallback.ToString()), out var v) ? v : fallback;

    public static async Task<decimal> GetDecimal(HttpContext? http, string service, string key, decimal fallback) =>
        Money.TryParse(await Get(http, service, key, Money.Format(fallback, 2)), out var v) ? v : fallback;
}
