using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SearchHit(string Id, string CustomerNumber, string FullName, string Status, string? Email);
public sealed record SearchPage(IReadOnlyList<SearchHit> Items, int Total, int Page, int PageSize, long IndexedUpTo);
public sealed record IndexStatus(long LastSeq, int Documents, string? LastSyncAt, long SyncCount, string? LastError);

/// <summary>
/// customer-search: índice de búsqueda de clientes. Lee el registro de cambios de customer-registry cada segundo,
/// así que un cliente recién creado tarda hasta ~1 s en aparecer (consistencia eventual: usa retry until).
/// </summary>
public sealed class CustomerSearch : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "customer-search", "D-customer", 6304, 1, "Clientes",
        "Búsqueda rápida de clientes por nombre, número o email. Se actualiza cada segundo desde el registro de cambios (consistencia eventual).",
        DependsOn: new[] { "customer-registry", "permission-service" });

    private readonly ConcurrentDictionary<string, SearchHit> _index = new();
    private long _lastSeq;
    private long _syncs;
    private string? _lastSync, _lastError;
    private Timer? _timer;
    private int _running;

    private async Task Sync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            while (true)
            {
                var feed = await Downstream.Get(null, "customer-registry", $"/api/v1/internal/customers/changes?since={_lastSeq}&limit=200");
                if (!feed.Ok) { _lastError = "customer-registry respondió " + feed.Status; return; }
                var items = feed.Json.GetProperty("items").EnumerateArray().ToList();
                foreach (var ch in items)
                {
                    var id = ch.GetProperty("customerId").GetString()!;
                    if (ch.GetProperty("kind").GetString() == "DELETED") { _index.TryRemove(id, out _); continue; }
                    var c = await Downstream.Get(null, "customer-registry", $"/api/v1/internal/customers/{id}");
                    if (c.Status == 404) { _index.TryRemove(id, out _); continue; }
                    if (!c.Ok) continue;
                    var j = c.Json;
                    _index[id] = new SearchHit(id, j.GetProperty("customerNumber").GetString()!, $"{j.GetProperty("firstName").GetString()} {j.GetProperty("lastName").GetString()}",
                        j.GetProperty("status").GetString()!, j.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null);
                }
                _lastSeq = feed.Json.GetProperty("lastSeq").GetInt64();
                _lastError = null;
                if (items.Count < 200) break;
            }
            _syncs++;
            _lastSync = Nordic.Iso(Nordic.ToStockholm(DateTimeOffset.UtcNow));
        }
        catch (Exception ex) { _lastError = ex.Message; }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => _ = Sync(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());

        app.MapGet("/api/v1/search/customers", async (HttpContext http, IClock clock, string? q, int? page, int? pageSize) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Compliance", "Admin" }, scope: "customers:read");
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Problems.BadRequest(http, "QUERY_TOO_SHORT", "q debe tener al menos 2 caracteres.", "Ejemplo: ?q=lind");
            var pg = page ?? 1; var size = pageSize ?? 20;
            IEnumerable<SearchHit> hits = _index.Values.Where(h => $"{h.FullName} {h.CustomerNumber} {h.Email}".Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));
            if (p.Role == "Advisor")
            {
                var mine = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{p.AdvisorId}/customers", forwardAuth: false);
                var ids = mine.Json.EnumerateArray().Select(a => a.GetProperty("customerId").GetString()).ToHashSet();
                hits = hits.Where(h => ids.Contains(h.Id));
            }
            // Trampa T8: el orden cambia en cada petición, así que las páginas se solapan o se saltan clientes
            var list = Chaos.TrapOn("T8") ? hits.OrderBy(_ => Random.Shared.Next()).ToList() : hits.OrderBy(h => h.CustomerNumber).ToList();
            return Results.Ok(new SearchPage(list.Skip((pg - 1) * size).Take(size).ToList(), list.Count, pg, size, _lastSeq));
        }).WithTags("Búsqueda").WithSummary("Buscar clientes (puede tardar ~1 s en ver los cambios)").Produces<SearchPage>();

        app.MapGet("/api/v1/search/status", () => Results.Ok(new IndexStatus(_lastSeq, _index.Count, _lastSync, _syncs, _lastError)))
            .WithTags("Búsqueda").WithSummary("Estado del índice: hasta qué cambio ha leído").Produces<IndexStatus>();

        app.MapPost("/api/v1/search/reindex", async () => { _index.Clear(); _lastSeq = 0; await Sync(); return Results.Ok(new IndexStatus(_lastSeq, _index.Count, _lastSync, _syncs, _lastError)); })
            .WithTags("Búsqueda").WithSummary("Reconstruir el índice desde cero");
    }
}
