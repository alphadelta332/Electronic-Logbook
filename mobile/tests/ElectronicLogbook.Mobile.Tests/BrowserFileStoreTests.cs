using ElectronicLogbook.Mobile;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class BrowserFileStoreTests
{
    [Fact]
    public async Task DownloadAsyncCallsBrowserFileBridge()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        await store.DownloadAsync("logbook.elogbook", bytes, "application/vnd.electronic-logbook");

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.download", call.Identifier);
        Assert.Equal("logbook.elogbook", call.Arguments[0]);
        Assert.Same(bytes, call.Arguments[1]);
        Assert.Equal("application/vnd.electronic-logbook", call.Arguments[2]);
    }

    [Fact]
    public async Task DownloadJsonAsyncCallsBrowserFileBridgeWithoutPackageExtension()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);

        await store.DownloadJsonAsync("summary.json", "{\"operationCount\":1}");

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.download", call.Identifier);
        Assert.Equal("summary.json", call.Arguments[0]);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("{\"operationCount\":1}"), Assert.IsType<byte[]>(call.Arguments[1]));
        Assert.Equal(BrowserFileStore.JsonContentType, call.Arguments[2]);
    }

    [Fact]
    public async Task ShareJsonOrDownloadAsyncUsesNativeTransferWhenAvailable()
    {
        var nativeResult = new BrowserFileTransferResult(
            "summary.json",
            "/storage/emulated/0/Android/data/com.alphadelta.electroniclogbook.dev/files/exports/summary.json",
            "/sdcard/Android/data/com.alphadelta.electroniclogbook.dev/files/exports/summary.json",
            true);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(nativeResult);
        var store = new BrowserFileStore(jsRuntime);

        var result = await store.ShareJsonOrDownloadAsync("summary.json", "{\"operationCount\":1}");

        Assert.Equal(nativeResult, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.nativeShareOrDownload", call.Identifier);
        Assert.Equal("summary.json", call.Arguments[0]);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("{\"operationCount\":1}"), Assert.IsType<byte[]>(call.Arguments[1]));
        Assert.Equal(BrowserFileStore.JsonContentType, call.Arguments[2]);
    }

    [Fact]
    public async Task CanShareAsyncCallsBrowserShareCapabilityBridge()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        var canShare = await store.CanShareAsync("logbook.elogbook", bytes);

        Assert.True(canShare);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.canShare", call.Identifier);
        Assert.Equal("logbook.elogbook", call.Arguments[0]);
        Assert.Same(bytes, call.Arguments[1]);
        Assert.Equal(BrowserFileStore.ElogbookContentType, call.Arguments[2]);
    }

    [Fact]
    public async Task ShareAsyncCallsBrowserShareBridge()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        await store.ShareAsync("logbook.elogbook", bytes);

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.share", call.Identifier);
        Assert.Equal("logbook.elogbook", call.Arguments[0]);
        Assert.Same(bytes, call.Arguments[1]);
        Assert.Equal(BrowserFileStore.ElogbookContentType, call.Arguments[2]);
    }

    [Fact]
    public async Task ShareOrDownloadAsyncUsesWebShareWhenAvailable()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(null);
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        var result = await store.ShareOrDownloadAsync("logbook.elogbook", bytes);

        Assert.True(result.Shared);
        Assert.Equal(3, jsRuntime.Calls.Count);
        Assert.Equal("electronicLogbookFiles.nativeShareOrDownload", jsRuntime.Calls[0].Identifier);
        Assert.Equal("electronicLogbookFiles.canShare", jsRuntime.Calls[1].Identifier);
        Assert.Equal("electronicLogbookFiles.share", jsRuntime.Calls[2].Identifier);
        Assert.Same(bytes, jsRuntime.Calls[2].Arguments[1]);
    }

    [Fact]
    public async Task ShareOrDownloadAsyncFallsBackToDownloadWhenWebShareIsUnavailable()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(null);
        jsRuntime.Results.Enqueue(false);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        var result = await store.ShareOrDownloadAsync("logbook.elogbook", bytes);

        Assert.False(result.Shared);
        Assert.Equal(3, jsRuntime.Calls.Count);
        Assert.Equal("electronicLogbookFiles.nativeShareOrDownload", jsRuntime.Calls[0].Identifier);
        Assert.Equal("electronicLogbookFiles.canShare", jsRuntime.Calls[1].Identifier);
        Assert.Equal("electronicLogbookFiles.download", jsRuntime.Calls[2].Identifier);
        Assert.Same(bytes, jsRuntime.Calls[2].Arguments[1]);
    }

    [Fact]
    public async Task ShareOrDownloadAsyncUsesNativeTransferWhenAvailable()
    {
        var nativeResult = new BrowserFileTransferResult(
            "logbook.elogbook",
            "/storage/emulated/0/Android/data/com.alphadelta.electroniclogbook/files/exports/logbook.elogbook",
            "/sdcard/Android/data/com.alphadelta.electroniclogbook/files/exports/logbook.elogbook",
            true);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(nativeResult);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        var result = await store.ShareOrDownloadAsync("logbook.elogbook", bytes);

        Assert.Equal(nativeResult, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.nativeShareOrDownload", call.Identifier);
        Assert.Equal("logbook.elogbook", call.Arguments[0]);
        Assert.Same(bytes, call.Arguments[1]);
    }

    [Fact]
    public async Task PickAsyncCallsBrowserFileBridgeWithElogbookAcceptFilter()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, [1, 2, 3]));
        var store = new BrowserFileStore(jsRuntime);

        var file = await store.PickAsync();

        Assert.NotNull(file);
        Assert.Equal("backup.elogbook", file.FileName);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.pick", call.Identifier);
        Assert.Equal(".elogbook", Assert.Single(call.Arguments));
    }

    [Fact]
    public async Task PickElogbookAsyncReturnsNullWhenSelectionIsCancelled()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(null);
        var store = new BrowserFileStore(jsRuntime);

        var file = await store.PickElogbookAsync();

        Assert.Null(file);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookFiles.pick", call.Identifier);
        Assert.Equal(BrowserFileStore.ElogbookExtension, Assert.Single(call.Arguments));
    }

    [Fact]
    public async Task PickElogbookAsyncValidatesSelectedFile()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(new BrowserFile("backup.zip", "application/zip", []));
        var store = new BrowserFileStore(jsRuntime);

        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.PickElogbookAsync());

        Assert.Single(jsRuntime.Calls);
    }

    [Fact]
    public async Task PickElogbookAsyncRejectsEmptySelectedFile()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, []));
        var store = new BrowserFileStore(jsRuntime);

        var error = await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.PickElogbookAsync());

        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
        Assert.Single(jsRuntime.Calls);
    }

    [Fact]
    public async Task DownloadAsyncRejectsInvalidArgumentsBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DownloadAsync(" ", []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DownloadAsync("logbook.elogbook", null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DownloadAsync("logbook.elogbook", [], " "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.CanShareAsync(" ", []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.CanShareAsync("logbook.elogbook", null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.CanShareAsync("logbook.elogbook", [], " "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ShareAsync(" ", []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ShareAsync("logbook.elogbook", null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ShareAsync("logbook.elogbook", [], " "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ShareOrDownloadAsync(" ", []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ShareOrDownloadAsync("logbook.elogbook", null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ShareOrDownloadAsync("logbook.elogbook", [], " "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DownloadJsonAsync(" ", "{}"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DownloadJsonAsync("summary.json", (string)null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ShareJsonOrDownloadAsync(" ", "{}"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ShareJsonOrDownloadAsync("summary.json", (string)null!));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ExportHelpersRejectWrongExtensionAndOversizedPackagesBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);
        var oversized = new byte[BrowserFileStore.MaxElogbookBytes + 1];

        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.CanShareAsync("logbook.zip", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadAsync("logbook.zip", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareAsync("logbook.zip", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareOrDownloadAsync("logbook.zip", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.CanShareAsync("logbook.elogbook", oversized));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadAsync("logbook.elogbook", oversized));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareAsync("logbook.elogbook", oversized));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareOrDownloadAsync("logbook.elogbook", oversized));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task ExportHelpersRejectEmptyPackagesBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);

        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.CanShareAsync("logbook.elogbook", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadAsync("logbook.elogbook", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareAsync("logbook.elogbook", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareOrDownloadAsync("logbook.elogbook", []));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task DownloadJsonAsyncRejectsWrongExtensionAndEmptyOrOversizedFilesBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserFileStore(jsRuntime);

        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadJsonAsync("summary.elogbook", "{}"));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadJsonAsync("summary.json", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.DownloadJsonAsync(
            "summary.json",
            new byte[BrowserFileStore.MaxJsonDownloadBytes + 1]));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareJsonOrDownloadAsync("summary.elogbook", "{}"));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareJsonOrDownloadAsync("summary.json", []));
        await Assert.ThrowsAsync<BrowserFileStoreException>(async () => await store.ShareJsonOrDownloadAsync(
            "summary.json",
            new byte[BrowserFileStore.MaxJsonDownloadBytes + 1]));

        Assert.Empty(jsRuntime.Calls);
    }

    [Theory]
    [InlineData("backup.elogbook", true)]
    [InlineData("BACKUP.ELOGBOOK", true)]
    [InlineData("backup.zip", false)]
    [InlineData("backup.elogbook.zip", false)]
    public void IsElogbookFileChecksExtensionCaseInsensitively(string fileName, bool expected)
    {
        var file = new BrowserFile(fileName, "application/octet-stream", []);

        Assert.Equal(expected, BrowserFileStore.IsElogbookFile(file));
    }

    [Fact]
    public void ValidateElogbookFileAcceptsPackageWithinSizeLimit()
    {
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, [1, 2, 3]);

        BrowserFileStore.ValidateElogbookFile(file);
    }

    [Fact]
    public void ValidateElogbookFileRejectsWrongExtension()
    {
        var file = new BrowserFile("backup.zip", "application/zip", []);

        var error = Assert.Throws<BrowserFileStoreException>(() => BrowserFileStore.ValidateElogbookFile(file));

        Assert.Contains(".elogbook", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateElogbookFileRejectsMissingBytes()
    {
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, null!);

        var error = Assert.Throws<BrowserFileStoreException>(() => BrowserFileStore.ValidateElogbookFile(file));

        Assert.Contains("package bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateElogbookFileRejectsEmptyPackage()
    {
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, []);

        var error = Assert.Throws<BrowserFileStoreException>(() => BrowserFileStore.ValidateElogbookFile(file));

        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateElogbookFileRejectsOversizedPackage()
    {
        var file = new BrowserFile(
            "backup.elogbook",
            BrowserFileStore.ElogbookContentType,
            new byte[BrowserFileStore.MaxElogbookBytes + 1]);

        var error = Assert.Throws<BrowserFileStoreException>(() => BrowserFileStore.ValidateElogbookFile(file));

        Assert.Contains("larger", error.Message, StringComparison.Ordinal);
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
