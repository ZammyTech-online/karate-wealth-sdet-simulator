using Nordvik.Shared;
using Xunit;

namespace Nordvik.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("2.345", 2, "2.34")]   // mitad al par: 4 es par
    [InlineData("2.355", 2, "2.36")]   // mitad al par: 6 es par
    [InlineData("2.3451", 2, "2.35")]  // no es mitad exacta
    [InlineData("100", 2, "100.00")]
    [InlineData("0.00005", 4, "0.0000")]
    [InlineData("-1.005", 2, "-1.00")]
    [InlineData("142.38175", 4, "142.3818")]
    public void Format_uses_bankers_rounding(string input, int scale, string expected) =>
        Assert.Equal(expected, Money.Format(decimal.Parse(input, Money.Inv), scale));

    [Theory]
    [InlineData("100.00", true)]
    [InlineData("0.5", true)]
    [InlineData("-3.25", true)]
    [InlineData("100,00", false)]
    [InlineData("1,000.00", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_accepts_only_dot_decimals(string? text, bool ok) =>
        Assert.Equal(ok, Money.TryParse(text, out _));

    [Theory]
    [InlineData("2026-10-26", true)]
    [InlineData("2026-02-29", false)]
    [InlineData("2028-02-29", true)]
    [InlineData("26/10/2026", false)]
    [InlineData("2026-1-5", false)]
    public void TryParseDate_requires_iso_date(string text, bool ok) =>
        Assert.Equal(ok, Money.TryParseDate(text, out _));
}
