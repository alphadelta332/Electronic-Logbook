using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWindowsCredentialStoreTests
{
    [Fact]
    public void CreateTargetNameIncludesLogbookAndDeviceIds()
    {
        var targetName = PortableLogbookWindowsCredentialStore.CreateTargetName(
            new LogbookId("log_credential"),
            new DeviceId("dev_windows"));

        Assert.Equal("ElectronicLogbook.Portable/log_credential/dev_windows", targetName);
    }

    [Fact]
    public void SaveLoadDeleteKeyRoundTripsThroughWindowsCredentialManager()
    {
        var targetName = "ElectronicLogbook.Tests/" + Guid.NewGuid().ToString("N");
        var key = PortableLogbookKey.Generate();

        try
        {
            PortableLogbookWindowsCredentialStore.SaveKey(targetName, key);
            var loaded = PortableLogbookWindowsCredentialStore.LoadKey(targetName);

            Assert.Equal(key, loaded);
            Assert.True(PortableLogbookWindowsCredentialStore.DeleteKey(targetName));
            Assert.Null(PortableLogbookWindowsCredentialStore.LoadKey(targetName));
        }
        finally
        {
            PortableLogbookWindowsCredentialStore.DeleteKey(targetName);
        }
    }
}
