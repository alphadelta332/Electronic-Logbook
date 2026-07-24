using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

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
        byte[] compressedPlaintext;
        try
        {
            compressedPlaintext = await keyStore
                .DecryptAsync(
                    localDocument.LogbookId,
                    decryptionPlan.Nonce,
                    decryptionPlan.Ciphertext,
                    decryptionPlan.Tag,
                    decryptionPlan.ManifestBytes)
                .ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw new MobilePackageImportWorkflowException(
                "Package could not be decrypted with the browser key stored on this device. " +
                "If browser storage was reset or this is a replacement install, preview the package, " +
                "restore the workbook recovery code, then import the package again.",
                ex);
        }
        var package = PortableLogbookPackage.ReadDecrypted(decryptionPlan, compressedPlaintext, localDocument.LogbookId);

        return new MobilePackageImportWorkflowResult(importPlan, package.Manifest, package.Document);
    }
}

public sealed record MobilePackageImportWorkflowResult(
    MobilePackageImportPlanResult ImportPlan,
    PortableLogbookPackageManifest Manifest,
    PortableLogbookDocument Document);

public sealed class MobilePackageImportWorkflowException : Exception
{
    public MobilePackageImportWorkflowException(string message)
        : base(message)
    {
    }

    public MobilePackageImportWorkflowException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
