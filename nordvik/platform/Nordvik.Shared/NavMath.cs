using System.Collections.Concurrent;

namespace Nordvik.Shared;

/// <summary>
/// La serie de NAV ficticia de cada fondo (misma fórmula que market-data) y la regla de publicación de las 18:00.
/// La usan los sistemas externos simulados (custodio y proveedor de precios) para que sus precios cuadren con los nuestros.
/// </summary>
public static class NavMath
{
    public static readonly DateOnly First = new(2025, 1, 2);
    public static readonly DateOnly Last = new(2026, 12, 31);
    private static readonly ConcurrentDictionary<string, SortedDictionary<DateOnly, string>> Cache = new();

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }
    }

    /// <summary>NAV con 4 decimales por día hábil de XSTO.</summary>
    public static SortedDictionary<DateOnly, string> Series(string isin) => Cache.GetOrAdd(isin, i =>
    {
        var f = FundSeed.All.First(x => x.Isin == i);
        var rnd = new Random(StableHash(f.Isin));
        var nav = f.StartNav;
        var s = new SortedDictionary<DateOnly, string>();
        for (var d = First; d <= Last; d = d.AddDays(1))
        {
            if (!NordicCalendar.IsTradingDay("XSTO", d)) continue;
            var shock = (rnd.NextDouble() * 2 - 1) * f.Volatility + f.Drift;
            nav = Math.Max(0.0001m, nav * (1 + (decimal)shock));
            s[d] = Money.Format(nav, 4);
        }
        return s;
    });

    public static bool Exists(string isin) => FundSeed.All.Any(f => f.Isin == isin);

    /// <summary>Último día con NAV publicado según el reloj virtual (se publica a las 18:00 de cada día hábil).</summary>
    public static DateOnly LastPublishedDate(DateTimeOffset localNow)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var candidate = NordicCalendar.IsTradingDay("XSTO", today) && localNow.TimeOfDay >= new TimeSpan(18, 0, 0) ? today : today.AddDays(-1);
        return NordicCalendar.PreviousOrSameTradingDay("XSTO", candidate);
    }

    /// <summary>NAV de una fecha si ya está publicado, o null.</summary>
    public static string? NavOn(string isin, DateOnly date, DateTimeOffset localNow) =>
        date <= LastPublishedDate(localNow) && Series(isin).TryGetValue(date, out var nav) ? nav : null;

    public static string PublishedAt(DateOnly d)
    {
        var local = new DateTime(d.Year, d.Month, d.Day, 18, 0, 0);
        return Nordic.Iso(new DateTimeOffset(local, Nordic.Stockholm.GetUtcOffset(local)));
    }
}
