using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileDeviceStateExportWorkflowTests
{
    [Fact]
    public async Task ExportAsyncUsesNativeShareForCurrentStoredStateJson()
    {
        var jsRuntime = new RecordingJsRuntime();
        var expectedTransfer = new BrowserFileTransferResult(
            "electronic-logbook-device-state-log-mobile-test-20260724T030405Z.json",
            "/storage/emulated/0/Android/data/com.alphadelta.electroniclogbook.dev/files/exports/electronic-logbook-device-state-log-mobile-test-20260724T030405Z.json",
            "/sdcard/Android/data/com.alphadelta.electroniclogbook.dev/files/exports/electronic-logbook-device-state-log-mobile-test-20260724T030405Z.json",
            true);
        jsRuntime.Results.Enqueue(expectedTransfer);
        var fileStore = new BrowserFileStore(jsRuntime);
        var exportedAt = DateTimeOffset.Parse("2026-07-24T03:04:05Z");
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log:mobile/test"),
            [],
            [CreateOperation()]);
        var state = new BrowserLogbookState(document, [], null);

        var result = await MobileDeviceStateExportWorkflow.ExportAsync(state, fileStore, exportedAt);

        Assert.Equal("electronic-logbook-device-state-log-mobile-test-20260724T030405Z.json", result.FileName);
        Assert.Equal(expectedTransfer, result.Transfer);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.nativeShareOrDownload", call.Identifier);
        Assert.Equal(result.FileName, call.Arguments[0]);
        Assert.Equal(BrowserFileStore.JsonContentType, call.Arguments[2]);

        var bytes = Assert.IsType<byte[]>(call.Arguments[1]);
        var stored = JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(
            Encoding.UTF8.GetString(bytes),
            PortableLogbookJson.SerializerOptions);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.StoreVersion);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, stored.SchemaVersion);
        Assert.Equal(PortableLogbookJson.Serialize(document), stored.DocumentJson);
    }

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log:mobile/test"),
            new EntryId("entry_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 24),
                Registration = "VH-DEV",
                From = "YSSY",
                To = "YMML",
                PilotInCommand = 1.1m
            });

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
