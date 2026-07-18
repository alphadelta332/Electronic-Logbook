using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageImportTests
{
    private static readonly DateTimeOffset ImportedAt = DateTimeOffset.Parse("2026-07-18T00:03:00Z");

    [Fact]
    public void ImportPackageAppliesReadyPackageAndCreatesStorageEnvelope()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-NEW", "rev_correct");
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(create.LogbookId, [create, correction]);
        var incomingPackage = PortableLogbookPackage.Write(incoming, key);

        var result = PortableLogbookPackageImport.ImportPackage(local, incomingPackage, key, [], ImportedAt);

        Assert.Equal(PortableLogbookPackageImportStatus.Applied, result.Status);
        Assert.Equal([create.RevisionId, correction.RevisionId], result.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(PortableLogbookImportPlanStatus.ReadyToApply, result.Plan?.Status);
        Assert.NotNull(result.NewReceipt);
        Assert.Equal(ImportedAt, result.NewReceipt.ImportedAt);
        Assert.NotNull(result.EncryptedHistoryPackage);
        Assert.NotNull(result.StorageEnvelope);
        Assert.Equal(result.NewReceipt.PackageSha256, Assert.Single(result.StorageEnvelope.ImportReceipts).PackageSha256);
        var stored = PortableLogbookPackage.Read(result.EncryptedHistoryPackage, key, create.LogbookId);
        Assert.Equal(correction.RevisionId, stored.Document.Operations.Last().RevisionId);
    }

    [Fact]
    public void ImportPackageRecordsValidDuplicateOnlyPackageReceipt()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incomingPackage = PortableLogbookPackage.Write(local, key);

        var result = PortableLogbookPackageImport.ImportPackage(local, incomingPackage, key, [], ImportedAt);

        Assert.Equal(PortableLogbookPackageImportStatus.DuplicateOperationsRecorded, result.Status);
        Assert.Equal(PortableLogbookImportPlanStatus.DuplicateOnly, result.Plan?.Status);
        Assert.Equal([create.RevisionId], result.Document.Operations.Select(operation => operation.RevisionId));
        Assert.NotNull(result.NewReceipt);
        Assert.NotNull(result.StorageEnvelope);
    }

    [Fact]
    public void ImportPackageShortCircuitsPreviouslySeenPackageFingerprint()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incomingPackage = PortableLogbookPackage.Write(local, key);
        var manifest = PortableLogbookPackage.Read(incomingPackage, key, create.LogbookId).Manifest;
        var receipt = PortableLogbookImportLedger.CreateReceipt(incomingPackage, manifest, ImportedAt.AddMinutes(-1));

        var result = PortableLogbookPackageImport.ImportPackage(local, incomingPackage, key, [receipt], ImportedAt);

        Assert.Equal(PortableLogbookPackageImportStatus.PackageReplay, result.Status);
        Assert.Null(result.Plan);
        Assert.Null(result.NewReceipt);
        Assert.Null(result.StorageEnvelope);
        Assert.Equal(receipt.PackageSha256, Assert.Single(result.ImportReceipts).PackageSha256);
    }

    [Fact]
    public void ImportPackageDoesNotMutateStorageWhenResolutionIsRequired()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var localCorrection = CorrectOperation(create, "VH-LOCAL", "rev_local");
        var incomingCorrection = CorrectOperation(create, "VH-INCOMING", "rev_incoming");
        var local = Document(create.LogbookId, [create, localCorrection]);
        var incoming = Document(create.LogbookId, [create, incomingCorrection]);
        var incomingPackage = PortableLogbookPackage.Write(incoming, key);

        var result = PortableLogbookPackageImport.ImportPackage(local, incomingPackage, key, [], ImportedAt);

        Assert.Equal(PortableLogbookPackageImportStatus.RequiresResolution, result.Status);
        Assert.Equal(PortableLogbookImportPlanStatus.RequiresConflictResolution, result.Plan?.Status);
        Assert.Null(result.NewReceipt);
        Assert.Null(result.StorageEnvelope);
        Assert.Equal([create.RevisionId, localCorrection.RevisionId], result.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ImportPackageRejectsWrongLogbookBeforePlanning()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var local = Document(create.LogbookId, [create]);
        var incoming = Document(new LogbookId("log_other"), [create with { LogbookId = new LogbookId("log_other") }]);
        var incomingPackage = PortableLogbookPackage.Write(incoming, key);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageImport.ImportPackage(local, incomingPackage, key, [], ImportedAt));

        Assert.Equal(PortableLogbookPackageError.WrongLogbook, exception.Error);
    }

    [Fact]
    public void ImportPackageCanRebuildReplacementReplicaAfterLocalLoss()
    {
        var key = PortableLogbookKey.Generate();
        var create = CreateOperation();
        var correction = CorrectOperation(create, "VH-RECOVERED", "rev_recovered");
        var survivingReplica = Document(create.LogbookId, [create, correction]);
        var recoveryPackage = PortableLogbookPackage.Write(survivingReplica, key);
        var replacementReplica = Document(create.LogbookId, []);

        var result = PortableLogbookPackageImport.ImportPackage(
            replacementReplica,
            recoveryPackage,
            key,
            [],
            ImportedAt);

        Assert.Equal(PortableLogbookPackageImportStatus.Applied, result.Status);
        Assert.Equal(PortableLogbookImportPlanStatus.ReadyToApply, result.Plan?.Status);
        Assert.Equal(2, result.Plan?.Preview.NewOperations.Count);
        Assert.Equal([create.EntryId, correction.EntryId], result.Document.Operations.Select(operation => operation.EntryId));
        Assert.Equal([create.RevisionId, correction.RevisionId], result.Document.Operations.Select(operation => operation.RevisionId));
        Assert.NotNull(result.NewReceipt);
        Assert.NotNull(result.StorageEnvelope);
        Assert.NotNull(result.EncryptedHistoryPackage);
        Assert.Equal(result.Document.LogbookId, result.StorageEnvelope.LogbookId);
        Assert.Equal(result.NewReceipt.PackageSha256, Assert.Single(result.StorageEnvelope.ImportReceipts).PackageSha256);
        var reopened = PortableLogbookWorkbookStorage.OpenEnvelope(result.StorageEnvelope, key);
        Assert.Equal(result.Document.Operations.Select(operation => operation.RevisionId), reopened.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(correction.RevisionId, reopened.Document.Operations.Last().RevisionId);
        Assert.Equal(correction.EntryId, reopened.Document.Operations.Last().EntryId);
    }

    private static PortableLogbookDocument Document(
        LogbookId logbookId,
        IReadOnlyList<PortableLogbookOperation> operations) =>
        PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], operations);

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_import"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry("VH-OLD"));

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
