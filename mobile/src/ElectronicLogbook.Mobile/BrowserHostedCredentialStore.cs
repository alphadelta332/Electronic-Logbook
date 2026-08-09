using System.Text.Json;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserHostedCredentialStore(IJSRuntime jsRuntime)
{
    private const string CredentialKey = "hosted-credential";

    public async ValueTask<BrowserHostedCredential?> LoadAsync()
    {
        var json = await jsRuntime.InvokeAsync<string?>("electronicLogbookStore.load", CredentialKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BrowserHostedCredential>(json, PortableLogbookJson.SerializerOptions);
    }

    public ValueTask SaveAsync(BrowserHostedCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return jsRuntime.InvokeVoidAsync(
            "electronicLogbookStore.save",
            CredentialKey,
            JsonSerializer.Serialize(credential, PortableLogbookJson.SerializerOptions));
    }

    public ValueTask DeleteAsync() =>
        jsRuntime.InvokeVoidAsync("electronicLogbookStore.delete", CredentialKey);
}

public sealed record BrowserHostedCredential(
    HostedAccountId AccountId,
    DeviceId DeviceId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    bool DeviceRegistrationPending = false);
