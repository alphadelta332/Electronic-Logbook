using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileEntryDraftDefaults(
    string AircraftType,
    string Registration,
    string From,
    string To,
    string Route)
{
    public static MobileEntryDraftDefaults Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

public static class MobileEntryDraftDefaultPlanner
{
    private static readonly char[] RouteSeparators = [' ', '-', ',', '/', ';'];

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
        var route = RouteStartsAt(latestEntry.Route, nextDeparture) ? latestEntry.Route!.Trim() : string.Empty;
        return new MobileEntryDraftDefaults(
            latestEntry.AircraftType?.Trim() ?? string.Empty,
            latestEntry.Registration?.Trim() ?? string.Empty,
            nextDeparture,
            RouteDestination(route, nextDeparture),
            route);
    }

    private static bool RouteStartsAt(string? route, string departure)
    {
        if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(departure))
        {
            return false;
        }

        var tokens = RouteTokens(route);
        return tokens.Length > 0 && string.Equals(tokens[0], departure.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string RouteDestination(string route, string departure)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        var tokens = RouteTokens(route);
        return tokens.Length > 1 && string.Equals(tokens[0], departure.Trim(), StringComparison.OrdinalIgnoreCase)
            ? tokens[^1]
            : string.Empty;
    }

    private static string[] RouteTokens(string route) =>
        route
            .Split(RouteSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsAirportLikeToken)
            .Select(token => token.ToUpperInvariant())
            .ToArray();

    private static bool IsAirportLikeToken(string value) =>
        value.Length is >= 2 and <= 4 &&
        value.All(char.IsLetterOrDigit) &&
        value.Any(char.IsLetter);
}
