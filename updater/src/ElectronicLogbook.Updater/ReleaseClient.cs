using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed class ReleaseClient
{
    private const string MasterFile = "Electronic_Logbook_Master.xlsm";
    private const string ManifestFile = "release-manifest.json";
    private const string ManifestSignatureFile = "release-manifest.json.sig";
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxManifestSignatureBytes = 16 * 1024;
    private const long MaxWorkbookBytes = 100L * 1024 * 1024;
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(5);
    private readonly HttpClient _httpClient;
    private readonly string _downloadRoot;
    private readonly TimeSpan _downloadTimeout;
    private readonly string _manifestSignaturePublicKeyPem;

    public ReleaseClient(
        HttpClient? httpClient = null,
        string? downloadRoot = null,
        TimeSpan? downloadTimeout = null,
        string? manifestSignaturePublicKeyPem = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _downloadRoot = downloadRoot ?? Path.GetTempPath();
        _downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
        _manifestSignaturePublicKeyPem = manifestSignaturePublicKeyPem ?? LoadManifestSignaturePublicKeyPem();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ElectronicLogbook-ExternalUpdater", "0.1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<VerifiedRelease> GetLatestReleaseAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_downloadTimeout);
        cancellationToken = timeout.Token;

        var releaseUri = new Uri($"https://api.github.com/repos/{repository}/releases/latest");
        ValidateHttpsAllowedHost(releaseUri, "latest release metadata");
        using var releaseResponse = await _httpClient.GetAsync(
            releaseUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();
        ValidateHttpsAllowedHost(
            releaseResponse.RequestMessage?.RequestUri ?? releaseUri,
            "latest release metadata");

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var releaseDocument = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken);
        var root = releaseDocument.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ??
            throw new InvalidDataException("Latest release is missing tag_name.");

        var assets = ParseReleaseAssets(root, tag);

        if (!assets.TryGetValue(ManifestFile, out var manifestUrl))
        {
            throw new InvalidDataException(
                $"Latest release {tag} does not contain {ManifestFile}. " +
                "Use --master for local prototype testing.");
        }
        if (!assets.TryGetValue(ManifestSignatureFile, out var manifestSignatureUrl))
        {
            throw new InvalidDataException(
                $"Latest release {tag} does not contain {ManifestSignatureFile}.");
        }
        if (!assets.TryGetValue(MasterFile, out var masterUrl))
        {
            throw new InvalidDataException($"Latest release {tag} does not contain {MasterFile}.");
        }

        var downloadDirectory = Path.Combine(
            _downloadRoot,
            $"ElectronicLogbookUpdater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(downloadDirectory);
        var manifestPath = Path.Combine(downloadDirectory, ManifestFile);
        var manifestSignaturePath = Path.Combine(downloadDirectory, ManifestSignatureFile);
        var masterPath = Path.Combine(downloadDirectory, MasterFile);

        try
        {
            await StreamGitHubAssetToFileAsync(
                manifestUrl,
                ManifestFile,
                manifestPath,
                MaxManifestBytes,
                cancellationToken);
            await StreamGitHubAssetToFileAsync(
                manifestSignatureUrl,
                ManifestSignatureFile,
                manifestSignaturePath,
                MaxManifestSignatureBytes,
                cancellationToken);
            await VerifyManifestSignatureAsync(
                manifestPath,
                manifestSignaturePath,
                cancellationToken);
            await using var manifestStream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(
                manifestStream,
                JsonDefaults.Web,
                cancellationToken) ?? throw new InvalidDataException("Release manifest could not be parsed.");
            ValidateManifest(manifest, tag);

            var masterAsset = manifest.Assets.SingleOrDefault(
                asset => string.Equals(asset.Name, MasterFile, StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidDataException($"Release manifest does not describe {MasterFile}.");

            await StreamGitHubAssetToFileAsync(
                masterUrl,
                MasterFile,
                masterPath,
                MaxWorkbookBytes,
                cancellationToken);
            await Integrity.VerifyFileAsync(masterPath, masterAsset, cancellationToken);

            return new(manifest, masterPath, downloadDirectory);
        }
        catch
        {
            try { Directory.Delete(downloadDirectory, recursive: true); } catch { }
            throw;
        }
    }

    public static void ValidateManifest(ReleaseManifest manifest, string expectedTag)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Tag) ||
            string.IsNullOrWhiteSpace(manifest.Commit))
        {
            throw new InvalidDataException("Release manifest metadata is incomplete.");
        }
        if (!string.Equals(manifest.Tag, expectedTag, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Release manifest tag {manifest.Tag} does not match release tag {expectedTag}.");
        }
        if (!string.Equals(manifest.Tag, $"v{manifest.Version}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Release manifest tag {manifest.Tag} does not match version {manifest.Version}.");
        }
        if (manifest.Commit.Length != 40 ||
            manifest.Commit.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Release manifest commit is not a 40-character SHA.");
        }
        if (manifest.Assets.Count == 0)
        {
            throw new InvalidDataException("Release manifest does not contain assets.");
        }
        foreach (var asset in manifest.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name) ||
                asset.Size <= 0 ||
                asset.Sha256.Length != 64 ||
                asset.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException($"Release manifest asset is invalid: {asset.Name}");
            }
        }

        var duplicateAsset = manifest.Assets
            .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAsset is not null)
        {
            throw new InvalidDataException(
                $"Release manifest contains duplicate asset: {duplicateAsset.Key}");
        }
    }

    private static IReadOnlyDictionary<string, GitHubReleaseAsset> ParseReleaseAssets(
        JsonElement root,
        string tag)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Latest release {tag} is missing its assets list.");
        }

        var assets = new Dictionary<string, GitHubReleaseAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = GetRequiredString(assetElement, "name", "release asset");
            if (assets.ContainsKey(name))
            {
                throw new InvalidDataException($"Latest release {tag} contains duplicate asset: {name}");
            }

            assets.Add(
                name,
                new GitHubReleaseAsset(
                    GetRequiredString(assetElement, "browser_download_url", name),
                    GetRequiredInt64(assetElement, "size", name),
                    assetElement.TryGetProperty("digest", out var digest) &&
                        digest.ValueKind == JsonValueKind.String
                        ? digest.GetString()
                        : null));
        }

        return assets;
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{context} is missing {propertyName}.");
        }

        return value.GetString()!;
    }

    private static long GetRequiredInt64(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            !value.TryGetInt64(out var result))
        {
            throw new InvalidDataException($"{context} is missing {propertyName}.");
        }

        return result;
    }

    private static string LoadManifestSignaturePublicKeyPem()
    {
        var assembly = typeof(ReleaseClient).Assembly;
        const string resourceName =
            "ElectronicLogbook.Updater.release-manifest-signing-public-key.pem";
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                "The release manifest signing public key is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task VerifyManifestSignatureAsync(
        string manifestPath,
        string signaturePath,
        CancellationToken cancellationToken)
    {
        if (!_manifestSignaturePublicKeyPem.Contains(
            "BEGIN PUBLIC KEY",
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Release manifest signing public key has not been configured.");
        }

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var signatureBytes = await File.ReadAllBytesAsync(signaturePath, cancellationToken);
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(_manifestSignaturePublicKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException(
                "Release manifest signing public key could not be imported.",
                ex);
        }

        if (!ecdsa.VerifyData(
            manifestBytes,
            signatureBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new InvalidDataException(
                "Release manifest signature verification failed.");
        }
    }

    private async Task StreamGitHubAssetToFileAsync(
        GitHubReleaseAsset asset,
        string assetName,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ValidateGitHubAssetMetadata(asset, assetName, maxBytes);
        var requestUri = new Uri(asset.Url);
        ValidateHttpsAllowedHost(requestUri, assetName);

        var partialPath = $"{destinationPath}.partial";
        File.Delete(partialPath);
        File.Delete(destinationPath);

        try
        {
            using var response = await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            ValidateHttpsAllowedHost(response.RequestMessage?.RequestUri ?? requestUri, assetName);

            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength != asset.Size)
            {
                throw new InvalidDataException(
                    $"{assetName} does not match GitHub's release asset size.");
            }

            long totalBytes = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > asset.Size || totalBytes > maxBytes)
                    {
                        throw new InvalidDataException($"{assetName} exceeds the expected download size.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                if (totalBytes != asset.Size)
                {
                    throw new InvalidDataException(
                        $"{assetName} does not match GitHub's release asset size.");
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(partialPath, destinationPath);
            await VerifyGitHubAssetFileAsync(destinationPath, assetName, asset, cancellationToken);
        }
        catch
        {
            try { File.Delete(partialPath); } catch { }
            try { File.Delete(destinationPath); } catch { }
            throw;
        }
    }

    private static void ValidateGitHubAssetMetadata(
        GitHubReleaseAsset asset,
        string assetName,
        long maxBytes)
    {
        if (asset.Size <= 0 || asset.Size > maxBytes)
        {
            throw new InvalidDataException(
                $"{assetName} has an unsupported release asset size.");
        }

        if (string.IsNullOrWhiteSpace(asset.Digest))
        {
            throw new InvalidDataException(
                $"{assetName} does not have a GitHub release asset digest.");
        }

        const string prefix = "sha256:";
        if (!asset.Digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} uses an unsupported GitHub release asset digest.");
        }
    }

    private static async Task VerifyGitHubAssetFileAsync(
        string path,
        string assetName,
        GitHubReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != asset.Size)
        {
            throw new InvalidDataException(
                $"{assetName} does not match GitHub's release asset size.");
        }

        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        const string prefix = "sha256:";
        var digest = asset.Digest ?? throw new InvalidDataException(
            $"{assetName} does not have a GitHub release asset digest.");
        if (!string.Equals(digest[prefix.Length..], actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} does not match GitHub's release asset digest.");
        }
    }

    private static void ValidateHttpsAllowedHost(Uri uri, string assetName)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{assetName} must be downloaded over HTTPS.");
        }

        if (!IsAllowedGitHubHost(uri.Host))
        {
            throw new InvalidDataException($"{assetName} uses an unsupported download host.");
        }
    }

    private static bool IsAllowedGitHubHost(string host)
    {
        return string.Equals(host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GitHubReleaseAsset(string Url, long Size, string? Digest);
}
