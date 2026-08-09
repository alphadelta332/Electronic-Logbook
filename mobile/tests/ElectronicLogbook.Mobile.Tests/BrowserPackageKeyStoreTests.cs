using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;
using System.Security.Cryptography;

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
    public async Task GetRecoveryPublicKeyCallsNativeRecoveryBridge()
    {
        var jsRuntime = new RecordingJsRuntime();
        var recoveryKey = new BrowserRecoveryPublicKey("public-key", "fingerprint", "RSA-OAEP-256");
        jsRuntime.Results.Enqueue(recoveryKey);
        var store = new BrowserPackageKeyStore(jsRuntime);

        var result = await store.GetRecoveryPublicKeyAsync();

        Assert.Same(recoveryKey, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.getRecoveryPublicKey", call.Identifier);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public async Task WrapPackageKeyForRecoveryServiceUsesScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        var wrappedKey = new BrowserRecoveryWrappedKey("wrapped-key", "RSA-OAEP-256");
        jsRuntime.Results.Enqueue(wrappedKey);
        var store = new BrowserPackageKeyStore(jsRuntime);

        var result = await store.WrapPackageKeyForRecoveryServiceAsync(
            new LogbookId("log_mobile"),
            "service-public-key");

        Assert.Same(wrappedKey, result);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.wrapPackageKeyForRecoveryService", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Equal("service-public-key", call.Arguments[1]);
    }

    [Fact]
    public async Task ImportRecoveryEnvelopeUsesScopedKeyName()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        var store = new BrowserPackageKeyStore(jsRuntime);

        Assert.True(await store.ImportRecoveryEnvelopeAsync(new LogbookId("log_mobile"), "wrapped-key"));

        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.importRecoveryEnvelope", call.Identifier);
        Assert.Equal("package-key:log_mobile", call.Arguments[0]);
        Assert.Equal("wrapped-key", call.Arguments[1]);
    }

    [Fact]
    public async Task EnrollmentUsesExistingScopedKeyAndDeviceWithoutGeneratingIdentifiers()
    {
        var serviceKey = PublicMaterial(1);
        var deviceKey = PublicMaterial(2);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        jsRuntime.Results.Enqueue(new BrowserRecoveryPublicKey(
            deviceKey.PublicKey,
            deviceKey.Fingerprint,
            "RSA-OAEP-256"));
        jsRuntime.Results.Enqueue(new BrowserRecoveryWrappedKey("wrapped-package-key", "RSA-OAEP-256"));
        var service = new RecordingRecoveryEnvelopeService(serviceKey.PublicKey, serviceKey.Fingerprint);
        var store = new BrowserPackageKeyStore(jsRuntime);
        var logbookId = new LogbookId("log_retained");
        var deviceId = new DeviceId("dev_retained");

        var result = await store.EnrollRecoveryEnvelopeAsync(logbookId, deviceId, service);

        Assert.True(result.Enrolled);
        Assert.Equal(
            [
                "electronicLogbookKeys.hasPackageKey",
                "electronicLogbookKeys.getRecoveryPublicKey",
                "electronicLogbookKeys.wrapPackageKeyForRecoveryService"
            ],
            jsRuntime.Calls.Select(call => call.Identifier));
        Assert.Equal("package-key:log_retained", jsRuntime.Calls[0].Arguments[0]);
        Assert.Equal("package-key:log_retained", jsRuntime.Calls[2].Arguments[0]);
        Assert.Equal(serviceKey.PublicKey, jsRuntime.Calls[2].Arguments[1]);
        var request = Assert.Single(service.EnrollmentRequests);
        Assert.Equal(logbookId, request.LogbookId);
        Assert.Equal(deviceId, request.DeviceId);
        Assert.Equal(deviceKey.PublicKey, request.DeviceKey.PublicKey);
        Assert.Equal("wrapped-package-key", request.WrappedPackageKey);
        Assert.Equal("managed-key-v1", request.IngressKeyVersionId);
    }

    [Fact]
    public async Task EnrollmentFailsClosedWhenLocalPackageKeyIsMissing()
    {
        var serviceKey = PublicMaterial(1);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(false);
        var service = new RecordingRecoveryEnvelopeService(serviceKey.PublicKey, serviceKey.Fingerprint);
        var store = new BrowserPackageKeyStore(jsRuntime);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await store.EnrollRecoveryEnvelopeAsync(
                new LogbookId("log_retained"),
                new DeviceId("dev_retained"),
                service));

        Assert.Equal("RECOVERY_PACKAGE_KEY_MISSING", error.ErrorCode);
        Assert.Empty(service.EnrollmentRequests);
        var call = Assert.Single(jsRuntime.Calls);
        Assert.Equal("electronicLogbookKeys.hasPackageKey", call.Identifier);
    }

    [Fact]
    public async Task EnrollmentFailsClosedWhenServicePublicMaterialIsInvalid()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        var service = new RecordingRecoveryEnvelopeService("not-base64", new string('a', 64));
        var store = new BrowserPackageKeyStore(jsRuntime);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await store.EnrollRecoveryEnvelopeAsync(
                new LogbookId("log_retained"),
                new DeviceId("dev_retained"),
                service));

        Assert.Equal("RECOVERY_SERVICE_KEY_INVALID", error.ErrorCode);
        Assert.Empty(service.EnrollmentRequests);
        Assert.Equal(["electronicLogbookKeys.hasPackageKey"], jsRuntime.Calls.Select(call => call.Identifier));
    }

    [Fact]
    public async Task EnrollmentFailsClosedWhenNativeRecoveryBridgeIsUnsupported()
    {
        var serviceKey = PublicMaterial(1);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        jsRuntime.Exceptions["electronicLogbookKeys.getRecoveryPublicKey"] =
            new JSException("Recovery bridge unavailable for this browser.");
        var service = new RecordingRecoveryEnvelopeService(serviceKey.PublicKey, serviceKey.Fingerprint);
        var store = new BrowserPackageKeyStore(jsRuntime);

        await Assert.ThrowsAsync<JSException>(async () =>
            await store.EnrollRecoveryEnvelopeAsync(
                new LogbookId("log_retained"),
                new DeviceId("dev_retained"),
                service));

        Assert.Empty(service.EnrollmentRequests);
        Assert.Equal(
            [
                "electronicLogbookKeys.hasPackageKey",
                "electronicLogbookKeys.getRecoveryPublicKey"
            ],
            jsRuntime.Calls.Select(call => call.Identifier));
    }

    [Theory]
    [InlineData(false, "managed-key-v1")]
    [InlineData(true, "wrong-key-version")]
    public async Task EnrollmentRejectsInvalidServiceEnrollmentResponses(bool enrolled, string keyVersionId)
    {
        var serviceKey = PublicMaterial(1);
        var deviceKey = PublicMaterial(2);
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Results.Enqueue(true);
        jsRuntime.Results.Enqueue(new BrowserRecoveryPublicKey(
            deviceKey.PublicKey,
            deviceKey.Fingerprint,
            "RSA-OAEP-256"));
        jsRuntime.Results.Enqueue(new BrowserRecoveryWrappedKey("wrapped-package-key", "RSA-OAEP-256"));
        var service = new RecordingRecoveryEnvelopeService(
            serviceKey.PublicKey,
            serviceKey.Fingerprint,
            enrolled,
            keyVersionId);
        var store = new BrowserPackageKeyStore(jsRuntime);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await store.EnrollRecoveryEnvelopeAsync(
                new LogbookId("log_retained"),
                new DeviceId("dev_retained"),
                service));

        Assert.Equal("RECOVERY_ENROLLMENT_INVALID", error.ErrorCode);
        Assert.Single(service.EnrollmentRequests);
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
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.WrapPackageKeyForRecoveryServiceAsync(logbookId, "service-public-key"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ImportRecoveryEnvelopeAsync(logbookId, "wrapped-key"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.EncryptAsync(logbookId, new byte[12], [], []));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DecryptAsync(logbookId, new byte[12], [], new byte[16], []));

        Assert.Empty(jsRuntime.Calls);
    }

    [Fact]
    public async Task RecoveryEnvelopeOperationsRejectBlankPayloadsBeforeCallingJavaScript()
    {
        var jsRuntime = new RecordingJsRuntime();
        var store = new BrowserPackageKeyStore(jsRuntime);
        var logbookId = new LogbookId("log_mobile");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.WrapPackageKeyForRecoveryServiceAsync(logbookId, " "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ImportRecoveryEnvelopeAsync(logbookId, " "));

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

        public Dictionary<string, Exception> Exceptions { get; } = [];

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
            if (Exceptions.TryGetValue(identifier, out var exception))
            {
                throw exception;
            }

            var result = Results.Count > 0 ? Results.Dequeue() : default;
            return new ValueTask<TValue>((TValue)result!);
        }
    }

    private static (string PublicKey, string Fingerprint) PublicMaterial(byte value)
    {
        var encoded = Enumerable.Repeat(value, 294).ToArray();
        return (
            Convert.ToBase64String(encoded),
            Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant());
    }

    private sealed class RecordingRecoveryEnvelopeService(
        string publicKey,
        string fingerprint,
        bool enrolled = true,
        string enrollmentKeyVersionId = "managed-key-v1")
        : IMobileRecoveryEnvelopeService
    {
        public List<MobileRecoveryEnvelopeEnrollmentRequest> EnrollmentRequests { get; } = [];

        public ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileRecoveryEnvelopeConfiguration(
                publicKey,
                fingerprint,
                "RSA-OAEP-256",
                "managed-key-v1"));

        public ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(
            MobileRecoveryEnvelopeEnrollmentRequest request,
            CancellationToken cancellationToken = default)
        {
            EnrollmentRequests.Add(request);
            return ValueTask.FromResult(new MobileRecoveryEnvelopeEnrollmentResult(enrolled, enrollmentKeyVersionId));
        }

        public ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(
            MobileRecoveryEnvelopeRestoreRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
