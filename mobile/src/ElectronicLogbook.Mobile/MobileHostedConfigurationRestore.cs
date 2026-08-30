using System.Security.Cryptography;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class MobileHostedConfigurationRestore(
    BrowserPackageKeyStore keyStore,
    IHostedConfigurationRevisionLedger configurationLedger)
{
    private const int MaxContinuationRuns = 100;

    public async ValueTask<PortableHostedConfigurationRevision?> RestoreLatestAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default)
    {
        HostedConfigurationRevisionEnvelope? latest = null;
        var afterHostedRevision = 0L;
        for (var run = 0; run < MaxContinuationRuns; run++)
        {
            var page = await configurationLedger.ReadConfigurationRevisionsAsync(
                logbookId,
                afterHostedRevision,
                IHostedConfigurationRevisionLedger.MaxConfigurationPageSize,
                cancellationToken);
            if (page.Revisions.Any(revision => revision.HostedRevision <= afterHostedRevision))
            {
                throw new InvalidDataException(
                    "Hosted configuration history repeated an acknowledged revision.");
            }

            var pageLatest = page.Revisions.MaxBy(revision => revision.HostedRevision);
            if (pageLatest is not null
                && (latest is null || pageLatest.HostedRevision > latest.HostedRevision))
            {
                latest = pageLatest;
            }

            if (!page.HasMore)
            {
                return latest is null
                    ? null
                    : await DecryptAsync(logbookId, latest, cancellationToken);
            }
            if (page.ThroughHostedRevision <= afterHostedRevision)
            {
                throw new InvalidDataException(
                    "Hosted configuration history did not advance to the next page.");
            }

            afterHostedRevision = page.ThroughHostedRevision;
        }

        throw new InvalidDataException(
            "Hosted configuration history exceeded the safe continuation limit.");
    }

    private async ValueTask<PortableHostedConfigurationRevision> DecryptAsync(
        LogbookId logbookId,
        HostedConfigurationRevisionEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ciphertext = Convert.FromBase64String(envelope.PayloadCiphertext);
            var expectedHash = Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant();
            if (!string.Equals(expectedHash, envelope.PayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new HostedConfigurationRevisionCipherException(
                    "Hosted configuration payload hash does not match the ciphertext.");
            }

            var plaintext = await keyStore.DecryptAsync(
                logbookId,
                Convert.FromBase64String(envelope.PayloadNonce),
                ciphertext,
                Convert.FromBase64String(envelope.PayloadTag),
                HostedConfigurationRevisionCipher.CreateAdditionalData(logbookId, envelope));
            try
            {
                return HostedConfigurationRevisionCipher.DeserializeDecryptedPayload(
                    logbookId,
                    envelope,
                    plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (HostedConfigurationRevisionCipherException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JSException)
        {
            throw new HostedConfigurationRevisionCipherException(
                "Hosted configuration payload is invalid.",
                ex);
        }
    }
}
