using System.Net.Http.Headers;
using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed class ReleaseClient
{
    private const string MasterFile = "Electronic_Logbook_Master.xlsm";
    private const string ManifestFile = "release-manifest.json";
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
        var releaseJson = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{repository}/releases/latest",
            cancellationToken);
        using var releaseDocument = JsonDocument.Parse(releaseJson);
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

        var manifestBytes = await _httpClient.GetByteArrayAsync(manifestUrl.Url, cancellationToken);
        VerifyGitHubAsset(manifestBytes, ManifestFile, manifestUrl);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonDefaults.Web) ??
            throw new InvalidDataException("Release manifest could not be parsed.");
        ValidateManifest(manifest, tag);

        var masterAsset = manifest.Assets.SingleOrDefault(
            asset => string.Equals(asset.Name, MasterFile, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException($"Release manifest does not describe {MasterFile}.");

        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ElectronicLogbookUpdater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(downloadDirectory);
        var masterPath = Path.Combine(downloadDirectory, MasterFile);

        try
        {
            var masterBytes = await _httpClient.GetByteArrayAsync(masterUrl.Url, cancellationToken);
            VerifyGitHubAsset(masterBytes, MasterFile, masterUrl);
            await File.WriteAllBytesAsync(masterPath, masterBytes, cancellationToken);
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

    private static void VerifyGitHubAsset(
        byte[] content,
        string assetName,
        GitHubReleaseAsset asset)
    {
        if (content.LongLength != asset.Size)
        {
            throw new InvalidDataException(
                $"{assetName} does not match GitHub's release asset size.");
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

        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(asset.Digest[prefix.Length..], actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} does not match GitHub's release asset digest.");
        }
    }

    private sealed record GitHubReleaseAsset(string Url, long Size, string? Digest);
}
