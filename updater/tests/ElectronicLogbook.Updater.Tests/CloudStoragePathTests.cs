namespace ElectronicLogbook.Updater.Tests;

public sealed class CloudStoragePathTests : IDisposable
{
    private readonly string _originalOneDriveConsumer =
        Environment.GetEnvironmentVariable("OneDriveConsumer") ?? string.Empty;
    private readonly string _originalOneDriveCommercial =
        Environment.GetEnvironmentVariable("OneDriveCommercial") ?? string.Empty;
    private readonly string _originalOneDrive =
        Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty;

    [Fact]
    public void IsLikelyCloudSyncedDetectsPathUnderOneDriveRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"OneDriveRoot-{Guid.NewGuid():N}");
        var workbook = Path.Combine(root, "Logbook", "logbook.xlsm");

        Environment.SetEnvironmentVariable("OneDriveConsumer", root);
        Environment.SetEnvironmentVariable("OneDriveCommercial", string.Empty);
        Environment.SetEnvironmentVariable("OneDrive", string.Empty);

        Assert.True(CloudStoragePath.IsLikelyCloudSynced(workbook));
    }

    [Fact]
    public void IsLikelyCloudSyncedIgnoresSimilarPrefixOutsideOneDriveRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"OneDriveRoot-{Guid.NewGuid():N}");
        var workbook = root + "-Other" + Path.DirectorySeparatorChar + "logbook.xlsm";

        Environment.SetEnvironmentVariable("OneDriveConsumer", root);
        Environment.SetEnvironmentVariable("OneDriveCommercial", string.Empty);
        Environment.SetEnvironmentVariable("OneDrive", string.Empty);

        Assert.False(CloudStoragePath.IsLikelyCloudSynced(workbook));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OneDriveConsumer", _originalOneDriveConsumer);
        Environment.SetEnvironmentVariable("OneDriveCommercial", _originalOneDriveCommercial);
        Environment.SetEnvironmentVariable("OneDrive", _originalOneDrive);
    }
}
