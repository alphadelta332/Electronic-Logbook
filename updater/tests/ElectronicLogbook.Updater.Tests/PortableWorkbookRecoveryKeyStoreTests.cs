using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableWorkbookRecoveryKeyStoreTests
{
    [Fact]
    public void SeparateRecoveryKeypairRoundTripsThroughWindowsCredentialManager()
    {
        var logbookId = LogbookId.New();
        var deviceId = DeviceId.New();
        var targetName = PortableWorkbookRecoveryKeyStore.CreateTargetName(logbookId, deviceId);
        var packageKey = PortableLogbookKey.Generate();
        using var original = PortableWorkbookRecoveryKeyPair.Create();

        try
        {
            PortableWorkbookRecoveryKeyStore.Save(targetName, original);
            using var loaded = PortableWorkbookRecoveryKeyStore.Load(targetName);

            Assert.NotNull(loaded);
            Assert.Equal(original.PublicKey, loaded.PublicKey);
            Assert.Equal(original.Fingerprint, loaded.Fingerprint);
            Assert.NotEqual(
                PortableLogbookWindowsCredentialStore.CreateTargetName(logbookId, deviceId),
                targetName);

            using var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(loaded.PublicKey), out _);
            var wrapped = rsa.Encrypt(
                packageKey.ToBytes(),
                System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);
            Assert.Equal(packageKey, loaded.DecryptPackageKey(Convert.ToBase64String(wrapped)));
        }
        finally
        {
            PortableWorkbookRecoveryKeyStore.Delete(targetName);
        }
    }
}
