using Nordvik.Services;
using Xunit;

namespace Nordvik.UnitTests;

public class PensionMathTests
{
    [Theory]
    [InlineData("100000", "102000", "2.00", true)]
    [InlineData("100000", "97999", "-2.00", true)] // -2,001 % redondea a -2,00: dentro
    [InlineData("100000", "97990", "-2.01", false)]
    [InlineData("50000", "52500", "5.00", false)]
    [InlineData("0", "0", "0", true)]
    public void Transfer_tolerance_is_two_percent_either_way(string valued, string actual, string dev, bool within)
    {
        var (d, w) = TransferValuation.Compare(decimal.Parse(valued), decimal.Parse(actual));
        Assert.Equal(decimal.Parse(dev, System.Globalization.CultureInfo.InvariantCulture), d);
        Assert.Equal(within, w);
    }

    [Theory]
    [InlineData("VALUATION_PENDING", "VALUED", true)]
    [InlineData("VALUATION_PENDING", "AWAITING_APPROVAL", false)]
    [InlineData("AWAITING_APPROVAL", "MANUAL_REVIEW", true)]
    [InlineData("MANUAL_REVIEW", "CANCELLED", false)]
    [InlineData("COMPLETED", "CANCELLED", false)]
    public void Transfer_state_machine(string from, string to, bool ok) => Assert.Equal(ok, TransferService.CanMove(from, to));

    [Theory]
    [InlineData("10000.00", "3000.00", "7000.00")]
    [InlineData("12345.67", "3703.70", "8641.97")]
    [InlineData("0.05", "0.02", "0.03")] // 0,015 → 0,02 (redondeo bancario al par)
    public void Provisional_withholding_is_30_percent_rounded_to_even(string gross, string tax, string net)
    {
        var (t, n) = PayoutExecution.Withhold(decimal.Parse(gross, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(tax, t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(net, n.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Death_claim_shares_always_add_up_to_the_value()
    {
        var parts = DeathClaims.Distribute(100000.01m, new[] { 33.33m, 33.33m, 33.34m });
        Assert.Equal(100000.01m, parts.Sum());
        Assert.Equal(3, parts.Count);
        Assert.Empty(DeathClaims.Distribute(500m, Array.Empty<decimal>()));
    }

    [Theory]
    [InlineData("556036-0793", true)]
    [InlineData("556036-0794", false)]
    [InlineData("551036-0793", false)]
    [InlineData("5560360793", false)]
    public void Org_numbers_need_format_legal_person_digit_and_luhn(string org, bool ok) => Assert.Equal(ok, EmployerPortalApi.OrgNumberValid(org));

    [Fact]
    public void Contributions_merge_lines_below_the_minimum_into_the_biggest()
    {
        var lines = ContributionService.Split(1000m, new[] { ("A", 92m), ("B", 5m), ("C", 3m) });
        Assert.Single(lines);
        Assert.Equal(("A", 1000m), lines[0]);
        var even = ContributionService.Split(1000.01m, new[] { ("A", 50m), ("B", 50m) });
        Assert.Equal(1000.01m, even.Sum(l => l.Amount));
    }

    [Theory]
    [InlineData(0, 0.5, 100)]
    [InlineData(3, 0.9, 0)]
    [InlineData(3, 1.5, 33.33)]
    [InlineData(3, 7, 100)]
    public void Vesting_is_proportional_per_full_year(int years, double service, double pct) =>
        Assert.Equal((decimal)pct, Math.Round(VestingService.VestedPct(years, service), 2));

    [Fact]
    public void Annuities_life_expectancy_falls_with_age_and_women_live_longer()
    {
        Assert.True(AnnuityCalculator.LifeExpectancy(65, "M") > AnnuityCalculator.LifeExpectancy(75, "M"));
        Assert.True(AnnuityCalculator.LifeExpectancy(65, "F") > AnnuityCalculator.LifeExpectancy(65, "M"));
        var term = AnnuityCalculator.Factor(65, "M", "TERM", 10, 0, 0);
        Assert.Equal(120, term, 6); // con interés 0 y plazo fijo, el factor es el número de pagos
    }
}
