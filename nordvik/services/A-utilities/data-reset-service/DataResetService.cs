using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RunPrefix(string Prefix, string CreatedAt);
public sealed record ServiceReset(string Service, string Status, int Deleted);
public sealed record ResetReport(string Prefix, int TotalDeleted, IReadOnlyList<ServiceReset> Services);

/// <summary>data-reset-service: borra en todos los servicios encendidos los datos creados por una ejecución de tests.</summary>
public sealed class DataResetService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "data-reset-service", "A-utilities", 6009, 1, "Plataforma de datos de prueba",
        "Da un prefijo único por ejecución y borra todo lo creado con él en todos los servicios encendidos.",
        DependsOn: new[] { "clock-service" });

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/runs", (IClock clock) =>
        {
            var now = clock.LocalNow;
            var prefix = $"RUN-{now:yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4]}";
            return Results.Created($"/api/v1/runs/{prefix}", new RunPrefix(prefix, Nordic.Iso(now)));
        }).WithTags("Ejecuciones").WithSummary("Pedir un prefijo único para los datos de una ejecución").Produces<RunPrefix>(201);

        app.MapDelete("/api/v1/runs/{prefix}", async (HttpContext http, string prefix) =>
        {
            if (prefix.Length < 3)
                return Problems.BadRequest(http, "PREFIX_TOO_SHORT", "El prefijo debe tener al menos 3 caracteres.", "Usa el que te dio POST /api/v1/runs.");
            var tasks = ServiceDirectory.All.Keys.Where(k => k != Info.Name).OrderBy(k => k).Select(async name =>
            {
                try
                {
                    var r = await Downstream.Send(http, name, HttpMethod.Delete, "/_test/data?prefix=" + Uri.EscapeDataString(prefix), forwardAuth: false);
                    return new ServiceReset(name, r.Ok ? "OK" : "ERROR_" + r.Status, r.Ok && r.Body is { } b && b.TryGetProperty("deleted", out var d) ? d.GetInt32() : 0);
                }
                catch (DependencyUnavailableException)
                {
                    return new ServiceReset(name, "APAGADO", 0);
                }
            });
            var results = await Task.WhenAll(tasks);
            return Results.Ok(new ResetReport(prefix, results.Sum(r => r.Deleted), results));
        }).WithTags("Ejecuciones").WithSummary("Borrar en todos los servicios los datos con ese prefijo").Produces<ResetReport>();
    }
}
