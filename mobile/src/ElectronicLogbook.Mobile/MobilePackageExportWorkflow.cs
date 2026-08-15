using System.Security.Cryptography;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobilePackageExportWorkflow
{
    public static async ValueTask<MobilePackageExportWorkflowResult> ExportAsync(
        PortableLogbookDocumentV2 document,
        BrowserPackageKeyStore keyStore,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt,
        MobilePackageExportDestination destination = MobilePackageExportDestination.Share)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(fileStore);

        var plan = MobilePackageExportPlan.Create(
            document,
            await keyStore.HasPackageKeyAsync(document.LogbookId).ConfigureAwait(false),
            exportedAt);
        var encryptionPlan = PortableLogbookPackage.CreateEncryptionPlan(document, exportedAt);
        var encrypted = await keyStore
            .EncryptAsync(
                document.LogbookId,
                encryptionPlan.Nonce,
                encryptionPlan.CompressedPlaintext,
                encryptionPlan.ManifestBytes)
            .ConfigureAwait(false);
        var packageBytes = PortableLogbookPackage.Assemble(encryptionPlan, encrypted.Ciphertext, encrypted.Tag);

        var transfer = destination == MobilePackageExportDestination.SaveToDevice
            ? await fileStore.SaveToDeviceAsync(plan.FileName, packageBytes, plan.ContentType).ConfigureAwait(false)
            : await fileStore.ShareOrDownloadAsync(plan.FileName, packageBytes, plan.ContentType).ConfigureAwait(false);

        return new MobilePackageExportWorkflowResult(
            plan.FileName,
            plan.ContentType,
            plan.ExportedAt,
            Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant(),
            packageBytes,
            transfer);
    }

    public static async ValueTask<MobilePackageExportWorkflowResult> ExportAsync(
        PortableLogbookDocument document,
        BrowserPackageKeyStore keyStore,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(fileStore);

        var plan = MobilePackageExportPlan.Create(
            document,
            await keyStore.HasPackageKeyAsync(document.LogbookId).ConfigureAwait(false),
            exportedAt);
        var encryptionPlan = PortableLogbookPackage.CreateEncryptionPlan(document, exportedAt);
        var encrypted = await keyStore
            .EncryptAsync(
                document.LogbookId,
                encryptionPlan.Nonce,
                encryptionPlan.CompressedPlaintext,
                encryptionPlan.ManifestBytes)
            .ConfigureAwait(false);
        var packageBytes = PortableLogbookPackage.Assemble(encryptionPlan, encrypted.Ciphertext, encrypted.Tag);

        var transfer = await fileStore.ShareOrDownloadAsync(plan.FileName, packageBytes, plan.ContentType).ConfigureAwait(false);

        return new MobilePackageExportWorkflowResult(
            plan.FileName,
            plan.ContentType,
            plan.ExportedAt,
            Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant(),
            packageBytes,
            transfer);
    }
}

public sealed record MobilePackageExportWorkflowResult(
    string FileName,
    string ContentType,
    DateTimeOffset ExportedAt,
    string PackageSha256,
    byte[] PackageBytes,
    BrowserFileTransferResult Transfer);

public enum MobilePackageExportDestination
{
    Share,
    SaveToDevice
}
