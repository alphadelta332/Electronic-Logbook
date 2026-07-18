using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookConflictResolutionTests
{
    [Fact]
    public void CreateResolutionReferencesEveryConflictHead()
    {
        var conflict = new PortableLogbookConflict(
            new EntryId("ent_1"),
            [new RevisionId("rev_a"), new RevisionId("rev_b")]);

        var resolution = PortableLogbookConflictResolution.CreateResolution(
            conflict,
            new LogbookId("log_test"),
            new DeviceId("dev_excel"),
            new RevisionId("rev_resolved"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-FINAL"),
            "User selected final row");

        Assert.Equal(conflict.EntryId, resolution.EntryId);
        Assert.Equal(conflict.HeadRevisionIds.OrderBy(id => id.Value), resolution.ParentRevisionIds.OrderBy(id => id.Value));
        Assert.Equal("User selected final row", resolution.ResolutionNote);
    }

    [Fact]
    public void CreateResolutionRejectsSingleHeadConflict()
    {
        var conflict = new PortableLogbookConflict(new EntryId("ent_1"), [new RevisionId("rev_a")]);

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookConflictResolution.CreateResolution(
            conflict,
            new LogbookId("log_test"),
            new DeviceId("dev_excel"),
            new RevisionId("rev_resolved"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-FINAL")));

        Assert.Equal("conflict", exception.ParamName);
    }

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.2m
        };
}
