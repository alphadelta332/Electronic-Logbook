namespace ElectronicLogbook.Updater.Tests;

public sealed class LegacyPreviewSourceResolverTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LegacyPreviewSourceResolverTests-{Guid.NewGuid():N}");

    public LegacyPreviewSourceResolverTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void ResolveRecoversOneExactLegacyPreviewWorkbookFromOneDrive()
    {
        var requested = Path.Combine(
            tempDirectory,
            "Documents",
            "Electronic Logbook - Working Copy.xlsm");
        var oneDrive = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "OneDrive")).FullName;
        var nested = Directory.CreateDirectory(
            Path.Combine(oneDrive, "WorkUni", "Logbook")).FullName;
        var source = TestRepo.CreateMinimalWorkbookPackage(
            nested,
            "2.0.3",
            Path.GetFileName(requested),
            githubBranch: "pilot");
        var master = TestRepo.CreateMinimalWorkbookPackage(
            tempDirectory,
            "3.0.0",
            "master.xlsm",
            githubBranch: "dev");

        var resolved = LegacyPreviewSourceResolver.Resolve(
            requested,
            master,
            [oneDrive]);

        Assert.Equal(Path.GetFullPath(source), resolved);
    }

    [Fact]
    public void ResolveFailsClosedWhenMoreThanOneWorkbookMatches()
    {
        var fileName = "Electronic Logbook - Working Copy.xlsm";
        var requested = Path.Combine(tempDirectory, "Documents", fileName);
        var oneDrive = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "OneDrive")).FullName;
        foreach (var folderName in new[] { "First", "Second" })
        {
            var folder = Directory.CreateDirectory(
                Path.Combine(oneDrive, folderName)).FullName;
            TestRepo.CreateMinimalWorkbookPackage(
                folder,
                "2.0.3",
                fileName,
                githubBranch: "pilot");
        }
        var master = TestRepo.CreateMinimalWorkbookPackage(
            tempDirectory,
            "3.0.0",
            "master.xlsm");

        var resolved = LegacyPreviewSourceResolver.Resolve(
            requested,
            master,
            [oneDrive]);

        Assert.Equal(Path.GetFullPath(requested), resolved);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("dev")]
    [InlineData("main")]
    public void ResolveDoesNotPromoteNonLegacyBranches(string branch)
    {
        var fileName = "Electronic Logbook - Working Copy.xlsm";
        var requested = Path.Combine(tempDirectory, "Documents", fileName);
        var oneDrive = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "OneDrive")).FullName;
        TestRepo.CreateMinimalWorkbookPackage(
            oneDrive,
            "2.0.3",
            fileName,
            githubBranch: branch);
        var master = TestRepo.CreateMinimalWorkbookPackage(
            tempDirectory,
            "3.0.0",
            "master.xlsm");

        var resolved = LegacyPreviewSourceResolver.Resolve(
            requested,
            master,
            [oneDrive]);

        Assert.Equal(Path.GetFullPath(requested), resolved);
    }

    [Fact]
    public void ResolveKeepsAnExistingRequestedSource()
    {
        var documents = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "Documents")).FullName;
        var requested = TestRepo.CreateMinimalWorkbookPackage(
            documents,
            "2.0.3",
            "Electronic Logbook - Working Copy.xlsm",
            githubBranch: "pilot");
        var oneDrive = Directory.CreateDirectory(
            Path.Combine(tempDirectory, "OneDrive")).FullName;
        TestRepo.CreateMinimalWorkbookPackage(
            oneDrive,
            "2.0.3",
            Path.GetFileName(requested),
            githubBranch: "pilot");
        var master = TestRepo.CreateMinimalWorkbookPackage(
            tempDirectory,
            "3.0.0",
            "master.xlsm");

        var resolved = LegacyPreviewSourceResolver.Resolve(
            requested,
            master,
            [oneDrive]);

        Assert.Equal(Path.GetFullPath(requested), resolved);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch
        {
            // Best effort test cleanup.
        }
    }
}
