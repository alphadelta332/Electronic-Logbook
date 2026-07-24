using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookStorageTests
{
    [Fact]
    public void StorageEnvelopeRoundTripsEncryptedHistoryAndReceipts()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var receipt = PortableLogbookImportLedger.CreateReceipt(
            packageBytes,
            PortableLogbookPackage.Read(packageBytes, key, document.LogbookId).Manifest,
            DateTimeOffset.Parse("2026-07-18T00:01:00Z"));

        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, packageBytes, [receipt]);
        var json = PortableLogbookWorkbookStorage.Serialize(envelope);
        var roundTripped = PortableLogbookWorkbookStorage.Deserialize(json);

        Assert.Equal(document.LogbookId, roundTripped.LogbookId);
        Assert.Equal(document.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(packageBytes, PortableLogbookWorkbookStorage.GetEncryptedHistoryPackage(roundTripped));
        Assert.Equal(receipt.PackageSha256, Assert.Single(roundTripped.ImportReceipts).PackageSha256);
    }

    [Fact]
    public void StorageEnvelopeRoundTripsV2EncryptedWorkbookFaithfulHistory()
    {
        var document = CreateDocumentV2();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);

        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, packageBytes, []);
        var json = PortableLogbookWorkbookStorage.Serialize(envelope);
        var roundTripped = PortableLogbookWorkbookStorage.Deserialize(json);
        var state = PortableLogbookWorkbookStorage.OpenEnvelopeV2(roundTripped, key);

        Assert.Equal(document.LogbookId, roundTripped.LogbookId);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(document.CurrencyOverrideDates, state.Document.CurrencyOverrideDates);
        var operation = Assert.Single(state.Document.Operations);
        Assert.Equal(1.2m, operation.Entry?.SeCommandDay);
        Assert.Equal(0.4m, operation.Entry?.IfrIf);
        Assert.Equal(2, operation.Entry?.Ils);
    }

    [Fact]
    public void OpenEnvelopeDecryptsHistoryAndPreservesReceipts()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var receipt = PortableLogbookImportLedger.CreateReceipt(
            packageBytes,
            PortableLogbookPackage.Read(packageBytes, key, document.LogbookId).Manifest,
            DateTimeOffset.Parse("2026-07-18T00:01:00Z"));
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, packageBytes, [receipt]);

        var state = PortableLogbookWorkbookStorage.OpenEnvelope(envelope, key);

        Assert.Equal(document.LogbookId, state.Document.LogbookId);
        Assert.Equal(document.Operations.Select(operation => operation.RevisionId), state.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(receipt.PackageSha256, Assert.Single(state.ImportReceipts).PackageSha256);
    }

    [Fact]
    public void OpenEnvelopePreservesSetupRowStableIdsForReconciliationAfterSaveReopen()
    {
        var key = PortableLogbookKey.Generate();
        var setup = PortableLogbookSetup.CreateInitialSetupPlan(
            [Entry("VH-ABC"), Entry("VH-DEF")],
            [],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            new LogbookId("log_storage"),
            new DeviceId("dev_excel"),
            key,
            new PortableLogbookIdFactory(
                QueueIds([new EntryId("ent_1"), new EntryId("ent_2")]),
                QueueIds([new RevisionId("rev_1"), new RevisionId("rev_2")])));
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            setup.InitialDocument,
            setup.InitialPackageBytes,
            []);
        var roundTripped = PortableLogbookWorkbookStorage.Deserialize(
            PortableLogbookWorkbookStorage.Serialize(envelope));

        var reopened = PortableLogbookWorkbookStorage.OpenEnvelope(roundTripped, key);
        var knownEntries = PortableLogbookMerger.Merge(reopened.Document.Operations).Entries.Values;
        var validation = PortableLogbookWorkbookRowValidator.Validate(setup.WorkbookRows, knownEntries);
        var reconciliation = PortableLogbookWorkbookProjection.Reconcile(
            knownEntries,
            setup.WorkbookRows,
            setup.LogbookId,
            setup.DeviceId,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));

        Assert.True(validation.IsValid);
        Assert.Empty(reconciliation.Operations);
    }

    [Fact]
    public void StorageEnvelopeJsonDoesNotExposeRawFlightDetails()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, PortableLogbookKey.Generate());

        var json = PortableLogbookWorkbookStorage.Serialize(
            PortableLogbookWorkbookStorage.CreateEnvelope(document, packageBytes, []));

        Assert.DoesNotContain("VH-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("YSBK", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Training details", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsUnsupportedStorageVersion()
    {
        var document = CreateDocument();
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, [1, 2, 3], []) with
        {
            StorageVersion = PortableLogbookWorkbookStorage.CurrentStorageVersion + 1
        };

        var exception = Assert.Throws<PortableLogbookWorkbookStorageException>(
            () => PortableLogbookWorkbookStorage.Deserialize(PortableLogbookWorkbookStorage.Serialize(envelope)));

        Assert.Equal(PortableLogbookWorkbookStorageError.UnsupportedStorageVersion, exception.Error);
    }

    [Fact]
    public void DeserializeRejectsMalformedEnvelopeJsonWithTypedStorageError()
    {
        var exception = Assert.Throws<PortableLogbookWorkbookStorageException>(
            () => PortableLogbookWorkbookStorage.Deserialize("{ not valid json"));

        Assert.Equal(PortableLogbookWorkbookStorageError.InvalidEnvelope, exception.Error);
    }

    [Fact]
    public void DeserializeRejectsInvalidEncryptedPackageBase64()
    {
        var document = CreateDocument();
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, [1, 2, 3], []) with
        {
            EncryptedHistoryPackageBase64 = "not base64"
        };

        var exception = Assert.Throws<PortableLogbookWorkbookStorageException>(
            () => PortableLogbookWorkbookStorage.Deserialize(PortableLogbookWorkbookStorage.Serialize(envelope)));

        Assert.Equal(PortableLogbookWorkbookStorageError.InvalidEncryptedHistoryPackage, exception.Error);
    }

    [Fact]
    public void DeserializeRejectsEmptyEncryptedPackage()
    {
        var document = CreateDocument();
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(document, [1, 2, 3], []) with
        {
            EncryptedHistoryPackageBase64 = string.Empty
        };

        var exception = Assert.Throws<PortableLogbookWorkbookStorageException>(
            () => PortableLogbookWorkbookStorage.Deserialize(PortableLogbookWorkbookStorage.Serialize(envelope)));

        Assert.Equal(PortableLogbookWorkbookStorageError.InvalidEncryptedHistoryPackage, exception.Error);
    }

    [Fact]
    public void OpenEnvelopeRejectsWrongKeyWithoutReturningStoredState()
    {
        var document = CreateDocument();
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, PortableLogbookKey.Generate()),
            []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookWorkbookStorage.OpenEnvelope(envelope, PortableLogbookKey.Generate()));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void OpenEnvelopeRejectsSummaryMismatch()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []) with
        {
            Summary = PortableLogbookSummary.Create(document) with { OperationCount = 99 }
        };

        var exception = Assert.Throws<PortableLogbookWorkbookStorageException>(
            () => PortableLogbookWorkbookStorage.OpenEnvelope(envelope, key));

        Assert.Equal(PortableLogbookWorkbookStorageError.EnvelopeSummaryMismatch, exception.Error);
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_storage"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-SECRET",
                From = "YSBK",
                To = "YSCN",
                Details = "Training details",
                PilotInCommand = 1.2m
            });

        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }

    private static PortableLogbookDocumentV2 CreateDocumentV2()
    {
        var logbookId = new LogbookId("log_storage_v2");
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var create = PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026,
                Month = 7,
                Day = 18,
                Type = "DA40",
                Reg = "VH-SECRET",
                From = "YSBK",
                To = "YSCN",
                Remarks = "Training details",
                FlightReview = true,
                CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Alpha" },
                SeCommandDay = 1.2m,
                IfrIf = 0.4m,
                Ils = 2
            });

        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(customFieldId, "Custom 1", 1)],
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 6, 1), null, null),
            [create]);
    }

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.2m
        };

    private static Func<T> QueueIds<T>(IEnumerable<T> ids)
    {
        var queue = new Queue<T>(ids);
        return () => queue.Dequeue();
    }
}
