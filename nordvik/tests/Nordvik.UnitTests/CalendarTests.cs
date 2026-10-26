using Nordvik.Shared;
using Xunit;

namespace Nordvik.UnitTests;

public class CalendarTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2030, 4, 21)]
    public void Easter_sunday_is_computed_correctly(int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), NordicCalendar.EasterSunday(y));

    [Fact]
    public void Stockholm_2026_has_12_holidays_including_midsummer_eve() 
    {
        var list = NordicCalendar.Holidays("XSTO", 2026);
        Assert.Equal(12, list.Count);
        Assert.Contains(list, h => h.Date == new DateOnly(2026, 6, 19) && h.Name == "Víspera de San Juan");
        Assert.Contains(list, h => h.Date == new DateOnly(2026, 5, 14) && h.Name == "Ascensión");
    }

    [Fact]
    public void Midsummer_eve_is_always_a_friday_between_19_and_25_june()
    {
        for (var y = NordicCalendar.MinYear; y <= NordicCalendar.MaxYear; y++)
        {
            var h = NordicCalendar.Holidays("XSTO", y).Single(x => x.Name == "Víspera de San Juan");
            Assert.Equal(DayOfWeek.Friday, h.Date.DayOfWeek);
            Assert.InRange(h.Date.Day, 19, 25);
            Assert.Equal(6, h.Date.Month);
        }
    }

    [Theory]
    [InlineData("XOSL", 2026, 5, 17, "Día de la Constitución de Noruega")]
    [InlineData("XCSE", 2026, 6, 5, "Día de la Constitución de Dinamarca")]
    [InlineData("XHEL", 2026, 12, 6, "Día de la Independencia de Finlandia")]
    [InlineData("XOSL", 2026, 5, 25, "Lunes de Pentecostés")]
    public void Each_market_has_its_national_holidays(string market, int y, int m, int d, string name) =>
        Assert.Equal(name, NordicCalendar.HolidayOn(market, new DateOnly(y, m, d))?.Name);

    [Fact]
    public void Swedish_national_day_is_not_a_norwegian_holiday() =>
        Assert.Null(NordicCalendar.HolidayOn("XOSL", new DateOnly(2026, 6, 6)));

    [Theory]
    [InlineData(2026, 10, 24, false)] // sábado
    [InlineData(2026, 10, 25, false)] // domingo
    [InlineData(2026, 10, 26, true)]  // lunes
    [InlineData(2026, 12, 24, false)] // Nochebuena
    [InlineData(2026, 12, 31, false)] // Nochevieja
    [InlineData(2026, 12, 30, true)]
    public void Trading_days_in_stockholm(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, NordicCalendar.IsTradingDay("XSTO", new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2026, 10, 23, 2, 2026, 10, 27)]   // viernes T+2 = martes
    [InlineData(2026, 12, 23, 1, 2026, 12, 28)]   // salta Navidad y fin de semana
    [InlineData(2026, 10, 26, -1, 2026, 10, 23)]  // día hábil anterior
    [InlineData(2026, 10, 24, 0, 2026, 10, 26)]   // offset 0 en sábado = lunes
    [InlineData(2026, 10, 26, 0, 2026, 10, 26)]   // offset 0 en hábil = el mismo
    public void Add_trading_days(int y, int m, int d, int offset, int ey, int em, int ed) =>
        Assert.Equal(new DateOnly(ey, em, ed), NordicCalendar.AddTradingDays("XSTO", new DateOnly(y, m, d), offset));

    [Fact]
    public void Previous_or_same_trading_day_skips_back_over_weekend() =>
        Assert.Equal(new DateOnly(2026, 6, 5), NordicCalendar.PreviousOrSameTradingDay("XSTO", new DateOnly(2026, 6, 7)));

    [Fact]
    public void Unknown_market_throws() =>
        Assert.Throws<ArgumentException>(() => NordicCalendar.Holidays("XNYS", 2026));

    [Fact]
    public void All_markets_have_new_year_and_christmas_every_year()
    {
        foreach (var market in NordicCalendar.Markets.Keys)
            for (var y = NordicCalendar.MinYear; y <= NordicCalendar.MaxYear; y++)
            {
                Assert.NotNull(NordicCalendar.HolidayOn(market, new DateOnly(y, 1, 1)));
                Assert.NotNull(NordicCalendar.HolidayOn(market, new DateOnly(y, 12, 25)));
            }
    }
}
