using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileDeviceStateExportWorkflowTests
{
    [Fact]
    public async Task ExportAsyncDownloadsCurrentStoredStateJson()
    {
        var jsRuntime = new RecordingJsRuntime();
        var fileStore = new BrowserFileStore(jsRuntime);
        var exportedAt = DateTimeOffset.Parse("2026-07-24T03:04:05Z");
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log:mobile/test"),
            [],
            [CreateOperation()]);
        var state = new BrowserLogbookState(document, [], null);

        var result = await MobileDeviceStateExportWorkflow.ExportAsync(state, fileStore, exportedAt);

        Assert.Equal("electronic-logbook-device-state-log-mobile-test-20260724T030405Z.json", result.FileName);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.download", call.Identifier);
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
            return new ValueTask<TValue>(default(TValue)!);
        }
    }

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
