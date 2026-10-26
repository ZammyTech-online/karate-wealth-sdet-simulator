using Nordvik.Shared;
using Xunit;

namespace Nordvik.UnitTests;

public class ClockTests
{
    [Fact]
    public void Remote_clock_falls_back_to_real_time_when_clock_service_is_down()
    {
        var clock = new RemoteClock("http://localhost:1", cacheMs: 0);
        var diff = (clock.UtcNow - DateTimeOffset.UtcNow).Duration();
        Assert.True(diff < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Stockholm_is_utc_plus_1_in_winter_and_plus_2_in_summer()
    {
        Assert.Equal(TimeSpan.FromHours(1), Nordic.ToStockholm(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)).Offset);
        Assert.Equal(TimeSpan.FromHours(2), Nordic.ToStockholm(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)).Offset);
    }

    [Fact]
    public void Iso_format_includes_offset() =>
        Assert.Equal("2026-10-26T13:00:00.000+01:00", Nordic.Iso(Nordic.ToStockholm(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero))));
}
