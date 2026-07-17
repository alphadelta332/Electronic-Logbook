namespace ElectronicLogbook.Updater;

internal static class AirportStatsText
{
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "IPC", "OPC", "FR", "IR", "IFR", "VFR", "TEST", "CHECK", "CIRCLING", "SIM"
    };

    public static IEnumerable<string> TokeniseDetails(string details)
    {
        var normalised = details.Replace("|", "", StringComparison.Ordinal);
        foreach (var delimiter in new[] { '-', ' ', ',', '(', ')' })
        {
            normalised = normalised.Replace(delimiter, '|');
        }

        return normalised
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool ShouldIgnoreToken(string token, IReadOnlyCollection<string> keywords)
    {
        if (IgnoredTokens.Contains(token))
        {
            return true;
        }

        return keywords.Any(keyword => keyword.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static void AddEndpointAirportMatch(
        ISet<string> matchedIcaos,
        IReadOnlyDictionary<string, string> aliasLookup,
        string rawValue)
    {
        var token = rawValue.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (aliasLookup.TryGetValue(token, out var icao) && !string.IsNullOrWhiteSpace(icao))
        {
            matchedIcaos.Add(icao);
        }
    }
}
