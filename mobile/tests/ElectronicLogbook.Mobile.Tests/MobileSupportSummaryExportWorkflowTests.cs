using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileSupportSummaryExportWorkflowTests
{
    [Fact]
    public async Task ExportAsyncDownloadsRedactedSummaryJson()
    {
        var jsRuntime = new RecordingJsRuntime();
        var fileStore = new BrowserFileStore(jsRuntime);
        var exportedAt = DateTimeOffset.Parse("2026-07-18T00:10:00Z");
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log_support"),
            [],
            [CreateOperation()]);

        var result = await MobileSupportSummaryExportWorkflow.ExportAsync(document, fileStore, exportedAt);

        Assert.Equal("electronic-logbook-summary-log_support-20260718T001000Z.json", result.FileName);
        Assert.Equal(BrowserFileStore.JsonContentType, result.ContentType);
        Assert.Equal(exportedAt, result.ExportedAt);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(result.SummaryBytes)).ToLowerInvariant(), result.SummarySha256);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.download", call.Identifier);
        Assert.Equal(result.FileName, call.Arguments[0]);
        Assert.Same(result.SummaryBytes, call.Arguments[1]);
        Assert.Equal(BrowserFileStore.JsonContentType, call.Arguments[2]);

        using var json = JsonDocument.Parse(result.SummaryBytes);
        var root = json.RootElement;
        Assert.Equal("log_support", root.GetProperty("logbookId").GetString());
        Assert.Equal(1, root.GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("distinctDeviceCount").GetInt32());
        var text = Encoding.UTF8.GetString(result.SummaryBytes);
        Assert.DoesNotContain("VH-SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("YSBK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("YSCN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dev_mobile", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsyncSanitizesUnexpectedLogbookIdCharactersInFileName()
    {
        var jsRuntime = new RecordingJsRuntime();
        var fileStore = new BrowserFileStore(jsRuntime);
        var exportedAt = DateTimeOffset.Parse("2026-07-18T00:10:00Z");
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            new LogbookId("log:support/test"),
            [],
            [CreateOperation() with { LogbookId = new LogbookId("log:support/test") }]);

        var result = await MobileSupportSummaryExportWorkflow.ExportAsync(document, fileStore, exportedAt);

        Assert.Equal("electronic-logbook-summary-log_support_test-20260718T001000Z.json", result.FileName);
    }

    private static CreateEntryOperation CreateOperation() =>
        new(
            new LogbookId("log_support"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-SECRET",
                From = "YSBK",
                To = "YSCN",
                PilotInCommand = 1.2m
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
