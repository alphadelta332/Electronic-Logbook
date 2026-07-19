using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageExportTests
{
    private static readonly DateTimeOffset ExportedAt = DateTimeOffset.Parse("2026-07-18T00:10:00Z");

    [Fact]
    public void ExportPackageWritesCurrentDocumentWhenWorkbookHasNoPendingChanges()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var document = Document(create.LogbookId, [create]);
        var known = KnownEntry(create.EntryId.Value, create.RevisionId.Value, create.Entry);
        var receipt = ReceiptFor(document, key);

        var result = PortableLogbookPackageExport.ExportPackage(
            document,
            [known],
            [new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, create.Entry)],
            create.DeviceId,
            key,
            [receipt],
            ExportedAt);

        Assert.Empty(result.Projection.Operations);
        Assert.False(result.WorkingCopyBeforeExport.ExportRequired);
        Assert.Equal([create.RevisionId], result.Document.Operations.Select(operation => operation.RevisionId));
        var workbookRow = Assert.Single(result.WorkbookRows);
        Assert.Equal(create.EntryId, workbookRow.EntryId);
        Assert.Equal(create.RevisionId, workbookRow.CurrentRevisionId);
        var read = PortableLogbookPackage.Read(result.PackageBytes, key, create.LogbookId);
        Assert.Equal(create.RevisionId, Assert.Single(read.Document.Operations).RevisionId);
        Assert.Equal(receipt.PackageSha256, Assert.Single(result.StorageEnvelope.ImportReceipts).PackageSha256);
    }

    [Fact]
    public void ExportPackageAddsPendingWorkbookCorrectionBeforeWritingPackage()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var document = Document(create.LogbookId, [create]);
        var known = KnownEntry(create.EntryId.Value, create.RevisionId.Value, create.Entry);

        var result = PortableLogbookPackageExport.ExportPackage(
            document,
            [known],
            [new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-UPDATED"))],
            create.DeviceId,
            key,
            [],
            ExportedAt,
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_export")));

        Assert.True(result.WorkingCopyBeforeExport.ExportRequired);
        Assert.Equal(1, result.WorkingCopyBeforeExport.PendingCorrectionCount);
        Assert.Equal([create.RevisionId, new RevisionId("rev_export")], result.Document.Operations.Select(operation => operation.RevisionId));
        var workbookRow = Assert.Single(result.WorkbookRows);
        Assert.Equal(create.EntryId, workbookRow.EntryId);
        Assert.Equal(new RevisionId("rev_export"), workbookRow.CurrentRevisionId);
        Assert.Equal("VH-UPDATED", workbookRow.Entry.Registration);
        var read = PortableLogbookPackage.Read(result.PackageBytes, key, create.LogbookId);
        var correction = Assert.IsType<CorrectEntryOperation>(read.Document.Operations.Last());
        Assert.Equal("VH-UPDATED", correction.Entry.Registration);
    }

    [Fact]
    public void ExportPackageAddsMissingKnownRowsAsTombstones()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var document = Document(create.LogbookId, [create]);
        var known = KnownEntry(create.EntryId.Value, create.RevisionId.Value, create.Entry);

        var result = PortableLogbookPackageExport.ExportPackage(
            document,
            [known],
            [],
            create.DeviceId,
            key,
            [],
            ExportedAt,
            new PortableLogbookIdFactory(() => new EntryId("unused"), () => new RevisionId("rev_delete")));

        Assert.Equal(1, result.WorkingCopyBeforeExport.PendingDeletionCount);
        var deletion = Assert.IsType<DeleteEntryOperation>(result.Document.Operations.Last());
        Assert.Equal(known.EntryId, deletion.EntryId);
        Assert.Equal(known.CurrentRevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.Empty(result.WorkbookRows);
    }

    private static PortableLogbookDocument Document(
        LogbookId logbookId,
        IReadOnlyList<PortableLogbookOperation> operations) =>
        PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], operations);

    private static PortableLogbookPackageReceipt ReceiptFor(PortableLogbookDocument document, PortableLogbookKey key)
    {
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var manifest = PortableLogbookPackage.Read(packageBytes, key, document.LogbookId).Manifest;
        return PortableLogbookImportLedger.CreateReceipt(packageBytes, manifest, DateTimeOffset.Parse("2026-07-18T00:01:00Z"));
    }

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_export"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-ABC"));

    private static PortableLogbookMaterializedEntry KnownEntry(
        string entryId,
        string revisionId,
        PortableLogbookEntry entry) =>
        new(
            new EntryId(entryId),
            new RevisionId(revisionId),
            false,
            entry,
            [new RevisionId(revisionId)]);

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
