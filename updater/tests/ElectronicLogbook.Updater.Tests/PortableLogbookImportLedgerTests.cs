using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookImportLedgerTests
{
    [Fact]
    public void CreateReceiptStoresPackageFingerprintAndManifestSummary()
    {
        var packageBytes = new byte[] { 1, 2, 3, 4 };
        var manifest = new PortableLogbookPackageManifest(
            PortableLogbookPackage.FormatVersion,
            new LogbookId("log_ledger"),
            PortableLogbookDocument.CurrentSchemaVersion,
            PortableLogbookDocument.AustraliaJurisdictionProfile,
            PortableLogbookDocument.AustraliaJurisdictionProfileVersion,
            CustomFieldCount: 0,
            OperationCount: 12,
            CreatedAt: DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Compression: "gzip",
            Encryption: "AES-256-GCM");

        var receipt = PortableLogbookImportLedger.CreateReceipt(
            packageBytes,
            manifest,
            DateTimeOffset.Parse("2026-07-18T00:01:00Z"));

        Assert.Equal("9F64A747E1B97F131FABB6B447296C9B6F0201E79FB3C5356E6C77E89B6A806A", receipt.PackageSha256);
        Assert.Equal(manifest.LogbookId, receipt.LogbookId);
        Assert.Equal(12, receipt.OperationCount);
        Assert.Equal(manifest.CreatedAt, receipt.PackageCreatedAt);
    }

    [Fact]
    public void HasSeenPackageDetectsDuplicatePackageBytes()
    {
        var packageBytes = new byte[] { 5, 6, 7 };
        var manifest = new PortableLogbookPackageManifest(
            PortableLogbookPackage.FormatVersion,
            new LogbookId("log_ledger"),
            PortableLogbookDocument.CurrentSchemaVersion,
            PortableLogbookDocument.AustraliaJurisdictionProfile,
            PortableLogbookDocument.AustraliaJurisdictionProfileVersion,
            CustomFieldCount: 0,
            OperationCount: 1,
            CreatedAt: DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Compression: "gzip",
            Encryption: "AES-256-GCM");
        var receipt = PortableLogbookImportLedger.CreateReceipt(packageBytes, manifest, DateTimeOffset.Parse("2026-07-18T00:01:00Z"));

        Assert.True(PortableLogbookImportLedger.HasSeenPackage([receipt], packageBytes));
        Assert.False(PortableLogbookImportLedger.HasSeenPackage([receipt], [5, 6, 8]));
    }
}
