using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileDashboardFlightActivityTests
{
    [Fact]
    public void HoursWithinDaysUsesInclusiveCalendarWindowsAndExcludesFutureEntries()
    {
        var today = new DateOnly(2026, 8, 1);
        var entries = new[]
        {
            Entry(today, 1m),
            Entry(today.AddDays(-27), 2m),
            Entry(today.AddDays(-28), 4m),
            Entry(today.AddDays(-364), 8m),
            Entry(today.AddDays(-365), 16m),
            Entry(today.AddDays(1), 32m)
        };

        Assert.Equal(3m, MobileDashboardFlightActivity.HoursWithinDays(entries, today, 28));
        Assert.Equal(15m, MobileDashboardFlightActivity.HoursWithinDays(entries, today, 365));
    }

    [Fact]
    public void HoursWithinDaysRejectsNonPositiveWindows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MobileDashboardFlightActivity.HoursWithinDays([], new DateOnly(2026, 8, 1), 0));
    }

    private static PortableLogbookWorkbookEntry Entry(DateOnly date, decimal hours) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            SeCommandDay = hours
        };
}
