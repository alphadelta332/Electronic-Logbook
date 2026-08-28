using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableWorkbookMigrationRecoveryStoreTests
{
    [Fact]
    public void TemporaryMigrationKeysResumeFromOneCredentialAndDeleteAfterCompletion()
    {
        var logbookId = LogbookId.New();
        var deviceId = DeviceId.New();
        var targetName = PortableWorkbookMigrationRecoveryStore.CreateTargetName(logbookId, deviceId);

        try
        {
            using var created = PortableWorkbookMigrationRecoveryStore.LoadOrCreate(logbookId, deviceId);
            using var resumed = PortableWorkbookMigrationRecoveryStore.LoadOrCreate(logbookId, deviceId);

            Assert.False(created.Resumed);
            Assert.True(resumed.Resumed);
            Assert.Equal(created.CredentialTargetName, resumed.CredentialTargetName);
            Assert.Equal(created.LogbookKey, resumed.LogbookKey);
            Assert.Equal(created.RecoveryKeyPair.Fingerprint, resumed.RecoveryKeyPair.Fingerprint);
            Assert.StartsWith("ElectronicLogbook.WorkbookMigration/", targetName, StringComparison.Ordinal);

            Assert.True(PortableWorkbookMigrationRecoveryStore.Delete(targetName));
            Assert.Null(PortableWorkbookMigrationRecoveryStore.Load(targetName));
        }
        finally
        {
            PortableWorkbookMigrationRecoveryStore.Delete(targetName);
        }
    }
}
