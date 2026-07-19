using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobilePackageImportWorkflow
{
    public static async ValueTask<MobilePackageImportWorkflowResult> ReadAsync(
        PortableLogbookDocument localDocument,
        BrowserFile file,
        BrowserPackageKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyStore);

        var importPlan = MobilePackageImportPlan.Inspect(file);
        var compatibility = MobilePackageImportPlan.CheckCompatibility(importPlan, localDocument.LogbookId);
        if (compatibility != MobilePackageImportCompatibility.Compatible)
        {
            throw new MobilePackageImportWorkflowException(
                $"Package is not compatible with this logbook: {compatibility}.");
        }

        var decryptionPlan = PortableLogbookPackage.CreateDecryptionPlan(file.Bytes, localDocument.LogbookId);
        var compressedPlaintext = await keyStore
            .DecryptAsync(
                localDocument.LogbookId,
                decryptionPlan.Nonce,
                decryptionPlan.Ciphertext,
                decryptionPlan.Tag,
                decryptionPlan.ManifestBytes)
            .ConfigureAwait(false);
        var package = PortableLogbookPackage.ReadDecrypted(decryptionPlan, compressedPlaintext, localDocument.LogbookId);

        return new MobilePackageImportWorkflowResult(importPlan, package.Manifest, package.Document);
    }
}

public sealed record MobilePackageImportWorkflowResult(
    MobilePackageImportPlanResult ImportPlan,
    PortableLogbookPackageManifest Manifest,
    PortableLogbookDocument Document);

public sealed class MobilePackageImportWorkflowException(string message) : Exception(message);
