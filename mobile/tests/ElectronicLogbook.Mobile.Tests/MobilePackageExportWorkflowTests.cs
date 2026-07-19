using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

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
        jsRuntime.Results.Enqueue(false);
        var keyStore = new BrowserPackageKeyStore(jsRuntime);
        var fileStore = new BrowserFileStore(jsRuntime);

        var result = await MobilePackageExportWorkflow.ExportAsync(document, keyStore, fileStore, exportedAt);

        Assert.Equal("log_mobile_20260719_040506.elogbook", result.FileName);
        Assert.Equal(BrowserFileStore.ElogbookContentType, result.ContentType);
        Assert.Equal(exportedAt, result.ExportedAt);
        Assert.Equal(
            [
                "electronicLogbookKeys.hasPackageKey",
                "electronicLogbookKeys.encrypt",
                "electronicLogbookFiles.canShare",
                "electronicLogbookFiles.download"
            ],
            jsRuntime.Calls.Select(call => call.Identifier));
        Assert.Equal("package-key:log_mobile", jsRuntime.Calls[1].Arguments[0]);
        Assert.Same(result.PackageBytes, jsRuntime.Calls[3].Arguments[1]);
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
}
