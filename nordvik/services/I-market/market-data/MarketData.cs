using Dapper;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record NavPrice(string Isin, string Date, string Nav, string Currency, string PublishedAt);
public sealed record NavPoint(string Date, string Nav);
public sealed record NavHistory(string Isin, string Currency, string From, string To, int Count, IReadOnlyList<NavPoint> Prices);

internal sealed record NavRow(string Isin, string Date, string Nav);

/// <summary>
/// market-data: valor liquidativo (NAV) diario de cada fondo. Se publica a las 18:00 hora de Estocolmo,
/// solo en días hábiles de XSTO. Antes de las 18:00 el último precio es el del día hábil anterior.
/// </summary>
public sealed class MarketData : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "market-data", "I-market", 6802, 0, "Inversiones",
        "Precios NAV diarios con 4 decimales, historial y último precio publicado (con ETag).",
        Naming: JsonNaming.SnakeCase, DependsOn: new[] { "clock-service" });

    private static readonly Regex IsinFormat = new("^[A-Z]{2}[A-Z0-9]{9}[0-9]$");
    public static readonly DateOnly First = new(2025, 1, 2);
    public static readonly DateOnly Last = new(2026, 12, 31);
    private static readonly TimeSpan PublishTime = new(18, 0, 0);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS navs (isin TEXT NOT NULL, date TEXT NOT NULL, nav TEXT NOT NULL, PRIMARY KEY (isin, date))");
        if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM navs") > 0) return;
        using var tx = db.BeginTransaction();
        foreach (var f in FundSeed.All)
        {
            var rnd = new Random(StableHash(f.Isin));
            var nav = f.StartNav;
            for (var d = First; d <= Last; d = d.AddDays(1))
            {
                if (!NordicCalendar.IsTradingDay("XSTO", d)) continue;
                var shock = (rnd.NextDouble() * 2 - 1) * f.Volatility + f.Drift;
                nav = Math.Max(0.0001m, nav * (1 + (decimal)shock));
                db.Execute("INSERT INTO navs (isin, date, nav) VALUES (@isin, @date, @nav)",
                    new { isin = f.Isin, date = Money.Date(d), nav = Money.Format(nav, 4) }, tx);
            }
        }
        tx.Commit();
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }
    }

    /// <summary>Último día cuyo NAV ya está publicado según el reloj virtual.</summary>
    public static DateOnly LastPublishedDate(IClock clock)
    {
        var local = clock.LocalNow;
        var today = DateOnly.FromDateTime(local.DateTime);
        var candidate = NordicCalendar.IsTradingDay("XSTO", today) && local.TimeOfDay >= PublishTime ? today : today.AddDays(-1);
        return NordicCalendar.PreviousOrSameTradingDay("XSTO", candidate);
    }

    private static string PublishedAt(DateOnly d)
    {
        var local = new DateTime(d.Year, d.Month, d.Day, 18, 0, 0);
        var offset = Nordic.Stockholm.GetUtcOffset(local);
        return Nordic.Iso(new DateTimeOffset(local, offset));
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/prices").WithTags("Precios NAV");

        g.MapGet("/{isin}", (HttpContext http, IClock clock, string isin, string? date) =>
        {
            var fund = CheckIsin(http, isin, out var problem);
            if (fund is null) return problem!;
            var lastPublished = LastPublishedDate(clock);
            DateOnly d;
            if (date is null) d = lastPublished;
            else if (!Money.TryParseDate(date, out d))
                return Problems.BadRequest(http, "INVALID_DATE", $"'{date}' no es una fecha válida.", "Usa el formato AAAA-MM-DD, por ejemplo 2026-10-23.");
            if (d < First || d > Last)
                return Problems.NotFound(http, "DATE_OUT_OF_RANGE", $"Solo hay precios entre {Money.Date(First)} y {Money.Date(Last)}.", "Elige una fecha dentro de ese rango.");
            if (d > lastPublished)
                return Problems.NotFound(http, "PRICE_NOT_YET_PUBLISHED", $"El NAV del {Money.Date(d)} todavía no se ha publicado.",
                    $"Los NAV se publican a las 18:00 (Estocolmo) de cada día hábil. El último publicado es el del {Money.Date(lastPublished)}. Puedes viajar en el tiempo con clock-service.");
            var holiday = NordicCalendar.HolidayOn("XSTO", d);
            if (NordicCalendar.IsWeekend(d) || holiday is not null)
                return Problems.NotFound(http, "NO_PRICE_FOR_DATE", $"El {Money.Date(d)} no es día hábil en XSTO" + (holiday is null ? " (fin de semana)." : $" ({holiday.Name})."),
                    $"No hay NAV en días no hábiles. El día hábil anterior es {Money.Date(NordicCalendar.PreviousOrSameTradingDay("XSTO", d))}; calendar-service te lo dice.");
            using var db = ctx.OpenDb();
            var row = db.QuerySingle<NavRow>("SELECT isin, date, nav FROM navs WHERE isin = @isin AND date = @date", new { isin, date = Money.Date(d) });
            return Results.Ok(new NavPrice(row.Isin, row.Date, row.Nav, fund.Currency, PublishedAt(d)));
        }).WithSummary("NAV de un fondo en una fecha (por defecto, el último publicado)").Produces<NavPrice>();

        g.MapGet("/{isin}/latest", (HttpContext http, IClock clock, string isin) =>
        {
            var fund = CheckIsin(http, isin, out var problem);
            if (fund is null) return problem!;
            var d = LastPublishedDate(clock);
            var etag = $"\"{isin}-{Money.Date(d)}\"";
            // Trampa T7: la caché sigue respondiendo 304 al ETag del día anterior aunque ya haya NAV nuevo
            var stale = $"\"{isin}-{Money.Date(NordicCalendar.AddTradingDays("XSTO", d, -1))}\"";
            if (Chaos.TrapOn("T7") && http.Request.Headers.IfNoneMatch.ToString() == stale) { http.Response.Headers.ETag = stale; return Results.StatusCode(304); }
            if (http.Request.Headers.IfNoneMatch.ToString() == etag)
            {
                http.Response.Headers.ETag = etag;
                return Results.StatusCode(304);
            }
            using var db = ctx.OpenDb();
            var row = db.QuerySingle<NavRow>("SELECT isin, date, nav FROM navs WHERE isin = @isin AND date = @date", new { isin, date = Money.Date(d) });
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "max-age=60";
            return Results.Ok(new NavPrice(row.Isin, row.Date, row.Nav, fund.Currency, PublishedAt(d)));
        }).WithSummary("Último NAV publicado (admite If-None-Match y devuelve 304)").Produces<NavPrice>();

        g.MapGet("/{isin}/history", (HttpContext http, IClock clock, string isin, string? from, string? to) =>
        {
            var fund = CheckIsin(http, isin, out var problem);
            if (fund is null) return problem!;
            var lastPublished = LastPublishedDate(clock);
            if (!Money.TryParseDate(from, out var f) || !Money.TryParseDate(to ?? Money.Date(lastPublished), out var t))
                return Problems.BadRequest(http, "INVALID_DATE", "from (obligatorio) y to deben tener formato AAAA-MM-DD.", "Ejemplo: ?from=2026-01-01&to=2026-03-31");
            if (f > t) return Problems.BadRequest(http, "INVALID_RANGE", "from es posterior a to.", "Intercambia las fechas.");
            if (t.DayNumber - f.DayNumber > 366)
                return Problems.BadRequest(http, "RANGE_TOO_LARGE", "El rango máximo es de 366 días.", "Pide el historial por años y únelo en tu test.");
            if (t > lastPublished) t = lastPublished;
            using var db = ctx.OpenDb();
            var points = db.Query<NavRow>("SELECT isin, date, nav FROM navs WHERE isin = @isin AND date BETWEEN @f AND @t ORDER BY date",
                new { isin, f = Money.Date(f), t = Money.Date(t) }).Select(r => new NavPoint(r.Date, r.Nav)).ToList();
            return Results.Ok(new NavHistory(isin, fund.Currency, Money.Date(f), Money.Date(t), points.Count, points));
        }).WithSummary("Historial de NAV entre dos fechas (máximo 366 días)").Produces<NavHistory>();
    }

    private static FundSeedRow? CheckIsin(HttpContext http, string isin, out IResult? problem)
    {
        problem = null;
        if (!IsinFormat.IsMatch(isin))
        {
            problem = Problems.BadRequest(http, "INVALID_ISIN", $"'{isin}' no tiene formato de ISIN.", "12 caracteres: 2 letras, 9 alfanuméricos y 1 dígito. Ejemplo: SE0000000001");
            return null;
        }
        var fund = FundSeed.All.FirstOrDefault(x => x.Isin == isin);
        if (fund is null)
            problem = Problems.NotFound(http, "INSTRUMENT_NOT_FOUND", $"market-data no tiene precios para {isin}.", "Comprueba en product-catalog que el fondo existe.");
        return fund;
    }
}
