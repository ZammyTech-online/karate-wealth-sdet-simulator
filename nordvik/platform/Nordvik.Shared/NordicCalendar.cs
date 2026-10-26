namespace Nordvik.Shared;

/// <summary>Un festivo de mercado.</summary>
public sealed record Holiday(DateOnly Date, string Name);

/// <summary>
/// Calendario bursátil de los cuatro mercados nórdicos que usa Nordvik.
/// Librería compartida: la usan calendar-service y market-data (como en una empresa real, código duplicado en dos equipos sería peor).
/// </summary>
public static class NordicCalendar
{
    public static readonly IReadOnlyDictionary<string, (string Name, string Country, string Currency)> Markets =
        new Dictionary<string, (string, string, string)>
        {
            ["XSTO"] = ("Nasdaq Stockholm", "SE", "SEK"),
            ["XOSL"] = ("Oslo Børs", "NO", "NOK"),
            ["XCSE"] = ("Nasdaq Copenhagen", "DK", "DKK"),
            ["XHEL"] = ("Nasdaq Helsinki", "FI", "EUR"),
        };

    public const int MinYear = 2020;
    public const int MaxYear = 2035;

    public static DateOnly EasterSunday(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(y, month, day);
    }

    private static DateOnly MidsummerEve(int y)
    {
        var d = new DateOnly(y, 6, 19);
        while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(1);
        return d;
    }

    public static IReadOnlyList<Holiday> Holidays(string market, int year)
    {
        var e = EasterSunday(year);
        var list = new List<Holiday> { new(new DateOnly(year, 1, 1), "Año Nuevo") };
        switch (market)
        {
            case "XSTO":
                list.AddRange(new Holiday[]
                {
                    new(new DateOnly(year, 1, 6), "Epifanía"), new(e.AddDays(-2), "Viernes Santo"), new(e.AddDays(1), "Lunes de Pascua"),
                    new(new DateOnly(year, 5, 1), "Día del Trabajo"), new(e.AddDays(39), "Ascensión"), new(new DateOnly(year, 6, 6), "Día Nacional de Suecia"),
                    new(MidsummerEve(year), "Víspera de San Juan"), new(new DateOnly(year, 12, 24), "Nochebuena"),
                    new(new DateOnly(year, 12, 25), "Navidad"), new(new DateOnly(year, 12, 26), "San Esteban"), new(new DateOnly(year, 12, 31), "Nochevieja"),
                });
                break;
            case "XOSL":
                list.AddRange(new Holiday[]
                {
                    new(e.AddDays(-3), "Jueves Santo"), new(e.AddDays(-2), "Viernes Santo"), new(e.AddDays(1), "Lunes de Pascua"),
                    new(new DateOnly(year, 5, 1), "Día del Trabajo"), new(new DateOnly(year, 5, 17), "Día de la Constitución de Noruega"),
                    new(e.AddDays(39), "Ascensión"), new(e.AddDays(50), "Lunes de Pentecostés"), new(new DateOnly(year, 12, 24), "Nochebuena"),
                    new(new DateOnly(year, 12, 25), "Navidad"), new(new DateOnly(year, 12, 26), "San Esteban"), new(new DateOnly(year, 12, 31), "Nochevieja"),
                });
                break;
            case "XCSE":
                list.AddRange(new Holiday[]
                {
                    new(e.AddDays(-3), "Jueves Santo"), new(e.AddDays(-2), "Viernes Santo"), new(e.AddDays(1), "Lunes de Pascua"),
                    new(e.AddDays(39), "Ascensión"), new(e.AddDays(40), "Día después de la Ascensión"), new(e.AddDays(50), "Lunes de Pentecostés"),
                    new(new DateOnly(year, 6, 5), "Día de la Constitución de Dinamarca"), new(new DateOnly(year, 12, 24), "Nochebuena"),
                    new(new DateOnly(year, 12, 25), "Navidad"), new(new DateOnly(year, 12, 26), "San Esteban"), new(new DateOnly(year, 12, 31), "Nochevieja"),
                });
                break;
            case "XHEL":
                list.AddRange(new Holiday[]
                {
                    new(new DateOnly(year, 1, 6), "Epifanía"), new(e.AddDays(-2), "Viernes Santo"), new(e.AddDays(1), "Lunes de Pascua"),
                    new(new DateOnly(year, 5, 1), "Día del Trabajo"), new(MidsummerEve(year), "Víspera de San Juan"),
                    new(new DateOnly(year, 12, 6), "Día de la Independencia de Finlandia"), new(new DateOnly(year, 12, 24), "Nochebuena"),
                    new(new DateOnly(year, 12, 25), "Navidad"), new(new DateOnly(year, 12, 26), "San Esteban"), new(new DateOnly(year, 12, 31), "Nochevieja"),
                });
                break;
            default:
                throw new ArgumentException("Mercado desconocido: " + market);
        }
        return list.OrderBy(h => h.Date).ToList();
    }

    public static Holiday? HolidayOn(string market, DateOnly date) =>
        Holidays(market, date.Year).FirstOrDefault(h => h.Date == date);

    public static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static bool IsTradingDay(string market, DateOnly d) => !IsWeekend(d) && HolidayOn(market, d) is null;

    /// <summary>Suma N días hábiles (T+N). Con offset 0 devuelve el propio día si es hábil, o el siguiente.</summary>
    public static DateOnly AddTradingDays(string market, DateOnly from, int offset)
    {
        var d = from;
        if (offset == 0)
        {
            while (!IsTradingDay(market, d)) d = d.AddDays(1);
            return d;
        }
        var step = offset > 0 ? 1 : -1;
        var remaining = Math.Abs(offset);
        while (remaining > 0)
        {
            d = d.AddDays(step);
            if (IsTradingDay(market, d)) remaining--;
        }
        return d;
    }

    public static DateOnly PreviousOrSameTradingDay(string market, DateOnly d)
    {
        while (!IsTradingDay(market, d)) d = d.AddDays(-1);
        return d;
    }
}
