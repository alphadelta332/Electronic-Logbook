using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileRouteMapFlight(
    EntryId EntryId, IReadOnlyList<MobileAirport> Airports, IReadOnlyList<string> UnresolvedLocations,
    bool HasMissingEndpoint)
{
    public bool IsIncomplete => HasMissingEndpoint || UnresolvedLocations.Count > 0;
}

public sealed record MobileRouteMapMarker(MobileAirport Airport, IReadOnlyList<EntryId> FlightIds);
public sealed record MobileRouteMapConnection(MobileAirport From, MobileAirport To, IReadOnlyList<EntryId> FlightIds);

public sealed record MobileRouteMap(
    IReadOnlyList<MobileRouteMapFlight> Flights,
    IReadOnlyList<MobileRouteMapMarker> Markers,
    IReadOnlyList<MobileRouteMapConnection> Connections)
{
    public static MobileRouteMap Create(
        IEnumerable<PortableLogbookMaterializedEntryV2> entries,
        MobileAnalyticsFilters filters, DateOnly today, MobileAirportCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(catalog);
        if (filters.From > filters.To)
            throw new ArgumentException("The start date must be on or before the end date.", nameof(filters));

        var flights = new List<MobileRouteMapFlight>();
        var airports = new Dictionary<string, MobileAirport>(StringComparer.OrdinalIgnoreCase);
        var markers = new Dictionary<string, HashSet<EntryId>>(StringComparer.OrdinalIgnoreCase);
        var connections = new Dictionary<(string From, string To), HashSet<EntryId>>();
        foreach (var item in entries)
        {
            if (item.IsDeleted || item.Entry is not { } entry || !filters.MatchesAircraft(entry) ||
                !filters.MatchesDate(entry, today) || MobileLogbookSession.WorkbookFlightTime(entry) <= 0)
                continue;

            var route = new List<MobileAirport>();
            var unresolved = new List<string>();
            foreach (var token in EndpointTokens(entry.From)
                         .Concat(MobileWorkbookEntryValidation.AirportTokens(entry.Via))
                         .Concat(EndpointTokens(entry.To)))
            {
                if (!catalog.TryFind(token, out var airport))
                {
                    unresolved.Add(token);
                    continue;
                }

                var code = airport.Icao.ToUpperInvariant();
                airports.TryAdd(code, airport);
                if (!markers.TryGetValue(code, out var visits)) markers[code] = visits = [];
                visits.Add(item.EntryId);
                if (route.Count > 0)
                {
                    var previous = route[^1].Icao.ToUpperInvariant();
                    if (previous != code)
                    {
                        var key = string.CompareOrdinal(previous, code) < 0 ? (previous, code) : (code, previous);
                        if (!connections.TryGetValue(key, out var matching)) connections[key] = matching = [];
                        matching.Add(item.EntryId);
                    }
                }
                route.Add(airport);
            }
            flights.Add(new(item.EntryId, route.ToArray(), unresolved.ToArray(),
                string.IsNullOrWhiteSpace(entry.From) || string.IsNullOrWhiteSpace(entry.To)));
        }

        return new(flights.ToArray(),
            markers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new MobileRouteMapMarker(airports[pair.Key], pair.Value.ToArray())).ToArray(),
            connections.OrderBy(pair => pair.Key.From, StringComparer.Ordinal).ThenBy(pair => pair.Key.To, StringComparer.Ordinal)
                .Select(pair => new MobileRouteMapConnection(airports[pair.Key.From], airports[pair.Key.To], pair.Value.ToArray())).ToArray());
    }

    private static IEnumerable<string> EndpointTokens(string? code) =>
        string.IsNullOrWhiteSpace(code) ? [] : new[] { code.Trim() };
}
