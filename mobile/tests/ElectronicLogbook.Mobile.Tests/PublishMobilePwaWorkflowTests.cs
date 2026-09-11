namespace ElectronicLogbook.Mobile.Tests;

public sealed class PublishMobilePwaWorkflowTests
{
    [Fact]
    public void WorkflowPublishesOnlyStaticWwwrootOutputToPages()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("dotnet publish mobile/src/ElectronicLogbook.Mobile/ElectronicLogbook.Mobile.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("--configuration Release", workflow, StringComparison.Ordinal);
        Assert.Contains("--output mobile/artifacts/pages", workflow, StringComparison.Ordinal);
        Assert.Contains("mobile/artifacts/pages/wwwroot", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-pages-artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("path: mobile/artifacts/pages/wwwroot", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowRewritesBasePathAndDisablesJekyllProcessing()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("$basePath = \"/$($env:GITHUB_REPOSITORY.Split('/')[1])/\"", workflow, StringComparison.Ordinal);
        Assert.Contains("index.html did not contain the expected root base href.", workflow, StringComparison.Ordinal);
        Assert.Contains("$index.Replace('<base href=\"/\" />', \"<base href=\"\"$basePath\"\" />\")", workflow, StringComparison.Ordinal);
        Assert.Contains("$serviceWorkerPath = Join-Path $siteRoot \"service-worker.js\"", workflow, StringComparison.Ordinal);
        Assert.Contains("service-worker.js did not contain the expected root cache base.", workflow, StringComparison.Ordinal);
        Assert.Contains("$serviceWorker.Replace('const base = \"/\";', \"const base = \"\"$basePath\"\";\")", workflow, StringComparison.Ordinal);
        Assert.Contains(".nojekyll", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowDoesNotConfigureRuntimeSecretsOrServerUploadEndpoints()
    {
        var workflow = ReadWorkflow();

        Assert.DoesNotContain("flight", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidDebugInstallWorkflowAutomatesInPlaceUpdatesAndVerifiedSigningMigration()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var packageJson = File.ReadAllText(Path.Combine(mobileRoot, "package.json"));
        var installScript = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "Install-AndroidDebugBuild.ps1"));
        var signingScript = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "AndroidDevelopmentSigning.ps1"));
        var bridgeScript = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "AndroidDebugDeviceBridge.ps1"));
        var gradle = File.ReadAllText(Path.Combine(mobileRoot, "android", "app", "build.gradle"));

        Assert.Contains("install:android:debug", packageJson, StringComparison.Ordinal);
        Assert.Contains("Install-AndroidDebugBuild.ps1", packageJson, StringComparison.Ordinal);
        Assert.Contains("Initialize-AndroidDevelopmentSigning", installScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-DataPreservingDebugInstall", installScript, StringComparison.Ordinal);
        Assert.Contains("LOCALAPPDATA", signingScript, StringComparison.Ordinal);
        Assert.Contains("ELECTRONIC_LOGBOOK_DEV_KEYSTORE", signingScript, StringComparison.Ordinal);
        Assert.Contains("ELECTRONIC_LOGBOOK_DEV_KEYSTORE", gradle, StringComparison.Ordinal);
        Assert.Contains("rootProject.file('../../versions.properties')", gradle, StringComparison.Ordinal);
        Assert.Contains("versionManifest.app_version", gradle, StringComparison.Ordinal);
        Assert.Contains("versionManifest.android_version_code", gradle, StringComparison.Ordinal);
        Assert.Contains("$packageName = \"com.alphadelta.electroniclogbook.dev\"", installScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-DataPreservingDebugInstall", installScript, StringComparison.Ordinal);
        Assert.Contains("adb devices failed", installScript, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = \"Continue\"", installScript, StringComparison.Ordinal);
        Assert.Contains("\"install\", \"-r\"", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("\"pm\", \"list\", \"packages\"", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("[switch] $SkipLaunch", installScript, StringComparison.Ordinal);
        Assert.Contains("if (-not $SkipLaunch)", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("android.intent.category.LAUNCHER", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("New-VerifiedIndexedDbSnapshot", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("app_webview/Default/IndexedDB", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("Assert-EquivalentIndexedDbSnapshots", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("source-backup-verified", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("\"uninstall\", $PackageName", bridgeScript, StringComparison.Ordinal);
        Assert.Contains("this script will not uninstall an inaccessible", bridgeScript, StringComparison.Ordinal);
        Assert.True(
            bridgeScript.IndexOf("source-backup-verified", StringComparison.Ordinal) <
            bridgeScript.IndexOf("\"uninstall\", $PackageName", StringComparison.Ordinal));
        Assert.DoesNotContain("pm clear", bridgeScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileAssemblyVersionComesFromTheCentralVersionManifest()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var project = File.ReadAllText(Path.Combine(
            mobileRoot,
            "src",
            "ElectronicLogbook.Mobile",
            "ElectronicLogbook.Mobile.csproj"));

        Assert.Contains("versions.properties", project, StringComparison.Ordinal);
        Assert.Contains("app_version", project, StringComparison.Ordinal);
        Assert.Contains("<Version>$(AppVersionFromManifest)</Version>", project, StringComparison.Ordinal);
        Assert.Contains("ValidateAppVersionManifest", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidDebugBuildUsesAStableDevelopmentOnlyAppName()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var debugStrings = File.ReadAllText(Path.Combine(
            mobileRoot,
            "android",
            "app",
            "src",
            "debug",
            "res",
            "values",
            "strings.xml"));
        var productionStrings = File.ReadAllText(Path.Combine(
            mobileRoot,
            "android",
            "app",
            "src",
            "main",
            "res",
            "values",
            "strings.xml"));

        Assert.Contains("<string name=\"app_name\">FlightLogX Dev</string>", debugStrings, StringComparison.Ordinal);
        Assert.Contains("<string name=\"title_activity_main\">FlightLogX Dev</string>", debugStrings, StringComparison.Ordinal);
        Assert.Contains("<string name=\"package_name\">com.alphadelta.electroniclogbook.dev</string>", debugStrings, StringComparison.Ordinal);
        Assert.DoesNotContain("FlightLogX Gate 3", debugStrings, StringComparison.Ordinal);
        Assert.Contains("<string name=\"app_name\">FlightLogX</string>", productionStrings, StringComparison.Ordinal);
        Assert.Contains("<string name=\"title_activity_main\">FlightLogX</string>", productionStrings, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidGoogleServicesConfigurationIsRestrictedToThePermanentPreviewVariant()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var gradle = File.ReadAllText(Path.Combine(mobileRoot, "android", "app", "build.gradle"));

        Assert.Contains("def googleServicesRequested", gradle, StringComparison.Ordinal);
        Assert.Contains("contains('preview')", gradle, StringComparison.Ordinal);
        Assert.Contains("if (googleServicesRequested)", gradle, StringComparison.Ordinal);
        Assert.Contains("Preview builds require mobile/android/app/google-services.json", gradle, StringComparison.Ordinal);
        Assert.Contains("apply plugin: 'com.google.gms.google-services'", gradle, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidShellRefreshesBundledWebAssetsAfterAnInPlaceUpdateWithoutClearingUserState()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var activity = File.ReadAllText(Path.Combine(
            mobileRoot,
            "android",
            "app",
            "src",
            "main",
            "java",
            "com",
            "alphadelta",
            "electroniclogbook",
            "MainActivity.java"));

        Assert.Contains("refreshBundledWebAssetsAfterUpdate();", activity, StringComparison.Ordinal);
        Assert.Contains(".lastUpdateTime", activity, StringComparison.Ordinal);
        Assert.Contains("getSharedPreferences(WEB_ASSET_STATE, MODE_PRIVATE)", activity, StringComparison.Ordinal);
        Assert.Contains("getBridge().getWebView().clearCache(true);", activity, StringComparison.Ordinal);
        Assert.Contains("getBridge().getWebView().reload();", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("clearData", activity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleteDatabase", activity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IndexedDB", activity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidUpgradePreservesProductionAndDebugApplicationIdentity()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var capacitorConfig = File.ReadAllText(Path.Combine(mobileRoot, "capacitor.config.json"));
        var gradle = File.ReadAllText(Path.Combine(mobileRoot, "android", "app", "build.gradle"));
        var acceptancePrepScript = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "Test-MobileAcceptancePrep.ps1"));
        var browserFileStoreTests = File.ReadAllText(Path.Combine(
            mobileRoot,
            "tests",
            "ElectronicLogbook.Mobile.Tests",
            "BrowserFileStoreTests.cs"));

        Assert.Contains("\"appId\": \"com.alphadelta.electroniclogbook\"", capacitorConfig, StringComparison.Ordinal);
        Assert.Contains("applicationId = \"com.alphadelta.electroniclogbook\"", gradle, StringComparison.Ordinal);
        Assert.Contains("applicationIdSuffix \".dev\"", gradle, StringComparison.Ordinal);
        Assert.Contains("applicationIdSuffix \".acceptance\"", gradle, StringComparison.Ordinal);
        Assert.Contains("versionCode = androidVersionCode", gradle, StringComparison.Ordinal);
        Assert.Contains("versionName = appVersion", gradle, StringComparison.Ordinal);
        Assert.Contains("Assert-Equal -Actual $metadata.applicationId -Expected \"com.alphadelta.electroniclogbook.dev\"", acceptancePrepScript, StringComparison.Ordinal);
        Assert.Contains("/Android/data/com.alphadelta.electroniclogbook/files/exports/logbook.elogbook", browserFileStoreTests, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidAcceptanceWorkflowUsesAnIsolatedDataPreservingPackage()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var packageJson = File.ReadAllText(Path.Combine(mobileRoot, "package.json"));
        var gradle = File.ReadAllText(Path.Combine(mobileRoot, "android", "app", "build.gradle"));
        var installScript = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "Install-AndroidAcceptanceBuild.ps1"));
        var acceptanceManifest = File.ReadAllText(Path.Combine(
            mobileRoot,
            "android",
            "app",
            "src",
            "acceptance",
            "AndroidManifest.xml"));

        Assert.Contains("install:android:acceptance", packageJson, StringComparison.Ordinal);
        Assert.Contains("Install-AndroidAcceptanceBuild.ps1", packageJson, StringComparison.Ordinal);
        Assert.Contains("acceptance", gradle, StringComparison.Ordinal);
        Assert.Contains("applicationIdSuffix \".acceptance\"", gradle, StringComparison.Ordinal);
        Assert.Contains("assembleAcceptance", installScript, StringComparison.Ordinal);
        Assert.Contains("Initialize-AndroidDevelopmentSigning", installScript, StringComparison.Ordinal);
        Assert.Contains("\"install\", \"-r\"", installScript, StringComparison.Ordinal);
        Assert.Contains("android:allowBackup=\"false\"", acceptanceManifest, StringComparison.Ordinal);
        Assert.Contains("tools:replace=\"android:allowBackup\"", acceptanceManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb uninstall", installScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileAcceptancePackageWorkflowQuotesNativeArgumentsContainingSpaces()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var script = File.ReadAllText(Path.Combine(mobileRoot, "scripts", "Test-MobileAcceptancePackage.ps1"));

        Assert.Contains("function ConvertTo-ProcessArgument", script, StringComparison.Ordinal);
        Assert.Contains("$Argument -match \"\\s\"", script, StringComparison.Ordinal);
        Assert.Contains("ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }", script, StringComparison.Ordinal);
    }

    private static string ReadWorkflow() =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            ".github",
            "workflows",
            "publish-mobile-pwa.yml")));
}
