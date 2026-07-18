using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookExchangeTests
{
    [Fact]
    public void PreviewImportTreatsAlreadySeenPackageAsDuplicateReplay()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Empty(preview.NewOperations);
        Assert.Equal(1, preview.DuplicateOperationCount);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportCountsIncomingCorrectionWithoutRollingBackLocalHistory()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ", "rev_correct");
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, correction]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Equal(1, preview.DuplicateOperationCount);
        Assert.Equal(1, preview.CorrectionCount);
        Assert.Equal(correction.RevisionId, Assert.Single(preview.NewOperations).RevisionId);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportCountsIncomingDeletion()
    {
        var create = CreateOperation();
        var deletion = new DeleteEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_delete"),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1));
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, deletion]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.Equal(1, preview.DeletionCount);
        Assert.False(preview.HasConflicts);
    }

    [Fact]
    public void PreviewImportReportsDivergentRevisionConflict()
    {
        var create = CreateOperation();
        var localCorrection = CorrectOperation(create, "VH-LOCAL", "rev_local");
        var incomingCorrection = CorrectOperation(create, "VH-INCOMING", "rev_incoming");
        var local = Document(create.LogbookId, [create, localCorrection]);
        var incoming = Document(create.LogbookId, [create, incomingCorrection]);

        var preview = PortableLogbookExchange.PreviewImport(local, incoming);

        Assert.True(preview.HasConflicts);
        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal([incomingCorrection.RevisionId, localCorrection.RevisionId], conflict.HeadRevisionIds);
    }

    [Fact]
    public void PreviewImportRejectsWrongLogbook()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(new LogbookId("log_other"), [create with { LogbookId = new LogbookId("log_other") }]);

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookExchange.PreviewImport(local, incoming));

        Assert.Equal("incomingDocument", exception.ParamName);
    }

    [Fact]
    public void ApplyImportKeepsAlreadySeenPackageIdempotent()
    {
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create]);

        var applied = PortableLogbookExchange.ApplyImport(local, incoming);

        Assert.Equal([create.RevisionId], applied.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ApplyImportAddsNewOperationsWhenPreviewHasNoConflicts()
    {
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-XYZ", "rev_correct");
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, correction]);

        var applied = PortableLogbookExchange.ApplyImport(local, incoming);

        Assert.Equal([create.RevisionId, correction.RevisionId], applied.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ApplyImportBlocksUnresolvedConflicts()
    {
        var create = CreateOperation();
        var localCorrection = CorrectOperation(create, "VH-LOCAL", "rev_local");
        var incomingCorrection = CorrectOperation(create, "VH-INCOMING", "rev_incoming");
        var local = Document(create.LogbookId, [create, localCorrection]);
        var incoming = Document(create.LogbookId, [create, incomingCorrection]);

        var exception = Assert.Throws<PortableLogbookImportException>(() => PortableLogbookExchange.ApplyImport(local, incoming));

        Assert.Equal(PortableLogbookImportError.UnresolvedConflicts, exception.Error);
    }

    private static PortableLogbookDocument Document(
        LogbookId logbookId,
        IReadOnlyList<PortableLogbookOperation> operations) =>
        PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], operations);

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_test"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-ABC"));

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation create,
        string registration,
        string revisionId) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(1),
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
