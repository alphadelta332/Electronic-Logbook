using ElectronicLogbook.Portable;
using Microsoft.JSInterop;
using System.Security.Cryptography;
using System.Text;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserPackageKeyStore(IJSRuntime jsRuntime)
{
    private const int AesGcmNonceSizeBytes = 12;
    private const int AesGcmTagSizeBytes = 16;
    private const string RecoveryKeyAlgorithm = "RSA-OAEP-256";

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

    public ValueTask<MobileRecoveryCodeEnvelopePayload> WrapPackageKeyForRecoveryCodeAsync(
        LogbookId logbookId,
        string recoveryCode) =>
        jsRuntime.InvokeAsync<MobileRecoveryCodeEnvelopePayload>(
            "electronicLogbookKeys.wrapPackageKeyForRecoveryCode",
            KeyName(logbookId),
            recoveryCode);

    public ValueTask<bool> TestRecoveryCodeEnvelopeAsync(
        LogbookId logbookId,
        string recoveryCode,
        MobileRecoveryCodeEnvelopePayload envelope) =>
        jsRuntime.InvokeAsync<bool>(
            "electronicLogbookKeys.testRecoveryCodeEnvelope",
            KeyName(logbookId),
            recoveryCode,
            envelope);

    public ValueTask<bool> ImportRecoveryCodeEnvelopeAsync(
        LogbookId logbookId,
        string recoveryCode,
        MobileRecoveryCodeEnvelopePayload envelope) =>
        jsRuntime.InvokeAsync<bool>(
            "electronicLogbookKeys.importRecoveryCodeEnvelope",
            KeyName(logbookId),
            recoveryCode,
            envelope);

    public async ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollRecoveryEnvelopeAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        IMobileRecoveryEnvelopeService recoveryService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryService);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasPackageKeyAsync(logbookId))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_PACKAGE_KEY_MISSING",
                "The local package key is unavailable for account recovery setup.");
        }

        var configuration = await recoveryService.GetConfigurationAsync(cancellationToken);
        ValidateRecoveryPublicKey(
            configuration.PublicKey,
            configuration.Fingerprint,
            configuration.Algorithm,
            "RECOVERY_SERVICE_KEY_INVALID");

        cancellationToken.ThrowIfCancellationRequested();
        var deviceKey = await GetRecoveryPublicKeyAsync();
        ValidateRecoveryPublicKey(
            deviceKey.PublicKey,
            deviceKey.Fingerprint,
            deviceKey.Algorithm,
            "RECOVERY_DEVICE_KEY_INVALID");

        cancellationToken.ThrowIfCancellationRequested();
        var wrappedPackageKey = await WrapPackageKeyForRecoveryServiceAsync(
            logbookId,
            configuration.PublicKey);
        if (!string.Equals(wrappedPackageKey.Algorithm, RecoveryKeyAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(wrappedPackageKey.WrappedKey))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_KEY_WRAP_INVALID",
                "The Android recovery bridge returned an invalid package-key envelope.");
        }

        var result = await recoveryService.EnrollAsync(
            new MobileRecoveryEnvelopeEnrollmentRequest(
                logbookId,
                deviceId,
                new MobileRecoveryDeviceKey(
                    deviceKey.PublicKey,
                    deviceKey.Fingerprint,
                    deviceKey.Algorithm),
                wrappedPackageKey.WrappedKey,
                configuration.KeyVersionId),
            cancellationToken);
        if (!result.Enrolled
            || !string.Equals(result.KeyVersionId, configuration.KeyVersionId, StringComparison.Ordinal))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_ENROLLMENT_INVALID",
                "Account recovery setup returned an invalid result.");
        }

        return result;
    }

    public async ValueTask RestoreRecoveryEnvelopeAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        string platformLabel,
        IMobileRecoveryEnvelopeService recoveryService,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformLabel);
        ArgumentNullException.ThrowIfNull(recoveryService);
        cancellationToken.ThrowIfCancellationRequested();

        var deviceKey = await GetRecoveryPublicKeyAsync();
        ValidateRecoveryPublicKey(
            deviceKey.PublicKey,
            deviceKey.Fingerprint,
            deviceKey.Algorithm,
            "RECOVERY_DEVICE_KEY_INVALID");
        var restored = await recoveryService.RestoreAsync(
            new MobileRecoveryEnvelopeRestoreRequest(
                logbookId,
                deviceId,
                new MobileRecoveryDeviceKey(
                    deviceKey.PublicKey,
                    deviceKey.Fingerprint,
                    deviceKey.Algorithm),
                platformLabel),
            cancellationToken);
        if (!string.Equals(restored.Algorithm, RecoveryKeyAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(restored.WrappedKey))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_ENVELOPE_INVALID",
                "Account recovery returned an invalid device envelope.");
        }

        if (!await ImportRecoveryEnvelopeAsync(logbookId, restored.WrappedKey)
            || !await HasPackageKeyAsync(logbookId))
        {
            throw new MobileHostedDiagnosticException(
                "RECOVERY_KEY_IMPORT_FAILED",
                "The recovered logbook could not be retained by Android Keystore.");
        }
        await VerifyPackageKeyAsync(logbookId, "RECOVERY_KEY_READBACK_FAILED", cancellationToken);
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

    private static void ValidateRecoveryPublicKey(
        string publicKey,
        string fingerprint,
        string algorithm,
        string errorCode)
    {
        if (!string.Equals(algorithm, RecoveryKeyAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new MobileHostedDiagnosticException(errorCode, "Account recovery public-key material is invalid.");
        }

        byte[] encoded;
        try
        {
            encoded = Convert.FromBase64String(publicKey);
        }
        catch (FormatException ex)
        {
            throw new MobileHostedDiagnosticException(
                errorCode,
                "Account recovery public-key material is invalid.",
                innerException: ex);
        }

        var actualFingerprint = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();
        if (!string.Equals(actualFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MobileHostedDiagnosticException(errorCode, "Account recovery public-key material is invalid.");
        }
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
