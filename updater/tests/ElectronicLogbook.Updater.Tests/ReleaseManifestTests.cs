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
