using System.Text.Json;
using System.Text.RegularExpressions;
using Nordvik.Shared;

namespace Nordvik.Host;

public sealed record EscaleraFile(int Version, string Note, List<LevelDef> Levels, List<MissionDef> Missions);
public sealed record LevelDef(int Id, string Name, string Title, string Profile, string Route, List<string> Lessons, string Summary,
    List<string> Karate, List<string> Business, string Checkpoint, List<string> Externals);
public sealed record MissionDef(string Id, int Level, string Title, string Story, List<string> Services, List<MissionStep> Steps,
    List<string> Lessons, string? Flow, List<string> Traps);
public sealed record MissionStep(string Service, string Method, string Path, string What);
public sealed record LessonInfo(string Id, string Folder, string Title, int Level, int Exercises, int Solved, IReadOnlyList<string> Services);

/// <summary>
/// La escalera de aprendizaje (torre/escalera.json): niveles 0-4, sus perfiles, lecciones y misiones.
/// Los servicios de cada nivel no se escriben a mano: nivel N = todos los servicios con oleada &lt;= N (sale del código).
/// </summary>
public sealed class Escalera
{
    public static readonly IReadOnlyDictionary<string, string> DomainNames = new Dictionary<string, string>
    {
        ["A-utilities"] = "Utilidades", ["B-platform"] = "Plataforma", ["C-identity"] = "Identidad", ["D-customer"] = "Clientes",
        ["E-compliance"] = "Cumplimiento", ["F-privacy"] = "Privacidad", ["G-advice"] = "Asesoramiento y voz", ["H-contracts"] = "Contratos y documentos",
        ["I-market"] = "Mercado", ["J-investment"] = "Inversiones", ["K-payments"] = "Pagos y contabilidad", ["L-pensions"] = "Pensiones",
        ["M-insurance"] = "Seguros", ["N-tax"] = "Fiscalidad", ["O-operations"] = "Operaciones", ["P-integrations"] = "Integraciones",
        ["Q-quality"] = "Calidad", ["X-external"] = "Sistemas externos (simulados)",
    };

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public EscaleraFile File { get; }
    public string RepoRoot { get; }
    public IReadOnlyDictionary<string, INordvikService> Catalog { get; }
    public int MaxLevel => File.Levels.Max(l => l.Id);

    public Escalera(string repoRoot, IReadOnlyDictionary<string, INordvikService> catalog)
    {
        RepoRoot = repoRoot;
        Catalog = catalog;
        File = JsonSerializer.Deserialize<EscaleraFile>(System.IO.File.ReadAllText(Path.Combine(repoRoot, "torre", "escalera.json")), Json)!;
    }

    public static int LevelOf(NordvikServiceInfo info) => Math.Min(info.Wave, 4);
    public LevelDef Level(int id) => File.Levels.Single(l => l.Id == id);
    public IReadOnlyList<string> ServicesOfLevel(int id) => Catalog.Values.Where(s => LevelOf(s.Info) <= id).Select(s => s.Info.Name).OrderBy(n => n).ToList();
    public IReadOnlyList<string> NewServicesOfLevel(int id) => Catalog.Values.Where(s => LevelOf(s.Info) == id).Select(s => s.Info.Name).OrderBy(n => n).ToList();
    public static string DomainName(string code) => DomainNames.TryGetValue(code, out var n) ? n : code;

    /// <summary>Las lecciones de la academia: título, ejercicios resueltos y qué servicios usan de verdad (leído de sus .feature).</summary>
    public IReadOnlyList<LessonInfo> Lessons()
    {
        var academia = Path.Combine(RepoRoot, "academia");
        var lessonsDir = Path.Combine(academia, "lecciones");
        if (!Directory.Exists(lessonsDir)) return Array.Empty<LessonInfo>();
        var urlToService = UrlKeysToServices();
        var progress = ReadProgress(Path.Combine(academia, "progreso.json"));
        var result = new List<LessonInfo>();
        foreach (var dir in Directory.GetDirectories(lessonsDir, "L*").OrderBy(d => d))
        {
            var folder = Path.GetFileName(dir);
            var id = folder[..3];
            var readme = Path.Combine(dir, "README.md");
            var title = System.IO.File.Exists(readme) ? System.IO.File.ReadLines(readme).FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() ?? folder : folder;
            var exercisesDir = Path.Combine(dir, "ejercicios");
            var exercises = Directory.Exists(exercisesDir) ? Directory.GetFiles(exercisesDir, "E*.feature").Length : 0;
            var solved = progress.TryGetValue(id, out var p) ? p.Count(kv => kv.Value == "OK") : 0;
            var level = File.Levels.FirstOrDefault(l => l.Lessons.Contains(id))?.Id ?? -1;
            result.Add(new LessonInfo(id, folder, title, level, exercises, solved, ServicesUsedBy(Path.Combine(academia, "soluciones", folder), urlToService)));
        }
        return result;
    }

    /// <summary>xxxUrl de karate-config.js → nombre del servicio (por su puerto).</summary>
    public IReadOnlyDictionary<string, string> UrlKeysToServices()
    {
        var config = Path.Combine(RepoRoot, "academia", "config", "karate-config.js");
        var byPort = Catalog.Values.ToDictionary(s => s.Info.Port, s => s.Info.Name);
        var map = new Dictionary<string, string>();
        if (!System.IO.File.Exists(config)) return map;
        foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(config), @"(\w+Url):\s*host \+ ':(\d+)'"))
            if (byPort.TryGetValue(int.Parse(m.Groups[2].Value), out var name)) map[m.Groups[1].Value] = name;
        return map;
    }

    private static IReadOnlyList<string> ServicesUsedBy(string dir, IReadOnlyDictionary<string, string> urlToService)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        var used = new SortedSet<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".feature") || f.EndsWith(".js")))
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(f), @"\b(\w+Url)\b"))
                if (urlToService.TryGetValue(m.Groups[1].Value, out var s)) used.Add(s);
        return used.ToList();
    }

    private static Dictionary<string, Dictionary<string, string>> ReadProgress(string path)
    {
        try
        {
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(System.IO.File.ReadAllText(path)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}
