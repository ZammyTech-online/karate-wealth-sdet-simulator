using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Nordvik.Services;
using Nordvik.Shared;

namespace Nordvik.Host;

/// <summary>
/// La Torre de control (ADR-014): una web en el puerto 3000 para encender y apagar niveles, dominios y servicios,
/// preparar misiones, ver la ficha de cada servicio y manejar el laboratorio (reloj, trampas, caos).
/// Vive dentro del anfitriÃ³n porque es quien sabe quÃ© estÃ¡ encendido.
/// </summary>
public static class ControlTower
{
    public const int Port = 3000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string[] SkipRequestHeaders = { "Host", "Content-Length", "Connection", "Origin", "Referer", "Accept-Encoding", "Cookie", "Content-Type" };
    private static readonly string[] SkipResponseHeaders = { "Transfer-Encoding", "Content-Length", "Server", "Date", "Connection", "Content-Type" };

    public static WebApplication Build(Supervisor sup, string repoRoot)
    {
        var escalera = new Escalera(repoRoot, sup.Catalog);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = "torre-de-control", ContentRootPath = AppContext.BaseDirectory, Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(Port + sup.Settings.PortOffset));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();

        var web = Path.Combine(repoRoot, "torre", "web");
        if (Directory.Exists(web))
        {
            var files = new PhysicalFileProvider(web);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
            });
        }

        app.MapGet("/health", () => Results.Ok(new { status = "UP", service = "torre-de-control" }));
        var api = app.MapGroup("/api/v1");

        // ---------- Estado general ----------
        api.MapGet("/estado", () =>
        {
            var p = Process.GetCurrentProcess();
            var running = sup.Running;
            return Results.Ok(new
            {
                profile = sup.Settings.ProfileName,
                running = running.Count,
                total = sup.Catalog.Count,
                businessServices = sup.Catalog.Values.Count(s => s.Info.Domain != "X-external"),
                externals = sup.Catalog.Values.Count(s => s.Info.Domain == "X-external"),
                memoryMb = p.WorkingSet64 / 1048576,
                privateMemoryMb = p.PrivateMemorySize64 / 1048576,
                currentLevel = CurrentLevel(sup, escalera),
                trapsOn = ChaosControl.Catalog.Count(t => t.Nature == "ACTIVABLE" && Chaos.TrapOn(t.Id)),
                learningMode = sup.Settings.LearningMode,
                portOffset = sup.Settings.PortOffset,
                services = running,
            });
        });

        // ---------- Niveles ----------
        api.MapGet("/niveles", () =>
        {
            var lessons = escalera.Lessons();
            return Results.Ok(new
            {
                currentLevel = CurrentLevel(sup, escalera),
                levels = escalera.File.Levels.OrderBy(l => l.Id).Select(l => LevelView(l, sup, escalera, lessons)).ToList(),
            });
        });

        api.MapGet("/niveles/{id:int}", (HttpContext http, int id) =>
            escalera.File.Levels.Any(l => l.Id == id)
                ? Results.Ok(LevelView(escalera.Level(id), sup, escalera, escalera.Lessons()))
                : Problem(http, 404, "LEVEL_NOT_FOUND", $"No existe el nivel {id}.", $"Los niveles van del 0 al {escalera.MaxLevel}."));

        api.MapPost("/niveles/{id:int}/encender", async (HttpContext http, int id, string? modo) =>
        {
            if (!escalera.File.Levels.Any(l => l.Id == id))
                return Problem(http, 404, "LEVEL_NOT_FOUND", $"No existe el nivel {id}.", $"Los niveles van del 0 al {escalera.MaxLevel}.");
            var services = escalera.ServicesOfLevel(id);
            if (modo is null or "exacto")
            {
                var (start, stop) = await sup.Apply(services);
                return Results.Ok(new { level = id, mode = "exacto", started = start.Started, stopped = stop.Stopped, failed = start.Failed, running = sup.Running.Count });
            }
            if (modo != "sumar")
                return Problem(http, 400, "INVALID_MODE", "modo debe ser 'exacto' o 'sumar'.", "exacto deja encendido solo ese nivel; sumar lo aÃ±ade a lo que ya hay.");
            var s = await sup.Start(services);
            return Results.Ok(new { level = id, mode = "sumar", started = s.Started, stopped = Array.Empty<string>(), failed = s.Failed, running = sup.Running.Count });
        });

        // ---------- Servicios ----------
        api.MapGet("/servicios", (int? nivel, string? dominio, string? q, bool? encendidos) =>
        {
            var items = sup.Catalog.Values.Select(s => ServiceRow(s, sup))
                .Where(s => nivel is null || s.Level <= nivel)
                .Where(s => dominio is null || s.Domain.Equals(dominio, StringComparison.OrdinalIgnoreCase))
                .Where(s => q is null || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(s => encendidos is null || s.Running == encendidos)
                .OrderBy(s => s.Domain).ThenBy(s => s.Port).ToList();
            return Results.Ok(new { items, count = items.Count, running = items.Count(i => i.Running) });
        });

        api.MapGet("/servicios/{name}", (HttpContext http, string name) =>
        {
            if (!sup.Knows(name)) return UnknownService(http, name);
            var s = sup.Catalog[name];
            var lessons = escalera.Lessons().Where(l => l.Services.Contains(name)).Select(l => new { l.Id, l.Title, l.Level }).ToList();
            var missions = escalera.File.Missions.Where(m => m.Services.Contains(name) || m.Steps.Any(st => st.Service == name)).Select(m => new { m.Id, m.Title, m.Level }).ToList();
            return Results.Ok(new
            {
                service = ServiceRow(s, sup),
                info = new { s.Info.Team, s.Info.Version, naming = s.Info.Naming.ToString(), emits = s.Info.Emits ?? Array.Empty<string>(), consumes = s.Info.Consumes ?? Array.Empty<string>() },
                dependsOn = (s.Info.DependsOn ?? Array.Empty<string>()).Select(d => new { name = d, running = sup.IsRunning(d) }).ToList(),
                optionalDependencies = (s.Info.OptionalDependsOn ?? Array.Empty<string>()).Select(d => new { name = d, running = sup.IsRunning(d) }).ToList(),
                allDependencies = sup.DependenciesOf(name),
                dependents = sup.DependentsOf(name).Select(d => new { name = d, running = sup.IsRunning(d) }).ToList(),
                endpoints = Endpoints(repoRoot, name),
                lessons,
                missions,
                swagger = $"http://localhost:{s.Info.Port + sup.Settings.PortOffset}/swagger",
                runningSince = sup.RunningSince(name),
            });
        });

        api.MapPost("/servicios/{name}/encender", async (HttpContext http, string name, bool? dependencias) =>
        {
            if (!sup.Knows(name)) return UnknownService(http, name);
            var r = await sup.Start(new[] { name }, dependencias ?? true);
            return r.Failed.Count > 0
                ? Problem(http, 500, "START_FAILED", $"No arrancaron: {string.Join(", ", r.Failed.Keys)}.", "Mira el log del anfitriÃ³n (logs/host.err.log): suele ser un puerto ocupado por otro programa.", new() { ["failed"] = r.Failed })
                : Results.Ok(new { started = r.Started, alreadyRunning = r.AlreadyRunning, running = sup.Running.Count });
        });

        api.MapPost("/servicios/{name}/apagar", async (HttpContext http, string name, bool? cascada) =>
        {
            if (!sup.Knows(name)) return UnknownService(http, name);
            if (Supervisor.Core.Contains(name))
                return Problem(http, 409, "CORE_SERVICE", $"{name} no se puede apagar.", "Todos los servicios le preguntan la hora al reloj virtual; sin Ã©l no funciona nada.");
            var dependents = sup.RunningDependentsOf(new[] { name });
            if (dependents.Count > 0 && cascada != true)
                return Problem(http, 409, "HAS_RUNNING_DEPENDENTS", $"{dependents.Count} servicios encendidos dependen de {name}.",
                    "ApÃ¡galos tambiÃ©n con ?cascada=true, o deja este encendido.", new() { ["dependents"] = dependents });
            var r = await sup.Stop(new[] { name }, cascada == true);
            return Results.Ok(new { stopped = r.Stopped, notRunning = r.NotRunning, running = sup.Running.Count });
        });

        // ---------- Dominios ----------
        api.MapGet("/dominios", () => Results.Ok(sup.Catalog.Values.GroupBy(s => s.Info.Domain).OrderBy(g => g.Key).Select(g => new
        {
            code = g.Key,
            name = Escalera.DomainName(g.Key),
            services = g.Count(),
            running = g.Count(s => sup.IsRunning(s.Info.Name)),
            levels = g.Select(s => Escalera.LevelOf(s.Info)).Distinct().OrderBy(l => l).ToList(),
        }).ToList()));

        api.MapPost("/dominios/{code}/encender", async (HttpContext http, string code) =>
        {
            var names = DomainServices(sup, code);
            if (names.Count == 0) return Problem(http, 404, "DOMAIN_NOT_FOUND", $"No existe el dominio {code}.", "Mira GET /api/v1/dominios (por ejemplo L-pensions).");
            var r = await sup.Start(names);
            return Results.Ok(new { started = r.Started, alreadyRunning = r.AlreadyRunning, failed = r.Failed, running = sup.Running.Count });
        });

        api.MapPost("/dominios/{code}/apagar", async (HttpContext http, string code, bool? cascada) =>
        {
            var names = DomainServices(sup, code).Where(n => !Supervisor.Core.Contains(n)).ToList();
            if (names.Count == 0) return Problem(http, 404, "DOMAIN_NOT_FOUND", $"No existe el dominio {code}.", "Mira GET /api/v1/dominios (por ejemplo L-pensions).");
            var dependents = sup.RunningDependentsOf(names);
            if (dependents.Count > 0 && cascada != true)
                return Problem(http, 409, "HAS_RUNNING_DEPENDENTS", $"{dependents.Count} servicios encendidos de otros dominios dependen de este.",
                    "ApÃ¡galos tambiÃ©n con ?cascada=true, o deja el dominio encendido.", new() { ["dependents"] = dependents });
            var r = await sup.Stop(names, cascada == true);
            return Results.Ok(new { stopped = r.Stopped, notRunning = r.NotRunning, running = sup.Running.Count });
        });

        // ---------- Misiones ----------
        api.MapGet("/misiones", (int? nivel) => Results.Ok(escalera.File.Missions.Where(m => nivel is null || m.Level == nivel)
            .Select(m => MissionView(m, sup)).ToList()));

        api.MapGet("/misiones/{id}", (HttpContext http, string id) =>
            escalera.File.Missions.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } m
                ? Results.Ok(MissionView(m, sup))
                : Problem(http, 404, "MISSION_NOT_FOUND", $"No existe la misiÃ³n {id}.", "Mira GET /api/v1/misiones."));

        api.MapPost("/misiones/{id}/preparar", async (HttpContext http, string id) =>
        {
            var m = escalera.File.Missions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (m is null) return Problem(http, 404, "MISSION_NOT_FOUND", $"No existe la misiÃ³n {id}.", "Mira GET /api/v1/misiones.");
            var r = await sup.Start(MissionServices(m));
            return Results.Ok(new { mission = m.Id, started = r.Started, alreadyRunning = r.AlreadyRunning, failed = r.Failed, running = sup.Running.Count, ready = r.Failed.Count == 0 });
        });

        // ---------- Academia, flujos y trampas ----------
        api.MapGet("/lecciones", () => Results.Ok(escalera.Lessons().Select(l => new
        {
            l.Id, l.Title, l.Level, l.Exercises, l.Solved, l.Services,
            ready = l.Services.All(sup.IsRunning),
            missing = l.Services.Where(s => !sup.IsRunning(s)).ToList(),
        }).ToList()));

        api.MapPost("/lecciones/{id}/preparar", async (HttpContext http, string id) =>
        {
            var lesson = escalera.Lessons().FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (lesson is null) return Problem(http, 404, "LESSON_NOT_FOUND", $"No existe la lecciÃ³n {id}.", "Las lecciones van de L00 a L33.");
            var r = await sup.Start(lesson.Services);
            return Results.Ok(new { lesson = lesson.Id, started = r.Started, alreadyRunning = r.AlreadyRunning, failed = r.Failed, running = sup.Running.Count });
        });

                api.MapGet("/lecciones/{id}", (HttpContext http, string id) =>
        {
            var dir = Directory.GetDirectories(Path.Combine(repoRoot, "academia", "lecciones"), id + "*").FirstOrDefault();
            if (dir is null || !Directory.Exists(dir)) return Problem(http, 404, "LESSON_NOT_FOUND", $"No existe la lección {id}.", "Las lecciones van de L00 a L33.");
            
            var readmePath = Path.Combine(dir, "README.md");
            var ejemploPath = Path.Combine(dir, "ejemplo.feature");
            var pistasPath = Path.Combine(dir, "pistas.md");
            var ejerciciosDir = Path.Combine(dir, "ejercicios");

            var theory = File.Exists(readmePath) ? File.ReadAllText(readmePath) : "";
            var example = File.Exists(ejemploPath) ? File.ReadAllText(ejemploPath) : "";
            var hints = File.Exists(pistasPath) ? File.ReadAllText(pistasPath) : "";
            
            var exercises = new List<object>();
            if (Directory.Exists(ejerciciosDir))
            {
                foreach (var f in Directory.GetFiles(ejerciciosDir, "*.feature").OrderBy(x => x))
                {
                    exercises.Add(new
                    {
                        file = Path.GetFileName(f),
                        code = File.ReadAllText(f)
                    });
                }
            }

            return Results.Ok(new { id = id.ToUpperInvariant(), theory, example, hints, exercises });
        });

        api.MapPost("/lecciones/{id}/guardar", async (HttpContext http, string id) =>
        {
            var body = await http.Request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (body is null) return Problem(http, 400, "BAD_BODY", "Cuerpo no válido", "Envía un objeto con los nombres de archivo y su código.");
            
            var dir = Directory.GetDirectories(Path.Combine(repoRoot, "academia", "lecciones"), id + "*").FirstOrDefault();
            if (dir is null || !Directory.Exists(dir)) return Problem(http, 404, "LESSON_NOT_FOUND", $"No existe la lección {id}.", "");
            
            var ejerciciosDir = Path.Combine(dir, "ejercicios");
            if (Directory.Exists(ejerciciosDir))
            {
                foreach (var (file, code) in body)
                {
                    var target = Path.Combine(ejerciciosDir, Path.GetFileName(file));
                    File.WriteAllText(target, code, System.Text.Encoding.UTF8);
                }
            }
            return Results.Ok(new { status = "SAVED", id });
        });

        api.MapPost("/lecciones/{id}/corregir", async (HttpContext http, string id) =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"cd '{Path.Combine(repoRoot, "academia")}'; .\\academia.ps1 corregir {id}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            var progFile = Path.Combine(repoRoot, "academia", "progreso.json");
            var progJson = File.Exists(progFile) ? File.ReadAllText(progFile) : "{}";

            return Results.Ok(new { output = stdout, error = stderr, exitCode = p.ExitCode, progress = progJson });
        });

        api.MapGet("/flujos/{file}", (HttpContext http, string file) =>
        {
            var path = Path.Combine(repoRoot, "docs", "flows", Path.GetFileName(file));
            return System.IO.File.Exists(path) && path.EndsWith(".md")
                ? Results.Text(System.IO.File.ReadAllText(path), "text/markdown; charset=utf-8")
                : Problem(http, 404, "FLOW_NOT_FOUND", $"No existe el flujo {file}.", "Los flujos estÃ¡n en docs/flows (F-01 a F-12).");
        });

        api.MapGet("/trampas", () => Results.Ok(ChaosControl.Catalog.Select(t => new
        {
            t.Id, t.Name, t.Symptom, t.HowToFind, t.Nature, level = Math.Min(t.Wave, 4),
            enabled = t.Nature == "SIEMPRE" || Chaos.TrapOn(t.Id),
        }).ToList()));

        // ---------- Pasarela: la web llama a cualquier servicio encendido a travÃ©s de la torre ----------
        api.Map("/proxy/{service}/{**rest}", async (HttpContext http, string service, string? rest) =>
        {
            if (!sup.Knows(service)) return UnknownService(http, service);
            if (!sup.IsRunning(service))
                return Problem(http, 503, "SERVICE_OFF", $"{service} estÃ¡ apagado.", $"EnciÃ©ndelo en la torre o con POST /api/v1/servicios/{service}/encender.");
            var url = $"http://localhost:{sup.Catalog[service].Info.Port + sup.Settings.PortOffset}/{rest}{http.Request.QueryString}";
            using var req = new HttpRequestMessage(new HttpMethod(http.Request.Method), url);
            foreach (var h in http.Request.Headers.Where(h => !SkipRequestHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase)))
                req.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            if (http.Request.ContentLength > 0 || http.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                using var ms = new MemoryStream();
                await http.Request.Body.CopyToAsync(ms);
                req.Content = new ByteArrayContent(ms.ToArray());
                if (http.Request.ContentType is { } ct) req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
            }
            HttpResponseMessage res;
            try { res = await Http.SendAsync(req); }
            catch (Exception ex) { return Problem(http, 502, "SERVICE_UNREACHABLE", $"{service} no respondiÃ³: {ex.Message}", "Puede que se estÃ© apagando; vuelve a intentarlo."); }
            using (res)
            {
                var body = await res.Content.ReadAsByteArrayAsync();
                foreach (var h in res.Headers.Concat(res.Content.Headers).Where(h => !SkipResponseHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase)))
                    http.Response.Headers[h.Key] = h.Value.ToArray();
                http.Response.StatusCode = (int)res.StatusCode;
                if (res.Content.Headers.ContentType is { } type) http.Response.ContentType = type.ToString();
                if (body.Length > 0) await http.Response.Body.WriteAsync(body);
                return Results.Empty;
            }
        });

        api.MapFallback((HttpContext http) => Problem(http, 404, "ROUTE_NOT_FOUND", $"La ruta {http.Request.Method} {http.Request.Path} no existe en la torre.", "Las rutas de la torre empiezan por /api/v1 (estado, niveles, servicios, dominios, misiones, lecciones, trampas, proxy)."));
        return app;
    }

    // ---------- Vistas ----------

    public sealed record ServiceRowView(string Name, string Domain, string DomainName, int Port, int Level, int Wave, string Team, string Description, bool Running, bool Core, bool External);

    private static ServiceRowView ServiceRow(INordvikService s, Supervisor sup) => new(s.Info.Name, s.Info.Domain, Escalera.DomainName(s.Info.Domain),
        s.Info.Port + sup.Settings.PortOffset, Escalera.LevelOf(s.Info), s.Info.Wave, s.Info.Team, s.Info.Description,
        sup.IsRunning(s.Info.Name), Supervisor.Core.Contains(s.Info.Name), s.Info.Domain == "X-external");

    private static object LevelView(LevelDef l, Supervisor sup, Escalera e, IReadOnlyList<LessonInfo> lessons)
    {
        var services = e.ServicesOfLevel(l.Id);
        var running = services.Count(sup.IsRunning);
        var nuevos = e.NewServicesOfLevel(l.Id);
        var ls = lessons.Where(x => l.Lessons.Contains(x.Id)).ToList();
        return new
        {
            l.Id, l.Name, l.Title, l.Profile, l.Route, l.Summary, l.Karate, l.Business, l.Checkpoint, l.Externals,
            services = services.Count, running,
            state = running == services.Count ? "ON" : running == 0 ? "OFF" : "PARTIAL",
            newServices = nuevos.Count,
            newDomains = nuevos.Select(n => e.Catalog[n].Info.Domain).Distinct().OrderBy(d => d).Select(d => new { code = d, name = Escalera.DomainName(d), services = nuevos.Count(n => e.Catalog[n].Info.Domain == d) }).ToList(),
            lessons = ls.Select(x => new { x.Id, x.Title, x.Exercises, x.Solved }).ToList(),
            exercises = ls.Sum(x => x.Exercises),
            solved = ls.Sum(x => x.Solved),
            missions = e.File.Missions.Where(m => m.Level == l.Id).Select(m => new { m.Id, m.Title }).ToList(),
            traps = ChaosControl.Catalog.Where(t => Math.Min(t.Wave, 4) == l.Id).Select(t => new { t.Id, t.Name, t.Nature }).ToList(),
        };
    }

    private static IReadOnlyList<string> MissionServices(MissionDef m) => m.Services.Concat(m.Steps.Select(s => s.Service)).Distinct().ToList();

    private static object MissionView(MissionDef m, Supervisor sup)
    {
        var needed = sup.StartOrder(MissionServices(m)).ToList();
        return new
        {
            m.Id, m.Level, m.Title, m.Story, m.Lessons, m.Flow, m.Traps,
            keyServices = m.Services,
            needed = needed.Count,
            missing = needed.Where(n => !sup.IsRunning(n)).ToList(),
            ready = needed.All(sup.IsRunning),
            steps = m.Steps.Select(s => new { s.Service, s.Method, s.Path, s.What, port = sup.Catalog.TryGetValue(s.Service, out var x) ? x.Info.Port + sup.Settings.PortOffset : 0, running = sup.IsRunning(s.Service) }).ToList(),
        };
    }

    /// <summary>El nivel mÃ¡s alto que estÃ¡ entero encendido (-1 si ni el 0).</summary>
    public static int CurrentLevel(Supervisor sup, Escalera e) =>
        e.File.Levels.OrderBy(l => l.Id).TakeWhile(l => e.ServicesOfLevel(l.Id).All(sup.IsRunning)).Select(l => l.Id).DefaultIfEmpty(-1).Last();

    private static List<string> DomainServices(Supervisor sup, string code) =>
        sup.Catalog.Values.Where(s => s.Info.Domain.Equals(code, StringComparison.OrdinalIgnoreCase)).Select(s => s.Info.Name).ToList();

    private static List<object> Endpoints(string repoRoot, string name)
    {
        var file = Path.Combine(repoRoot, "docs", "openapi", name + ".json");
        var list = new List<object>();
        if (!System.IO.File.Exists(file)) return list;
        try
        {
            var doc = JsonNode.Parse(System.IO.File.ReadAllText(file));
            foreach (var (path, ops) in doc?["paths"]?.AsObject() ?? new JsonObject())
                foreach (var (method, op) in ops!.AsObject())
                    list.Add(new { method = method.ToUpperInvariant(), path, summary = op?["summary"]?.GetValue<string>() ?? "" });
        }
        catch { /* un OpenAPI raro no rompe la ficha */ }
        return list;
    }

    private static IResult UnknownService(HttpContext http, string name) =>
        Problem(http, 404, "SERVICE_NOT_FOUND", $"No existe el servicio {name}.", "Mira la lista completa en GET /api/v1/servicios.");

    private static IResult Problem(HttpContext http, int status, string code, string detail, string hint, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = Problems.BaseType + code.ToLowerInvariant().Replace('_', '-'),
            ["title"] = status switch { 400 => "PeticiÃ³n no vÃ¡lida", 404 => "No encontrado", 409 => "Conflicto", 503 => "Servicio apagado", _ => "Error" },
            ["status"] = status,
            ["detail"] = detail,
            ["instance"] = http.Request.Path.Value,
            ["code"] = code,
            ["correlationId"] = http.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? "corr-" + Guid.NewGuid().ToString("N")[..12],
            ["service"] = "torre-de-control",
            ["hint"] = hint,
        };
        if (extra is not null) foreach (var (k, v) in extra) body[k] = v;
        return Results.Json(body, statusCode: status, contentType: "application/problem+json; charset=utf-8");
    }
}
