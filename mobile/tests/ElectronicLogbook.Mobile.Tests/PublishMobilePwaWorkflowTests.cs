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
        Assert.Contains("$index.Replace('<base href=\"/\" />', \"<base href=\"\"$basePath\"\" />\")", workflow, StringComparison.Ordinal);
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
