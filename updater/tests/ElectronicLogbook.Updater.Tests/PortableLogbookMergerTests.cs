using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookMergerTests
{
    [Fact]
    public void MergeTreatsDuplicateOperationDeliveryAsIdempotent()
    {
        var operation = CreateOperation();

        var result = PortableLogbookMerger.Merge([operation, operation]);

        Assert.Equal(1, result.OperationCount);
        Assert.Empty(result.Conflicts);
        var entry = Assert.Single(result.Entries.Values);
        Assert.Equal(operation.EntryId, entry.EntryId);
        Assert.Equal(operation.RevisionId, entry.CurrentRevisionId);
        Assert.False(entry.IsDeleted);
        Assert.Equal("VH-ABC", entry.Entry?.Registration);
    }

    [Fact]
    public void MergeIsOrderIndependentForLinearHistory()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ");

        var forward = PortableLogbookMerger.Merge([create, correction]);
        var reverse = PortableLogbookMerger.Merge([correction, create]);

        Assert.Equal(forward.Entries.Keys, reverse.Entries.Keys);
        var entry = Assert.Single(reverse.Entries.Values);
        Assert.Equal(correction.RevisionId, entry.CurrentRevisionId);
        Assert.Equal("VH-XYZ", entry.Entry?.Registration);
        Assert.Equal([correction.RevisionId, create.RevisionId], entry.RevisionHistory);
    }

    [Fact]
    public void MergeKeepsDeletionAsTombstone()
    {
        var create = CreateOperation();
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            "Duplicate entry");

        var result = PortableLogbookMerger.Merge([deletion, create]);

        var entry = Assert.Single(result.Entries.Values);
        Assert.True(entry.IsDeleted);
        Assert.Null(entry.Entry);
        Assert.Equal(deletion.RevisionId, entry.CurrentRevisionId);
    }

    [Fact]
    public void MergeReportsDivergentHeadsAsConflict()
    {
        var create = CreateOperation();
        var deviceA = new DeviceId("dev_a");
        var deviceB = new DeviceId("dev_b");
        var correctionA = CorrectOperation(create, "VH-AAA", "rev_a", deviceA);
        var correctionB = CorrectOperation(create, "VH-BBB", "rev_b", deviceB);

        var result = PortableLogbookMerger.Merge([create, correctionB, correctionA]);

        Assert.Empty(result.Entries);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal([correctionA.RevisionId, correctionB.RevisionId], conflict.HeadRevisionIds);
    }

    [Fact]
    public void MergeAcceptsExplicitConflictResolutionReferencingBothBranches()
    {
        var create = CreateOperation();
        var correctionA = CorrectOperation(create, "VH-AAA", "rev_a", new DeviceId("dev_a"));
        var correctionB = CorrectOperation(create, "VH-BBB", "rev_b", new DeviceId("dev_b"));
        var resolution = new ResolveConflictOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_resolved"),
            new HashSet<RevisionId> { correctionA.RevisionId, correctionB.RevisionId },
            new DeviceId("dev_excel"),
            create.CreatedAt.AddMinutes(2),
            EntryWithRegistration("VH-FINAL"),
            "Resolved from workbook review");

        var result = PortableLogbookMerger.Merge([correctionB, resolution, create, correctionA]);

        Assert.Empty(result.Conflicts);
        var entry = Assert.Single(result.Entries.Values);
        Assert.Equal(resolution.RevisionId, entry.CurrentRevisionId);
        Assert.Equal("VH-FINAL", entry.Entry?.Registration);
        Assert.Contains(correctionA.RevisionId, entry.RevisionHistory);
        Assert.Contains(correctionB.RevisionId, entry.RevisionHistory);
    }

    [Fact]
    public void MergeDoesNotRollBackWhenOlderPackageOperationsAreReplayed()
    {
        var create = CreateOperation();
        var firstCorrection = CorrectOperation(create, "VH-FIRST", "rev_first");
        var secondCorrection = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_second"),
            new HashSet<RevisionId> { firstCorrection.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            EntryWithRegistration("VH-SECOND"));

        var result = PortableLogbookMerger.Merge([secondCorrection, create, firstCorrection, create, firstCorrection]);

        Assert.Empty(result.Conflicts);
        var entry = Assert.Single(result.Entries.Values);
        Assert.Equal(secondCorrection.RevisionId, entry.CurrentRevisionId);
        Assert.Equal("VH-SECOND", entry.Entry?.Registration);
    }

    [Fact]
    public void MergeProducesSameMaterializedHeadAcrossBatchAssociations()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ");
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { correction.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            "Deleted after correction");

        var allAtOnce = PortableLogbookMerger.Merge([create, correction, deletion]);
        var replayedInBatches = PortableLogbookMerger.Merge([correction, create, deletion, create]);

        Assert.Empty(allAtOnce.Conflicts);
        Assert.Empty(replayedInBatches.Conflicts);
        Assert.Equal(
            Assert.Single(allAtOnce.Entries.Values).CurrentRevisionId,
            Assert.Single(replayedInBatches.Entries.Values).CurrentRevisionId);
        Assert.True(Assert.Single(replayedInBatches.Entries.Values).IsDeleted);
    }

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_test"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            EntryWithRegistration("VH-ABC"));

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation create,
        string registration,
        string revisionId = "rev_correct",
        DeviceId? deviceId = null) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            deviceId ?? create.DeviceId,
            create.CreatedAt.AddMinutes(1),
            EntryWithRegistration(registration));

    private static PortableLogbookEntry EntryWithRegistration(string registration) =>
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
