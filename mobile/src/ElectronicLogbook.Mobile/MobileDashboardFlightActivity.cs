using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileDashboardFlightActivity
{
    public static decimal HoursWithinDays(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today,
        int dayCount)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dayCount);

        var cutoff = today.AddDays(1 - dayCount);
        return entries
            .Where(entry => entry.Date is { } date && date >= cutoff && date <= today)
            .Sum(MobileLogbookSession.WorkbookLoggedTime);
    }
}
