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
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
            [$"{releaseBaseUrl}/release-manifest.json.sig"] = signedManifest.SignatureBytes,
            [$"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm"] = workbookBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

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
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion,
            manifestUrl: $"http://github.com/owner/repo/releases/download/v{releaseVersion}/release-manifest.json");
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsOversizedManifest()
    {
        var releaseVersion = TestRepo.Version;
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion,
            manifestSize: 1024L * 1024 + 1);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsOversizedWorkbook()
    {
        var releaseVersion = TestRepo.Version;
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion,
            workbookSize: 100L * 1024 * 1024 + 1);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{BuildReleaseBaseUrl(releaseVersion)}/release-manifest.json"] = signedManifest.ManifestBytes,
            [$"{BuildReleaseBaseUrl(releaseVersion)}/release-manifest.json.sig"] = signedManifest.SignatureBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncPropagatesReleaseApiFailure()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            new Dictionary<string, byte[]>(),
            statusCodes: new Dictionary<string, HttpStatusCode>
            {
                ["https://api.github.com/repos/owner/repo/releases/latest"] = HttpStatusCode.InternalServerError
            }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: TestManifestPublicKeyPem);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestReleaseAsyncHonoursDownloadTimeout()
    {
        using var client = new HttpClient(new HangingResponseHandler());
        var releaseClient = new ReleaseClient(
            client,
            downloadTimeout: TimeSpan.FromMilliseconds(10),
            manifestSignaturePublicKeyPem: TestManifestPublicKeyPem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsMalformedReleaseMetadata()
    {
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] =
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    tag_name = "v" + TestRepo.Version
                })
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: TestManifestPublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("assets", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsDuplicateReleaseAssets()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v" + releaseVersion,
            assets = new object[]
            {
                BuildReleaseAsset("release-manifest.json", $"{releaseBaseUrl}/release-manifest.json", signedManifest.ManifestBytes),
                BuildReleaseAsset("release-manifest.json", $"{releaseBaseUrl}/release-manifest-copy.json", signedManifest.ManifestBytes),
                BuildReleaseAsset("release-manifest.json.sig", $"{releaseBaseUrl}/release-manifest.json.sig", signedManifest.SignatureBytes),
                BuildReleaseAsset("Electronic_Logbook_Master.xlsm", $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm", workbookBytes)
            }
        });
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsRedirectToUnsupportedHost()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion);
        using var client = new HttpClient(new StaticResponseHandler(
            new Dictionary<string, byte[]>
            {
                ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
                [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
                [$"{releaseBaseUrl}/release-manifest.json.sig"] = signedManifest.SignatureBytes
            },
            redirectedUris: new Dictionary<string, string>
            {
                [$"{releaseBaseUrl}/release-manifest.json"] = "https://example.invalid/release-manifest.json"
            }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsMissingGitHubAssetDigest()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v" + releaseVersion,
            assets = new object[]
            {
                new
                {
                    name = "release-manifest.json",
                    browser_download_url = $"{releaseBaseUrl}/release-manifest.json",
                    size = signedManifest.ManifestBytes.Length,
                    digest = (string?)null
                },
                BuildReleaseAsset("release-manifest.json.sig", $"{releaseBaseUrl}/release-manifest.json.sig", signedManifest.SignatureBytes),
                BuildReleaseAsset("Electronic_Logbook_Master.xlsm", $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm", workbookBytes)
            }
        });
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("digest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsUnsupportedGitHubAssetDigest()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v" + releaseVersion,
            assets = new object[]
            {
                BuildReleaseAsset(
                    "release-manifest.json",
                    $"{releaseBaseUrl}/release-manifest.json",
                    signedManifest.ManifestBytes,
                    digestOverride: "md5:unsupported"),
                BuildReleaseAsset("release-manifest.json.sig", $"{releaseBaseUrl}/release-manifest.json.sig", signedManifest.SignatureBytes),
                BuildReleaseAsset("Electronic_Logbook_Master.xlsm", $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm", workbookBytes)
            }
        });
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("digest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsMismatchedGitHubAssetDigest()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v" + releaseVersion,
            assets = new object[]
            {
                BuildReleaseAsset("release-manifest.json", $"{releaseBaseUrl}/release-manifest.json", signedManifest.ManifestBytes),
                BuildReleaseAsset("release-manifest.json.sig", $"{releaseBaseUrl}/release-manifest.json.sig", signedManifest.SignatureBytes),
                BuildReleaseAsset(
                    "Electronic_Logbook_Master.xlsm",
                    $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm",
                    workbookBytes,
                    digestOverride: "sha256:" + new string('0', 64))
            }
        });
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
            [$"{releaseBaseUrl}/release-manifest.json.sig"] = signedManifest.SignatureBytes,
            [$"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm"] = workbookBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("digest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRemovesDownloadDirectoryAfterPartialDownloadFailure()
    {
        var downloadRoot = Directory.CreateTempSubdirectory("ElectronicLogbookReleaseClientTests-").FullName;
        try
        {
            var releaseVersion = TestRepo.Version;
            var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
            var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
            var truncatedWorkbookBytes = Encoding.UTF8.GetBytes("truncated");
            var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
            var releaseBytes = BuildReleaseBytes(
                signedManifest.ManifestBytes,
                signedManifest.SignatureBytes,
                workbookBytes,
                releaseVersion);
            using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
            {
                ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
                [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
                [$"{releaseBaseUrl}/release-manifest.json.sig"] = signedManifest.SignatureBytes,
                [$"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm"] = truncatedWorkbookBytes
            }));
            var releaseClient = new ReleaseClient(
                client,
                downloadRoot,
                manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

            Assert.Empty(Directory.EnumerateFileSystemEntries(downloadRoot));
        }
        finally
        {
            Directory.Delete(downloadRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsMissingManifestSignatureAsset()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion,
            includeManifestSignature: false);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("release-manifest.json.sig", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsInvalidManifestSignature()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var invalidSignatureBytes = signedManifest.SignatureBytes.Reverse().ToArray();
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            invalidSignatureBytes,
            workbookBytes,
            releaseVersion);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
            [$"{releaseBaseUrl}/release-manifest.json.sig"] = invalidSignatureBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem: signedManifest.PublicKeyPem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReleaseAsyncRejectsPlaceholderPublicKey()
    {
        var releaseVersion = TestRepo.Version;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        var workbookBytes = Encoding.UTF8.GetBytes("verified workbook package");
        var signedManifest = BuildSignedManifest(workbookBytes, releaseVersion);
        var releaseBytes = BuildReleaseBytes(
            signedManifest.ManifestBytes,
            signedManifest.SignatureBytes,
            workbookBytes,
            releaseVersion);
        using var client = new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/owner/repo/releases/latest"] = releaseBytes,
            [$"{releaseBaseUrl}/release-manifest.json"] = signedManifest.ManifestBytes,
            [$"{releaseBaseUrl}/release-manifest.json.sig"] = signedManifest.SignatureBytes
        }));
        var releaseClient = new ReleaseClient(
            client,
            manifestSignaturePublicKeyPem:
                "Replace this file with the PEM public key generated by tools/New-ReleaseManifestSigningKey.ps1.");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            releaseClient.GetLatestReleaseAsync("owner/repo", CancellationToken.None));

        Assert.Contains("public key", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static SignedManifest BuildSignedManifest(byte[] workbookBytes, string releaseVersion)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestBytes = BuildManifestBytes(workbookBytes, releaseVersion);
        var signatureBytes = ecdsa.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new(
            manifestBytes,
            signatureBytes,
            ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    private static byte[] BuildReleaseBytes(
        byte[] manifestBytes,
        byte[] manifestSignatureBytes,
        byte[] workbookBytes,
        string releaseVersion,
        string? manifestUrl = null,
        long? manifestSize = null,
        long? workbookSize = null,
        bool includeManifestSignature = true)
    {
        var releaseTag = "v" + releaseVersion;
        var releaseBaseUrl = BuildReleaseBaseUrl(releaseVersion);
        manifestUrl ??= $"{releaseBaseUrl}/release-manifest.json";
        var assets = new List<object>
        {
            BuildReleaseAsset(
                "release-manifest.json",
                manifestUrl,
                manifestBytes,
                manifestSize)
        };
        if (includeManifestSignature)
        {
            assets.Add(BuildReleaseAsset(
                "release-manifest.json.sig",
                $"{releaseBaseUrl}/release-manifest.json.sig",
                manifestSignatureBytes));
        }

        assets.Add(BuildReleaseAsset(
            "Electronic_Logbook_Master.xlsm",
            $"{releaseBaseUrl}/Electronic_Logbook_Master.xlsm",
            workbookBytes,
            workbookSize));

        var release = new
        {
            tag_name = releaseTag,
            assets
        };

        return JsonSerializer.SerializeToUtf8Bytes(release);
    }

    private static object BuildReleaseAsset(
        string name,
        string url,
        byte[] content,
        long? size = null,
        string? digestOverride = null)
    {
        return new
        {
            name,
            browser_download_url = url,
            size = size ?? content.Length,
            digest = digestOverride ?? $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}"
        };
    }

    private static string BuildReleaseBaseUrl(string releaseVersion)
    {
        return $"https://github.com/owner/repo/releases/download/v{releaseVersion}";
    }

    private static string TestManifestPublicKeyPem
    {
        get
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return ecdsa.ExportSubjectPublicKeyInfoPem();
        }
    }

    private sealed record SignedManifest(
        byte[] ManifestBytes,
        byte[] SignatureBytes,
        string PublicKeyPem);

    private sealed class StaticResponseHandler(
        IReadOnlyDictionary<string, byte[]> responses,
        IReadOnlyDictionary<string, string>? redirectedUris = null,
        IReadOnlyDictionary<string, HttpStatusCode>? statusCodes = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null &&
                statusCodes is not null &&
                statusCodes.TryGetValue(request.RequestUri.AbsoluteUri, out var statusCode))
            {
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    RequestMessage = request,
                    Content = new StringContent(statusCode.ToString())
                });
            }

            if (request.RequestUri is not null &&
                responses.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                var responseRequest = request;
                if (redirectedUris is not null &&
                    redirectedUris.TryGetValue(request.RequestUri.AbsoluteUri, out var redirectedUri))
                {
                    responseRequest = new HttpRequestMessage(request.Method, redirectedUri);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = responseRequest,
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

    private sealed class HangingResponseHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after timeout cancellation.");
        }
    }
}
