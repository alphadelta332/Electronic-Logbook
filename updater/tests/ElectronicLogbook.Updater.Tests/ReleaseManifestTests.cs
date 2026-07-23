namespace ElectronicLogbook.Updater.Tests;

public sealed class ReleaseManifestTests
{
    private static readonly ReleaseAsset ValidAsset =
        new("Electronic_Logbook_Master.xlsm", 100, new string('a', 64));

    [Fact]
    public void ValidateManifestAcceptsMatchingMetadata()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset]);

        ReleaseClient.ValidateManifest(manifest, "v1.4.0");
    }

    [Fact]
    public void ValidateManifestRejectsMismatchedTag()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset]);

        Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.1"));
    }

    [Fact]
    public void ValidateManifestRejectsTagThatDoesNotMatchVersion()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.1",
            new string('b', 40),
            [ValidAsset]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.1"));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateManifestRejectsMissingMetadata()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            string.Empty,
            [ValidAsset]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));

        Assert.Contains("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-a-sha")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbz")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void ValidateManifestRejectsInvalidCommitSha(string commit)
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            commit,
            [ValidAsset]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));

        Assert.Contains("SHA", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateManifestRejectsEmptyAssets()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            []);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));

        Assert.Contains("assets", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateManifestRejectsInvalidHash()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset with { Sha256 = "bad" }]);

        Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Electronic_Logbook_Master.xlsm", 0)]
    [InlineData("Electronic_Logbook_Master.xlsm", -1)]
    public void ValidateManifestRejectsInvalidAssetMetadata(string name, long size)
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset with { Name = name, Size = size }]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));

        Assert.Contains("asset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateManifestRejectsDuplicateAssets()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset, ValidAsset]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
