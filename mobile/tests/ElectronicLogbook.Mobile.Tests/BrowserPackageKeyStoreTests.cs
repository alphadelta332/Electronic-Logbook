using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class BrowserPackageKeyStoreTests
{
    [Fact]
    public async Task IsSupportedAsyncCallsBrowserKeyBridge()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserPackageKeyStore(jsRuntime);

        Assert.True(await store.IsSupportedAsync());

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.isSupported", call.Identifier);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public async Task HasAndEnsurePackageKeyUseLogbookScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(false);
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserPackageKeyStore(jsRuntime);
        var logbookId = new LogbookId("log_mobile");

        Assert.False(await store.HasPackageKeyAsync(logbookId));
        Assert.True(await store.EnsurePackageKeyAsync(logbookId));

        Assert.Equal(
            ["electronicLogbookKeys.hasPackageKey", "electronicLogbookKeys.ensurePackageKey"],
            jsRuntime.Calls.Select(call => call.Identifier));
        Assert.All(
            jsRuntime.Calls,
            call => Assert.Equal("package-key:log_mobile", Assert.Single(call.Arguments)));
    }

    [Fact]
    public async Task DeletePackageKeyUsesLogbookScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserPackageKeyStore(jsRuntime);

        await store.DeletePackageKeyAsync(new LogbookId("log_mobile"));

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.deletePackageKey", call.Identifier);
        Assert.Equal("package-key:log_mobile", Assert.Single(call.Arguments));
    }

    [Fact]
    public async Task ImportRecoveryCodeValidatesCodeAndStoresScopedNonExtractableBrowserKey()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserPackageKeyStore(jsRuntime);
        var key = PortableLogbookKey.Generate();
        var groupedRecoveryCode = string.Join(" ", key.ToRecoveryCode().Chunk(4).Select(chunk => new string(chunk)));

        Assert.True(await store.ImportRecoveryCodeAsync(new LogbookId("log_mobile"), $"Recovery code: {groupedRecoveryCode}"));

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.importPackageKey", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Equal(key.ToBytes(), Assert.IsType<byte[]>(call.Arguments[1]));
    }

    [Fact]
    public async Task ImportRecoveryCodeRejectsInvalidCodeBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserPackageKeyStore(jsRuntime);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ImportRecoveryCodeAsync(new LogbookId("log_mobile"), "not-a-recovery-code"));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task EncryptAsyncCallsBrowserCryptoBridgeWithScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        var encrypted = new BrowserPackageCiphertext([4, 5, 6], [7, 8, 9]);
        jsRuntime.Results.Enqueue(encrypted);
        var store = new BrowserPackageKeyStore(jsRuntime);
        var nonce = new byte[12];
        var plaintext = new byte[] { 1, 2, 3 };
        var additionalData = new byte[] { 9, 8, 7 };

        var result = await store.EncryptAsync(new LogbookId("log_mobile"), nonce, plaintext, additionalData);

        Assert.Same(encrypted, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.encrypt", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Same(nonce, call.Arguments[1]);
        Assert.Same(plaintext, call.Arguments[2]);
        Assert.Same(additionalData, call.Arguments[3]);
    }

    [Fact]
    public async Task DecryptAsyncCallsBrowserCryptoBridgeWithScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        var plaintext = new byte[] { 1, 2, 3 };
        jsRuntime.Results.Enqueue(plaintext);
        var store = new BrowserPackageKeyStore(jsRuntime);
        var nonce = new byte[12];
        var ciphertext = new byte[] { 4, 5, 6 };
        var tag = new byte[16];
        var additionalData = new byte[] { 9, 8, 7 };

        var result = await store.DecryptAsync(new LogbookId("log_mobile"), nonce, ciphertext, tag, additionalData);

        Assert.Same(plaintext, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.decrypt", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Same(nonce, call.Arguments[1]);
        Assert.Same(ciphertext, call.Arguments[2]);
        Assert.Same(tag, call.Arguments[3]);
        Assert.Same(additionalData, call.Arguments[4]);
    }

    [Fact]
    public async Task PackageKeyOperationsRejectBlankLogbookIdsBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserPackageKeyStore(jsRuntime);
        var logbookId = new LogbookId(" ");

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.HasPackageKeyAsync(logbookId));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.EnsurePackageKeyAsync(logbookId));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DeletePackageKeyAsync(logbookId));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ImportRecoveryCodeAsync(logbookId, PortableLogbookKey.Generate().ToRecoveryCode()));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.EncryptAsync(logbookId, new byte[12], [], []));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DecryptAsync(logbookId, new byte[12], [], new byte[16], []));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task CryptoOperationsRejectInvalidArgumentsBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserPackageKeyStore(jsRuntime);
        var logbookId = new LogbookId("log_mobile");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.EncryptAsync(logbookId, null!, [], []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.EncryptAsync(logbookId, new byte[12], null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.EncryptAsync(logbookId, new byte[12], [], null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.EncryptAsync(logbookId, new byte[11], [], []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DecryptAsync(logbookId, null!, [], new byte[16], []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DecryptAsync(logbookId, new byte[12], null!, new byte[16], []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DecryptAsync(logbookId, new byte[12], [], null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DecryptAsync(logbookId, new byte[12], [], new byte[16], null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DecryptAsync(logbookId, new byte[12], [], new byte[15], []));

        Assert.Empty(jsRuntime.Calls);
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
