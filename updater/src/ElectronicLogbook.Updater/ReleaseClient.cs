using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed class ReleaseClient
{
    private const string MasterFile = "Electronic_Logbook_Master.xlsm";
    private const string ManifestFile = "release-manifest.json";
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxWorkbookBytes = 100L * 1024 * 1024;
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(5);
    private readonly HttpClient _httpClient;

    public ReleaseClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
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
        timeout.CancelAfter(DefaultDownloadTimeout);
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

        var assets = root.GetProperty("assets")
            .EnumerateArray()
            .ToDictionary(
                asset => asset.GetProperty("name").GetString()!,
                asset => new GitHubReleaseAsset(
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64(),
                    asset.TryGetProperty("digest", out var digest)
                        ? digest.GetString()
                        : null),
                StringComparer.OrdinalIgnoreCase);

        if (!assets.TryGetValue(ManifestFile, out var manifestUrl))
        {
            throw new InvalidDataException(
                $"Latest release {tag} does not contain {ManifestFile}. " +
                "Use --master for local prototype testing.");
        }
        if (!assets.TryGetValue(MasterFile, out var masterUrl))
        {
            throw new InvalidDataException($"Latest release {tag} does not contain {MasterFile}.");
        }

        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ElectronicLogbookUpdater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(downloadDirectory);
        var manifestPath = Path.Combine(downloadDirectory, ManifestFile);
        var masterPath = Path.Combine(downloadDirectory, MasterFile);

        try
        {
            await StreamGitHubAssetToFileAsync(
                manifestUrl,
                ManifestFile,
                manifestPath,
                MaxManifestBytes,
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
