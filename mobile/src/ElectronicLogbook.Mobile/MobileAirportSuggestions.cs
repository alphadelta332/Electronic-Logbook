using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileAirportSuggestions
{
    private static readonly char[] RouteSeparators = [' ', '-', ',', '/', ';'];

    public static IReadOnlyList<string> Create(
        IEnumerable<PortableLogbookMaterializedEntry> currentEntries,
        int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(currentEntries);

        return MobileRecentValues.CreateMany(currentEntries, ValuesForEntry, limit);
    }

    public static IReadOnlyList<string> Create(
        IEnumerable<PortableLogbookMaterializedEntryV2> currentEntries,
        int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(currentEntries);

        return MobileRecentValues.CreateMany(currentEntries, ValuesForEntry, limit);
    }

    private static IEnumerable<string?> ValuesForEntry(PortableLogbookEntry entry)
    {
        yield return AirportSuggestion(entry.From);
        yield return AirportSuggestion(entry.To);

        if (string.IsNullOrWhiteSpace(entry.Route))
        {
            yield break;
        }

        foreach (var token in entry.Route.Split(RouteSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsAirportLikeToken(token))
            {
                yield return token.ToUpperInvariant();
            }
        }
    }

    private static IEnumerable<string?> ValuesForEntry(PortableLogbookWorkbookEntry entry)
    {
        yield return AirportSuggestion(entry.From);
        yield return AirportSuggestion(entry.To);

        if (string.IsNullOrWhiteSpace(entry.Via))
        {
            yield break;
        }

        foreach (var token in entry.Via.Split(RouteSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsAirportLikeToken(token))
            {
                yield return token.ToUpperInvariant();
            }
        }
    }

    private static string? AirportSuggestion(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();

    private static bool IsAirportLikeToken(string value) =>
        value.Length is >= 2 and <= 4 &&
        value.All(char.IsLetterOrDigit) &&
        value.Any(char.IsLetter);
}
