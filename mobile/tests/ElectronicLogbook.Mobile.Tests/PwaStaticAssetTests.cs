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
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.False(root.GetProperty("prefer_related_applications").GetBoolean());
        Assert.Equal("./", root.GetProperty("start_url").GetString());
        Assert.Equal("./", root.GetProperty("id").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToArray();
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "512x512");
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

    private static string ExtractFileBridge(string bridge)
    {
        var start = bridge.IndexOf("window.electronicLogbookFiles", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return bridge[start..];
    }

    private static string ReadMobileAsset(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "wwwroot",
            relativePath)));
}
