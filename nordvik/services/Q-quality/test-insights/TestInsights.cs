using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TestResult(string? Name, string? Feature, string? Status, long? DurationMs, string? Error);
public sealed record RunRequest(string? Suite, string? Environment, string? Commit, TestResult[]? Results);

/// <summary>
/// test-insights: recibe los resultados de tus ejecuciones de Karate (resumen por escenario) y calcula lo que un SDET senior mira: tasa de
/// éxito por ejecución, tests inestables (flaky: pasan y fallan sin cambios, con su tasa de alternancia), los más lentos y la tendencia.
/// </summary>
public sealed class TestInsights : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "test-insights", "Q-quality", 7604, 4, "Calidad y test",
        "Resultados de tus ejecuciones de Karate: tasa de éxito, tests inestables (flaky) con su tasa de alternancia, los más lentos y la tendencia por suite.",
        DependsOn: new[] { "clock-service" });

    /// <summary>Alternancias entre ejecuciones consecutivas (pass→fail o fail→pass) divididas entre las transiciones posibles.</summary>
    public static double FlipRate(IReadOnlyList<string> statuses)
    {
        var s = statuses.Where(x => x is "passed" or "failed").ToList();
        if (s.Count < 2) return 0;
        var flips = s.Zip(s.Skip(1)).Count(p => p.First != p.Second);
        return Math.Round((double)flips / (s.Count - 1), 3);
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, seq INTEGER, suite TEXT, environment TEXT, commit_sha TEXT, total INTEGER, passed INTEGER, failed INTEGER, skipped INTEGER, duration_ms INTEGER, at TEXT);
                     CREATE TABLE IF NOT EXISTS results (run_id TEXT, seq INTEGER, suite TEXT, name TEXT, feature TEXT, status TEXT, duration_ms INTEGER, error TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        var gate = new object();

        app.MapPost("/api/v1/runs", (HttpContext http, RunRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Suite)) errors.Add(new FieldError("suite", "obligatorio"));
            if (b.Results is not { Length: > 0 }) errors.Add(new FieldError("results", "al menos un resultado"));
            else for (var i = 0; i < b.Results.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(b.Results[i].Name)) errors.Add(new FieldError($"results[{i}].name", "obligatorio"));
                if (b.Results[i].Status?.ToLowerInvariant() is not ("passed" or "failed" or "skipped")) errors.Add(new FieldError($"results[{i}].status", "passed, failed o skipped"));
                if (b.Results[i].DurationMs is < 0) errors.Add(new FieldError($"results[{i}].durationMs", "≥ 0"));
            }
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Ejecución no válida.", "Ejemplo: { \"suite\": \"regresion\", \"results\": [ { \"name\": \"L16 E01\", \"status\": \"passed\", \"durationMs\": 340 } ] }", errors);
            var dupNames = b.Results!.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupNames.Count > 0) return Problems.Unprocessable(http, "DUPLICATE_TEST_NAMES", "Dos resultados con el mismo nombre en una ejecución: " + string.Join(", ", dupNames), null);
            var id = "run_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            lock (gate)
            {
                var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM runs WHERE suite = @Suite", new { b.Suite }) ?? 0) + 1;
                var rs = b.Results!;
                using var tx = db.BeginTransaction();
                db.Execute("INSERT INTO runs VALUES (@id, @seq, @Suite, @env, @Commit, @t, @p, @f, @s, @d, @at)",
                    new { id, seq, b.Suite, env = b.Environment ?? "local", b.Commit, t = rs.Length, p = rs.Count(r => r.Status!.ToLowerInvariant() == "passed"), f = rs.Count(r => r.Status!.ToLowerInvariant() == "failed"),
                          s = rs.Count(r => r.Status!.ToLowerInvariant() == "skipped"), d = rs.Sum(r => r.DurationMs ?? 0), at = Nordic.Iso(clock.LocalNow) }, tx);
                foreach (var r in rs)
                    db.Execute("INSERT INTO results VALUES (@id, @seq, @Suite, @Name, @Feature, @st, @d, @Error)", new { id, seq, b.Suite, r.Name, r.Feature, st = r.Status!.ToLowerInvariant(), d = r.DurationMs ?? 0, r.Error }, tx);
                tx.Commit();
                var passRate = Math.Round(100.0 * rs.Count(r => r.Status!.ToLowerInvariant() == "passed") / Math.Max(1, rs.Count(r => r.Status!.ToLowerInvariant() != "skipped")), 2);
                return Results.Created($"/api/v1/runs/{id}", new { id, suite = b.Suite, runNumber = seq, total = rs.Length, passRate });
            }
        }).WithTags("Ejecuciones").WithSummary("Registrar una ejecución con sus resultados");

        app.MapGet("/api/v1/suites/{suite}/trend", (HttpContext http, string suite, int? last) =>
        {
            using var db = ctx.OpenDb();
            var runs = db.Query<(long Seq, string Id, long T, long P, long F, long S, long D, string At)>("SELECT seq, id, total, passed, failed, skipped, duration_ms, at FROM runs WHERE suite = @suite ORDER BY seq DESC LIMIT @n",
                new { suite, n = Math.Clamp(last ?? 20, 1, 200) }).Reverse().ToList();
            if (runs.Count == 0) return Problems.NotFound(http, "SUITE_NOT_FOUND", $"No hay ejecuciones de la suite {suite}.", null);
            var items = runs.Select(r => new { run = r.Seq, id = r.Id, total = r.T, passed = r.P, failed = r.F, skipped = r.S, passRate = Math.Round(100.0 * r.P / Math.Max(1, r.P + r.F), 2), durationMs = r.D, at = r.At }).ToList();
            var direction = items.Count < 2 ? "FLAT" : items[^1].passRate > items[0].passRate ? "IMPROVING" : items[^1].passRate < items[0].passRate ? "WORSENING" : "FLAT";
            return Results.Ok(new { suite, runs = items, direction });
        }).WithTags("Análisis").WithSummary("Tendencia de la tasa de éxito por ejecución");

        app.MapGet("/api/v1/suites/{suite}/flaky", (HttpContext http, string suite, int? minRuns, double? minFlipRate) =>
        {
            using var db = ctx.OpenDb();
            var rows = db.Query<(string Name, long Seq, string Status)>("SELECT name, seq, status FROM results WHERE suite = @suite ORDER BY name, seq", new { suite }).ToList();
            if (rows.Count == 0) return Problems.NotFound(http, "SUITE_NOT_FOUND", $"No hay ejecuciones de la suite {suite}.", null);
            var min = Math.Max(2, minRuns ?? 3);
            var items = rows.GroupBy(r => r.Name).Select(g =>
            {
                var st = g.OrderBy(x => x.Seq).Select(x => x.Status).ToList();
                var exec = st.Where(s => s != "skipped").ToList();
                return new { name = g.Key, runs = exec.Count, passed = exec.Count(s => s == "passed"), failed = exec.Count(s => s == "failed"), flipRate = FlipRate(st), history = string.Concat(exec.Select(s => s == "passed" ? 'P' : 'F')) };
            }).Where(x => x.runs >= min && x.passed > 0 && x.failed > 0 && x.flipRate >= (minFlipRate ?? 0)).OrderByDescending(x => x.flipRate).ThenBy(x => x.name).ToList();
            return Results.Ok(new { suite, minRuns = min, items, count = items.Count });
        }).WithTags("Análisis").WithSummary("Tests inestables: pasan y fallan en las mismas condiciones");

        app.MapGet("/api/v1/suites/{suite}/slowest", (HttpContext http, string suite, int? top) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<(string Name, double Avg, long Max, long N)>("SELECT name, AVG(duration_ms), MAX(duration_ms), COUNT(*) FROM results WHERE suite = @suite AND status <> 'skipped' GROUP BY name ORDER BY AVG(duration_ms) DESC LIMIT @n",
                new { suite, n = Math.Clamp(top ?? 10, 1, 100) }).Select(x => new { name = x.Name, avgMs = Math.Round(x.Avg, 1), maxMs = x.Max, runs = x.N }).ToList();
            return items.Count == 0 ? Problems.NotFound(http, "SUITE_NOT_FOUND", $"No hay ejecuciones de la suite {suite}.", null) : Results.Ok(new { suite, items });
        }).WithTags("Análisis").WithSummary("Los tests más lentos de media");

        app.MapGet("/api/v1/tests/{name}/history", (HttpContext http, string name, string? suite) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<(string Suite, long Seq, string Status, long D, string? Err)>("SELECT suite, seq, status, duration_ms, error FROM results WHERE name = @name AND (@suite IS NULL OR suite = @suite) ORDER BY suite, seq", new { name, suite })
                .Select(x => new { suite = x.Suite, run = x.Seq, status = x.Status, durationMs = x.D, error = x.Err }).ToList();
            return items.Count == 0 ? Problems.NotFound(http, "TEST_NOT_FOUND", $"No hay resultados del test {name}.", null) : Results.Ok(new { name, items, count = items.Count });
        }).WithTags("Análisis").WithSummary("Historial de un test");
    }
}
