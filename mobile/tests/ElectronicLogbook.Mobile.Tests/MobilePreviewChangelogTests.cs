namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePreviewChangelogTests
{
    [Fact]
    public void UpdatedUnacknowledgedPreviewShowsTheChangelog()
    {
        var installation = Installation(wasUpdated: true, acknowledgedVersionCode: 0);

        Assert.Equal(MobilePreviewChangelogDecision.Show, MobilePreviewChangelog.Evaluate(installation));
        Assert.Same(MobilePreviewChangelog.Current, MobilePreviewChangelog.ReleaseFor(installation));
    }

    [Fact]
    public void CurrentAcknowledgedPreviewDoesNotShowTheChangelogAgain()
    {
        var installation = Installation(
            wasUpdated: true,
            acknowledgedVersionCode: MobilePreviewChangelog.Current.VersionCode);

        Assert.Equal(MobilePreviewChangelogDecision.None, MobilePreviewChangelog.Evaluate(installation));
    }

    [Fact]
    public void CleanInstallIsAcknowledgedWithoutClaimingItWasUpdated()
    {
        var installation = Installation(wasUpdated: false, acknowledgedVersionCode: 0);

        Assert.Equal(
            MobilePreviewChangelogDecision.AcknowledgeSilently,
            MobilePreviewChangelog.Evaluate(installation));
    }

    [Fact]
    public void BrowserAndNonPreviewBuildsNeverShowThePreviewChangelog()
    {
        var installation = Installation(wasUpdated: true, acknowledgedVersionCode: 0) with { Enabled = false };

        Assert.Equal(MobilePreviewChangelogDecision.None, MobilePreviewChangelog.Evaluate(installation));
        Assert.Equal(MobilePreviewChangelogDecision.None, MobilePreviewChangelog.Evaluate(null));
    }

    [Fact]
    public void CurrentChangelogMatchesTheCentralVersionManifest()
    {
        var repoRoot = FindRepoRoot();
        var values = File.ReadLines(Path.Combine(repoRoot, "versions.properties"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        Assert.Equal(values["app_version"], MobilePreviewChangelog.Current.VersionName);
        Assert.Equal(long.Parse(values["android_version_code"]), MobilePreviewChangelog.Current.VersionCode);
        Assert.NotEmpty(MobilePreviewChangelog.Current.Items);
    }

    private static MobilePreviewInstallationInfo Installation(bool wasUpdated, long acknowledgedVersionCode) =>
        new(
            true,
            MobilePreviewChangelog.Current.VersionName,
            MobilePreviewChangelog.Current.VersionCode,
            wasUpdated,
            acknowledgedVersionCode);

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "versions.properties")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
