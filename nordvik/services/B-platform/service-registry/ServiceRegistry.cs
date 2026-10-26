using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// service-registry: qué servicio está en qué puerto, en qué versión y de quién depende. Con ?probe=true llama al /health de cada uno
/// y dice si está UP o DOWN (en tu perfil no tienen por qué estar todos encendidos). Dependencias directas, transitivas y quién depende de ti.
/// </summary>
public sealed class ServiceRegistry : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "service-registry", "B-platform", 6110, 4, "Plataforma",
        "Registro de servicios: puerto, versión, dominio y oleada de cada uno, dependencias directas y transitivas, dependientes y sondeo de salud.");

    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly SemaphoreSlim ProbeGate = new(16, 16);

    public static List<string> Transitive(string name, IReadOnlyDictionary<string, NordvikServiceInfo> all)
    {
        var seen = new List<string>();
        void Visit(string n) { foreach (var d in all.TryGetValue(n, out var i) ? i.DependsOn ?? Array.Empty<string>() : Array.Empty<string>()) if (!seen.Contains(d)) { seen.Add(d); Visit(d); } }
        Visit(name);
        return seen;
    }

    private static async Task<string> Health(string name)
    {
        await ProbeGate.WaitAsync();
        try { var r = await Probe.GetAsync(ServiceDirectory.UrlOf(name) + "/health"); return r.IsSuccessStatusCode ? "UP" : "DOWN"; }
        catch { return "DOWN"; }
        finally { ProbeGate.Release(); }
    }

    private static object View(NordvikServiceInfo i, string? status) => new
    {
        name = i.Name, domain = i.Domain, port = i.Port, url = ServiceDirectory.UrlOf(i.Name), wave = i.Wave, version = i.Version, team = i.Team,
        external = i.Domain == "X-external", dependsOn = i.DependsOn ?? Array.Empty<string>(), emits = i.Emits ?? Array.Empty<string>(), status,
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Dictionary<string, NordvikServiceInfo> All() => ServiceDirectory.Infos.ToDictionary(i => i.Name);

        app.MapGet("/api/v1/services", async (string? domain, int? wave, bool? probe, bool? external) =>
        {
            var list = ServiceDirectory.Infos.Where(i => (domain == null || i.Domain.StartsWith(domain, StringComparison.OrdinalIgnoreCase)) && (wave == null || i.Wave == wave)
                                                       && (external == null || (i.Domain == "X-external") == external)).OrderBy(i => i.Port).ToList();
            var statuses = probe == true ? await Task.WhenAll(list.Select(i => Health(i.Name))) : list.Select(_ => (string?)null).ToArray()!;
            var items = list.Select((i, n) => View(i, statuses[n])).ToList();
            return Results.Ok(new { items, count = items.Count, up = probe == true ? statuses.Count(s => s == "UP") : (int?)null });
        }).WithTags("Registro").WithSummary("Todos los servicios (filtra por dominio, oleada o externos; probe=true sondea su salud)");

        app.MapGet("/api/v1/services/{name}", async (HttpContext http, string name, bool? probe) =>
            All().TryGetValue(name, out var i) ? Results.Ok(View(i, probe == true ? await Health(name) : null))
                : Problems.NotFound(http, "SERVICE_NOT_FOUND", $"No hay ningún servicio llamado {name}.", "Lista completa: GET /api/v1/services"))
            .WithTags("Registro").WithSummary("Un servicio");

        app.MapGet("/api/v1/services/{name}/dependencies", (HttpContext http, string name) =>
        {
            var all = All();
            if (!all.TryGetValue(name, out var i)) return Problems.NotFound(http, "SERVICE_NOT_FOUND", $"No hay ningún servicio llamado {name}.", null);
            var trans = Transitive(name, all);
            return Results.Ok(new { name, direct = i.DependsOn ?? Array.Empty<string>(), transitive = trans, transitiveCount = trans.Count });
        }).WithTags("Registro").WithSummary("Dependencias directas y transitivas (lo que tiene que estar encendido)");

        app.MapGet("/api/v1/services/{name}/dependents", (HttpContext http, string name) =>
        {
            var all = All();
            if (!all.ContainsKey(name)) return Problems.NotFound(http, "SERVICE_NOT_FOUND", $"No hay ningún servicio llamado {name}.", null);
            var direct = all.Values.Where(i => (i.DependsOn ?? Array.Empty<string>()).Contains(name)).Select(i => i.Name).OrderBy(n => n).ToList();
            var all2 = all.Keys.Where(n => n != name && Transitive(n, all).Contains(name)).OrderBy(n => n).ToList();
            return Results.Ok(new { name, direct, transitive = all2, blastRadius = all2.Count });
        }).WithTags("Registro").WithSummary("Quién depende de este servicio (radio de impacto si se cae)");

        app.MapGet("/api/v1/domains", () => Results.Ok(new
        {
            items = ServiceDirectory.Infos.GroupBy(i => i.Domain).OrderBy(g => g.Key)
                .Select(g => new { domain = g.Key, services = g.Count(), ports = $"{g.Min(i => i.Port)}-{g.Max(i => i.Port)}", waves = g.Select(i => i.Wave).Distinct().OrderBy(w => w) }),
        })).WithTags("Registro").WithSummary("Dominios con su número de servicios y rango de puertos");
    }
}
