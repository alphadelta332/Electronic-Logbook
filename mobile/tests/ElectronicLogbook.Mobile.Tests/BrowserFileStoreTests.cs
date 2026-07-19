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
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        await store.ShareOrDownloadAsync("logbook.elogbook", bytes);

        Assert.Equal(2, jsRuntime.Calls.Count);
        Assert.Equal("electronicLogbookFiles.canShare", jsRuntime.Calls[0].Identifier);
        Assert.Equal("electronicLogbookFiles.share", jsRuntime.Calls[1].Identifier);
        Assert.Same(bytes, jsRuntime.Calls[1].Arguments[1]);
    }

    [Fact]
    public async Task ShareOrDownloadAsyncFallsBackToDownloadWhenWebShareIsUnavailable()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(false);
        var store = new BrowserFileStore(jsRuntime);
        var bytes = new byte[] { 1, 2, 3 };

        await store.ShareOrDownloadAsync("logbook.elogbook", bytes);

        Assert.Equal(2, jsRuntime.Calls.Count);
        Assert.Equal("electronicLogbookFiles.canShare", jsRuntime.Calls[0].Identifier);
        Assert.Equal("electronicLogbookFiles.download", jsRuntime.Calls[1].Identifier);
        Assert.Same(bytes, jsRuntime.Calls[1].Arguments[1]);
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
