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
        Assert.Contains("new Request(new URL(asset.url, baseUrl)", worker, StringComparison.Ordinal);
        Assert.Contains("shouldServeIndexHtml ? new Request(new URL('index.html', baseUrl)) : event.request", worker, StringComparison.Ordinal);
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
    public void NativeCapacitorShellDoesNotRegisterServiceWorker()
    {
        var index = ReadMobileAsset("index.html");

        Assert.Contains("document.documentElement.classList.add(\"capacitor-native\")", index, StringComparison.Ordinal);
        Assert.Contains("const isCapacitorNative = document.documentElement.classList.contains(\"capacitor-native\");", index, StringComparison.Ordinal);
        Assert.Contains("!isEphemeralTunnel && !isCapacitorNative", index, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register('service-worker.js')", index, StringComparison.Ordinal);
    }

    [Fact]
    public void WebManifestIsInstallableWithoutRelatedNativeApplications()
    {
        using var manifest = JsonDocument.Parse(ReadMobileAsset("manifest.webmanifest"));
        var root = manifest.RootElement;

        Assert.Equal("LogbookOne", root.GetProperty("name").GetString());
        Assert.Equal("LogbookOne", root.GetProperty("short_name").GetString());
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
    public void BrowserKeyBridgeStoresNonExtractablePackageKeysAndImportsRecoveryKeys()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("window.electronicLogbookKeys", bridge, StringComparison.Ordinal);
        Assert.Contains("nativeKeyPlugin()", bridge, StringComparison.Ordinal);
        Assert.Contains("native.importPackageKey({ keyName, keyBytes: Array.from(keyBytes) })", bridge, StringComparison.Ordinal);
        Assert.Contains("{ name: \"AES-GCM\", length: 256 }", bridge, StringComparison.Ordinal);
        Assert.Contains("false,", bridge, StringComparison.Ordinal);
        Assert.Contains("[\"encrypt\", \"decrypt\"]", bridge, StringComparison.Ordinal);
        Assert.Contains("portable-keys", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("exportKey", bridge, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.importKey", bridge, StringComparison.Ordinal);
        Assert.Contains("\"raw\"", bridge, StringComparison.Ordinal);
        Assert.Contains("false,", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserKeyBridgeEncryptsAndDecryptsWithoutExportingPackageKey()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("getRequiredPackageKey", bridge, StringComparison.Ordinal);
        Assert.Contains("native.encryptPackagePayload", bridge, StringComparison.Ordinal);
        Assert.Contains("native.decryptPackagePayload", bridge, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.encrypt", bridge, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.decrypt", bridge, StringComparison.Ordinal);
        Assert.Contains("additionalData: new Uint8Array(additionalData)", bridge, StringComparison.Ordinal);
        Assert.Contains("tagLength: 128", bridge, StringComparison.Ordinal);
        Assert.Contains("encrypted.slice(encrypted.length - 16)", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("exportKey", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidNativeKeyBridgeWrapsPackageKeysWithAndroidKeystore()
    {
        var plugin = ReadRepositoryFile(
            "android",
            "app",
            "src",
            "main",
            "java",
            "com",
            "alphadelta",
            "electroniclogbook",
            "ElectronicLogbookNativeFilesPlugin.java");

        Assert.Contains("AndroidKeyStore", plugin, StringComparison.Ordinal);
        Assert.Contains("electronic-logbook.package-key-wrapper", plugin, StringComparison.Ordinal);
        Assert.Contains("KeyGenParameterSpec.Builder", plugin, StringComparison.Ordinal);
        Assert.Contains("KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT", plugin, StringComparison.Ordinal);
        Assert.Contains("setBlockModes(KeyProperties.BLOCK_MODE_GCM)", plugin, StringComparison.Ordinal);
        Assert.Contains("setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)", plugin, StringComparison.Ordinal);
        Assert.Contains("getSharedPreferences(NativeKeyPreferences", plugin, StringComparison.Ordinal);
        Assert.Contains("cipher.updateAAD(keyName.getBytes(StandardCharsets.UTF_8))", plugin, StringComparison.Ordinal);
        Assert.Contains("Arrays.fill(packageKey, (byte) 0)", plugin, StringComparison.Ordinal);
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
    public void BrowserFileBridgeDoesNotUploadDownloadedSupportArtifacts()
    {
        var fileBridge = ExtractFileBridge(ReadMobileAsset(Path.Combine("js", "logbookStore.js")));

        Assert.Contains("download: (fileName, bytes, contentType)", fileBridge, StringComparison.Ordinal);
        Assert.Contains("link.click()", fileBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", fileBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", fileBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("sendBeacon", fileBridge, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFileBridgeSharesFilesThroughWebShareApiWhenAvailable()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("navigator.canShare", bridge, StringComparison.Ordinal);
        Assert.Contains("navigator.share", bridge, StringComparison.Ordinal);
        Assert.Contains("new File([new Uint8Array(bytes)]", bridge, StringComparison.Ordinal);
        Assert.Contains("files: [file]", bridge, StringComparison.Ordinal);
        Assert.Contains("nativeShareOrDownload", bridge, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbookNativeFiles", bridge, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"canShare:[\s\S]*try\s*\{[\s\S]*navigator\.canShare[\s\S]*\}\s*catch\s*\{[\s\S]*return false", RegexOptions.Singleline), bridge);
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
    public void BrowserFileBridgeCleansUpTemporaryPickerInput()
    {
        var fileBridge = ExtractFileBridge(ReadMobileAsset(Path.Combine("js", "logbookStore.js")));

        Assert.Contains("input.style.display = \"none\"", fileBridge, StringComparison.Ordinal);
        Assert.Contains("let pickerSettled = false", fileBridge, StringComparison.Ordinal);
        Assert.Contains("if (pickerSettled)", fileBridge, StringComparison.Ordinal);
        Assert.Contains("pickerSettled = true", fileBridge, StringComparison.Ordinal);
        Assert.Contains("input.remove()", fileBridge, StringComparison.Ordinal);
        Assert.Contains("document.body.appendChild(input)", fileBridge, StringComparison.Ordinal);
        Assert.Contains("input.oncancel = () =>", fileBridge, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"input\.oncancel[\s\S]*settle\(\(\) => resolve\(null\)\)", RegexOptions.Singleline), fileBridge);
        Assert.Matches(new Regex(@"file\.size === 0[\s\S]*settle\(\(\) => reject\(new Error\(""Selected file is empty\.""\)\)\)", RegexOptions.Singleline), fileBridge);
        Assert.Matches(new Regex(@"file\.size > maxElogbookBytes[\s\S]*settle\(\(\) => reject\(new Error\(`Selected file is larger", RegexOptions.Singleline), fileBridge);
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

    [Fact]
    public void AppCssMakesDisabledAndPackageExchangeStatesVisible()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));

        Assert.Contains("button:disabled", css, StringComparison.Ordinal);
        Assert.Contains("input:disabled", css, StringComparison.Ordinal);
        Assert.Contains("select:disabled", css, StringComparison.Ordinal);
        Assert.Contains("textarea:disabled", css, StringComparison.Ordinal);
        Assert.Contains("cursor: not-allowed", css, StringComparison.Ordinal);
        Assert.Contains("opacity: 1", css, StringComparison.Ordinal);
        Assert.Contains("button.primary:disabled", css, StringComparison.Ordinal);
        Assert.Contains(".exchange-message", css, StringComparison.Ordinal);
        Assert.Contains(".exchange-blocked", css, StringComparison.Ordinal);
        Assert.Contains(".exchange-busy", css, StringComparison.Ordinal);
        Assert.Contains(".package-exchange-feedback", css, StringComparison.Ordinal);
        Assert.Contains(".package-file-summary", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCssLetsSegmentedControlsMatchRenderedButtonCount()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));
        var segmentedControl = ExtractCssRule(css, ".segmented-control");

        Assert.Contains("display: flex", segmentedControl, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-columns", segmentedControl, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"\.segmented-control button\s*\{[\s\S]*flex:\s*1 1 0", RegexOptions.Singleline),
            css);
    }

    [Fact]
    public void AppCssKeepsCurrentCurrencyStatusIndependentFromThemeAccent()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));
        var currencyRowCurrent = ExtractCssRule(css, ".currency-row-current");

        Assert.Contains("--app-success: #187a46;", css, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"html\[data-elb-theme=""dark""\]\s*\{[^}]*--app-success:\s*#37d47d", RegexOptions.Singleline),
            css);
        Assert.Matches(
            new Regex(@"\.dashboard-currency-overview-current,[\s\S]*?--dashboard-currency-accent:\s*var\(--app-success\)", RegexOptions.Singleline),
            css);
        Assert.Matches(
            new Regex(@"\.currency-overview-current \.mud-icon-root,[\s\S]*?color:\s*var\(--app-success\)", RegexOptions.Singleline),
            css);
        Assert.Contains("--currency-row-accent: var(--app-success)", currencyRowCurrent, StringComparison.Ordinal);
        Assert.DoesNotContain("var(--app-primary)", currencyRowCurrent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCssKeepsDashboardExperienceColoursStaticAndDistinct()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));
        var expectedColours = new Dictionary<string, string>
        {
            [".dashboard-experience-command"] = "#0072b2",
            [".dashboard-experience-icus"] = "#009e73",
            [".dashboard-experience-dual"] = "#e69f00",
            [".dashboard-experience-copilot"] = "#cc79a7",
            [".dashboard-experience-se"] = "#56b4e9",
            [".dashboard-experience-me"] = "#d55e00"
        };

        Assert.Equal(expectedColours.Count, expectedColours.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var (selector, colour) in expectedColours)
        {
            var rule = ExtractCssRule(css, selector);
            Assert.Contains($"background: {colour};", rule, StringComparison.Ordinal);
            Assert.DoesNotContain("var(--app-", rule, StringComparison.Ordinal);
            Assert.DoesNotContain("color-mix", rule, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppCssKeepsMobileShellNavigationAnchoredToViewport()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));
        var appShell = ExtractCssRule(css, ".app-shell");
        var appMain = ExtractCssRule(css, ".app-main");
        var bottomNav = ExtractCssRule(css, ".bottom-nav");

        Assert.Matches(new Regex(@"html,\s*body,\s*#app\s*\{[\s\S]*height:\s*100%", RegexOptions.Singleline), css);
        Assert.Matches(new Regex(@"html,\s*body,\s*#app\s*\{[\s\S]*overflow:\s*hidden", RegexOptions.Singleline), css);
        Assert.Contains("display: flex", appShell, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh", appShell, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", appShell, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto", appMain, StringComparison.Ordinal);
        Assert.Contains("min-height: 0", appMain, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", appMain, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", appMain, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior-y: contain", appMain, StringComparison.Ordinal);
        Assert.Contains("position: fixed", bottomNav, StringComparison.Ordinal);
        Assert.Contains("bottom: 0", bottomNav, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCssAdaptsTheShellAndDashboardForTabletWidths()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));

        Assert.Matches(
            new Regex(
                @"(?s)@media \(min-width: 720px\)\s*\{.*?\.bottom-nav\s*\{.*?top:\s*74px.*?width:\s*88px.*?grid-template-columns:\s*1fr.*?\.app-main\s*\{.*?padding-left:\s*116px",
                RegexOptions.Singleline),
            css);
        Assert.Matches(
            new Regex(
                @"(?s)@media \(min-width: 900px\)\s*\{.*?\.dashboard-page\s*\{.*?grid-template-columns:\s*minmax\(0, 3fr\) minmax\(300px, 2fr\).*?\.dashboard-side-column\s*\{.*?display:\s*grid.*?align-content:\s*start.*?gap:\s*20px",
                RegexOptions.Singleline),
            css);
    }

    [Fact]
    public void NavigationBridgeResetsTheScrollableShellAfterRouteChanges()
    {
        var bridge = ReadMobileAsset(Path.Combine("js", "logbookStore.js"));

        Assert.Contains("scrollMainToTop", bridge, StringComparison.Ordinal);
        Assert.Contains("document.querySelector(\".app-main\")", bridge, StringComparison.Ordinal);
        Assert.Contains("main.scrollTop = 0", bridge, StringComparison.Ordinal);
        Assert.Contains("main.scrollLeft = 0", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCssHighlightsActiveNavigationIconAndLabelWithThemeColour()
    {
        var css = ReadMobileAsset(Path.Combine("css", "app.css"));
        var navigationLink = ExtractCssRule(css, ".bottom-nav a");
        var pressedNavigationLink = ExtractCssRule(css, ".bottom-nav a:active");
        var activeNavigationLink = ExtractCssRule(css, ".bottom-nav a.active");
        var pendingNavigationLink = ExtractCssRule(css, ".bottom-nav a.nav-pending-link");
        var navigationLabel = ExtractCssRule(css, ".bottom-nav a > span:last-child");

        Assert.DoesNotContain(".bottom-nav::before", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-tap-highlight-color: transparent", navigationLink, StringComparison.Ordinal);
        Assert.Contains("transition: color 130ms ease, opacity 100ms ease, transform 100ms ease", navigationLink, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-primary)", pressedNavigationLink, StringComparison.Ordinal);
        Assert.Contains("filter: none", pressedNavigationLink, StringComparison.Ordinal);
        Assert.Contains("opacity: 0.72", pressedNavigationLink, StringComparison.Ordinal);
        Assert.Contains("transform: scale(0.96)", pressedNavigationLink, StringComparison.Ordinal);
        Assert.Contains("background: transparent", activeNavigationLink, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-primary)", activeNavigationLink, StringComparison.Ordinal);
        Assert.Contains("background: transparent", pendingNavigationLink, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-primary)", pendingNavigationLink, StringComparison.Ordinal);
        Assert.Contains("width: 100%", navigationLabel, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%", navigationLabel, StringComparison.Ordinal);
        Assert.Contains("text-align: center", navigationLabel, StringComparison.Ordinal);
    }

    private static string ExtractFileBridge(string bridge)
    {
        var start = bridge.IndexOf("window.electronicLogbookFiles", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return bridge[start..];
    }

    private static string ExtractCssRule(string css, string selector)
    {
        var start = css.IndexOf($"{selector} {{", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = css.IndexOf('}', start);
        Assert.True(end > start);
        return css[start..(end + 1)];
    }

    private static string ReadMobileAsset(string relativePath) =>
        File.ReadAllText(GetMobileAssetPath(relativePath));

    private static string ReadRepositoryFile(params string[] relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            [
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                .. relativePath
            ])));

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
