using System.Security.Cryptography;

namespace ElectronicLogbook.Portable;

public static class PortableLogbookImportLedger
{
    public static PortableLogbookPackageReceipt CreateReceipt(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookPackageManifest manifest,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new PortableLogbookPackageReceipt(
            Convert.ToHexString(SHA256.HashData(packageBytes)),
            manifest.LogbookId,
            manifest.OperationCount,
            manifest.CreatedAt,
            importedAt);
    }

    public static bool HasSeenPackage(
        IEnumerable<PortableLogbookPackageReceipt> receipts,
        ReadOnlySpan<byte> packageBytes)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var fingerprint = Convert.ToHexString(SHA256.HashData(packageBytes));
        return receipts.Any(receipt => string.Equals(receipt.PackageSha256, fingerprint, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record PortableLogbookPackageReceipt(
    string PackageSha256,
    LogbookId LogbookId,
    int OperationCount,
    DateTimeOffset PackageCreatedAt,
    DateTimeOffset ImportedAt);
