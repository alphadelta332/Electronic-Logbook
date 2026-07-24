using System.Security.Cryptography;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageImportWorkflowTests
{
    [Fact]
    public async Task ReadV2AsyncDecryptsWorkbookFaithfulPackageWithBrowserKey()
    {
        var local = CreateDocumentV2("rev_local");
        var incoming = CreateDocumentV2("rev_incoming");
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var decryptionPlan = PortableLogbookPackage.CreateDecryptionPlanV2(packageBytes, local.LogbookId);
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
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportWorkflow.ReadV2Async(local, file, keyStore);

        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, result.Manifest.SchemaVersion);
        Assert.Equal(incoming.Operations.Single().RevisionId, result.Document.Operations.Single().RevisionId);
        Assert.Equal(incoming.Operations.Count, result.ImportPlan.OperationCount);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
    }

    [Fact]
    public async Task ReadV2AsyncRejectsV1PackageBeforeDecrypting()
    {
        var local = CreateDocumentV2("rev_local");
        var v1PackageBytes = PortableLogbookPackage.Write(CreateDocument("rev_v1"), FixedKey());
        var jsRuntime = new RecordingJsRuntime();
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, v1PackageBytes);

        var error = await Assert.ThrowsAsync<MobilePackageImportWorkflowException>(async () =>
            await MobilePackageImportWorkflow.ReadV2Async(local, file, keyStore));

        Assert.Contains("UnsupportedSchema", error.Message, StringComparison.Ordinal);
        Assert.Contains("Re-import the authoritative workbook", error.Message, StringComparison.Ordinal);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ReadAsyncDecryptsPackageWithBrowserKeyWithoutMutatingLocalDocument()
    {
        var local = CreateDocument("rev_local");
        var incoming = CreateDocument("rev_incoming");
        var key = FixedKey();
        var packageBytes = PortableLogbookPackage.Write(incoming, key);
        var decryptionPlan = PortableLogbookPackage.CreateDecryptionPlan(packageBytes, local.LogbookId);
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
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var result = await MobilePackageImportWorkflow.ReadAsync(local, file, keyStore);

        Assert.Equal(incoming.Operations.Single().RevisionId, result.Document.Operations.Single().RevisionId);
        Assert.Equal(local.Operations.Single().RevisionId, local.Operations.Single().RevisionId);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Equal(incoming.Operations.Count, result.ImportPlan.OperationCount);
    }

    [Fact]
    public async Task ReadAsyncStopsBeforeDecryptionWhenManifestIsWrongLogbook()
    {
        var local = CreateDocument("rev_local");
        var incoming = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_other"), [], []);
        var packageBytes = PortableLogbookPackage.Write(incoming, FixedKey());
        var jsRuntime = new RecordingJsRuntime();
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var error = await Assert.ThrowsAsync<MobilePackageImportWorkflowException>(async () =>
            await MobilePackageImportWorkflow.ReadAsync(local, file, keyStore));

        Assert.Contains("WrongLogbook", error.Message, StringComparison.Ordinal);
        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ReadAsyncExplainsBrowserKeyMismatchWhenDecryptFails()
    {
        var local = CreateDocument("rev_local");
        var incoming = CreateDocument("rev_incoming");
        var packageBytes = PortableLogbookPackage.Write(incoming, FixedKey());
        var jsRuntime = new ThrowingJsRuntime(
            "OperationError at Object.decrypt (https://localhost/js/logbookStore.js:165:55)");
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var error = await Assert.ThrowsAsync<MobilePackageImportWorkflowException>(async () =>
            await MobilePackageImportWorkflow.ReadAsync(local, file, keyStore));

        Assert.Contains("Package could not be decrypted with the browser key stored on this device.", error.Message, StringComparison.Ordinal);
        Assert.Contains("restore the workbook recovery code", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("logbookStore.js", error.Message, StringComparison.Ordinal);
        Assert.IsType<JSException>(error.InnerException);
        Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", jsRuntime.Calls[0].Identifier);
    }

    private static PortableLogbookDocument CreateDocument(string revisionId)
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSCN",
                PilotInCommand = 1.2m
            });
        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }

    private static PortableLogbookDocumentV2 CreateDocumentV2(string revisionId)
    {
        var create = PortableLogbookOperationV2.Create(
            new LogbookId("log_mobile"),
            new EntryId("ent_00000000000000000000000000000001"),
            new RevisionId(revisionId),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026,
                Month = 7,
                Day = 18,
                Type = "C172",
                Reg = "VH-ABC",
                From = "YSBK",
                To = "YSCN",
                Pic = "Pilot",
                SeCommandDay = 1.2m
            });
        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            create.LogbookId,
            [],
            PortableLogbookCurrencyOverrideDates.Empty,
            [create]);
    }

    private static byte[] FixedKey()
    {
        var key = new byte[PortableLogbookPackage.KeySizeBytes];
        Array.Fill<byte>(key, 7);
        return key;
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
