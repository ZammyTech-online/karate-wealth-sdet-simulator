using Nordvik.Services;
using Xunit;

namespace Nordvik.UnitTests;

public class UuidV7Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Has_version_7_and_rfc_variant()
    {
        var s = UuidService.NewV7(T0).ToString("D");
        Assert.Equal('7', s[14]);
        Assert.Contains(s[19], "89ab");
    }

    [Fact]
    public void First_48_bits_are_the_unix_milliseconds()
    {
        var s = UuidService.NewV7(T0).ToString("N");
        var ms = Convert.ToInt64(s[..12], 16);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), ms);
    }

    [Fact]
    public void Later_times_sort_after_earlier_times()
    {
        var ids = Enumerable.Range(0, 200).Select(i => UuidService.NewV7(T0.AddMilliseconds(i)).ToString("D")).ToList();
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal), ids);
    }

    [Fact]
    public void Same_millisecond_still_produces_unique_values()
    {
        var ids = Enumerable.Range(0, 5000).Select(_ => UuidService.NewV7(T0)).ToHashSet();
        Assert.Equal(5000, ids.Count);
    }
}
