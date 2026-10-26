using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ScreenRequest(string? Name, string? BirthDate, string? Country, string[]? Lists);
public sealed record ListEntry(string EntryId, string List, string Type, string Name, string? BirthDate, string Country, string? Role, string? RelatedTo);
public sealed record ScreenLog(string At, string? ApiKey, string Body);

/// <summary>
/// sanctionscreen-sim · sistema externo E3 (simulado): listas de sanciones y de PEP con coincidencia difusa por nombre
/// (y fecha de nacimiento como desempate). Exige X-Api-Key (la actual y la anterior valen). Guarda las peticiones recibidas
/// en /__admin/requests para que compruebes que Nordvik nunca le manda el número nacional.
/// </summary>
public sealed class SanctionScreenSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "sanctionscreen-sim", "X-external", 7703, 3, "Sistema externo (simulado)",
        "E3 SanctionScreen: cribado de sanciones y PEP con coincidencia difusa, API key rotatoria, respuestas parciales y registro de peticiones recibidas.",
        DependsOn: new[] { "clock-service" });

    public static readonly string[] ValidKeys = { "ss-key-2026-b", "ss-key-2026-a" };
    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentQueue<ScreenLog> _requests = new();
    private volatile bool _partial;
    private readonly ConcurrentDictionary<string, ListEntry> _entries = new(new[]
    {
        new ListEntry("EU-0001", "EU_CONSOLIDATED", "SANCTION", "Viktor Draganov", "1961-04-12", "XX", null, null),
        new ListEntry("EU-0002", "EU_CONSOLIDATED", "SANCTION", "Marat Ilyasov", "1958-09-30", "XX", null, null),
        new ListEntry("UN-0101", "UN_SC", "SANCTION", "Hassan Al-Rahim Bakri", "1970-01-01", "YY", null, null),
        new ListEntry("OFAC-0007", "OFAC_SDN", "SANCTION", "Elena Kravets", "1975-06-18", "XX", null, null),
        new ListEntry("OFAC-0008", "OFAC_SDN", "SANCTION", "Nordic Shadow Shipping AB", null, "SE", null, null),
        new ListEntry("PEP-SE-001", "PEP_NORDIC", "PEP", "Karl-Johan Lindqvist", "1966-02-03", "SE", "Ministro de Hacienda (ficticio)", null),
        new ListEntry("PEP-SE-002", "PEP_NORDIC", "PEP", "Annika Strömberg", "1972-11-21", "SE", "Diputada del Riksdag (ficticia)", null),
        new ListEntry("PEP-NO-001", "PEP_NORDIC", "PEP", "Ola Haugland", "1959-05-17", "NO", "Gobernador del banco central (ficticio)", null),
        new ListEntry("PEP-DK-001", "PEP_NORDIC", "PEP", "Mette Kjærgaard", "1980-08-08", "DK", "Alcaldesa de Aarhus (ficticia)", null),
        new ListEntry("RCA-SE-001", "PEP_NORDIC", "PEP_RCA", "Sofia Lindqvist", "1968-07-14", "SE", "Cónyuge", "PEP-SE-001"),
        new ListEntry("RCA-SE-002", "PEP_NORDIC", "PEP_RCA", "Erik Strömberg", "1999-03-02", "SE", "Hijo", "PEP-SE-002"),
    }.Select(e => new KeyValuePair<string, ListEntry>(e.EntryId, e)));

    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch) switch { 'ø' => 'o', 'æ' => 'a', 'ß' => 's', var x => x };
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(w => w, StringComparer.Ordinal));
    }

    public static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }

    /// <summary>Puntuación 0-100: similitud del nombre (palabras ordenadas, sin acentos) y ±10 por la fecha de nacimiento.</summary>
    public static int Score(string name, string? birthDate, ListEntry e)
    {
        var a = Fold(name); var b = Fold(e.Name);
        if (a.Length == 0) return 0;
        var sim = 1.0 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
        var score = (int)Math.Round(sim * 90);
        if (birthDate is not null && e.BirthDate is not null) score += birthDate == e.BirthDate ? 10 : -10;
        return Math.Clamp(score, 0, 100);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);

        app.MapPost("/v2/screen", async (HttpContext http, ScreenRequest b) =>
        {
            var key = http.Request.Headers["X-Api-Key"].FirstOrDefault();
            http.Request.EnableBuffering();
            _requests.Enqueue(new ScreenLog(DateTimeOffset.UtcNow.ToString("O"), key is null ? null : key[..Math.Min(6, key.Length)] + "…", System.Text.Json.JsonSerializer.Serialize(b, Canonical.Web)));
            while (_requests.Count > 200) _requests.TryDequeue(out _);
            if (await _behaviour.Apply() is { } fail) return fail;
            if (key is null || !ValidKeys.Contains(key)) return Results.Json(new { error = "INVALID_API_KEY", message = "X-Api-Key missing, revoked or expired" }, statusCode: 401);
            if (string.IsNullOrWhiteSpace(b.Name)) return Results.Json(new { error = "BAD_REQUEST", message = "name is required" }, statusCode: 400);
            if (b.Name.Any(char.IsDigit) && b.Name.Count(char.IsDigit) >= 6)
                return Results.Json(new { error = "BAD_REQUEST", message = "name looks like an identifier; send names only" }, statusCode: 400);
            var lists = b.Lists is { Length: > 0 } ? b.Lists : _entries.Values.Select(e => e.List).Distinct().ToArray();
            if (_partial) lists = lists.Where(l => l != "OFAC_SDN").ToArray();
            var hits = _entries.Values.Where(e => lists.Contains(e.List))
                .Select(e => new { entryId = e.EntryId, list = e.List, type = e.Type, name = e.Name, birthDate = e.BirthDate, country = e.Country, role = e.Role, relatedTo = e.RelatedTo, score = Score(b.Name, b.BirthDate, e) })
                .Where(h => h.score >= 60).OrderByDescending(h => h.score).ToList();
            return Results.Ok(new { screeningId = "ss_" + Guid.NewGuid().ToString("N")[..12], screenedLists = lists, partial = _partial, hits });
        }).WithTags("SanctionScreen v2").WithSummary("Cribar un nombre (y fecha de nacimiento) contra las listas; devuelve coincidencias con score ≥ 60");

        app.MapGet("/__admin/requests", () => Results.Ok(new { items = _requests.Reverse().ToList(), count = _requests.Count })).WithTags("Administración del simulador")
            .WithSummary("Peticiones recibidas, la última primero (para comprobar qué datos nos mandan)");
        app.MapDelete("/__admin/requests", () => { _requests.Clear(); return Results.NoContent(); }).WithTags("Administración del simulador");
        app.MapGet("/__admin/entries", () => Results.Ok(new { items = _entries.Values.OrderBy(e => e.EntryId) })).WithTags("Administración del simulador").WithSummary("Listas cargadas");
        app.MapPost("/__admin/entries", (ListEntry e) =>
        {
            if (string.IsNullOrWhiteSpace(e.EntryId) || string.IsNullOrWhiteSpace(e.Name)) return Results.Json(new { error = "BAD_REQUEST", message = "entryId and name are required" }, statusCode: 400);
            _entries[e.EntryId] = e;
            return Results.Created($"/__admin/entries/{e.EntryId}", e);
        }).WithTags("Administración del simulador").WithSummary("Añadir una entrada a una lista (para preparar un caso)");
        app.MapPost("/__admin/partial", (HttpContext http, bool? on) => { _partial = on ?? true; return Results.Ok(new { partial = _partial }); })
            .WithTags("Administración del simulador").WithSummary("Respuestas parciales: OFAC_SDN deja de cribarse (?on=false para volver)");
    }
}
