using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElectronicLogbook.Updater.Tests;

public sealed class ReleaseClientTests
{
    [Fact]
    public async Task GetLatestReleaseAsyncStreamsAndVerifiesReleaseAssets()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var manifestBytes = BuildManifestBytes(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(manifestBytes, workbookBytes, releaseVersion);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = manifestBytes,
            [$"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm"] = workbookBytes
        }));
        var releaseClient = new ReleaseClient(client);

        var release = await releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None);

        try
        {
            Assert.Equal(releaseVersion, release.Manifest.Version);
            Assert.True(File.Exists(release.MasterWorkbookPath));
            Assert.Equal(workbookBytes, await File.ReadAllBytesAsync(release.MasterWorkbookPath));
            Assert.False(File.Exists($"{release.MasterWorkbookPath}.partial"));
        }
        finally
        {
            Directory.Delete(release.DownloadDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsNonHttpsAssetUrl()
    {
        var releaseVersion = TestRepo.Version;
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var manifestBytes = BuildManifestBytes(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            manifestBytes,
            workbookBytes,
            releaseVersion,
            manifestUrl: $"http://github.com/owner/repo/releases/download/v{releaseVersion}/release-manifest.json");
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsOversizedManifest()
    {
        var releaseVersion = TestRepo.Version;
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var manifestBytes = BuildManifestBytes(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            manifestBytes,
            workbookBytes,
            releaseVersion,
            manifestSize: 1024L * 1024 + 1);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildManifestBytes(byte[] workbookBytes, string releaseVersion)
    {
        var manifest = new ReleaseManifest(
            releaseVersion,
            "v" + releaseVersion,
            new string('b', 40),
            [
                new(
                    "Electronic_Logbook_Master.xlsm",
                    workbookBytes.Length,
                    Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant())
            ]);

        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonDefaults.Web);
    }

    private static byte[] BuildReleaseBytes(
        byte[] manifestBytes,
        byte[] workbookBytes,
        string releaseVersion,
        string? manifestUrl = null,
        long? manifestSize = null)
    {
        var releaseTag = "v" + releaseVersion;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        manifestUrl ??= $"{releaseBaseUrl}/release-manifest.json";
        var release = new
        {
            tag_name = releaseTag,
            assets = new object[]
            {
                new
                {
                    name = "release-manifest.json",
                    browser_download_url = manifestUrl,
                    size = manifestSize ?? manifestBytes.Length,
                    digest = $"sha256:{Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant()}"
                },
                new
                {
                    name = "Electronic_Logbook_Master.xlsm",
                    browser_download_url = $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm",
                    size = workbookBytes.Length,
                    digest = $"sha256:{Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant()}"
                }
            }
        };

        return JsonSerializer.SerializeToUtf8Bytes(release);
    }

    private static string BuildReleaseBaseUrl(string releaseVersion)
    {
        return $"https://github.com/owner/repo/releases/download/v{releaseVersion}";
    }

    private sealed class StaticResponseHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null &&
                responses.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent("not found")
            });
        }
    }
}
