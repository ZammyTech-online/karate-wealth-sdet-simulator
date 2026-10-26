using Nordvik.Services;
using Xunit;

namespace Nordvik.UnitTests;

public class InsuranceTaxMathTests
{
    [Theory]
    [InlineData(40, false, 23, new string[0], false, "STANDARD", 0)]
    [InlineData(40, true, 23, new string[0], false, "RATED", 50)]
    [InlineData(40, false, 31, new string[0], false, "RATED", 25)]
    [InlineData(40, true, 36, new[] { "DIABETES" }, true, "DECLINED", 0)]
    [InlineData(62, false, 23, new string[0], false, "RATED", 25)]
    [InlineData(40, false, 22, new[] { "CANCER_ACTIVE" }, false, "DECLINED", 0)]
    [InlineData(40, false, 22, new[] { "HEART_DISEASE" }, false, "RATED", 50)]
    public void Underwriting_points(int age, bool smoker, int bmi, string[] conditions, bool hosp, string decision, int loading)
    {
        var u = InsurerSim.Underwrite(age, smoker, bmi, conditions, hosp);
        Assert.Equal(decision, u.Decision);
        Assert.Equal(loading, u.LoadingPct);
    }

    [Fact]
    public void Premiums_grow_with_age_capital_and_loading()
    {
        var p = PremiumCalculator.Monthly(40, 1_000_000m, "LIFE", 0, 20);
        Assert.True(PremiumCalculator.Monthly(50, 1_000_000m, "LIFE", 0, 20) > p);
        Assert.InRange(PremiumCalculator.Monthly(40, 2_000_000m, "LIFE", 0, 20) - p * 2, -0.02m, 0.02m);
        Assert.Equal(p, PremiumCalculator.Monthly(40, 1_000_000m, "LIFE", 0, 20));
        Assert.True(PremiumCalculator.Monthly(40, 1_000_000m, "LIFE", 75, 20) > PremiumCalculator.Monthly(40, 1_000_000m, "LIFE", 25, 20));
    }

    [Theory]
    [InlineData("2026-10", "2026-10-01", "2026-10-31")]
    [InlineData("2024-02", "2024-02-01", "2024-02-29")]
    [InlineData("2026-Q1", "2026-01-01", "2026-03-31")]
    [InlineData("2026-Q4", "2026-10-01", "2026-12-31")]
    public void Statement_periods(string text, string from, string to)
    {
        var p = StatementService.Period(text)!.Value;
        Assert.Equal(DateOnly.Parse(from), p.From);
        Assert.Equal(DateOnly.Parse(to), p.To);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-Q5")]
    [InlineData("26-10")]
    [InlineData("")]
    public void Bad_statement_periods(string text) => Assert.Null(StatementService.Period(text));

    [Theory]
    [InlineData(22, "<30")]
    [InlineData(30, "30-39")]
    [InlineData(39, "30-39")]
    [InlineData(69, "60-69")]
    [InlineData(70, "70+")]
    public void Age_bands(int age, string band) => Assert.Equal(band, AnalyticsReporting.AgeBand(age));

    [Fact]
    public void Tax_tables_are_deterministic_and_from_the_municipal_list()
    {
        var a = TaxAgencySim.TableFor("19800101-1234");
        Assert.Equal(a, TaxAgencySim.TableFor("19800101-1234"));
        Assert.Contains(TaxAgencySim.Municipalities, m => m.Municipality == a.Municipality && m.Rate == a.Rate);
        Assert.Equal((int)Math.Round(a.Rate), a.Table);
    }

    [Theory]
    [InlineData("10000.00", "32.60", "3260.00", "6740.00")]
    [InlineData("4372.74", "29.82", "1303.95", "3068.79")]
    public void Withholding_with_a_table_rate(string gross, string rate, string tax, string net)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var (t, n) = PayoutExecution.Withhold(decimal.Parse(gross, inv), decimal.Parse(rate, inv));
        Assert.Equal(tax, t.ToString("F2", inv));
        Assert.Equal(net, n.ToString("F2", inv));
    }
}
