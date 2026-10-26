using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Pide el siguiente número de una secuencia a sequence-service (la crea si no existe). Si no responde, usa un aleatorio.</summary>
public static class Sequences
{
    public static async Task<string> Next(HttpContext? http, string name, string prefix, int padding = 6)
    {
        try
        {
            var next = await Downstream.Post(http, "sequence-service", $"/api/v1/sequences/{name}/next", null, forwardAuth: false);
            if (next.Status == 404)
            {
                await Downstream.Post(http, "sequence-service", "/api/v1/sequences", new { name, prefix, padding }, forwardAuth: false);
                next = await Downstream.Post(http, "sequence-service", $"/api/v1/sequences/{name}/next", null, forwardAuth: false);
            }
            if (next.Ok) return next.Json.GetProperty("values")[0].GetString()!;
        }
        catch (DependencyUnavailableException) { }
        return prefix + "-" + Random.Shared.Next(1, 999999).ToString().PadLeft(padding, '0');
    }
}
