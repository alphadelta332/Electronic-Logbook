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
    public void AndroidDebugInstallWorkflowPreservesDeviceDataByDefault()
    {
        var packageJson = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "package.json")));
        var installScript = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "scripts",
            "Install-AndroidDebugBuild.ps1")));

        Assert.Contains("install:android:debug", packageJson, StringComparison.Ordinal);
        Assert.Contains("Install-AndroidDebugBuild.ps1", packageJson, StringComparison.Ordinal);
        Assert.Contains("\"install\", \"-r\"", installScript, StringComparison.Ordinal);
        Assert.Contains("deliberately does not clear, uninstall, or reset", installScript, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb uninstall", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("force-stop", installScript, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("install:android:acceptance", packageJson, StringComparison.Ordinal);
        Assert.Contains("Install-AndroidAcceptanceBuild.ps1", packageJson, StringComparison.Ordinal);
        Assert.Contains("acceptance", gradle, StringComparison.Ordinal);
        Assert.Contains("applicationIdSuffix \".acceptance\"", gradle, StringComparison.Ordinal);
        Assert.Contains("assembleAcceptance", installScript, StringComparison.Ordinal);
        Assert.Contains("\"install\", \"-r\"", installScript, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb uninstall", installScript, StringComparison.OrdinalIgnoreCase);
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
