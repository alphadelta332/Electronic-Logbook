namespace ElectronicLogbook.Mobile.Tests;

using ElectronicLogbook.Portable;

public sealed class MobileCurrencyPeriodDetailTests
{
    [Theory]
    [InlineData("ILS", "3 × 3D|5 × CDI")]
    [InlineData("VOR", "4 × 2D|5 × CDI")]
    [InlineData("RNP", "4 × 2D|5 × CDI")]
    [InlineData("DGA (CDI)", "4 × 2D|5 × CDI")]
    [InlineData("NDB", "4 × 2D|6 × azimuth")]
    [InlineData("DGA (Azi)", "4 × 2D|6 × azimuth")]
    public void Items_ApproachRowsShowTheirRelevantRegulatoryGroups(
        string requirement,
        string expected)
    {
        var row = Row("Approaches", requirement, 0);
        var totals = new MobileCurrencyApproachPeriodTotals(3, 4, 5, 6);

        Assert.Equal(expected, string.Join('|', MobileCurrencyPeriodDetail.Items(row, totals)));
    }

    [Fact]
    public void Items_RnpRowUsesApproachesAcrossTheRegulatoryGroupsRatherThanRawRnpTotal()
    {
        var row = Row("Approaches", "RNP", 0);
        var totals = new MobileCurrencyApproachPeriodTotals(1, 1, 1, 1);

        Assert.Equal(["1 × 2D", "1 × CDI"], MobileCurrencyPeriodDetail.Items(row, totals));
    }

    [Fact]
    public void Items_ApproachRowWithNoQualifyingActivityUsesAClearZeroState()
    {
        var row = Row("Approaches", "ILS", 0);

        Assert.Equal(
            ["No qualifying approaches in period"],
            MobileCurrencyPeriodDetail.Items(row, MobileCurrencyApproachPeriodTotals.Empty));
    }

    [Fact]
    public void Items_NonApproachRowKeepsTheExistingPeriodWording()
    {
        var row = Row("Passenger Carrying", "Day", 3);

        Assert.Equal(
            ["3 in period"],
            MobileCurrencyPeriodDetail.Items(row, MobileCurrencyApproachPeriodTotals.Empty));
    }

    [Fact]
    public void Items_CirclingUsesItsOwnQualifyingTotal()
    {
        var row = Row("Approaches", "Circling", 1);

        Assert.Equal(
            ["1 × circling"],
            MobileCurrencyPeriodDetail.Items(row, MobileCurrencyApproachPeriodTotals.Empty));
    }

    private static PortableLogbookCurrencyRow Row(string category, string requirement, int count) =>
        new(category, string.Empty, requirement, count, "Current", 60, null, null);
}
