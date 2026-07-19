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
        new(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry(pilotInCommand));

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
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = pilotInCommand
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

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
