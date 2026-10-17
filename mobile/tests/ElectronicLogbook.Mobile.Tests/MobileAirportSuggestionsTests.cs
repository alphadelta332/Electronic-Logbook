using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileAirportSuggestionsTests
{
    private static readonly MobileAirportCatalog Catalog = MobileAirportCatalog.Create([
        new("AAAA", "Alpha Airport", "AAA", null, 0, 0),
        new("BBBB", "Bravo Airport", null, null, 0, 0),
        new("CCCC", "Charlie Airport", null, null, 0, 0),
        new("DDDD", "Delta Airport", null, null, 0, 0),
        new("EEEE", "Echo Airport", null, null, 0, 0)]);

    [Fact]
    public void RankedPutsLastDestinationThenBasesThenVisitsAndDeduplicatesAliasesPerFlight()
    {
        var entries = new[] {
            Flight(1, "CCCC", "CCCC", "DDDD CCCC"),
            Flight(2, "CCCC", "AAAA", "CCCC AAA"),
            Flight(3, "AAAA", "BBBB", "AAAA AAA AAA") };
        Assert.Equal(new[] { "BBBB", "AAAA", "EEEE", "CCCC", "DDDD" },
            MobileAirportSuggestions.Ranked(entries, ["aaa", "EEEE", "BBBB"], new(2026, 10, 17), Catalog));
    }

    [Fact]
    public void RankedExcludesDeletedFutureUndatedAndSimulatorOnlyEntries()
    {
        var entries = new[] {
            Flight(1, "AAAA", "BBBB", ""),
            Flight(2, "CCCC", "DDDD", "") with { IsDeleted = true },
            Flight(18, "EEEE", "DDDD", ""),
            Flight(3, "CCCC", "DDDD", "") with { Entry = PortableLogbookWorkbookEntry.Empty with { From = "CCCC", To = "DDDD", IfrSim = 1 } } };
        Assert.Equal(new[] { "BBBB", "AAAA" },
            MobileAirportSuggestions.Ranked(entries, [], new(2026, 10, 17), Catalog));
        Assert.Equal(new[] { "EEEE" }, MobileAirportSuggestions.Ranked([], ["EEEE"], new(2026, 10, 17), Catalog));
    }

    [Fact]
    public void RankedKeepsAnUnrecognisedLastDestinationAndNeverReadsRemarksAsAirports()
    {
        var last = Flight(1, "AAAA", "PRIVATE FARM", "") with {
            Entry = Flight(1, "AAAA", "PRIVATE FARM", "").Entry! with { Remarks = "CCCC DDDD" } };
        Assert.Equal(new[] { "PRIVATE FARM", "AAAA" },
            MobileAirportSuggestions.Ranked([last], [], new(2026, 10, 17), Catalog));
    }

    [Fact]
    public void CatalogueSearchMatchesPrefixesAndNamesCaseInsensitivelyAndReturnsEachIcaoOnce()
    {
        Assert.Equal(new[] { "AAAA" }, Catalog.Search(" aa ", 20).Select(airport => airport.Icao));
        Assert.Equal(new[] { "CCCC" }, Catalog.Search("harlie", 20).Select(airport => airport.Icao));
        Assert.Equal(new[] { "AAAA", "BBBB" }, Catalog.Search("airport", 2).Select(airport => airport.Icao));
        Assert.Empty(Catalog.Search("unrecognised", 20));
        Assert.Empty(Catalog.Search("", 20));
        Assert.Empty(Catalog.Search("airport", 0));
    }

    private static PortableLogbookMaterializedEntryV2 Flight(int day, string from, string to, string via) =>
        new(EntryId.New(), RevisionId.New(), false,
            PortableLogbookWorkbookEntry.Empty with { Year = 2026, Month = 10, Day = day, From = from, To = to, Via = via, SeCommandDay = 1 }, []);

    [Fact]
    public void CreateIncludesEndpointsAndAirportLikeRouteTokensInRecentOrder()
    {
        var entries = new[]
        {
            Current("ent_old", Entry(new DateOnly(2026, 7, 18), "YMML", "YSSY", "CN/SYD 12 B2 WAYPOINTTOOLONG")),
            Current("ent_latest", Entry(new DateOnly(2026, 7, 20), " ysbk ", "yscn", "ysbk YWOL-WOL")),
            Current("ent_mid", Entry(new DateOnly(2026, 7, 19), "YSCN", "YSHW", "ysbk"))
        };

        var suggestions = MobileAirportSuggestions.Create(entries);

        Assert.Equal(["YSBK", "YSCN", "YWOL", "WOL", "YSHW", "YMML", "YSSY", "CN", "SYD", "B2"], suggestions);
    }

    [Fact]
    public void CreateIgnoresDeletedEntriesAndHonoursLimit()
    {
        var entries = new[]
        {
            Current("ent_deleted", Entry("YDEL", "YXXX", "YDEL")) with { IsDeleted = true },
            Current("ent_1", Entry("YSBK", "YSCN", "YSSY YMML")),
            Current("ent_2", Entry("YWOL", "YSHW", "YGLB"))
        };

        var suggestions = MobileAirportSuggestions.Create(entries, limit: 4);

        Assert.Equal(["YSBK", "YSCN", "YSSY", "YMML"], suggestions);
    }

    private static PortableLogbookMaterializedEntry Current(string entryId, PortableLogbookEntry entry) =>
        new(
            new EntryId(entryId),
            new RevisionId($"rev_{entryId}"),
            IsDeleted: false,
            entry,
            [new RevisionId($"rev_{entryId}")]);

    private static PortableLogbookEntry Entry(string from, string to, string route) =>
        Entry(new DateOnly(2026, 7, 19), from, to, route);

    private static PortableLogbookEntry Entry(DateOnly date, string from, string to, string route) =>
        PortableLogbookEntry.Empty with
        {
            Date = date,
            AircraftType = "C172",
            Registration = "VH-ABC",
            From = from,
            To = to,
            Route = route,
            PilotInCommand = 1.0m
        };
}
