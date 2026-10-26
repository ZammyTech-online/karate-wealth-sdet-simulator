using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Nordvik.Shared;

namespace Nordvik.Host;

public sealed record StartResult(IReadOnlyList<string> Started, IReadOnlyList<string> AlreadyRunning, IReadOnlyDictionary<string, string> Failed);
public sealed record StopResult(IReadOnlyList<string> Stopped, IReadOnlyList<string> NotRunning, IReadOnlyList<string> Protected);

/// <summary>
/// Enciende y apaga microservicios dentro del anfitrión sin reiniciarlo (ADR-014). Cada servicio es su propia
/// WebApplication: encender = construirla y abrir su puerto; apagar = pararla y liberar el puerto.
/// Siempre respeta las dependencias: al encender trae las suyas; al apagar avisa de quién depende de él.
/// </summary>
public sealed class Supervisor
{
    /// <summary>Servicios que nunca se apagan: todos los demás le preguntan la hora.</summary>
    public static readonly IReadOnlySet<string> Core = new HashSet<string> { "clock-service" };

    private readonly Dictionary<string, INordvikService> _catalog;
    private readonly HostSettings _settings;
    private readonly ConcurrentDictionary<string, WebApplication> _running = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _since = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Supervisor(IEnumerable<INordvikService> catalog, HostSettings settings)
    {
        _catalog = catalog.ToDictionary(s => s.Info.Name);
        _settings = settings;
    }

    public HostSettings Settings => _settings;
    public IReadOnlyDictionary<string, INordvikService> Catalog => _catalog;
    public IReadOnlyCollection<string> Running => _running.Keys.OrderBy(n => n).ToList();
    public bool IsRunning(string name) => _running.ContainsKey(name);
    public DateTimeOffset? RunningSince(string name) => _since.TryGetValue(name, out var t) ? t : null;
    public bool Knows(string name) => _catalog.ContainsKey(name);

    /// <summary>Se dispara después de cada cambio (para reescribir data/host.json).</summary>
    public event Action<Supervisor>? Changed;

    /// <summary>Todo aquello de lo que depende un servicio, recursivamente (sin él mismo).</summary>
    public IReadOnlyList<string> DependenciesOf(string name)
    {
        var seen = new List<string>();
        void Visit(string n)
        {
            if (!_catalog.TryGetValue(n, out var s)) return;
            foreach (var d in s.Info.DependsOn ?? Array.Empty<string>())
                if (d != name && !seen.Contains(d) && _catalog.ContainsKey(d)) { seen.Add(d); Visit(d); }
        }
        Visit(name);
        return seen;
    }

    /// <summary>Quién depende de un servicio, directa o indirectamente (sin él mismo).</summary>
    public IReadOnlyList<string> DependentsOf(string name)
    {
        var result = new List<string>();
        var queue = new Queue<string>(new[] { name });
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var s in _catalog.Values)
                if ((s.Info.DependsOn ?? Array.Empty<string>()).Contains(current) && s.Info.Name != name && !result.Contains(s.Info.Name))
                {
                    result.Add(s.Info.Name);
                    queue.Enqueue(s.Info.Name);
                }
        }
        return result;
    }

    /// <summary>Servicios encendidos que se quedarían sin una dependencia si se apagan estos.</summary>
    public IReadOnlyList<string> RunningDependentsOf(IEnumerable<string> names)
    {
        var set = names.ToHashSet();
        return set.SelectMany(DependentsOf).Distinct().Where(d => !set.Contains(d) && IsRunning(d)).OrderBy(d => d).ToList();
    }

    /// <summary>Los servicios y sus dependencias en orden de arranque (primero lo que no depende de nada).</summary>
    public IReadOnlyList<string> StartOrder(IEnumerable<string> names, bool withDependencies = true)
    {
        var order = new List<string>();
        var wanted = names.Where(_catalog.ContainsKey).ToHashSet();
        void Visit(string n, HashSet<string> path)
        {
            if (order.Contains(n) || !path.Add(n)) return;
            foreach (var d in _catalog[n].Info.DependsOn ?? Array.Empty<string>())
                if (_catalog.ContainsKey(d) && (withDependencies || wanted.Contains(d))) Visit(d, path);
            path.Remove(n);
            order.Add(n);
        }
        foreach (var n in wanted.OrderBy(n => n)) Visit(n, new HashSet<string>());
        foreach (var c in Core) if (!order.Contains(c)) order.Insert(0, c);
        order.Remove("clock-service");
        order.Insert(0, "clock-service");
        return order;
    }

    public async Task<StartResult> Start(IEnumerable<string> names, bool withDependencies = true)
    {
        await _gate.WaitAsync();
        try { return await StartLocked(names, withDependencies); }
        finally { _gate.Release(); }
    }

    private async Task<StartResult> StartLocked(IEnumerable<string> names, bool withDependencies)
    {
        var started = new List<string>(); var already = new List<string>(); var failed = new Dictionary<string, string>();
        foreach (var name in StartOrder(names, withDependencies))
        {
            if (_running.ContainsKey(name)) { already.Add(name); continue; }
            WebApplication? app = null;
            try
            {
                app = ServiceRunner.Build(_catalog[name], _settings);
                await app.StartAsync();
                _running[name] = app;
                _since[name] = DateTimeOffset.UtcNow;
                started.Add(name);
            }
            catch (Exception ex)
            {
                failed[name] = ex.Message;
                if (app is not null) try { await app.DisposeAsync(); } catch { /* ya estaba roto */ }
            }
        }
        if (started.Count > 0) Changed?.Invoke(this);
        return new StartResult(started, already, failed);
    }

    /// <summary>Apaga servicios. Con cascade también apaga los encendidos que dependen de ellos; sin él, solo los pedidos.</summary>
    public async Task<StopResult> Stop(IEnumerable<string> names, bool cascade = false)
    {
        await _gate.WaitAsync();
        try { return await StopLocked(names, cascade); }
        finally { _gate.Release(); }
    }

    private async Task<StopResult> StopLocked(IEnumerable<string> names, bool cascade)
    {
        var requested = names.Where(_catalog.ContainsKey).ToHashSet();
        if (cascade) foreach (var d in RunningDependentsOf(requested)) requested.Add(d);
        var protectedOnes = requested.Where(Core.Contains).OrderBy(n => n).ToList();
        requested.ExceptWith(Core);
        // Primero los que dependen de otros: se apagan en el orden inverso al de arranque.
        var order = StartOrder(requested, withDependencies: false).Where(requested.Contains).Reverse().ToList();
        var stopped = new List<string>(); var notRunning = new List<string>();
        foreach (var name in order)
        {
            if (!_running.TryRemove(name, out var app)) { notRunning.Add(name); continue; }
            _since.TryRemove(name, out _);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await app.StopAsync(cts.Token);
            }
            catch { /* si tarda, lo soltamos igual */ }
            await app.DisposeAsync();
            stopped.Add(name);
        }
        if (stopped.Count > 0)
        {
            GC.Collect();
            Changed?.Invoke(this);
        }
        return new StopResult(stopped, notRunning, protectedOnes);
    }

    /// <summary>Deja encendido exactamente este conjunto (más sus dependencias y el núcleo): apaga lo demás y enciende lo que falte.</summary>
    public async Task<(StartResult Started, StopResult Stopped)> Apply(IEnumerable<string> names)
    {
        await _gate.WaitAsync();
        try
        {
            var target = StartOrder(names).ToHashSet();
            var stop = await StopLocked(_running.Keys.Where(r => !target.Contains(r)).ToList(), cascade: false);
            var start = await StartLocked(target, withDependencies: true);
            return (start, stop);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAll()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var (name, app) in _running.ToArray())
            {
                _running.TryRemove(name, out _);
                try { using var cts = new CancellationTokenSource(3000); await app.StopAsync(cts.Token); } catch { }
                await app.DisposeAsync();
            }
        }
        finally { _gate.Release(); }
    }
}
