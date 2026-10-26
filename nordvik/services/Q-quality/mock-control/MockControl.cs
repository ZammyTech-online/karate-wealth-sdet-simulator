using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record MockBehaviourRequest(string? Mode, int? DelayMs, int? FailNext);

/// <summary>
/// mock-control: un solo sitio para ver y controlar todos los sistemas externos simulados (NordID, custodio, precios, banco...).
/// Reenvía a su /__admin/behaviour. Así tu framework no necesita conocer el puerto de cada simulador.
/// </summary>
public sealed class MockControl : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "mock-control", "Q-quality", 7601, 3, "Calidad y test",
        "Panel único de los sistemas externos simulados: estado, comportamiento (normal, down, error, slow, failNext) y reinicio de todos a la vez.");

    private static IEnumerable<NordvikServiceInfo> Mocks => ServiceDirectory.Infos.Where(i => i.Domain == "X-external").OrderBy(i => i.Port);

    private static async Task<object> Describe(NordvikServiceInfo m)
    {
        try
        {
            var r = await Downstream.SendUrl(HttpMethod.Get, ServiceDirectory.UrlOf(m.Name) + "/__admin/behaviour", null, null, TimeSpan.FromSeconds(2));
            return new { name = m.Name, port = m.Port, simulates = m.Description, up = true, behaviour = r.Ok ? r.Body : null };
        }
        catch (DependencyUnavailableException)
        {
            return new { name = m.Name, port = m.Port, simulates = m.Description, up = false, behaviour = (JsonElement?)null };
        }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/mocks", async () =>
        {
            var items = new List<object>();
            foreach (var m in Mocks) items.Add(await Describe(m));
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Simuladores").WithSummary("Todos los simuladores: si están encendidos y cómo se comportan");

        app.MapGet("/api/v1/mocks/{name}", async (HttpContext http, string name) =>
            Mocks.FirstOrDefault(m => m.Name == name) is { } m ? Results.Ok(await Describe(m))
                : Problems.NotFound(http, "MOCK_NOT_FOUND", $"No existe el simulador {name}.", "Lista: GET /api/v1/mocks"))
            .WithTags("Simuladores").WithSummary("Un simulador");

        app.MapPut("/api/v1/mocks/{name}/behaviour", async (HttpContext http, string name, MockBehaviourRequest b) =>
        {
            var m = Mocks.FirstOrDefault(x => x.Name == name);
            if (m is null) return Problems.NotFound(http, "MOCK_NOT_FOUND", $"No existe el simulador {name}.", "Lista: GET /api/v1/mocks");
            try
            {
                var r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf(name) + "/__admin/behaviour", b, null, TimeSpan.FromSeconds(3));
                return Results.Content(r.Raw, r.Status >= 400 ? "application/problem+json" : "application/json", System.Text.Encoding.UTF8, r.Status);
            }
            catch (DependencyUnavailableException)
            {
                return Problems.Of(http, 503, "MOCK_NOT_RUNNING", "Simulador apagado", $"{name} no está encendido en tu perfil.", "Arráncalo: nordvik up -Profile nivel-6 (o superior).");
            }
        }).WithTags("Simuladores").WithSummary("Cambiar el comportamiento de un simulador (lo mismo que su /__admin/behaviour)");

        app.MapPost("/api/v1/mocks/reset", async () =>
        {
            var done = new List<string>(); var off = new List<string>();
            foreach (var m in Mocks)
            {
                try { await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf(m.Name) + "/__admin/behaviour/reset", new { }, null, TimeSpan.FromSeconds(2)); done.Add(m.Name); }
                catch (DependencyUnavailableException) { off.Add(m.Name); }
            }
            return Results.Ok(new { reset = done, notRunning = off });
        }).WithTags("Simuladores").WithSummary("Todos los simuladores a normal");
    }
}
