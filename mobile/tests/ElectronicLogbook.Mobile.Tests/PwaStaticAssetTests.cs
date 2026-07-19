using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class PwaStaticAssetTests
{
    [Fact]
    public void PublishedServiceWorkerUsesVersionedOfflineCacheAndDeletesOldCaches()
    {
        var worker = ReadMobileAsset("service-worker.published.js");

        Assert.Contains("self.assetsManifest.version", worker, StringComparison.Ordinal);
        Assert.Contains("const cacheNamePrefix = 'offline-cache-';", worker, StringComparison.Ordinal);
        Assert.Contains("key.startsWith(cacheNamePrefix) && key !== cacheName", worker, StringComparison.Ordinal);
        Assert.Contains("caches.delete(key)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedServiceWorkerCachesApplicationShellAndNavigationFallback()
    {
        var worker = ReadMobileAsset("service-worker.published.js");

        Assert.Matches(new Regex(@"offlineAssetsInclude\s*=\s*\[[^\]]*\\\.html", RegexOptions.Singleline), worker);
        Assert.Matches(new Regex(@"offlineAssetsInclude\s*=\s*\[[^\]]*\\\.js", RegexOptions.Singleline), worker);
        Assert.Matches(new Regex(@"offlineAssetsInclude\s*=\s*\[[^\]]*\\\.css", RegexOptions.Singleline), worker);
        Assert.Contains("cache.addAll(assetsRequests)", worker, StringComparison.Ordinal);
        Assert.Contains("event.request.mode === 'navigate'", worker, StringComparison.Ordinal);
        Assert.Contains("shouldServeIndexHtml ? 'index.html' : event.request", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedServiceWorkerUsesRollbackSafeLifecycle()
    {
        var worker = ReadMobileAsset("service-worker.published.js");
        var installIndex = worker.IndexOf("async function onInstall", StringComparison.Ordinal);
        var activateIndex = worker.IndexOf("async function onActivate", StringComparison.Ordinal);
        var cachePopulateIndex = worker.IndexOf("cache.addAll(assetsRequests)", StringComparison.Ordinal);
        var oldCacheDeleteIndex = worker.IndexOf("caches.delete(key)", StringComparison.Ordinal);

        Assert.True(installIndex >= 0);
        Assert.True(activateIndex > installIndex);
        Assert.True(cachePopulateIndex > installIndex && cachePopulateIndex < activateIndex);
        Assert.True(oldCacheDeleteIndex > activateIndex);
        Assert.DoesNotContain("skipWaiting", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("clients.claim", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentServiceWorkerDoesNotEnableOfflineCaching()
    {
        var worker = ReadMobileAsset("service-worker.js");

        Assert.Contains("always fetch from the network", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("caches.open", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void WebManifestIsInstallableWithoutRelatedNativeApplications()
    {
        using var manifest = JsonDocument.Parse(ReadMobileAsset("manifest.webmanifest"));
        var root = manifest.RootElement;

        Assert.Equal("Electronic Logbook", root.GetProperty("name").GetString());
        Assert.Equal("Logbook", root.GetProperty("short_name").GetString());
        Assert.Contains("offline", root.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.False(root.GetProperty("prefer_related_applications").GetBoolean());
        Assert.Equal("./", root.GetProperty("start_url").GetString());
        Assert.Equal("./", root.GetProperty("id").GetString());
        Assert.Equal("./", root.GetProperty("scope").GetString());
        Assert.Contains(root.GetProperty("categories").EnumerateArray(), category => category.GetString() == "productivity");
        Assert.Contains(root.GetProperty("categories").EnumerateArray(), category => category.GetString() == "utilities");

        var icons = root.GetProperty("icons").EnumerateArray().ToArray();
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "512x512");
        Assert.All(icons, icon => Assert.Equal("any maskable", icon.GetProperty("purpose").GetString()));
    }

    [Theory]
    [InlineData("icon-192.png", 192, 192)]
    [InlineData("icon-512.png", 512, 512)]
    public void PwaIconsHaveInstallablePngDimensions(string fileName, int expectedWidth, int expectedHeight)
    {
        var path = GetMobileAssetPath(fileName);
        var dimensions = ReadPngDimensions(path);

        Assert.Equal((expectedWidth, expectedHeight), dimensions);
        Assert.True(new FileInfo(path).Length > 1024);
    }

    [Fact]
    public void BrowserKeyBridgeStoresNonExtractablePackageKeysWithoutExportingRawKeyBytes()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("window.electronicLogbookKeys", bridge, StringComparison.Ordinal);
        Assert.Contains("{ name: \"AES-GCM\", length: 256 }", bridge, StringComparison.Ordinal);
        Assert.Contains("false,", bridge, StringComparison.Ordinal);
        Assert.Contains("[\"encrypt\", \"decrypt\"]", bridge, StringComparison.Ordinal);
        Assert.Contains("portable-keys", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("exportKey", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("raw", bridge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserKeyBridgeEncryptsAndDecryptsWithoutExportingPackageKey()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("getRequiredPackageKey", bridge, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.encrypt", bridge, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.decrypt", bridge, StringComparison.Ordinal);
        Assert.Contains("additionalData: new Uint8Array(additionalData)", bridge, StringComparison.Ordinal);
        Assert.Contains("tagLength: 128", bridge, StringComparison.Ordinal);
        Assert.Contains("encrypted.slice(encrypted.length - 16)", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("exportKey", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedDbBridgeResolvesWritesOnlyAfterTransactionCompletes()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("transaction.oncomplete = () => resolve(result)", bridge, StringComparison.Ordinal);
        Assert.Contains("transaction.onabort = () => reject(transaction.error)", bridge, StringComparison.Ordinal);
        Assert.Contains("result = request.result", bridge, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"async function withStore[\s\S]*transaction\.oncomplete = \(\) => resolve\(result\)", RegexOptions.Singleline),
            bridge);
    }

    [Fact]
    public void BrowserFileBridgeDownloadsBytesWithObjectUrlCleanup()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("window.electronicLogbookFiles", bridge, StringComparison.Ordinal);
        Assert.Contains("new Blob([new Uint8Array(bytes)]", bridge, StringComparison.Ordinal);
        Assert.Contains("URL.createObjectURL(blob)", bridge, StringComparison.Ordinal);
        Assert.Contains("link.download = fileName", bridge, StringComparison.Ordinal);
        Assert.Contains("URL.revokeObjectURL(url)", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFileBridgeSharesFilesThroughWebShareApiWhenAvailable()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("navigator.canShare", bridge, StringComparison.Ordinal);
        Assert.Contains("navigator.share", bridge, StringComparison.Ordinal);
        Assert.Contains("new File([new Uint8Array(bytes)]", bridge, StringComparison.Ordinal);
        Assert.Contains("files: [file]", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFileBridgePicksPackageBytesWithoutPersistingThem()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("electronicLogbookFiles", bridge, StringComparison.Ordinal);
        Assert.Contains("input.type = \"file\"", bridge, StringComparison.Ordinal);
        Assert.Contains("input.accept = accept", bridge, StringComparison.Ordinal);
        Assert.Contains("file.arrayBuffer()", bridge, StringComparison.Ordinal);
        Assert.Contains("bytes", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("electronicLogbookStore.save", ExtractFileBridge(bridge), StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFileBridgeRejectsOversizedPackagesBeforeReadingBytes()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));
        var emptyCheckIndex = bridge.IndexOf("file.size === 0", StringComparison.Ordinal);
        var sizeCheckIndex = bridge.IndexOf("file.size > maxElogbookBytes", StringComparison.Ordinal);
        var readIndex = bridge.IndexOf("file.arrayBuffer()", StringComparison.Ordinal);

        Assert.Contains("const maxElogbookBytes = 64 * 1024 * 1024", bridge, StringComparison.Ordinal);
        Assert.True(emptyCheckIndex >= 0);
        Assert.True(sizeCheckIndex >= 0);
        Assert.True(sizeCheckIndex > emptyCheckIndex);
        Assert.True(readIndex > sizeCheckIndex);
        Assert.Contains("reject(new Error(\"Selected file is empty.\"))", bridge, StringComparison.Ordinal);
        Assert.Contains("reject(new Error(`Selected file is larger than the ${maxElogbookBytes} byte package limit.`))", bridge, StringComparison.Ordinal);
    }

    private static string ExtractFileBridge(string bridge)
    {
        var start = bridge.IndexOf("window.electronicLogbookFiles", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return bridge[start..];
    }

    private static string ReadMobileAsset(string relativePath) =>
        File.ReadAllText(GetMobileAssetPath(relativePath));

    private static string GetMobileAssetPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "wwwroot",
            relativePath));

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        var bytesRead = stream.Read(header);

        Assert.Equal(24, bytesRead);
        Assert.True(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));

        var width = ReadBigEndianInt32(header[16..20]);
        var height = ReadBigEndianInt32(header[20..24]);
        return (width, height);
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
