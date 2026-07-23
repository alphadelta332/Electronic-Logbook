using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookRevisionHistoryTests
{
    [Fact]
    public void ForEntryReturnsChronologicalHistoryAndCurrentRevision()
    {
        var create = CreateOperation("rev_create", "VH-OLD", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correction = CorrectOperation(create, "rev_correct", "VH-NEW", create.CreatedAt.AddMinutes(1));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [correction, create]);

        var history = PortableLogbookRevisionHistory.ForEntry(document, create.EntryId);

        Assert.False(history.HasConflict);
        Assert.False(history.IsDeleted);
        Assert.Equal(correction.RevisionId, history.CurrentRevisionId);
        Assert.Equal([create.RevisionId, correction.RevisionId], history.Revisions.Select(revision => revision.RevisionId));
        Assert.Equal("VH-NEW", history.Revisions.Last().Entry?.Registration);
    }

    [Fact]
    public void ForEntryReportsDeletedCurrentState()
    {
        var create = CreateOperation("rev_create", "VH-OLD", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, deletion]);

        var history = PortableLogbookRevisionHistory.ForEntry(document, create.EntryId);

        Assert.True(history.IsDeleted);
        Assert.Equal(deletion.RevisionId, history.CurrentRevisionId);
        Assert.Null(history.Revisions.Last().Entry);
    }

    [Fact]
    public void ForEntryReportsConflictHeadsWhenEntryHasDivergentBranches()
    {
        var create = CreateOperation("rev_create", "VH-OLD", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var local = CorrectOperation(create, "rev_local", "VH-LOCAL", create.CreatedAt.AddMinutes(1));
        var incoming = CorrectOperation(create, "rev_incoming", "VH-INCOMING", create.CreatedAt.AddMinutes(2));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create, incoming, local]);

        var history = PortableLogbookRevisionHistory.ForEntry(document, create.EntryId);

        Assert.True(history.HasConflict);
        Assert.Null(history.CurrentRevisionId);
        Assert.Equal([incoming.RevisionId, local.RevisionId], history.ConflictHeadRevisionIds);
    }

    [Fact]
    public void ForEntryRejectsMissingEntry()
    {
        var create = CreateOperation("rev_create", "VH-OLD", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);

        Assert.Throws<KeyNotFoundException>(() => PortableLogbookRevisionHistory.ForEntry(document, new EntryId("ent_missing")));
    }

    private static CreateEntryOperation CreateOperation(
        string revisionId,
        string registration,
        DateTimeOffset createdAt) =>
        new(
            new LogbookId("log_history"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            Entry(registration));

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation create,
        string revisionId,
        string registration,
        DateTimeOffset createdAt) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            createdAt,
            Entry(registration));

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
