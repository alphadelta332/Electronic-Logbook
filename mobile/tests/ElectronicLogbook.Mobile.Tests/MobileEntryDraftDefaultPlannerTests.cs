using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileEntryDraftDefaultPlannerTests
{
    [Fact]
    public void CreateDefaultsAircraftRegistrationAndNextDepartureFromLatestCurrentEntry()
    {
        var entries = new[]
        {
            Current("ent_latest", Entry("  C172  ", " VH-ABC ", "YSBK", "YSCN", "YSCN YMML")),
            Current("ent_old", Entry("PA28", "VH-OLD", "YMML", "YSSY", "YSSY YSBK"))
        };

        var defaults = MobileEntryDraftDefaultPlanner.Create(entries);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSCN", "YMML", "YSCN YMML"), defaults);
    }

    [Fact]
    public void CreateSelectsLatestDatedEntryWhenInputIsUnsorted()
    {
        var entries = new[]
        {
            Current("ent_old", Entry(new DateOnly(2026, 7, 18), "PA28", "VH-OLD", "YMML", "YSSY", "YSSY YSBK")),
            Current("ent_latest", Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "YSBK", "YSCN", "YSCN YMML"))
        };

        var defaults = MobileEntryDraftDefaultPlanner.Create(entries);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSCN", "YMML", "YSCN YMML"), defaults);
    }

    [Fact]
    public void CreateFallsBackToPreviousDepartureWhenLatestDestinationIsBlank()
    {
        var defaults = MobileEntryDraftDefaultPlanner.Create([
            Current("ent_latest", Entry("C172", "VH-ABC", "YSBK", "", "YSBK YSCN"))
        ]);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSBK", "YSCN", "YSBK YSCN"), defaults);
    }

    [Fact]
    public void CreateDoesNotDefaultRouteWhenItDoesNotStartAtNextDeparture()
    {
        var defaults = MobileEntryDraftDefaultPlanner.Create([
            Current("ent_latest", Entry("C172", "VH-ABC", "YSBK", "YSCN", "YSBK YSCN"))
        ]);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSCN", string.Empty, string.Empty), defaults);
    }

    [Fact]
    public void CreateIgnoresDeletedAndPayloadlessEntries()
    {
        var entries = new[]
        {
            new PortableLogbookMaterializedEntry(
                new EntryId("ent_deleted"),
                new RevisionId("rev_deleted"),
                IsDeleted: true,
                Entry("PA28", "VH-DEL", "YMML", "YSSY", "YSSY YSBK"),
                [new RevisionId("rev_deleted")]),
            new PortableLogbookMaterializedEntry(
                new EntryId("ent_null"),
                new RevisionId("rev_null"),
                IsDeleted: false,
                Entry: null,
                [new RevisionId("rev_null")]),
            Current("ent_current", Entry("C172", "VH-ABC", "YSBK", "YSCN", "YSCN YMML"))
        };

        var defaults = MobileEntryDraftDefaultPlanner.Create(entries);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSCN", "YMML", "YSCN YMML"), defaults);
    }

    [Fact]
    public void CreateDefaultsDestinationFromAirportLikeEndOfCarriedRoute()
    {
        var defaults = MobileEntryDraftDefaultPlanner.Create([
            Current("ent_latest", Entry("C172", "VH-ABC", "YSBK", "YSCN", "YSCN/12/YMML"))
        ]);

        Assert.Equal(new MobileEntryDraftDefaults("C172", "VH-ABC", "YSCN", "YMML", "YSCN/12/YMML"), defaults);
    }

    private static PortableLogbookMaterializedEntry Current(string entryId, PortableLogbookEntry entry) =>
        new(
            new EntryId(entryId),
            new RevisionId($"rev_{entryId}"),
            IsDeleted: false,
            entry,
            [new RevisionId($"rev_{entryId}")]);

    private static PortableLogbookEntry Entry(
        string aircraftType,
        string registration,
        string from,
        string to,
        string route) =>
        Entry(new DateOnly(2026, 7, 19), aircraftType, registration, from, to, route);

    private static PortableLogbookEntry Entry(
        DateOnly date,
        string aircraftType,
        string registration,
        string from,
        string to,
        string route) =>
        PortableLogbookEntry.Empty with
        {
            Date = date,
            AircraftType = aircraftType,
            Registration = registration,
            From = from,
            To = to,
            Route = route,
            PilotInCommand = 1.0m
        };
}
