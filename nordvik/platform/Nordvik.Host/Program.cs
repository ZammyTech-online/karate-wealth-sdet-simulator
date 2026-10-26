using System.Diagnostics;
using System.Text.Json;
using Nordvik.Host;
using Nordvik.Shared;

// Nordvik.Host: arranca dentro de un solo proceso los microservicios de un perfil (ADR-001).
// Uso: Nordvik.Host --profile nivel-0 [--only uuid-service,clock-service] [--no-learning]

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
var wanted = Arg("--only") is { Length: > 0 } only
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

// El reloj arranca primero: los demás le preguntan la hora.
var ordered = wanted.OrderBy(w => w == "clock-service" ? 0 : 1).Select(w => catalog[w]).ToList();
var apps = new List<(INordvikService Svc, Microsoft.AspNetCore.Builder.WebApplication App)>();
var sw = Stopwatch.StartNew();
foreach (var svc in ordered)
{
    try
    {
        var app = ServiceRunner.Build(svc, settings);
        await app.StartAsync();
        apps.Add((svc, app));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ERROR] {svc.Info.Name} no arrancó en el puerto {svc.Info.Port}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"  Nordvik.Host · perfil {profile.Name} · {apps.Count}/{ordered.Count} servicios en {sw.ElapsedMilliseconds} ms · modo didáctico: {(learning ? "sí" : "no")}");
Console.WriteLine("  " + new string('-', 78));
foreach (var (svc, _) in apps.OrderBy(a => a.Svc.Info.Port))
    Console.WriteLine($"  {svc.Info.Port,-6} {svc.Info.Name,-24} http://localhost:{svc.Info.Port}/swagger");
Console.WriteLine("  " + new string('-', 78));
Console.WriteLine($"  PID {Environment.ProcessId} · Ctrl+C para parar · logs en {logDir}");

File.WriteAllText(Path.Combine(dataDir, "host.json"), JsonSerializer.Serialize(new
{
    pid = Environment.ProcessId,
    profile = profile.Name,
    only = Arg("--only") is { Length: > 0 },
    startedAt = DateTimeOffset.Now.ToString("O"),
    services = apps.Select(a => new { name = a.Svc.Info.Name, port = a.Svc.Info.Port }).ToList(),
}, new JsonSerializerOptions { WriteIndented = true }));

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
foreach (var (_, app) in apps) { using var cts = new CancellationTokenSource(3000); await app.StopAsync(cts.Token); }
return apps.Count == ordered.Count ? 0 : 1;

static string FindRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nordvik.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

internal sealed record Profile(string Name, int Wave, string Description, bool LearningMode, string[] Services);
