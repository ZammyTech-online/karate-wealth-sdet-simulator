using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Hop(int Order, int Depth, string Service, string Method, string Path, int Status, long Ms, string StartedAt, string EndedAt);
public sealed record Trace(string CorrelationId, int Hops, IReadOnlyList<string> Services, long TotalMs, int Errors, string? FirstError, IReadOnlyList<Hop> Chain, string Tree);

/// <summary>
/// correlation-tracer: reconstruye la cadena de llamadas de un X-Correlation-Id leyendo los logs de todos los servicios.
/// Es lo primero que mira un SDET cuando un test E2E falla: ¿en qué servicio se rompió?
/// </summary>
public sealed class CorrelationTracer : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "correlation-tracer", "B-platform", 6108, 2, "Plataforma",
        "Reconstruye la cadena de llamadas de un correlationId a partir de los logs (servicio, ruta, estado, duración y anidamiento).");

    private sealed record Line(string Ts, string Service, string Method, string Path, int Status, long Ms, string CorrelationId);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/traces/{correlationId}", (HttpContext http, string correlationId) =>
        {
            if (correlationId.Length < 6)
                return Problems.BadRequest(http, "INVALID_CORRELATION_ID", "El correlationId es demasiado corto.", "Cópialo de la cabecera X-Correlation-Id de la respuesta o del campo correlationId de un error.");
            var lines = new List<Line>();
            foreach (var file in Directory.EnumerateFiles(ctx.LogDir, "*.log"))
            {
                if (Path.GetFileName(file).StartsWith("host")) continue;
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string? l;
                    while ((l = sr.ReadLine()) is not null)
                    {
                        if (!l.Contains(correlationId, StringComparison.Ordinal)) continue;
                        try
                        {
                            var j = JsonDocument.Parse(l).RootElement;
                            if (j.GetProperty("correlationId").GetString() != correlationId) continue;
                            lines.Add(new Line(j.GetProperty("ts").GetString()!, j.GetProperty("service").GetString()!, j.GetProperty("method").GetString()!,
                                j.GetProperty("path").GetString()!, j.GetProperty("status").GetInt32(), j.GetProperty("ms").GetInt64(), correlationId));
                        }
                        catch (JsonException) { }
                    }
                }
                catch (IOException) { }
            }
            if (lines.Count == 0)
                return Problems.NotFound(http, "TRACE_NOT_FOUND", $"No hay ninguna llamada con el correlationId {correlationId}.",
                    "Los logs se borran con nordvik reset. ¿Es el id exacto? ¿La petición llegó a algún servicio?");
            var spans = lines.Select(l =>
            {
                var end = DateTimeOffset.Parse(l.Ts);
                return (Line: l, Start: end.AddMilliseconds(-l.Ms), End: end);
            }).OrderBy(s => s.Start).ThenByDescending(s => s.End).ToList();
            var hops = new List<Hop>();
            for (var i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                var depth = spans.Take(i).Count(p => p.Start <= s.Start && p.End >= s.End && p.Line.Service != s.Line.Service);
                hops.Add(new Hop(i + 1, depth, s.Line.Service, s.Line.Method, s.Line.Path, s.Line.Status, s.Line.Ms,
                    Nordic.Iso(Nordic.ToStockholm(s.Start)), Nordic.Iso(Nordic.ToStockholm(s.End))));
            }
            var total = (long)(spans.Max(s => s.End) - spans.Min(s => s.Start)).TotalMilliseconds;
            var firstError = hops.Where(h => h.Status >= 400).OrderByDescending(h => h.Depth).ThenBy(h => h.Order).FirstOrDefault();
            var tree = string.Join("\n", hops.Select(h => new string(' ', h.Depth * 2) + $"{(h.Depth == 0 ? "" : "└─ ")}{h.Service} {h.Method} {h.Path} → {h.Status} ({h.Ms} ms)"));
            return Results.Ok(new Trace(correlationId, hops.Count, hops.Select(h => h.Service).Distinct().ToList(), total, hops.Count(h => h.Status >= 400),
                firstError is null ? null : $"{firstError.Service} {firstError.Method} {firstError.Path} → {firstError.Status}", hops, tree));
        }).WithTags("Trazas").WithSummary("Cadena de llamadas de un correlationId").Produces<Trace>();
    }
}
