using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FeedNav(string Isin, string Date, string Nav, string Currency, string PublishedAt, string Source, bool Stale, string? ETag);
public sealed record FeedStats(long Requests, long FeedCalls, long NotModified, long CacheHits, long StaleServed, long Errors);

/// <summary>
/// price-feed-adapter: descarga NAV del proveedor E5 usando ETag (If-None-Match → 304 = no se vuelve a descargar).
/// Si el proveedor está caído, sirve el último valor conocido marcado como stale (cabecera Warning).
/// </summary>
public sealed class PriceFeedAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "price-feed-adapter", "P-integrations", 7501, 2, "Integraciones",
        "Adaptador del proveedor de precios E5 con caché por ETag, contadores de 304 y respuesta stale si el proveedor cae.",
        DependsOn: new[] { "pricefeed-sim", "clock-service" }, Emits: new[] { "price.nav.published" });

    private static readonly Dictionary<string, string> Key = new() { ["X-Api-Key"] = "feed-key-2026" };
    private long _requests, _feedCalls, _notModified, _cacheHits, _stale, _errors;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS navs (isin TEXT, date TEXT, nav TEXT, currency TEXT, published_at TEXT, PRIMARY KEY (isin, date));
                     CREATE TABLE IF NOT EXISTS latest (isin TEXT PRIMARY KEY, etag TEXT, date TEXT)");
    }

    private static string Url(string path) => ServiceDirectory.UrlOf("pricefeed-sim") + path;

    private FeedNav? Cached(Microsoft.Data.Sqlite.SqliteConnection db, string isin, string date, string source, bool stale, string? etag)
    {
        var r = db.QuerySingleOrDefault<(string Isin, string Date, string Nav, string Ccy, string Pub)>("SELECT isin, date, nav, currency, published_at FROM navs WHERE isin = @isin AND date = @date", new { isin, date });
        return r.Isin is null ? null : new FeedNav(r.Isin, r.Date, r.Nav, r.Ccy, r.Pub, source, stale, etag);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapGet("/api/v1/nav/{isin}", async (HttpContext http, IClock clock, string isin, string? date) =>
        {
            Interlocked.Increment(ref _requests);
            using var db = ctx.OpenDb();
            if (date is not null)
            {
                if (Cached(db, isin, date, "cache", false, null) is { } hit) { Interlocked.Increment(ref _cacheHits); return Results.Ok(hit); }
            }
            var latest = date is null ? db.QuerySingleOrDefault<(string? Isin, string? ETag, string? Date)>("SELECT isin, etag, date FROM latest WHERE isin = @isin", new { isin }) : default;
            var headers = new Dictionary<string, string>(Key);
            if (latest.ETag is not null) headers["If-None-Match"] = latest.ETag;
            DownstreamReply r;
            try
            {
                Interlocked.Increment(ref _feedCalls);
                r = await Downstream.SendUrl(HttpMethod.Get, Url($"/feed/v1/nav/{isin}" + (date is null ? "" : "?date=" + date)), null, headers, TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                r = new DownstreamReply(599, null, "");
            }
            if (r.Status == 304 && latest.Date is not null)
            {
                Interlocked.Increment(ref _notModified);
                return Results.Ok(Cached(db, isin, latest.Date, "cache-304", false, latest.ETag));
            }
            if (r.Status >= 500)
            {
                Interlocked.Increment(ref _errors);
                var fallbackDate = latest.Date ?? db.ExecuteScalar<string?>("SELECT MAX(date) FROM navs WHERE isin = @isin", new { isin });
                if (date is null && fallbackDate is not null)
                {
                    Interlocked.Increment(ref _stale);
                    http.Response.Headers["Warning"] = "110 price-feed-adapter \"Response is stale\"";
                    return Results.Ok(Cached(db, isin, fallbackDate, "stale-cache", true, latest.ETag));
                }
                return Problems.Of(http, 503, "PRICE_FEED_UNAVAILABLE", "Proveedor de precios no disponible", $"El proveedor E5 respondió {r.Status}.",
                    "Sin caché previa no hay nada que servir. Pide antes el NAV con el proveedor en marcha para que se guarde.");
            }
            if (r.Status == 404)
                return r.Str("error") switch
                {
                    "NOT_PUBLISHED" => Problems.NotFound(http, "NAV_NOT_PUBLISHED", $"El NAV de {isin} para {date ?? "hoy"} aún no está publicado.", "Se publica a las 18:00 (reloj virtual)."),
                    "NO_TRADING" => Problems.NotFound(http, "NO_TRADING_DAY", $"{date} no es día hábil.", null),
                    _ => Problems.NotFound(http, "INSTRUMENT_NOT_FOUND", $"El proveedor no conoce {isin}.", null),
                };
            if (!r.Ok) { Interlocked.Increment(ref _errors); return Problems.Of(http, 502, "PRICE_FEED_ERROR", "Error del proveedor", $"El proveedor respondió {r.Status}.", null); }
            var j = r.Json;
            var d = j.GetProperty("date").GetString()!;
            var isNew = db.Execute("INSERT OR IGNORE INTO navs VALUES (@isin, @d, @nav, @ccy, @pub)",
                new { isin, d, nav = j.GetProperty("nav").GetString(), ccy = j.GetProperty("currency").GetString(), pub = j.GetProperty("publishedAt").GetString() }) == 1;
            if (isNew) Outbox.Add(db, http, clock, "price.nav.published", isin, new { isin, date = d, nav = j.GetProperty("nav").GetString() });
            if (date is null)
            {
                db.Execute("INSERT INTO latest VALUES (@isin, @etag, @d) ON CONFLICT(isin) DO UPDATE SET etag = excluded.etag, date = excluded.date",
                    new { isin, etag = r.Raw.Length > 0 ? ETagOf(isin, d) : null, d });
            }
            return Results.Ok(Cached(db, isin, d, "feed", false, ETagOf(isin, d)));
        }).WithTags("NAV").WithSummary("NAV de un fondo (fecha opcional) desde el proveedor con caché").Produces<FeedNav>();

        app.MapPost("/api/v1/feed/sync", async (HttpContext http, IClock clock, string? date) =>
        {
            DownstreamReply r;
            try { r = await Downstream.SendUrl(HttpMethod.Get, Url("/feed/v1/nav" + (date is null ? "" : "?date=" + date)), null, Key, TimeSpan.FromSeconds(5)); }
            catch (Exception) { return Problems.Of(http, 503, "PRICE_FEED_UNAVAILABLE", "Proveedor de precios no disponible", "No respondió.", null); }
            if (r.Status == 404) return Problems.NotFound(http, "NAV_NOT_PUBLISHED", "No hay precios publicados para esa fecha.", null);
            if (!r.Ok) return Problems.Of(http, 503, "PRICE_FEED_UNAVAILABLE", "Proveedor de precios no disponible", $"Respondió {r.Status}.", null);
            var d = r.Json.GetProperty("date").GetString()!;
            var pub = r.Json.GetProperty("publishedAt").GetString();
            using var db = ctx.OpenDb();
            var added = 0;
            foreach (var p in r.Json.GetProperty("prices").EnumerateArray())
            {
                var isin = p.GetProperty("isin").GetString()!;
                if (db.Execute("INSERT OR IGNORE INTO navs VALUES (@isin, @d, @nav, @ccy, @pub)", new { isin, d, nav = p.GetProperty("nav").GetString(), ccy = p.GetProperty("currency").GetString(), pub }) == 1)
                {
                    added++;
                    Outbox.Add(db, http, clock, "price.nav.published", isin, new { isin, date = d, nav = p.GetProperty("nav").GetString() });
                }
            }
            return Results.Ok(new { date = d, received = r.Json.GetProperty("prices").GetArrayLength(), added });
        }).WithTags("NAV").WithSummary("Descargar todos los NAV de un día");

        app.MapGet("/api/v1/feed/stats", () => Results.Ok(new FeedStats(_requests, _feedCalls, _notModified, _cacheHits, _stale, _errors)))
            .WithTags("NAV").WithSummary("Contadores: llamadas al proveedor, 304, aciertos de caché, respuestas stale").Produces<FeedStats>();
    }

    private static string ETagOf(string isin, string date) => $"\"{isin}-{date}-v1\"";
}
