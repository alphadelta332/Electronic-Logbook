using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileAirportSuggestions
{
    private static readonly char[] RouteSeparators = [' ', '-', ',', '/', ';'];

    public static IReadOnlyList<string> Ranked(
        IEnumerable<PortableLogbookMaterializedEntryV2> currentEntries,
        IEnumerable<string> bases,
        DateOnly today,
        MobileAirportCatalog catalog)
    {
        var flights = currentEntries.Where(item => !item.IsDeleted && item.Entry?.Date is { } date && date <= today)
            .Select(item => item.Entry!)
            .Where(entry => MobileLogbookSession.WorkbookFlightTime(entry) > 0)
            .OrderByDescending(entry => entry.Date).ToArray();
        string Canonical(string code) => catalog.TryFind(code, out var airport) ? airport.Icao : code.Trim().ToUpperInvariant();
        var visits = flights.SelectMany(entry => new[] { entry.From, entry.To }
                .Concat(MobileWorkbookEntryValidation.AirportTokens(entry.Via))
                .Where(code => code is not null && catalog.TryFind(code, out _))
                .Select(code => Canonical(code!)).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key);
        return new[] { flights.FirstOrDefault()?.To }
            .Concat(bases).Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => Canonical(code!)).Concat(visits)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

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
