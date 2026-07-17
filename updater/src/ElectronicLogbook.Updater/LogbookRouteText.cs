namespace ElectronicLogbook.Updater;

internal static class LogbookRouteText
{
    public static string BuildAirportStatsSource(
        string from,
        string via,
        string to,
        string remarks)
    {
        var routeText = JoinNonBlank(" ", from, via, to);
        return string.IsNullOrWhiteSpace(routeText) ? remarks : routeText;
    }

    private static string JoinNonBlank(string separator, params string[] values)
    {
        return string.Join(
            separator,
            values.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
