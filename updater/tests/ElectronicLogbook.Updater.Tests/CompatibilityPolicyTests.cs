namespace ElectronicLogbook.Updater.Tests;

public sealed class CompatibilityPolicyTests
{
    private const string CompatibilityFloor = "2.0.2";
    private const string CompatibilityFloorTag = "v" + CompatibilityFloor;

    [Fact]
    public void SupportedTagsIncludesFloorAndExcludesOlderTags()
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");
        var currentVersion = TestRepo.Version;
        var currentTag = "v" + currentVersion;

        var tags = policy.SupportedTags(
            ["v1.4.2", "v1.9.9", "v2.0.0", "v2.0.1", CompatibilityFloorTag, currentTag],
            currentVersion);

        Assert.Equal([CompatibilityFloorTag], tags);
    }

    [Fact]
    public void SupportedTagsSortsSemantically()
    {
        var policy = new CompatibilityPolicy(CompatibilityFloor, "git-tags");

        var tags = policy.SupportedTags(
            ["v2.0.10", "v2.0.2", CompatibilityFloorTag, "v2.0.9"],
            "3.0.0");

        Assert.Equal([CompatibilityFloorTag, "v2.0.2", "v2.0.9", "v2.0.10"], tags);
    }

    [Fact]
    public void LoadAcceptsTrackedPolicy()
    {
        var policyPath = TestRepo.FindFile("updater", "compatibility-policy.json");

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
    [InlineData("1.4.2", false)]
    [InlineData("1.9.9", false)]
    [InlineData("2.0.0", false)]
    [InlineData("v2.0.1", false)]
    [InlineData("2.0.2", true)]
    [InlineData("v2.0.2", true)]
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
            policy.ThrowIfUnsupported("1.4.2"));

        Assert.Contains("1.4.2", exception.Message, StringComparison.Ordinal);
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

}
