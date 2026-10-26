using System.Text.RegularExpressions;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.UnitTests;

public class FundSeedTests
{
    [Fact]
    public void Isins_are_unique_and_well_formed()
    {
        Assert.Equal(FundSeed.All.Count, FundSeed.All.Select(f => f.Isin).Distinct().Count());
        Assert.All(FundSeed.All, f => Assert.Matches(new Regex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$"), f.Isin));
    }

    [Fact]
    public void Sri_is_between_1_and_7_and_equity_is_at_least_4()
    {
        Assert.All(FundSeed.All, f => Assert.InRange(f.Sri, 1, 7));
        Assert.All(FundSeed.All.Where(f => f.Category == "EQUITY"), f => Assert.InRange(f.Sri, 4, 7));
    }

    [Fact]
    public void Fees_are_decimal_strings_with_two_decimals() =>
        Assert.All(FundSeed.All, f =>
        {
            Assert.Matches(@"^\d+\.\d{2}$", f.ManagementFee);
            Assert.Matches(@"^\d+\.\d{2}$", f.EntryFee);
            Assert.Matches(@"^\d+\.\d{2}$", f.ExitFee);
        });

    [Fact]
    public void Categories_are_from_the_allowed_list() =>
        Assert.All(FundSeed.All, f => Assert.Contains(f.Category, FundSeed.Categories));

    [Fact]
    public void Exactly_one_fund_is_closed() =>
        Assert.Single(FundSeed.All, f => f.Status == "CLOSED");
}
