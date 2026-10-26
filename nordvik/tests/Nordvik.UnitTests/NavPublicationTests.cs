using Nordvik.Services;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.UnitTests;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset local) => LocalNow = local;
    public DateTimeOffset UtcNow => LocalNow.ToUniversalTime();
    public DateTimeOffset LocalNow { get; }
    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);
}

public class NavPublicationTests
{
    private static DateOnly At(int y, int m, int d, int h, int min, int offset = 1) =>
        MarketData.LastPublishedDate(new FakeClock(new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.FromHours(offset))));

    [Fact]
    public void Before_18h_on_a_trading_day_the_last_nav_is_the_previous_trading_day() =>
        Assert.Equal(new DateOnly(2026, 10, 23), At(2026, 10, 26, 17, 59));

    [Fact]
    public void From_18h_the_nav_of_today_is_published() =>
        Assert.Equal(new DateOnly(2026, 10, 26), At(2026, 10, 26, 18, 0));

    [Fact]
    public void On_a_sunday_the_last_nav_is_friday() =>
        Assert.Equal(new DateOnly(2026, 10, 23), At(2026, 10, 25, 20, 0));

    [Fact]
    public void After_christmas_the_last_nav_is_the_23rd() =>
        Assert.Equal(new DateOnly(2026, 12, 23), At(2026, 12, 26, 12, 0));

    [Fact]
    public void Summer_time_offset_is_respected() =>
        Assert.Equal(new DateOnly(2026, 7, 1), At(2026, 7, 1, 18, 30, offset: 2));
}
