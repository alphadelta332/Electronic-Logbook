namespace ElectronicLogbook.Updater;

internal static class AirportStatsRows
{
    public static bool IsSimOnly(
        int row,
        object?[] ifrSim,
        IReadOnlyCollection<object?[]> hourColumns)
    {
        var simHours = WorkbookValue.ToDouble(ifrSim[row]);
        var otherHours = hourColumns.Sum(column => WorkbookValue.ToDouble(column[row]));
        return simHours > 0 && otherHours == 0;
    }
}
