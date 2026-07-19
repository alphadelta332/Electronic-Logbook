using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileAirportSuggestionsTests
{
    [Fact]
    public void CreateIncludesEndpointsAndAirportLikeRouteTokensInRecentOrder()
    {
        var entries = new[]
        {
            Current("ent_1", Entry(" ysbk ", "yscn", "ysbk YWOL-WOL")),
            Current("ent_2", Entry("YMML", "YSSY", "CN/SYD 12 B2 WAYPOINTTOOLONG")),
            Current("ent_3", Entry("YSCN", "YSHW", "ysbk"))
        };

        var suggestions = MobileAirportSuggestions.Create(entries);

        Assert.Equal(["YSBK", "YSCN", "YWOL", "WOL", "YMML", "YSSY", "CN", "SYD", "B2", "YSHW"], suggestions);
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
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 19),
            AircraftType = "C172",
            Registration = "VH-ABC",
            From = from,
            To = to,
            Route = route,
            PilotInCommand = 1.0m
        };
}
