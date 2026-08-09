using ElectronicLogbook.Portable;
using Microsoft.JSInterop;
using System.Security.Cryptography;
using System.Text;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserPackageKeyStore(IJSRuntime jsRuntime)
{
    private const int AesGcmNonceSizeBytes = 12;
    private const int AesGcmTagSizeBytes = 16;

    public ValueTask<bool> IsSupportedAsync() =>
        jsRuntime.InvokeAsync<bool>("electronicLogbookKeys.isSupported");

    public ValueTask<bool> HasPackageKeyAsync(LogbookId logbookId) =>
        jsRuntime.InvokeAsync<bool>("electronicLogbookKeys.hasPackageKey", KeyName(logbookId));

    public ValueTask<bool> EnsurePackageKeyAsync(LogbookId logbookId) =>
        jsRuntime.InvokeAsync<bool>("electronicLogbookKeys.ensurePackageKey", KeyName(logbookId));

    public ValueTask<bool> ImportRecoveryCodeAsync(LogbookId logbookId, string recoveryCode)
    {
        var keyName = KeyName(logbookId);
        var key = PortableLogbookKey.FromRecoveryCode(recoveryCode);
        return jsRuntime.InvokeAsync<bool>(
            "electronicLogbookKeys.importPackageKey",
            keyName,
            key.ToBytes());
    }

    public ValueTask DeletePackageKeyAsync(LogbookId logbookId) =>
        jsRuntime.InvokeVoidAsync("electronicLogbookKeys.deletePackageKey", KeyName(logbookId));

    public ValueTask<BrowserRecoveryPublicKey> GetRecoveryPublicKeyAsync() =>
        jsRuntime.InvokeAsync<BrowserRecoveryPublicKey>("electronicLogbookKeys.getRecoveryPublicKey");

    public ValueTask<BrowserRecoveryWrappedKey> WrapPackageKeyForRecoveryServiceAsync(
        LogbookId logbookId,
        string servicePublicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePublicKey);
        return jsRuntime.InvokeAsync<BrowserRecoveryWrappedKey>(
            "electronicLogbookKeys.wrapPackageKeyForRecoveryService",
            KeyName(logbookId),
            servicePublicKey);
    }

    public ValueTask<bool> ImportRecoveryEnvelopeAsync(LogbookId logbookId, string wrappedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedKey);
        return jsRuntime.InvokeAsync<bool>(
            "electronicLogbookKeys.importRecoveryEnvelope",
            KeyName(logbookId),
            wrappedKey);
    }

    public async ValueTask RunDisposableProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probeId = new LogbookId("log_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            var imported = await ImportRecoveryCodeAsync(probeId, PortableLogbookKey.Generate().ToRecoveryCode());
            if (!imported || !await HasPackageKeyAsync(probeId))
            {
                throw new MobileHostedDiagnosticException("ANDROID_KEYSTORE_IMPORT_FAILED", "The disposable Android Keystore key was not retained.");
            }

            await VerifyPackageKeyAsync(probeId, "ANDROID_KEYSTORE_ROUNDTRIP_MISMATCH", cancellationToken);
        }
        finally
        {
            await DeletePackageKeyAsync(probeId);
        }
    }

    public async ValueTask VerifyPackageKeyAsync(
        LogbookId logbookId,
        string errorCode = "PACKAGE_KEY_VERIFY_FAILED",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plaintext = Encoding.UTF8.GetBytes("electronic-logbook-keystore-probe");
        var additionalData = Encoding.UTF8.GetBytes("disposable-probe");
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceSizeBytes);
        var encrypted = await EncryptAsync(logbookId, nonce, plaintext, additionalData);
        var decrypted = await DecryptAsync(logbookId, nonce, encrypted.Ciphertext, encrypted.Tag, additionalData);
        if (!CryptographicOperations.FixedTimeEquals(plaintext, decrypted))
        {
            throw new MobileHostedDiagnosticException(errorCode, "The package-key encrypt/decrypt verification did not match.");
        }
    }

    public ValueTask<BrowserPackageCiphertext> EncryptAsync(
        LogbookId logbookId,
        byte[] nonce,
        byte[] plaintext,
        byte[] additionalData)
    {
        ValidateAesGcmArguments(nonce, plaintext, additionalData);
        return jsRuntime.InvokeAsync<BrowserPackageCiphertext>(
            "electronicLogbookKeys.encrypt",
            KeyName(logbookId),
            nonce,
            plaintext,
            additionalData);
    }

    public ValueTask<byte[]> DecryptAsync(
        LogbookId logbookId,
        byte[] nonce,
        byte[] ciphertext,
        byte[] tag,
        byte[] additionalData)
    {
        ValidateAesGcmArguments(nonce, ciphertext, additionalData);
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != AesGcmTagSizeBytes)
        {
            throw new ArgumentException($"AES-GCM tag must be {AesGcmTagSizeBytes} bytes.", nameof(tag));
        }

        return jsRuntime.InvokeAsync<byte[]>(
            "electronicLogbookKeys.decrypt",
            KeyName(logbookId),
            nonce,
            ciphertext,
            tag,
            additionalData);
    }

    private static string KeyName(LogbookId logbookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        return $"package-key:{logbookId.Value}";
    }

    private static void ValidateAesGcmArguments(byte[] nonce, byte[] payload, byte[] additionalData)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(additionalData);
        if (nonce.Length != AesGcmNonceSizeBytes)
        {
            throw new ArgumentException($"AES-GCM nonce must be {AesGcmNonceSizeBytes} bytes.", nameof(nonce));
        }
    }
}

public sealed record BrowserPackageCiphertext(
    byte[] Ciphertext,
    byte[] Tag);

public sealed record BrowserRecoveryPublicKey(
    string PublicKey,
    string Fingerprint,
    string Algorithm);

public sealed record BrowserRecoveryWrappedKey(
    string WrappedKey,
    string Algorithm);
