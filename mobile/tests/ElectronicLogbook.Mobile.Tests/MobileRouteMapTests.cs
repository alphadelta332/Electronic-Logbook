using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileRouteMapTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly MobileAirport A = new("AAAA", "Alpha", "AAA", "AA", -33, 151);
    private static readonly MobileAirport B = new("BBBB", "Bravo", "BBB", "BB", -36, 178);
    private static readonly MobileAirport C = new("CCCC", "Charlie", "CCC", "CC", -17, -179);
    private static readonly MobileAirportCatalog Catalog = MobileAirportCatalog.Create([A, B, C]);

    [Fact]
    public void AliasesAndViaOrderProduceOneConnectionPerPairAndDistinctFlightIds()
    {
        var circular = Flight() with { Via = " bb,CC / AAA BBB", To = " aa " };
        var first = Materialized(circular);
        var reverse = Materialized(Flight() with { From = "BBB", To = "AAA" });
        var map = Create([first, reverse]);

        Assert.Equal(new[] { A, B, C, A, B, A }, map.Flights[0].Airports);
        Assert.All(map.Flights, flight => Assert.False(flight.IsIncomplete));
        Assert.Equal(3, map.Connections.Count);
        var repeated = Assert.Single(map.Connections, line => line.From == A && line.To == B);
        Assert.Equal(new[] { first.EntryId, reverse.EntryId }, repeated.FlightIds);
        Assert.Equal(2, map.Markers.Single(marker => marker.Airport == A).FlightIds.Count);
        Assert.Single(map.Markers.Single(marker => marker.Airport == C).FlightIds);
    }

    [Fact]
    public void UnknownStopsAreReportedAndSkippedWithoutChangingChartsDistanceRules()
    {
        var item = Materialized(Flight() with { Via = "UNKNOWN BBB", To = "CCC" });
        var map = Create([item]);

        var route = Assert.Single(map.Flights);
        Assert.True(route.IsIncomplete);
        Assert.Equal(new[] { "UNKNOWN" }, route.UnresolvedLocations);
        Assert.Equal(new[] { A, B, C }, route.Airports);
        Assert.Equal(new[] { (A, B), (B, C) }, map.Connections.Select(line => (line.From, line.To)));
        var charts = MobileLogbookAnalytics.Create([item], new(), Today, [], Catalog);
        Assert.Equal("BBBB", charts.Statistics.LongestLeg!.From.Icao);
        Assert.Equal("CCCC", charts.Statistics.LongestLeg.To.Icao);
        Assert.Equal(3, charts.Statistics.AirportsVisited);
    }

    [Fact]
    public void SameAirportAndIsolatedKnownStopsRetainMarkerSelectionWithoutSelfLines()
    {
        var local = Materialized(Flight() with { From = "AA", Via = "AAA", To = "AAAA" });
        var isolated = Materialized(Flight() with { From = "UNKNOWN", Via = "BBB", To = "OTHER" });
        var map = Create([local, isolated]);

        Assert.Empty(map.Connections);
        Assert.Equal(local.EntryId, Assert.Single(map.Markers.Single(marker => marker.Airport == A).FlightIds));
        Assert.Equal(isolated.EntryId, Assert.Single(map.Markers.Single(marker => marker.Airport == B).FlightIds));
        Assert.True(map.Flights[1].IsIncomplete);
        Assert.Equal(new[] { "UNKNOWN", "OTHER" }, map.Flights[1].UnresolvedLocations);
    }

    [Fact]
    public void EmptyAndWhollyUnresolvedRoutesRemainAccountedForWithoutUsingRemarks()
    {
        var empty = Materialized(Flight() with { From = null, To = " ", Remarks = "AAA BBB CCC" });
        var unknown = Materialized(Flight() with { From = "UNKNOWN", To = "OTHER" });
        var map = Create([empty, unknown]);

        Assert.Equal(2, map.Flights.Count);
        Assert.All(map.Flights, flight => { Assert.True(flight.IsIncomplete); Assert.Empty(flight.Airports); });
        Assert.True(map.Flights[0].HasMissingEndpoint);
        Assert.False(map.Flights[1].HasMissingEndpoint);
        Assert.Empty(map.Markers);
        Assert.Empty(map.Connections);
        Assert.Empty(Create([]).Flights);
    }

    [Fact]
    public void FiltersMatchChartsIncludingDatesAircraftDeletedAndNonFlightEntries()
    {
        var start = Today.AddDays(-2);
        var entries = new[]
        {
            Materialized(Flight() with { Day = start.Day, Type = " TYPE ", Reg = "reg" }),
            Materialized(Flight()),
            Materialized(Flight() with { Day = start.AddDays(-1).Day }),
            Materialized(Flight() with { Day = Today.AddDays(1).Day }),
            Materialized(Flight() with { Type = "OTHER" }),
            Materialized(Flight() with { Reg = "OTHER" }),
            Materialized(Flight() with { Day = null }),
            Materialized(Flight() with { Month = 2, Day = 30 }),
            Materialized(Flight() with { SeCommandDay = 0, IfrSim = 4 }),
            Materialized(Flight()) with { IsDeleted = true },
            Materialized(Flight()) with { Entry = null }
        };
        foreach (var filters in new MobileAnalyticsFilters[]
                 { new(), new(start, Today, ["type"], [" REG "]), new(Today, Today),
                     new(AircraftTypes: ["OTHER", "type"]), new(Registrations: ["OTHER"]), new(AircraftTypes: ["absent"]) })
        {
            var map = Create(entries, filters);
            var charts = MobileLogbookAnalytics.Create(entries, filters, Today, [], Catalog);
            Assert.Equal(charts.FlightCount, map.Flights.Count);
        }
        var selected = Create(entries, new(start, Today, ["type"], [" REG "]));
        Assert.Equal(entries.Take(2).Select(item => item.EntryId), selected.Flights.Select(flight => flight.EntryId));
        Assert.Equal(2, Assert.Single(selected.Connections).FlightIds.Count);
    }

    [Fact]
    public void RebuildReflectsFlightEditsDeletionAndAirportCoordinateUpdates()
    {
        var item = Materialized(Flight());
        Assert.Equal(B, Assert.Single(Create([item]).Connections).To);
        var changed = item with { Entry = item.Entry! with { To = "CCC" } };
        Assert.Equal(C, Assert.Single(Create([changed]).Connections).To);
        Assert.Empty(Create([changed with { IsDeleted = true }]).Markers);

        var catalog = MobileAirportCatalog.Create([A, B, C]);
        var relocated = C with { Longitude = -178 };
        catalog.Replace([A, B, relocated]);
        var map = MobileRouteMap.Create([changed], new(), Today, catalog);
        Assert.Equal(relocated, Assert.Single(map.Connections).To);
        Assert.Equal(-178, map.Flights[0].Airports[1].Longitude);
    }

    [Fact]
    public void DateLineCoordinatesAndTenThousandMatchingFlightsArePreserved()
    {
        var entries = Enumerable.Range(0, 10_000).Select(_ => Materialized(
            Flight() with { From = "BBB", To = "CCC", Via = "CCC BBB" })).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var map = Create(entries);
        watch.Stop();

        var line = Assert.Single(map.Connections);
        Assert.Equal(178, line.From.Longitude);
        Assert.Equal(-179, line.To.Longitude);
        Assert.Equal(10_000, line.FlightIds.Count);
        Assert.Equal(10_000, line.FlightIds.Distinct().Count());
        Assert.All(map.Markers, marker => Assert.Equal(10_000, marker.FlightIds.Count));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"10,000-flight map model took {watch.Elapsed.TotalMilliseconds:N0} ms (desktop ceiling: 5 seconds).");
    }

    [Fact]
    public void ReversedDateRangeIsRejectedLikeCharts()
    {
        Assert.Throws<ArgumentException>(() => Create([], new(Today, Today.AddDays(-1))));
    }

    private static PortableLogbookWorkbookEntry Flight() => PortableLogbookWorkbookEntry.Empty with
    {
        Year = Today.Year, Month = Today.Month, Day = Today.Day,
        SeCommandDay = 1, Type = "TYPE", Reg = "REG", From = "AAA", To = "BBB"
    };

    private static PortableLogbookMaterializedEntryV2 Materialized(PortableLogbookWorkbookEntry entry) =>
        new(EntryId.New(), RevisionId.New(), false, entry, []);

    private static MobileRouteMap Create(IEnumerable<PortableLogbookMaterializedEntryV2> entries, MobileAnalyticsFilters? filters = null) =>
        MobileRouteMap.Create(entries, filters ?? new(), Today, Catalog);
}
