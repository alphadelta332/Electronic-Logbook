namespace ElectronicLogbook.Portable;

public static class PortableLogbookExchangeStatus
{
    public const string ManualBackupRequiredNotice =
        "Browser or workbook storage is not a backup. Export a portable logbook package and keep it somewhere separate.";

    public static PortableLogbookExchangeStatusSnapshot Create(
        PortableLogbookWorkingCopyState workingCopy,
        IEnumerable<PortableLogbookPackageReceipt> importReceipts,
        DateTimeOffset? lastSuccessfulExportAt)
    {
        ArgumentNullException.ThrowIfNull(workingCopy);
        ArgumentNullException.ThrowIfNull(importReceipts);

        var lastImport = importReceipts
            .OrderByDescending(receipt => receipt.ImportedAt)
            .ThenByDescending(receipt => receipt.PackageCreatedAt)
            .FirstOrDefault();

        return new PortableLogbookExchangeStatusSnapshot(
            workingCopy.ExportRequired,
            workingCopy.PendingOperationCount,
            lastImport?.ImportedAt,
            lastImport?.PackageCreatedAt,
            lastSuccessfulExportAt,
            ManualBackupRequiredNotice);
    }
}

public sealed record PortableLogbookExchangeStatusSnapshot(
    bool HasUnexportedChanges,
    int PendingOperationCount,
    DateTimeOffset? LastSuccessfulImportAt,
    DateTimeOffset? LastImportedPackageCreatedAt,
    DateTimeOffset? LastSuccessfulExportAt,
    string BackupNotice);
