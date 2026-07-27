using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class BrowserLogbookStoreGoldenFixtureTests
{
    [Fact]
    public void MobileEndpointSerializesPortableSchemaV1ToGoldenFixture()
    {
        var fixtureJson = ReadGoldenFixture();
        var document = PortableLogbookJson.Deserialize(fixtureJson)
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");

        var serialized = PortableLogbookJson.Serialize(document);

        Assert.Equal(Normalize(fixtureJson), Normalize(serialized));
    }

    [Fact]
    public async Task LoadDocumentAsyncCanReadGoldenFixtureFromVersionedBrowserEnvelope()
    {
        var fixtureJson = ReadGoldenFixture();
        var stored = new BrowserLogbookStoredDocument(
            1,
            PortableLogbookDocument.CurrentSchemaVersion,
            fixtureJson);
        var jsRuntime = new MemoryJsRuntime
        {
            StoredJson = JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions)
        };
        var store = new BrowserLogbookStore(jsRuntime);

        var document = await store.LoadDocumentAsync();

        Assert.NotNull(document);
        AssertGoldenFixtureDocument(document);
    }

    [Fact]
    public async Task SaveDocumentAsyncPersistsGoldenFixtureInVersionedBrowserEnvelope()
    {
        var fixtureJson = ReadGoldenFixture();
        var document = PortableLogbookJson.Deserialize(fixtureJson)
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveDocumentAsync(document);

        var storedJson = Assert.IsType<string>(jsRuntime.StoredJson);
        var stored = JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(
            storedJson,
            PortableLogbookJson.SerializerOptions);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.StoreVersion);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, stored.SchemaVersion);
        Assert.Equal(Normalize(PortableLogbookJson.Serialize(document)), Normalize(stored.DocumentJson));

        jsRuntime.StoredJson = JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions);
        AssertGoldenFixtureDocument(await store.LoadDocumentAsync());
    }

    [Fact]
    public async Task SaveAndLoadStateRoundTripsExchangeReceiptsAndLastExport()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var receipt = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var lastExport = DateTimeOffset.Parse("2026-07-19T01:00:00Z");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveStateAsync(new BrowserLogbookState(document, [receipt], lastExport));

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        AssertGoldenFixtureDocument(state.Document);
        Assert.Equal([receipt], state.ImportReceipts);
        Assert.Equal(lastExport, state.LastSuccessfulExportAt);
    }

    [Fact]
    public async Task SaveAndLoadStateV2RoundTripsWorkbookFaithfulDocumentAcrossReload()
    {
        var document = CreateV2Document();
        var receipt = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-24T00:01:00Z"));
        var checkpoint = CreateCheckpoint(document);
        var jsRuntime = new MemoryJsRuntime();
        var storeBeforeReload = new BrowserLogbookStore(jsRuntime);

        await storeBeforeReload.SaveStateAsync(new BrowserLogbookStateV2(
            document,
            [receipt],
            checkpoint.ExportedAt,
            checkpoint));
        var storeAfterReload = new BrowserLogbookStore(jsRuntime);
        var reloaded = await storeAfterReload.LoadStateV2Async();

        Assert.NotNull(reloaded);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, reloaded.Document.SchemaVersion);
        Assert.Equal(document.CurrencyOverrideDates, reloaded.Document.CurrencyOverrideDates);
        Assert.Equal([receipt], reloaded.ImportReceipts);
        Assert.Equal(checkpoint, reloaded.LastSuccessfulExport);
        var operation = Assert.Single(reloaded.Document.Operations);
        Assert.Equal(new EntryId("ent_v2"), operation.EntryId);
        Assert.NotNull(operation.Entry);
        Assert.Equal("DA40", operation.Entry.Type);
        Assert.Equal("VH-VTQ", operation.Entry.Reg);
        Assert.Equal(1.2m, operation.Entry.SeCommandDay);
        Assert.Equal(2, operation.Entry.Ils);
        Assert.True(PortableLogbookValidatorV2.Validate(reloaded.Document, new DateOnly(2026, 7, 24)).IsValid);
    }

    [Fact]
    public async Task V2WorkbookPackageMobileStatePackageWorkbookRoundTripPreservesEverySourceFact()
    {
        var sourceDocument = CreateV2Document();
        var sourceWorkbookRows = PortableLogbookWorkbookProjection.CreateCurrentRows(sourceDocument);
        var sourceWorkbookRow = Assert.Single(sourceWorkbookRows);
        var sourceFacts = PortableLogbookJson.SerializeV2(sourceDocument);
        var key = PortableLogbookKey.Generate();

        var workbookPackage = PortableLogbookPackage.Write(sourceDocument, key);
        var mobileImported = PortableLogbookPackage.ReadV2(workbookPackage, key, sourceDocument.LogbookId);
        var mobileRuntime = new MemoryJsRuntime();
        var mobileStore = new BrowserLogbookStore(mobileRuntime);

        await mobileStore.SaveStateAsync(new BrowserLogbookStateV2(mobileImported.Document, [], null));

        var mobileState = await new BrowserLogbookStore(mobileRuntime).LoadStateV2Async();
        Assert.NotNull(mobileState);
        var mobilePackage = PortableLogbookPackage.Write(mobileState.Document, key);
        var returnedWorkbookDocument = PortableLogbookPackage.ReadV2(mobilePackage, key, sourceDocument.LogbookId).Document;
        var returnedWorkbookRows = PortableLogbookWorkbookProjection.CreateCurrentRows(returnedWorkbookDocument);
        var returnedWorkbookRow = Assert.Single(returnedWorkbookRows);

        Assert.Equal(sourceFacts, PortableLogbookJson.SerializeV2(returnedWorkbookDocument));
        Assert.Equal(sourceWorkbookRow.EntryId, returnedWorkbookRow.EntryId);
        Assert.Equal(sourceWorkbookRow.CurrentRevisionId, returnedWorkbookRow.CurrentRevisionId);
        Assert.Equal(
            PortableLogbookJson.SerializeV2(sourceDocument with { Operations = [sourceDocument.Operations.Single() with { Entry = sourceWorkbookRow.Entry }] }),
            PortableLogbookJson.SerializeV2(sourceDocument with { Operations = [sourceDocument.Operations.Single() with { Entry = returnedWorkbookRow.Entry }] }));
        Assert.Single(returnedWorkbookDocument.CustomFieldDefinitions);
        Assert.Equal(sourceDocument.CustomFieldDefinitions, returnedWorkbookDocument.CustomFieldDefinitions);
    }

    [Fact]
    public async Task LoadStateV2RejectsLegacyV1BrowserStateWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                PortableLogbookDocument.CurrentSchemaVersion,
                ReadGoldenFixture()),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadStateV2Async());

        Assert.Contains("Re-import the authoritative workbook", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task SaveAndLoadStateRoundTripsValidExportCheckpoint()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveStateAsync(new BrowserLogbookState(document, [], checkpoint.ExportedAt, checkpoint));

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(checkpoint.ExportedAt, state.LastSuccessfulExportAt);
        Assert.Equal(checkpoint, state.LastSuccessfulExport);
    }

    [Fact]
    public async Task SaveAndLoadStatePrefersExplicitLastExportTimestampOverCheckpointTimestamp()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var newerExport = checkpoint.ExportedAt.AddHours(2);
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveStateAsync(new BrowserLogbookState(document, [], newerExport, checkpoint));

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(newerExport, state.LastSuccessfulExportAt);
        Assert.Equal(checkpoint, state.LastSuccessfulExport);
    }

    [Fact]
    public async Task LoadStateAsyncDropsStaleStoredExportCheckpointWithoutLosingTimestamp()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document) with { OperationCount = document.Operations.Count - 1 };
        var stored = new BrowserLogbookStoredDocument(
            1,
            document.SchemaVersion,
            PortableLogbookJson.Serialize(document),
            [],
            checkpoint.ExportedAt,
            checkpoint);
        var store = new BrowserLogbookStore(new MemoryJsRuntime
        {
            StoredJson = JsonSerializer.Serialize(stored, PortableLogbookJson.SerializerOptions)
        });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(checkpoint.ExportedAt, state.LastSuccessfulExportAt);
        Assert.Null(state.LastSuccessfulExport);
    }

    [Fact]
    public async Task SaveDocumentAsyncPreservesExistingExchangeMetadata()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var receipt = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var lastExport = DateTimeOffset.Parse("2026-07-19T01:00:00Z");
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);
        await store.SaveStateAsync(new BrowserLogbookState(document, [receipt], lastExport));

        await store.SaveDocumentAsync(document with
        {
            Operations = document.Operations.Concat([
                new CorrectEntryOperation(
                    document.LogbookId,
                    new EntryId("ent_fixture"),
                    new RevisionId("rev_mobile"),
                    new HashSet<RevisionId> { new("rev_create") },
                    new DeviceId("dev_mobile"),
                    DateTimeOffset.Parse("2026-07-19T02:00:00Z"),
                    ((CreateEntryOperation)Assert.Single(document.Operations)).Entry with { Details = "Mobile correction" })
            ]).ToArray()
        });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal([receipt], state.ImportReceipts);
        Assert.Equal(lastExport, state.LastSuccessfulExportAt);
        Assert.Equal(2, state.Document.Operations.Count);
    }

    [Fact]
    public async Task SaveStateAsyncPersistsOfflineMobileCorrectionAcrossReload()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var create = Assert.IsType<CreateEntryOperation>(Assert.Single(document.Operations));
        var correction = new CorrectEntryOperation(
            document.LogbookId,
            create.EntryId,
            new RevisionId("rev_offline_mobile_correction"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_mobile_offline"),
            DateTimeOffset.Parse("2026-07-19T02:15:00Z"),
            create.Entry with
            {
                Registration = "VH-OFF",
                Details = "Offline correction persisted"
            });
        var updated = MobileLogbookDocument.AppendOperation(document, document.CustomFieldDefinitions, correction);
        var jsRuntime = new MemoryJsRuntime();
        var storeBeforeReload = new BrowserLogbookStore(jsRuntime);

        await storeBeforeReload.SaveStateAsync(new BrowserLogbookState(updated, [], null));
        var storeAfterReload = new BrowserLogbookStore(jsRuntime);
        var reloaded = await storeAfterReload.LoadStateAsync();

        Assert.NotNull(reloaded);
        Assert.Equal([create.RevisionId, correction.RevisionId], reloaded.Document.Operations.Select(operation => operation.RevisionId));
        var materialized = Assert.Single(PortableLogbookMerger.Merge(reloaded.Document.Operations).Entries.Values);
        Assert.Equal(correction.RevisionId, materialized.CurrentRevisionId);
        Assert.Equal("VH-OFF", materialized.Entry?.Registration);
        Assert.Equal("Offline correction persisted", materialized.Entry?.Details);
        Assert.True(PortableLogbookValidator.Validate(reloaded.Document, new DateOnly(2026, 7, 19)).IsValid);
    }

    [Fact]
    public async Task SaveDocumentAsyncPreservesValidExportCheckpointForUnchangedDocument()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);
        await store.SaveStateAsync(new BrowserLogbookState(document, [], checkpoint.ExportedAt, checkpoint));

        await store.SaveDocumentAsync(document);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(checkpoint.ExportedAt, state.LastSuccessfulExportAt);
        Assert.Equal(checkpoint, state.LastSuccessfulExport);
    }

    [Fact]
    public async Task SaveDocumentAsyncDropsStaleExportCheckpointForChangedDocument()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var jsRuntime = new MemoryJsRuntime();
        var store = new BrowserLogbookStore(jsRuntime);
        await store.SaveStateAsync(new BrowserLogbookState(document, [], checkpoint.ExportedAt, checkpoint));

        await store.SaveDocumentAsync(document with
        {
            Operations = document.Operations.Concat([
                new CorrectEntryOperation(
                    document.LogbookId,
                    new EntryId("ent_fixture"),
                    new RevisionId("rev_changed"),
                    new HashSet<RevisionId> { new("rev_create") },
                    new DeviceId("dev_mobile"),
                    DateTimeOffset.Parse("2026-07-19T04:00:00Z"),
                    ((CreateEntryOperation)Assert.Single(document.Operations)).Entry with { Details = "Changed after export" })
            ]).ToArray()
        });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(checkpoint.ExportedAt, state.LastSuccessfulExportAt);
        Assert.Null(state.LastSuccessfulExport);
    }

    [Fact]
    public async Task SaveStateAsyncDropsStaleExportCheckpointForChangedDocument()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var changedDocument = document with
        {
            Operations = document.Operations.Concat([
                new CorrectEntryOperation(
                    document.LogbookId,
                    new EntryId("ent_fixture"),
                    new RevisionId("rev_state_changed"),
                    new HashSet<RevisionId> { new("rev_create") },
                    new DeviceId("dev_mobile"),
                    DateTimeOffset.Parse("2026-07-19T04:00:00Z"),
                    ((CreateEntryOperation)Assert.Single(document.Operations)).Entry with { Details = "Changed through state save" })
            ]).ToArray()
        };
        var store = new BrowserLogbookStore(new MemoryJsRuntime());

        await store.SaveStateAsync(new BrowserLogbookState(
            changedDocument,
            [],
            checkpoint.ExportedAt,
            checkpoint));

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(checkpoint.ExportedAt, state.LastSuccessfulExportAt);
        Assert.Null(state.LastSuccessfulExport);
    }

    [Fact]
    public async Task RecordImportReceiptAsyncDeduplicatesByPackageHash()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var first = new PortableLogbookPackageReceipt(
            "ABC123",
            document.LogbookId,
            document.Operations.Count,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        var replacement = first with
        {
            PackageSha256 = "abc123",
            ImportedAt = DateTimeOffset.Parse("2026-07-19T01:00:00Z")
        };
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [first], null));

        await store.RecordImportReceiptAsync(replacement);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        var receipt = Assert.Single(state.ImportReceipts);
        Assert.Equal(replacement, receipt);
    }

    [Fact]
    public async Task RecordSuccessfulExportAsyncPersistsTimestampWithoutChangingDocument()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var exportedAt = DateTimeOffset.Parse("2026-07-19T03:00:00Z");
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [], null));

        await store.RecordSuccessfulExportAsync(exportedAt);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(exportedAt, state.LastSuccessfulExportAt);
        Assert.Equal(Normalize(PortableLogbookJson.Serialize(document)), Normalize(PortableLogbookJson.Serialize(state.Document)));
    }

    [Fact]
    public async Task RecordSuccessfulExportAsyncTimestampPreservesValidCheckpointAndUpdatesTimestamp()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);
        var exportedAt = checkpoint.ExportedAt.AddHours(1);
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [], checkpoint.ExportedAt, checkpoint));

        await store.RecordSuccessfulExportAsync(exportedAt);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(exportedAt, state.LastSuccessfulExportAt);
        Assert.Equal(checkpoint, state.LastSuccessfulExport);
    }

    [Fact]
    public async Task RecordSuccessfulExportAsyncPersistsPackageCheckpoint()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var exportedAt = DateTimeOffset.Parse("2026-07-19T03:00:00Z");
        var export = new MobilePackageExportWorkflowResult(
            "log_fixture_20260719_030000.elogbook",
            BrowserFileStore.ElogbookContentType,
            exportedAt,
            new string('a', 64),
            [1, 2, 3],
            new BrowserFileTransferResult(
                "log_fixture_20260719_030000.elogbook",
                null,
                null,
                false));
        var store = new BrowserLogbookStore(new MemoryJsRuntime());
        await store.SaveStateAsync(new BrowserLogbookState(document, [], null));

        await store.RecordSuccessfulExportAsync(export);

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        Assert.Equal(exportedAt, state.LastSuccessfulExportAt);
        Assert.NotNull(state.LastSuccessfulExport);
        Assert.True(state.LastSuccessfulExport.Covers(document));
        Assert.Equal(export.PackageSha256, state.LastSuccessfulExport.PackageSha256);
    }

    [Fact]
    public void ExportCheckpointRejectsMismatchedOrMalformedBackupProof()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var checkpoint = CreateCheckpoint(document);

        Assert.False((checkpoint with { LogbookId = new LogbookId("log_other") }).Covers(document));
        Assert.False((checkpoint with { OperationCount = document.Operations.Count - 1 }).Covers(document));
        Assert.False((checkpoint with { LatestOperationCreatedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z") }).Covers(document));
        Assert.False((checkpoint with { PackageSha256 = new string('g', 64) }).Covers(document));
        Assert.False((checkpoint with { PackageSha256 = new string('a', 63) }).Covers(document));
    }

    [Fact]
    public async Task LoadDocumentAsyncCanReadLegacyRawGoldenFixtureJson()
    {
        var store = new BrowserLogbookStore(new MemoryJsRuntime { StoredJson = ReadGoldenFixture() });

        var document = await store.LoadDocumentAsync();

        Assert.NotNull(document);
        AssertGoldenFixtureDocument(document);
    }

    [Fact]
    public async Task LoadStateAsyncCanReadLegacyRawGoldenFixtureJsonAsDocumentOnlyState()
    {
        var store = new BrowserLogbookStore(new MemoryJsRuntime { StoredJson = ReadGoldenFixture() });

        var state = await store.LoadStateAsync();

        Assert.NotNull(state);
        AssertGoldenFixtureDocument(state.Document);
        Assert.Empty(state.ImportReceipts);
        Assert.Null(state.LastSuccessfulExportAt);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsUnsupportedStoreVersionWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                2,
                PortableLogbookDocument.CurrentSchemaVersion,
                ReadGoldenFixture()),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("version 2", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsMalformedStoredJsonWithoutOverwritingState()
    {
        const string originalJson = "{ not valid json";
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsMalformedEnvelopeDocumentJsonWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                PortableLogbookDocument.CurrentSchemaVersion,
                "{ not valid document json"),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task LoadDocumentAsyncRejectsSchemaEnvelopeMismatchWithoutOverwritingState()
    {
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                0,
                ReadGoldenFixture()),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.LoadDocumentAsync());

        Assert.Contains("schema metadata does not match", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task SaveDocumentAsyncRejectsUnsupportedSchemaWithoutOverwritingState()
    {
        var originalJson = "existing-browser-state";
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.SaveDocumentAsync(document with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1 }));

        Assert.Contains("cannot be saved", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
        Assert.False(jsRuntime.SaveWasCalled);
    }

    [Fact]
    public async Task SaveStateAsyncRejectsSchemaUpgradeWithoutValidBackupCheckpoint()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var oldDocument = document with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion - 1 };
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                oldDocument.SchemaVersion,
                PortableLogbookJson.Serialize(oldDocument),
                [],
                null),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.SaveStateAsync(new BrowserLogbookState(document, [], null)));

        Assert.Contains("valid backup package", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
    }

    [Fact]
    public async Task SaveStateAsyncRejectsSchemaUpgradeWithStaleBackupCheckpoint()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var oldDocument = document with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion - 1 };
        var staleCheckpoint = CreateCheckpoint(oldDocument) with { OperationCount = oldDocument.Operations.Count - 1 };
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                oldDocument.SchemaVersion,
                PortableLogbookJson.Serialize(oldDocument),
                [],
                staleCheckpoint.ExportedAt,
                staleCheckpoint),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserLogbookStoreException>(async () =>
            await store.SaveStateAsync(new BrowserLogbookState(document, [], null)));

        Assert.Contains("valid backup package", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalJson, jsRuntime.StoredJson);
    }

    [Fact]
    public async Task SaveStateAsyncAllowsSchemaUpgradeAfterValidBackupCheckpoint()
    {
        var document = PortableLogbookJson.Deserialize(ReadGoldenFixture())
            ?? throw new InvalidOperationException("Golden fixture did not deserialize.");
        var oldDocument = document with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion - 1 };
        var checkpoint = CreateCheckpoint(oldDocument);
        var originalJson = JsonSerializer.Serialize(
            new BrowserLogbookStoredDocument(
                1,
                oldDocument.SchemaVersion,
                PortableLogbookJson.Serialize(oldDocument),
                [],
                checkpoint.ExportedAt,
                checkpoint),
            PortableLogbookJson.SerializerOptions);
        var jsRuntime = new MemoryJsRuntime { StoredJson = originalJson };
        var store = new BrowserLogbookStore(jsRuntime);

        await store.SaveStateAsync(new BrowserLogbookState(document, [], checkpoint.ExportedAt, checkpoint));

        Assert.True(jsRuntime.SaveWasCalled);
        var state = await store.LoadStateAsync();
        Assert.NotNull(state);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, state.Document.SchemaVersion);
    }

    private static void AssertGoldenFixtureDocument(PortableLogbookDocument? document)
    {
        Assert.NotNull(document);
        Assert.Equal(new LogbookId("log_fixture"), document.LogbookId);
        Assert.True(PortableLogbookValidator.Validate(document).IsValid);

        var operation = Assert.IsType<CreateEntryOperation>(Assert.Single(document.Operations));
        Assert.Equal(new EntryId("ent_fixture"), operation.EntryId);
        Assert.Equal(new RevisionId("rev_create"), operation.RevisionId);
        Assert.Equal("VH-ABC", operation.Entry.Registration);
        Assert.Equal("YSBK", operation.Entry.From);
        Assert.Equal("YSCN", operation.Entry.To);
        Assert.Equal("Training", operation.Entry.CustomFields[new CustomFieldId("cf_training_kind")]);
    }

    private static string ReadGoldenFixture() =>
        File.ReadAllText(Path.Combine("Fixtures", "portable-logbook-v1.json"));

    private static BrowserLogbookExportCheckpoint CreateCheckpoint(PortableLogbookDocument document) =>
        new(
            DateTimeOffset.Parse("2026-07-19T03:00:00Z"),
            document.SchemaVersion,
            document.LogbookId,
            document.Operations.Count,
            document.Operations.Max(operation => operation.CreatedAt),
            new string('a', 64));

    private static BrowserLogbookExportCheckpoint CreateCheckpoint(PortableLogbookDocumentV2 document) =>
        new(
            DateTimeOffset.Parse("2026-07-24T03:00:00Z"),
            document.SchemaVersion,
            document.LogbookId,
            document.Operations.Count,
            document.Operations.Max(operation => operation.CreatedAt),
            new string('b', 64));

    private static PortableLogbookDocumentV2 CreateV2Document()
    {
        var logbookId = new LogbookId("log_v2");
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "DA40",
            Reg = "VH-VTQ",
            FlightId = "ELB24",
            Pic = "A Delta",
            From = "YSBK",
            To = "YSCN",
            Via = "BK CN",
            Remarks = "Workbook-faithful mobile reload",
            FlightReview = true,
            CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Training" },
            SeCommandDay = 1.2m,
            LandingsDay = 1,
            Ils = 2
        };
        var operation = PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId("ent_v2"),
            new RevisionId("rev_v2_create"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            entry);

        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(customFieldId, "Custom 1", 1)],
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 7, 1), null, null),
            [operation]);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed class MemoryJsRuntime : IJSRuntime
    {
        public string? StoredJson { get; set; }

        public bool SaveWasCalled { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => new ValueTask<TValue>((TValue)(object?)StoredJson!),
                "electronicLogbookStore.save" => Save<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            Assert.Equal("portable-document", Assert.IsType<string>(args[0]));
            SaveWasCalled = true;
            StoredJson = Assert.IsType<string>(args[1]);
            return new ValueTask<TValue>(default(TValue)!);
        }
    }
}
