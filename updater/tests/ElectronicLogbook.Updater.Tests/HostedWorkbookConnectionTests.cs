using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class HostedWorkbookConnectionTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"HostedWorkbookConnectionTests-{Guid.NewGuid():N}");

    public HostedWorkbookConnectionTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void ConnectionRebindsWorkbookToHostedIdentityAndPersistsThreeScopedCredentials()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var accountId = new HostedAccountId("acct_" + Guid.NewGuid().ToString("N"));
        var logbookId = LogbookId.New();
        var deviceId = DeviceId.New();
        var packageKey = PortableLogbookKey.Generate();
        var credential = new PortableHostedCredential(
            "access-token",
            "refresh-token",
            DateTimeOffset.Parse("2026-08-15T12:00:00Z"));
        using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();
        PortableHostedConnectionResult? result = null;

        try
        {
            result = PortableLogbookCommandRunner.ConnectHostedWorkbook(
                workbook,
                accountId,
                logbookId,
                deviceId,
                credential,
                packageKey,
                recoveryKeyPair,
                DateTimeOffset.Parse("2026-08-15T10:00:00Z"));

            var identity = PortableLogbookWorkbookPackageStorage.ReadWorkbookIdentityMetadata(workbook);
            var hosted = PortableLogbookWorkbookPackageStorage.ReadHostedWorkbookMetadata(workbook);
            var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, packageKey);
            using var storedRecovery = PortableWorkbookRecoveryKeyStore.Load(result.RecoveryKeyTargetName);

            Assert.True(File.Exists(result.BackupPath));
            Assert.Equal(logbookId, identity?.LogbookId);
            Assert.Equal(deviceId, identity?.DeviceId);
            Assert.Equal(accountId, hosted?.AccountId);
            Assert.Equal("Signing in", hosted?.Status);
            Assert.Equal(logbookId, state?.Document.LogbookId);
            Assert.Equal(packageKey, PortableLogbookWindowsCredentialStore.LoadKey(result.PackageKeyTargetName));
            Assert.Equal(credential, PortableHostedCredentialStore.Load(result.HostedCredentialTargetName));
            Assert.Equal(recoveryKeyPair.Fingerprint, storedRecovery?.Fingerprint);
        }
        finally
        {
            if (result is not null)
            {
                PortableHostedCredentialStore.Delete(result.HostedCredentialTargetName);
                PortableLogbookWindowsCredentialStore.DeleteKey(result.PackageKeyTargetName);
                PortableWorkbookRecoveryKeyStore.Delete(result.RecoveryKeyTargetName);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Test cleanup only.
        }
    }
}
