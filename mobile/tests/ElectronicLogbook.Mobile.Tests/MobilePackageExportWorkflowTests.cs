using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;
using System.Security.Cryptography;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageExportWorkflowTests
{
    [Fact]
    public async Task ExportAsyncEncryptsPackageWithBrowserKeyAndFallsBackToDownload()
    {
        var document = CreateDocument();
        var exportedAt = DateTimeOffset.Parse("2026-07-19T04:05:06Z");
        var encryptionPlan = PortableLogbookPackage.CreateEncryptionPlan(document, exportedAt);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        jsRuntime.Results.Enqueue(new BrowserPackageCiphertext(new byte[encryptionPlan.CompressedPlaintext.Length], new byte[16]));
        jsRuntime.Results.Enqueue(null);
        jsRuntime.Results.Enqueue(false);
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var fileStore = new BrowserFileStore(jsRuntime);

        var result = await MobilePackageExportWorkflow.ExportAsync(document, keyStore, fileStore, exportedAt);

        Assert.Equal("log_mobile_20260719_040506.elogbook", result.FileName);
        Assert.Equal(BrowserFileStore.ElogbookContentType, result.ContentType);
        Assert.Equal(exportedAt, result.ExportedAt);
        Assert.Matches("^[0-9a-f]{64}$", result.PackageSha256);
        Assert.Equal(
            [
                "electronicLogbookKeys.hasPackageKey",
                "electronicLogbookKeys.encrypt",
                "electronicLogbookFiles.nativeShareOrDownload",
                "electronicLogbookFiles.canShare",
                "electronicLogbookFiles.download"
            ],
            jsRuntime.Calls.Select(call => call.Identifier));
        Assert.Equal("package-key:log_mobile", jsRuntime.Calls[1].Arguments[0]);
        Assert.Same(result.PackageBytes, jsRuntime.Calls[4].Arguments[1]);
        Assert.Null(result.Transfer.AdbPath);
    }

    [Fact]
    public async Task ExportAsyncStopsBeforeEncryptionWhenPackageKeyIsMissing()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(false);
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var fileStore = new BrowserFileStore(jsRuntime);

        var error = await Assert.ThrowsAsync<MobilePackageExportPlanException>(async () =>
            await MobilePackageExportWorkflow.ExportAsync(
                CreateDocument(),
                keyStore,
                fileStore,
                DateTimeOffset.Parse("2026-07-19T04:05:06Z")));

        Assert.Contains("package key", error.Message, StringComparison.OrdinalIgnoreCase);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.hasPackageKey", call.Identifier);
    }

    [Fact]
    public async Task ExportAsyncProducesDecryptablePortablePackage()
    {
        var document = CreateDocument();
        var exportedAt = DateTimeOffset.Parse("2026-07-19T04:05:06Z");
        var key = PortableLogbookKey.FromBytes(Enumerable.Range(1, PortableLogbookPackage.KeySizeBytes).Select(value => (byte)value).ToArray());
        var jsRuntime = new EncryptingJsRuntime(key);
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var fileStore = new BrowserFileStore(jsRuntime);

        var result = await MobilePackageExportWorkflow.ExportAsync(document, keyStore, fileStore, exportedAt);
        var read = PortableLogbookPackage.Read(result.PackageBytes, key, document.LogbookId);

        Assert.Equal(exportedAt, read.Manifest.CreatedAt);
        Assert.Equal(document.LogbookId, read.Document.LogbookId);
        Assert.Equal(document.Operations.Select(operation => operation.RevisionId), read.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(document.Operations.Count, read.Manifest.OperationCount);
        Assert.Equal(result.PackageBytes, Assert.Single(jsRuntime.DownloadedPackages));
    }

    [Fact]
    public async Task ExportAsyncProducesDecryptablePackageContainingOfflineCorrection()
    {
        var document = CreateDocument();
        var create = Assert.IsType<CreateEntryOperation>(Assert.Single(document.Operations));
        var correction = new CorrectEntryOperation(
            document.LogbookId,
            create.EntryId,
            new RevisionId("rev_mobile_exported_correction"),
            new HashSet<RevisionId> { create.RevisionId },
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-19T04:00:00Z"),
            create.Entry with
            {
                Registration = "VH-EXP",
                Details = "Offline correction exported"
            });
        var changedDocument = MobileLogbookDocument.AppendOperation(document, [], correction);
        var exportedAt = DateTimeOffset.Parse("2026-07-19T04:05:06Z");
        var key = PortableLogbookKey.FromBytes(Enumerable.Range(1, PortableLogbookPackage.KeySizeBytes).Select(value => (byte)value).ToArray());
        var jsRuntime = new EncryptingJsRuntime(key);
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var fileStore = new BrowserFileStore(jsRuntime);

        var result = await MobilePackageExportWorkflow.ExportAsync(changedDocument, keyStore, fileStore, exportedAt);
        var read = PortableLogbookPackage.Read(result.PackageBytes, key, changedDocument.LogbookId);
        var materialized = Assert.Single(PortableLogbookMerger.Merge(read.Document.Operations).Entries.Values);

        Assert.Equal(2, read.Manifest.OperationCount);
        Assert.Equal([create.RevisionId, correction.RevisionId], read.Document.Operations.Select(operation => operation.RevisionId));
        Assert.Equal(correction.RevisionId, materialized.CurrentRevisionId);
        Assert.Equal("VH-EXP", materialized.Entry?.Registration);
        Assert.Equal("Offline correction exported", materialized.Entry?.Details);
        Assert.Equal(result.PackageBytes, Assert.Single(jsRuntime.DownloadedPackages));
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_mobile"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
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

    private sealed class EncryptingJsRuntime(PortableLogbookKey key) : IJSRuntime
    {
        private readonly byte[] _keyBytes = key.ToBytes();

        public List<byte[]> DownloadedPackages { get; } = [];

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
            args ??= [];
            object? result = identifier switch
            {
                "electronicLogbookKeys.hasPackageKey" => true,
                "electronicLogbookKeys.encrypt" => Encrypt(
                    (byte[])args[1]!,
                    (byte[])args[2]!,
                    (byte[])args[3]!),
                "electronicLogbookFiles.nativeShareOrDownload" => null,
                "electronicLogbookFiles.canShare" => false,
                "electronicLogbookFiles.download" => CaptureDownload((byte[])args[1]!),
                _ => throw new InvalidOperationException($"Unexpected JS call: {identifier}")
            };

            return new ValueTask<TValue>((TValue)result!);
        }

        private BrowserPackageCiphertext Encrypt(byte[] nonce, byte[] plaintext, byte[] additionalData)
        {
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_keyBytes, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, additionalData);
            return new BrowserPackageCiphertext(ciphertext, tag);
        }

        private object? CaptureDownload(byte[] packageBytes)
        {
            DownloadedPackages.Add(packageBytes);
            return default;
        }
    }
}
