using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

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
