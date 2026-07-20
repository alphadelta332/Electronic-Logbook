using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageImportApplyWorkflowTests
{
    [Fact]
    public async Task ApplyIfReadyAsyncAppliesConflictFreePackageAndRecordsReceipt()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var incoming = CreateDocument("rev_local", 1.0m, CreateCorrection("rev_incoming", "rev_local", 1.4m));
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.Applied, result.Status);
        Assert.Equal(2, result.Document.Operations.Count);
        Assert.Equal(PortableLogbookImportPlanStatus.ReadyToApply, result.Plan?.Status);
        var receipt = Assert.Single(result.ImportReceipts);
        Assert.Equal(incoming.Operations.Count, receipt.OperationCount);
        Assert.Same(receipt, result.Receipt);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncRecordsDuplicateOperationsWithoutChangingDocument()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(local, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.DuplicateOperationsRecorded, result.Status);
        Assert.Same(local, result.Document);
        Assert.Equal(PortableLogbookImportPlanStatus.DuplicateOnly, result.Plan?.Status);
        Assert.Single(result.ImportReceipts);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncSkipsAlreadyRecordedPackageBeforeDecrypting()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(local, key);
        var manifest = PortableLogbookPackage.ReadManifest(packageBytes);
        var receipt = PortableLogbookImportLedger.CreateReceipt(
            packageBytes,
            manifest,
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));
        var jsRuntime = new RecordingJsRuntime();
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [receipt],
            DateTimeOffset.Parse("2026-07-19T00:02:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.PackageReplay, result.Status);
        Assert.Same(local, result.Document);
        Assert.Same(receipt, Assert.Single(result.ImportReceipts));
        Assert.Null(result.Plan);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncRejectsWrongLogbookBeforeDecryptingOrRecordingReceipt()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_other"), [], []);
        var packageBytes = PortableLogbookPackage.Write(incoming, FixedKey());
        var jsRuntime = new RecordingJsRuntime();
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var error = await Assert.ThrowsAsync<MobilePackageImportWorkflowException>(async () =>
            await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
                local,
                file,
                new BrowserPackageKeyStore(jsRuntime),
                [],
                DateTimeOffset.Parse("2026-07-19T00:01:00Z")));

        Assert.Contains("WrongLogbook", error.Message, StringComparison.Ordinal);
        Assert.Single(local.Operations);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncRejectsUnsupportedSchemaBeforeDecryptingOrRecordingReceipt()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var packageBytes = PortableLogbookPackage.Write(local, FixedKey());
        var manifest = PortableLogbookPackage.ReadManifest(packageBytes) with
        {
            SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1
        };
        var modified = ReplaceManifest(packageBytes, manifest);
        var jsRuntime = new RecordingJsRuntime();
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, modified);

        var error = await Assert.ThrowsAsync<MobilePackageImportWorkflowException>(async () =>
            await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
                local,
                file,
                new BrowserPackageKeyStore(jsRuntime),
                [],
                DateTimeOffset.Parse("2026-07-19T00:01:00Z")));

        Assert.Contains("UnsupportedSchema", error.Message, StringComparison.Ordinal);
        Assert.Single(local.Operations);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncRejectsInvalidFileBeforeDecryptingOrRecordingReceipt()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var jsRuntime = new RecordingJsRuntime();
        var file = new BrowserFile("backup.txt", BrowserFileStore.ElogbookContentType, [1, 2, 3]);

        await Assert.ThrowsAsync<BrowserFileStoreException>(async () =>
            await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
                local,
                file,
                new BrowserPackageKeyStore(jsRuntime),
                [],
                DateTimeOffset.Parse("2026-07-19T00:01:00Z")));

        Assert.Single(local.Operations);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncRejectsBrowserDecryptionFailureWithoutMutatingDocumentOrReceipts()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var incoming = CreateDocument("rev_local", 1.0m, CreateCorrection("rev_incoming", "rev_local", 1.4m));
        var packageBytes = PortableLogbookPackage.Write(incoming, FixedKey());
        var localPackageBytes = PortableLogbookPackage.Write(local, FixedKey());
        var existingReceipt = PortableLogbookImportLedger.CreateReceipt(
            localPackageBytes,
            PortableLogbookPackage.ReadManifest(localPackageBytes),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var jsRuntime = new ThrowingJsRuntime("Package key is not available.");
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var error = await Assert.ThrowsAsync<JSException>(async () =>
            await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
                local,
                file,
                new BrowserPackageKeyStore(jsRuntime),
                [existingReceipt],
                DateTimeOffset.Parse("2026-07-19T00:01:00Z")));

        Assert.Contains("Package key is not available", error.Message, StringComparison.Ordinal);
        Assert.Single(local.Operations);
        Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", jsRuntime.Calls[0].Identifier);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncImportsEntryConflictsForLocalResolution()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var incoming = CreateDocument("rev_local", 1.0m, CreateCorrection("rev_conflict", "rev_local", 1.6m));
        local = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [],
            local.Operations.Append(CreateCorrection("rev_existing", "rev_local", 1.2m)));
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.AppliedWithConflicts, result.Status);
        Assert.NotSame(local, result.Document);
        Assert.Equal(3, result.Document.Operations.Count);
        Assert.Single(result.ImportReceipts);
        Assert.Equal(PortableLogbookImportPlanStatus.RequiresConflictResolution, result.Plan?.Status);
        Assert.Single(PortableLogbookMerger.Merge(result.Document.Operations).Conflicts);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncReturnsResolutionRequiredForCustomFieldConflictWithoutMutatingLocalDocument()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_mobile"),
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.RequiresResolution, result.Status);
        Assert.Same(local, result.Document);
        Assert.Empty(result.ImportReceipts);
        Assert.Equal(PortableLogbookImportPlanStatus.RequiresCustomFieldResolution, result.Plan?.Status);
    }

    [Fact]
    public async Task ApplyWithCustomFieldResolutionsAsyncAppliesPackageWithLocalDefinition()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_mobile"),
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [CreateOperation("rev_local", 1.0m), CreateCorrection("rev_incoming", "rev_local", 1.4m)]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            [new PortableLogbookCustomFieldDefinitionResolution(fieldId, PortableLogbookCustomFieldDefinitionChoice.KeepLocal)],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.Applied, result.Status);
        Assert.Equal("Training kind", Assert.Single(result.Document.CustomFieldDefinitions).Label);
        Assert.Equal(2, result.Document.Operations.Count);
        Assert.Single(result.ImportReceipts);
    }

    [Fact]
    public async Task ApplyWithCustomFieldResolutionsAsyncAppliesPackageWithIncomingDefinition()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_mobile"),
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [CreateOperation("rev_local", 1.0m), CreateCorrection("rev_incoming", "rev_local", 1.4m)]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            [new PortableLogbookCustomFieldDefinitionResolution(fieldId, PortableLogbookCustomFieldDefinitionChoice.UseIncoming)],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.Applied, result.Status);
        Assert.Equal("Training category", Assert.Single(result.Document.CustomFieldDefinitions).Label);
        Assert.Equal(2, result.Document.Operations.Count);
        Assert.Single(result.ImportReceipts);
    }

    [Fact]
    public async Task ApplyWithCustomFieldResolutionsAsyncRejectsIncompleteResolutionWithoutMutatingDocumentOrReceipts()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_mobile"),
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [CreateOperation("rev_local", 1.0m), CreateCorrection("rev_incoming", "rev_local", 1.4m)]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);
        var localPackageBytes = PortableLogbookPackage.Write(local, key);
        var existingReceipt = PortableLogbookImportLedger.CreateReceipt(
            localPackageBytes,
            PortableLogbookPackage.ReadManifest(localPackageBytes),
            DateTimeOffset.Parse("2026-07-18T00:01:00Z"));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
                local,
                file,
                new BrowserPackageKeyStore(jsRuntime),
                [existingReceipt],
                [],
                DateTimeOffset.Parse("2026-07-19T00:01:00Z")));

        Assert.Contains(fieldId.Value, error.Message, StringComparison.Ordinal);
        Assert.Single(local.Operations);
        Assert.Equal("Training kind", Assert.Single(local.CustomFieldDefinitions).Label);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", call.Identifier);
    }

    [Fact]
    public async Task ApplyWithCustomFieldResolutionsAsyncReusesReadPackageWhenResolutionIsNotRequired()
    {
        var local = CreateDocument("rev_local", 1.0m);
        var incoming = CreateDocument("rev_local", 1.0m, CreateCorrection("rev_incoming", "rev_local", 1.4m));
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.Applied, result.Status);
        Assert.Equal(2, result.Document.Operations.Count);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", call.Identifier);
    }

    [Fact]
    public async Task ApplyWithCustomFieldResolutionsAsyncSkipsAlreadyRecordedPackageBeforeDecrypting()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_mobile"),
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [CreateOperation("rev_local", 1.0m)]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(
            local.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training category", 1)],
            [CreateOperation("rev_local", 1.0m), CreateCorrection("rev_incoming", "rev_local", 1.4m)]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var manifest = PortableLogbookPackage.ReadManifest(packageBytes);
        var receipt = PortableLogbookImportLedger.CreateReceipt(
            packageBytes,
            manifest,
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));
        var jsRuntime = new RecordingJsRuntime();
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyWithCustomFieldResolutionsAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [receipt],
            [new PortableLogbookCustomFieldDefinitionResolution(fieldId, PortableLogbookCustomFieldDefinitionChoice.KeepLocal)],
            DateTimeOffset.Parse("2026-07-19T00:02:00Z"));

        Assert.Equal(MobilePackageImportApplyStatus.PackageReplay, result.Status);
        Assert.Same(local, result.Document);
        Assert.Same(receipt, Assert.Single(result.ImportReceipts));
        Assert.Null(result.Plan);
        Assert.Null(result.Receipt);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ApplyIfReadyAsyncSupportsPwaPackageToPackageRoundTripWithoutFieldOrIdLoss()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var localCreate = CreateOperation("rev_local", 1.0m, fieldId, "Local");
        var incomingCorrection = new CorrectEntryOperation(
            localCreate.LogbookId,
            localCreate.EntryId,
            new RevisionId("rev_mobile_edit"),
            new HashSet<RevisionId> { localCreate.RevisionId },
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:05:00Z"),
            Entry(1.4m, fieldId, "Imported") with
            {
                Date = new DateOnly(2026, 7, 19),
                AircraftType = "DA42",
                Registration = "VH-XYZ",
                FlightNumber = "ELB42",
                From = "YSBK",
                To = "YMML",
                Route = "YSBK YSCN",
                Details = "Package round trip",
                MultiPilot = 0.3m,
                CoPilot = 0.2m,
                Dual = 0.4m,
                Instructor = 0.5m,
                Day = 1.2m,
                Night = 0.2m,
                InstrumentActual = 0.6m,
                InstrumentSimulated = 0.7m,
                TakeoffsDay = 1,
                TakeoffsNight = 2,
                LandingsDay = 1,
                LandingsNight = 2,
                IfrApproaches = 2,
                Holding = 1,
                Rnav = 1,
                Circling = 1
            });
        var definition = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var local = PortableLogbookDocument.CreateAustraliaFirst(localCreate.LogbookId, [definition], [localCreate]);
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(localCreate.LogbookId, [definition], [localCreate, incomingCorrection]);
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var jsRuntime = RuntimeWithDecryption(packageBytes, key, local.LogbookId);
        var file = new BrowserFile("workbook-export.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportApplyWorkflow.ApplyIfReadyAsync(
            local,
            file,
            new BrowserPackageKeyStore(jsRuntime),
            [],
            DateTimeOffset.Parse("2026-07-19T00:01:00Z"));
        var exportedPackage = PortableLogbookPackage.Write(result.Document, key);
        var exported = PortableLogbookPackage.Read(exportedPackage, key, local.LogbookId);
        var materialized = Assert.Single(PortableLogbookMerger.Merge(exported.Document.Operations).Entries.Values);

        Assert.Equal(MobilePackageImportApplyStatus.Applied, result.Status);
        Assert.Equal([localCreate.RevisionId, incomingCorrection.RevisionId], exported.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(incomingCorrection.EntryId, materialized.EntryId);
        Assert.Equal(incomingCorrection.RevisionId, materialized.CurrentRevisionId);
        Assert.Equal(new DateOnly(2026, 7, 19), materialized.Entry?.Date);
        Assert.Equal("DA42", materialized.Entry?.AircraftType);
        Assert.Equal("VH-XYZ", materialized.Entry?.Registration);
        Assert.Equal("ELB42", materialized.Entry?.FlightNumber);
        Assert.Equal("YSBK", materialized.Entry?.From);
        Assert.Equal("YMML", materialized.Entry?.To);
        Assert.Equal("YSBK YSCN", materialized.Entry?.Route);
        Assert.Equal("Package round trip", materialized.Entry?.Details);
        Assert.Equal(0.3m, materialized.Entry?.MultiPilot);
        Assert.Equal(1.4m, materialized.Entry?.PilotInCommand);
        Assert.Equal(0.2m, materialized.Entry?.CoPilot);
        Assert.Equal(0.4m, materialized.Entry?.Dual);
        Assert.Equal(0.5m, materialized.Entry?.Instructor);
        Assert.Equal(1.2m, materialized.Entry?.Day);
        Assert.Equal(0.2m, materialized.Entry?.Night);
        Assert.Equal(0.6m, materialized.Entry?.InstrumentActual);
        Assert.Equal(0.7m, materialized.Entry?.InstrumentSimulated);
        Assert.Equal(1, materialized.Entry?.TakeoffsDay);
        Assert.Equal(2, materialized.Entry?.TakeoffsNight);
        Assert.Equal(1, materialized.Entry?.LandingsDay);
        Assert.Equal(2, materialized.Entry?.LandingsNight);
        Assert.Equal(2, materialized.Entry?.IfrApproaches);
        Assert.Equal(1, materialized.Entry?.Holding);
        Assert.Equal(1, materialized.Entry?.Rnav);
        Assert.Equal(1, materialized.Entry?.Circling);
        Assert.Equal("Imported", materialized.Entry?.CustomFields[fieldId]);
        Assert.Equal("Training kind", Assert.Single(exported.Document.CustomFieldDefinitions).Label);
    }

    private static PortableLogbookDocument CreateDocument(
        string revisionId,
        decimal pilotInCommand,
        params PortableLogbookOperation[] additionalOperations)
    {
        var create = CreateOperation(revisionId, pilotInCommand);
        return PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [],
            new PortableLogbookOperation[] { create }.Concat(additionalOperations));
    }

    private static CreateEntryOperation CreateOperation(string revisionId, decimal pilotInCommand) =>
        CreateOperation(revisionId, pilotInCommand, null, null);

    private static CreateEntryOperation CreateOperation(
        string revisionId,
        decimal pilotInCommand,
        CustomFieldId? customFieldId,
        string? customFieldValue) =>
        new(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry(pilotInCommand, customFieldId, customFieldValue));

    private static CorrectEntryOperation CreateCorrection(
        string revisionId,
        string parentRevisionId,
        decimal pilotInCommand) =>
        new(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new HashSet<RevisionId> { new(parentRevisionId) },
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:05:00Z"),
            Entry(pilotInCommand));

    private static PortableLogbookEntry Entry(decimal pilotInCommand) =>
        Entry(pilotInCommand, null, null);

    private static PortableLogbookEntry Entry(
        decimal pilotInCommand,
        CustomFieldId? customFieldId,
        string? customFieldValue) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = pilotInCommand,
            CustomFields = customFieldId.HasValue
                ? new Dictionary<CustomFieldId, string?> { [customFieldId.Value] = customFieldValue }
                : new Dictionary<CustomFieldId, string?>()
        };

    private static RecordingJsRuntime RuntimeWithDecryption(
        byte[] packageBytes,
        byte[] key,
        LogbookId logbookId)
    {
        var decryptionPlan = PortableLogbookPackage.CreateDecryptionPlan(packageBytes, logbookId);
        var compressedPlaintext = new byte[decryptionPlan.Ciphertext.Length];
        using var aes = new AesGcm(key, decryptionPlan.Tag.Length);
        aes.Decrypt(
            decryptionPlan.Nonce,
            decryptionPlan.Ciphertext,
            decryptionPlan.Tag,
            compressedPlaintext,
            decryptionPlan.ManifestBytes);

        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(compressedPlaintext);
        return jsRuntime;
    }

    private static byte[] FixedKey()
    {
        var key = new byte[PortableLogbookPackage.KeySizeBytes];
        Array.Fill<byte>(key, 7);
        return key;
    }

    private static byte[] ReplaceManifest(
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest)
    {
        var newManifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        var originalManifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan("ELOGPKG1".Length, sizeof(int)));
        var remainderStart = "ELOGPKG1".Length + sizeof(int) + originalManifestLength;
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ELOGPKG1"));
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, newManifestBytes.Length);
        output.Write(manifestLength);
        output.Write(newManifestBytes);
        output.Write(packageBytes.AsSpan(remainderStart));
        return output.ToArray();
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public Queue<object?> Results { get; } = [];

        public List<JsCall> Calls { get; } = [];

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
            Calls.Add(new JsCall(identifier, args ?? []));
            var result = Results.Count > 0 ? Results.Dequeue() : default;
            return new ValueTask<TValue>((TValue)result!);
        }
    }

    private sealed class ThrowingJsRuntime(string message) : IJSRuntime
    {
        public List<JsCall> Calls { get; } = [];

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
            Calls.Add(new JsCall(identifier, args ?? []));
            throw new JSException(message);
        }
    }

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
