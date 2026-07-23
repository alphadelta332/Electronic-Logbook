using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkingCopyTests
{
    [Fact]
    public void FromProjectionMarksCleanWorkbookWhenNoOperationsArePending()
    {
        var reconciledAt = DateTimeOffset.Parse("2026-07-18T00:00:00Z");

        var state = PortableLogbookWorkingCopy.FromProjection(new PortableLogbookProjectionResult([]), reconciledAt);

        Assert.False(state.HasUnexportedChanges);
        Assert.False(state.ExportRequired);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Empty(state.PendingRevisionIds);
        Assert.Equal(reconciledAt, state.ReconciledAt);
    }

    [Fact]
    public void FromProjectionCountsPendingWorkbookOperationsUntilExport()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_working"),
            new EntryId("ent_new"),
            new RevisionId("rev_b"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-NEW"));
        var correction = new CorrectEntryOperation(
            create.LogbookId,
            new EntryId("ent_existing"),
            new RevisionId("rev_a"),
            new HashSet<RevisionId> { new("rev_existing") },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            Entry("VH-COR"));
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            new EntryId("ent_deleted"),
            new RevisionId("rev_c"),
            new HashSet<RevisionId> { new("rev_deleted") },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2));

        var state = PortableLogbookWorkingCopy.FromProjection(
            new PortableLogbookProjectionResult([create, deletion, correction]),
            DateTimeOffset.Parse("2026-07-18T00:05:00Z"));

        Assert.True(state.HasUnexportedChanges);
        Assert.True(state.ExportRequired);
        Assert.Equal(3, state.PendingOperationCount);
        Assert.Equal(1, state.PendingCreateCount);
        Assert.Equal(1, state.PendingCorrectionCount);
        Assert.Equal(1, state.PendingDeletionCount);
        Assert.Equal([new RevisionId("rev_a"), new RevisionId("rev_b"), new RevisionId("rev_c")], state.PendingRevisionIds);
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
