using System.Security.Cryptography;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageImportWorkflowTests
{
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

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
