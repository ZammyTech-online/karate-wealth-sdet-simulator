using System.Diagnostics;
using System.Text.Json;
using Nordvik.Host;
using Nordvik.Shared;

// Nordvik.Host: arranca dentro de un solo proceso los microservicios de un perfil (ADR-001).
// Uso: Nordvik.Host --profile nivel-0 [--level 2] [--only uuid-service,clock-service] [--no-learning] [--no-tower]

string Arg(string name, string? fallback = null)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback ?? "";
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var root = Environment.GetEnvironmentVariable("NORDVIK_HOME") ?? FindRoot(AppContext.BaseDirectory);
var profileName = Arg("--profile", "nivel-0");
var profilePath = Path.Combine(root, "profiles", profileName + ".json");
if (!File.Exists(profilePath))
{
    Console.Error.WriteLine($"No existe el perfil '{profileName}' ({profilePath}).");
    return 2;
}

var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(profilePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var catalog = ServiceCatalog.Create().ToDictionary(s => s.Info.Name);
ServiceDirectory.Register(catalog.Values.Select(s => s.Info));
Nordvik.Services.ApiGateway.UseCatalog(catalog.Values.Select(s => s.Info));
// --only a,b enciende esos servicios y todas sus dependencias (para gastar la mínima RAM)
var wanted = Arg("--level") is { Length: > 0 } lvl && int.TryParse(lvl, out var levelId)
    ? catalog.Values.Where(s => Escalera.LevelOf(s.Info) <= levelId).Select(s => s.Info.Name).ToArray()
    : Arg("--only") is { Length: > 0 } only
    ? ServiceCatalog.WithDependencies(only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), catalog).ToArray()
    : profile.Services;
var unknown = wanted.Where(w => !catalog.ContainsKey(w)).ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine("Servicios desconocidos en el perfil: " + string.Join(", ", unknown));
    return 3;
}

var dataDir = Path.Combine(root, "data");
var logDir = Path.Combine(root, "logs");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(logDir);
var learning = profile.LearningMode && !args.Contains("--no-learning");
var clockUrl = $"http://localhost:{catalog["clock-service"].Info.Port}";
var settings = new HostSettings(root, dataDir, logDir, learning, clockUrl, profile.Name);

// El Supervisor enciende y apaga servicios en caliente (ADR-014); la Torre de control (puerto 3000) lo maneja desde el navegador.
var supervisor = new Supervisor(catalog.Values, settings);
Escalera? escalera = null;
try { escalera = new Escalera(root, catalog); } catch { /* sin escalera la torre no arranca, pero los servicios sí */ }
var hotChanges = 0;
IReadOnlyDictionary<string, string> failedAtStart = new Dictionary<string, string>();
void WriteHostJson(Supervisor s) =>
    File.WriteAllText(Path.Combine(dataDir, "host.json"), JsonSerializer.Serialize(new
    {
        pid = Environment.ProcessId,
        profile = profile.Name,
        only = Arg("--only") is { Length: > 0 },
        startedAt = DateTimeOffset.Now.ToString("O"),
        tower = args.Contains("--no-tower") ? null : $"http://localhost:{ControlTower.Port}",
        // Nivel de la escalera entero encendido ahora mismo y cuántos cambios en caliente ha habido desde el arranque
        level = escalera is null ? (int?)null : ControlTower.CurrentLevel(s, escalera),
        hotChanges,
        failed = failedAtStart.Keys.ToList(),
        services = s.Running.Select(n => new { name = n, port = catalog[n].Info.Port }).OrderBy(x => x.port).ToList(),
    }, new JsonSerializerOptions { WriteIndented = true }));

var sw = Stopwatch.StartNew();
var result = await supervisor.Start(wanted, withDependencies: false);
failedAtStart = result.Failed;
foreach (var (name, error) in result.Failed)
    Console.Error.WriteLine($"[ERROR] {name} no arrancó en el puerto {catalog[name].Info.Port}: {error}");

Microsoft.AspNetCore.Builder.WebApplication? tower = null;
if (!args.Contains("--no-tower"))
{
    try { tower = ControlTower.Build(supervisor, root); await tower.StartAsync(); }
    catch (Exception ex) { Console.Error.WriteLine($"[AVISO] La torre de control no arrancó en el puerto {ControlTower.Port}: {ex.Message}"); tower = null; }
}

Console.WriteLine();
Console.WriteLine($"  Nordvik.Host · perfil {profile.Name} · {supervisor.Running.Count}/{wanted.Length} servicios en {sw.ElapsedMilliseconds} ms · modo didáctico: {(learning ? "sí" : "no")}");
Console.WriteLine("  " + new string('-', 78));
foreach (var name in supervisor.Running.OrderBy(n => catalog[n].Info.Port))
    Console.WriteLine($"  {catalog[name].Info.Port,-6} {name,-24} http://localhost:{catalog[name].Info.Port}/swagger");
Console.WriteLine("  " + new string('-', 78));
if (tower is not null) Console.WriteLine($"  Torre de control: http://localhost:{ControlTower.Port}");
Console.WriteLine($"  PID {Environment.ProcessId} · Ctrl+C para parar · logs en {logDir}");

WriteHostJson(supervisor);
supervisor.Changed += s => { hotChanges++; WriteHostJson(s); };

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
if (tower is not null) { using var cts = new CancellationTokenSource(3000); await tower.StopAsync(cts.Token); }
await supervisor.StopAll();
return result.Failed.Count == 0 ? 0 : 1;

static string FindRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nordvik.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

internal sealed record Profile(string Name, int Wave, string Description, bool LearningMode, string[] Services);
