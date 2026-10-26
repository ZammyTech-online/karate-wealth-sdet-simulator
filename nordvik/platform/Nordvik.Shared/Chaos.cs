using System.Collections.Concurrent;

namespace Nordvik.Shared;

/// <summary>Un fallo inyectado: en qué servicio y ruta, cuánta latencia, qué estado de error, con qué probabilidad y cuántas veces.</summary>
public sealed record ChaosRule(string Id, string Service, string? Method, string? PathPrefix, int DelayMs, int Status, double Probability, int? Remaining, long? ExpiresTicks, string CreatedAt, long Hits);

/// <summary>
/// Caos deliberado. chaos-control escribe aquí las reglas y NordvikMiddleware las aplica en cada petición de cada servicio
/// (todos viven en el mismo proceso). Sin reglas, el coste es una búsqueda en un diccionario vacío.
/// También se puede pedir caos para UNA petición con la cabecera X-Chaos-Scenario: "latency=1500", "status=503" o "latency=800;status=500".
/// </summary>
public static class Chaos
{
    public const string ScenarioHeader = "X-Chaos-Scenario";
    private static readonly ConcurrentDictionary<string, ChaosRule> Rules = new();
    private static readonly ConcurrentDictionary<string, bool> Traps = new();
    private static readonly object Gate = new();

    public static IReadOnlyList<ChaosRule> All => Rules.Values.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    public static void Put(ChaosRule r) => Rules[r.Id] = r;
    public static bool Remove(string id) => Rules.TryRemove(id, out _);
    public static void Clear() => Rules.Clear();

    public static bool TrapOn(string id) => Traps.TryGetValue(id, out var on) && on;
    public static void SetTrap(string id, bool on) => Traps[id] = on;
    public static void ClearTraps() => Traps.Clear();

    /// <summary>La primera regla que aplica a esta petición (y gasta una vez si es limitada). chaos-control nunca se sabotea a sí mismo.</summary>
    public static ChaosRule? Match(string service, string method, string path, long nowTicks)
    {
        if (Rules.IsEmpty || service == "chaos-control") return null;
        lock (Gate)
        {
            foreach (var r in Rules.Values.OrderBy(r => r.CreatedAt))
            {
                if (r.ExpiresTicks is { } e && nowTicks >= e) { Rules.TryRemove(r.Id, out _); continue; }
                if (r.Service != "*" && r.Service != service) continue;
                if (r.Method is not null && !r.Method.Equals(method, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.PathPrefix is not null && !path.StartsWith(r.PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Probability < 1 && Random.Shared.NextDouble() >= r.Probability) continue;
                var next = r with { Hits = r.Hits + 1, Remaining = r.Remaining - 1 };
                if (next.Remaining is <= 0) Rules.TryRemove(r.Id, out _); else Rules[r.Id] = next;
                return next;
            }
        }
        return null;
    }

    /// <summary>Interpreta X-Chaos-Scenario. Devuelve null si la cabecera no es válida.</summary>
    public static (int DelayMs, int Status)? Parse(string? scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario)) return null;
        int delay = 0, status = 0;
        foreach (var part in scenario.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !int.TryParse(kv[1], out var v)) return null;
            switch (kv[0].ToLowerInvariant())
            {
                case "latency": delay = Math.Clamp(v, 0, 30000); break;
                case "status": if (v is < 400 or > 599) return null; status = v; break;
                default: return null;
            }
        }
        return (delay, status);
    }
}
