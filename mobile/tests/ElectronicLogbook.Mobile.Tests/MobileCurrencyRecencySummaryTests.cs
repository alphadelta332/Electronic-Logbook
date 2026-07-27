using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileCurrencyRecencySummaryTests
{
    [Fact]
    public void CreateShowsEveryWorkbookRowAndEveryExpiredSingleEngineItem()
    {
        var today = new DateOnly(2026, 7, 27);

        var summary = MobileCurrencyRecencySummary.Create([], PortableLogbookCurrencyOverrideDates.Empty, today);

        Assert.Equal(14, summary.SingleEngineRows.Count);
        Assert.Equal(2, summary.MultiEngineRows.Count);
        Assert.Equal(summary.SingleEngineRows, summary.CurrentlyExpiredSingleEngineRows);
        Assert.Null(summary.NextExpiringSingleEngineRow);
        Assert.Contains(summary.SingleEngineRows, row => row.Requirement == "Flight Review");
        Assert.Contains(summary.SingleEngineRows, row => row.Requirement == "Circling");
        Assert.All(summary.MultiEngineRows, row => Assert.Equal("Not Current", row.Status));
    }

    [Fact]
    public void CreateShowsEarliestFutureSingleEngineExpiryWithDaysRemaining()
    {
        var today = new DateOnly(2026, 7, 27);
        var overrides = new PortableLogbookCurrencyOverrideDates(
            new DateOnly(2024, 8, 1),
            null,
            null);

        var summary = MobileCurrencyRecencySummary.Create([], overrides, today);

        var nextExpiring = Assert.IsType<PortableLogbookCurrencyRow>(summary.NextExpiringSingleEngineRow);
        Assert.Equal("Flight Review", nextExpiring.Requirement);
        Assert.Equal(new DateOnly(2026, 8, 31), nextExpiring.CurrentOrRecentUntil);
        Assert.Equal(35, nextExpiring.DaysRemaining);
    }
}
