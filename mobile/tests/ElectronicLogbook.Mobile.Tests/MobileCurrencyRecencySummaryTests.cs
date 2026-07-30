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

    [Fact]
    public void DashboardPanelsUseSingleEngineVfrAndIfrThresholds()
    {
        var rows = new[]
        {
            Row("License", "Flight Review", 95),
            Row("License", "IPC", 95),
            Row("Passenger Carrying", "Day", 31),
            Row("Operation", "IFR (Apps)", 30),
            Row("Operation", "Single Pilot IFR", 95)
        };

        var vfr = MobileDashboardCurrencyPanel.CreateVfr(rows);
        var ifr = MobileDashboardCurrencyPanel.CreateIfr(rows);

        Assert.Equal("Current", vfr.StatusLabel);
        Assert.Equal("current", vfr.StatusTone);
        Assert.Equal("Action soon", ifr.StatusLabel);
        Assert.Equal("warning", ifr.StatusTone);
        Assert.Equal("IFR (Apps)", ifr.Rules[0].Label);
        Assert.Contains("needs attention", ifr.ActionSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardPanelsPutExpiredRequiredItemsFirst()
    {
        var rows = new[]
        {
            Row("License", "Flight Review", 120),
            Row("License", "IPC", 0, "Not Current"),
            Row("Passenger Carrying", "Day", 90),
            Row("Operation", "IFR (Apps)", 45),
            Row("Operation", "Single Pilot IFR", 110)
        };

        var panel = MobileDashboardCurrencyPanel.CreateIfr(rows);

        Assert.Equal("Not current", panel.StatusLabel);
        Assert.Equal("expired", panel.StatusTone);
        Assert.Equal("IPC", panel.Rules[0].Label);
        Assert.Contains("IPC is not current", panel.ActionSentence, StringComparison.Ordinal);
    }

    private static PortableLogbookCurrencyRow Row(
        string category,
        string requirement,
        int daysRemaining,
        string status = "Current") => new(
        category,
        "61.000",
        requirement,
        1,
        status,
        daysRemaining,
        new DateOnly(2026, 7, 27).AddDays(daysRemaining),
        new DateOnly(2026, 7, 27).AddDays(daysRemaining));
}
