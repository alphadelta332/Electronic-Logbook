using System.Security.Cryptography;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed class MobileHostedConfigurationPublisher(
    BrowserPackageKeyStore keyStore,
    IHostedConfigurationRevisionLedger configurationLedger)
{
    public async ValueTask<HostedConfigurationRevisionEnvelope> PublishAsync(
        PortableLogbookDocumentV2 document,
        RevisionId revisionId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var revision = PortableHostedConfigurationRevision.Create(
            document,
            revisionId,
            deviceId,
            createdAt);
        var plaintext = HostedConfigurationRevisionCipher.CreateCompressedPayload(revision);
        try
        {
            var nonce = HostedConfigurationRevisionCipher.DeriveNonce(
                revision.LogbookId,
                revision.RevisionId,
                plaintext);
            var encrypted = await keyStore.EncryptAsync(
                revision.LogbookId,
                nonce,
                plaintext,
                HostedConfigurationRevisionCipher.CreateAdditionalData(revision));
            var upload = new HostedConfigurationRevisionUpload(
                revision.RevisionId,
                revision.DeviceId,
                revision.CreatedAt,
                revision.SchemaVersion,
                Convert.ToBase64String(encrypted.Ciphertext),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(encrypted.Tag),
                Convert.ToHexString(SHA256.HashData(encrypted.Ciphertext)).ToLowerInvariant());
            return await configurationLedger.AppendConfigurationRevisionAsync(
                revision.LogbookId,
                revision.DeviceId,
                upload,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
