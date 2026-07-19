using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileEntryDraftDefaults(
    string AircraftType,
    string Registration,
    string From,
    string Route)
{
    public static MobileEntryDraftDefaults Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

public static class MobileEntryDraftDefaultPlanner
{
    public static MobileEntryDraftDefaults Create(IEnumerable<PortableLogbookMaterializedEntry> currentEntries)
    {
        ArgumentNullException.ThrowIfNull(currentEntries);

        var latestEntry = currentEntries
            .FirstOrDefault(entry => !entry.IsDeleted && entry.Entry is not null)
            ?.Entry;
        if (latestEntry is null)
        {
            return MobileEntryDraftDefaults.Empty;
        }

        var nextDeparture = FirstNonBlank(latestEntry.To, latestEntry.From);
        return new MobileEntryDraftDefaults(
            latestEntry.AircraftType?.Trim() ?? string.Empty,
            latestEntry.Registration?.Trim() ?? string.Empty,
            nextDeparture,
            RouteStartsAt(latestEntry.Route, nextDeparture) ? latestEntry.Route!.Trim() : string.Empty);
    }

    private static bool RouteStartsAt(string? route, string departure)
    {
        if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(departure))
        {
            return false;
        }

        var trimmedRoute = route.Trim();
        var trimmedDeparture = departure.Trim();
        return trimmedRoute.Equals(trimmedDeparture, StringComparison.OrdinalIgnoreCase)
            || trimmedRoute.StartsWith(trimmedDeparture + " ", StringComparison.OrdinalIgnoreCase)
            || trimmedRoute.StartsWith(trimmedDeparture + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
