namespace ElectronicLogbook.Updater.Tests;

public sealed class CompatibilityPolicyTests
{
    private const string CompatibilityFloor = "1.4.1";
    private const string CompatibilityFloorTag = "v" + CompatibilityFloor;

    [Fact]
    public void SupportedTagsIncludesFloorAndExcludesOlderTags()
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");

        var tags = policy.SupportedTags(
            ["v1.3.1", "v1.4.0", CompatibilityFloorTag, "v1.4.2", "v1.5.0", "v2.0.0"],
            "2.0.0");

        Assert.Equal([CompatibilityFloorTag, "v1.4.2", "v1.5.0"], tags);
    }

    [Fact]
    public void SupportedTagsSortsSemantically()
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");

        var tags = policy.SupportedTags(
            ["v1.10.0", "v1.4.2", CompatibilityFloorTag, "v1.9.0"],
            "2.0.0");

        Assert.Equal([CompatibilityFloorTag, "v1.4.2", "v1.9.0", "v1.10.0"], tags);
    }

    [Fact]
    public void LoadAcceptsTrackedPolicy()
    {
        var policyPath = FindRepoFile("updater", "compatibility-policy.json");

        var policy = CompatibilityPolicy.Load(policyPath);

        Assert.Equal(CompatibilityFloor, policy.MinimumSupportedVersion);
        Assert.Equal("git-tags", policy.Source);
    }

    [Fact]
    public void LoadDefaultReadsEmbeddedPolicy()
    {
        var policy = CompatibilityPolicy.LoadDefault();

        Assert.Equal(CompatibilityFloor, policy.MinimumSupportedVersion);
        Assert.Equal("git-tags", policy.Source);
    }

    [Theory]
    [InlineData("1.4.0", false)]
    [InlineData("1.4.1", true)]
    [InlineData("v1.4.1", true)]
    [InlineData("1.5.0", true)]
    public void IsVersionSupportedAppliesFloor(string version, bool expected)
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");

        Assert.Equal(expected, policy.IsVersionSupported(version));
    }

    [Fact]
    public void ThrowIfUnsupportedDescribesFloor()
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");

        var exception = Assert.Throws<InvalidDataException>(() =>
            policy.ThrowIfUnsupported("1.4.0"));

        Assert.Contains("1.4.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains(CompatibilityFloor, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsInvalidVersion()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CompatibilityPolicyTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "compatibility-policy.json");
        File.WriteAllText(path, """{"minimumSupportedVersion":"1.4","source":"git-tags"}""");

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                CompatibilityPolicy.Load(path));

            Assert.Contains("semantic version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file: {Path.Combine(relativeParts)}");
    }
}
