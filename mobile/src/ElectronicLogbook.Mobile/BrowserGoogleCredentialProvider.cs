using System.Security.Cryptography;
using System.Text;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserGoogleCredentialProvider(IJSRuntime jsRuntime)
{
    public async ValueTask<GoogleIdTokenCredential> GetAsync(
        string webClientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webClientId);

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var hashedNonce = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(nonce)))
            .ToLowerInvariant();
        var result = await jsRuntime.InvokeAsync<NativeGoogleIdTokenCredential>(
            "electronicLogbookCredentials.getGoogleIdToken",
            cancellationToken,
            new { webClientId = webClientId.Trim(), nonce = hashedNonce });

        if (string.IsNullOrWhiteSpace(result?.IdToken))
        {
            throw new InvalidOperationException("Google sign-in returned no ID token.");
        }

        return new GoogleIdTokenCredential(result.IdToken, nonce, result.Email);
    }

    private sealed record NativeGoogleIdTokenCredential(string IdToken, string? Email);
}

public sealed record GoogleIdTokenCredential(string IdToken, string Nonce, string? Email);
