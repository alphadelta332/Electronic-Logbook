namespace ElectronicLogbook.Portable;

public static class PortableLogbookPackageImport
{
    public static PortableLogbookPackageImportResult ImportPackage(
        PortableLogbookDocument localDocument,
        byte[] incomingPackageBytes,
        PortableLogbookKey key,
        IEnumerable<PortableLogbookPackageReceipt> existingReceipts,
        DateTimeOffset importedAt,
        PortableLogbookPackageReadOptions? readOptions = null)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        ArgumentNullException.ThrowIfNull(incomingPackageBytes);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(existingReceipts);

        var receipts = existingReceipts.ToArray();
        if (PortableLogbookImportLedger.HasSeenPackage(receipts, incomingPackageBytes))
        {
            return PortableLogbookPackageImportResult.PackageReplay(localDocument, receipts);
        }

        var read = PortableLogbookPackage.Read(
            incomingPackageBytes,
            key,
            localDocument.LogbookId,
            readOptions);
        var plan = PortableLogbookExchange.PlanImport(localDocument, read.Document);
        if (plan.Status is PortableLogbookImportPlanStatus.RequiresConflictResolution or
            PortableLogbookImportPlanStatus.RequiresCustomFieldResolution)
        {
            return PortableLogbookPackageImportResult.RequiresResolution(localDocument, receipts, plan);
        }

        var importedDocument = plan.Status == PortableLogbookImportPlanStatus.DuplicateOnly
            ? localDocument
            : PortableLogbookExchange.ApplyImport(localDocument, read.Document);
        var receipt = PortableLogbookImportLedger.CreateReceipt(incomingPackageBytes, read.Manifest, importedAt);
        var updatedReceipts = receipts.Concat([receipt]).ToArray();
        var encryptedHistoryPackage = PortableLogbookPackage.Write(importedDocument, key);
        var storageEnvelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            importedDocument,
            encryptedHistoryPackage,
            updatedReceipts);

        return PortableLogbookPackageImportResult.Imported(
            plan.Status == PortableLogbookImportPlanStatus.DuplicateOnly
                ? PortableLogbookPackageImportStatus.DuplicateOperationsRecorded
                : PortableLogbookPackageImportStatus.Applied,
            importedDocument,
            PortableLogbookWorkbookProjection.CreateCurrentRows(importedDocument),
            updatedReceipts,
            plan,
            receipt,
            encryptedHistoryPackage,
            storageEnvelope);
    }
}

public sealed record PortableLogbookPackageImportResult(
    PortableLogbookPackageImportStatus Status,
    PortableLogbookDocument Document,
    IReadOnlyList<PortableLogbookWorkbookRow>? WorkbookRows,
    IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts,
    PortableLogbookImportPlan? Plan,
    PortableLogbookPackageReceipt? NewReceipt,
    byte[]? EncryptedHistoryPackage,
    PortableLogbookWorkbookStorageEnvelope? StorageEnvelope)
{
    public static PortableLogbookPackageImportResult PackageReplay(
        PortableLogbookDocument document,
        IReadOnlyList<PortableLogbookPackageReceipt> receipts) =>
        new(
            PortableLogbookPackageImportStatus.PackageReplay,
            document,
            null,
            receipts,
            null,
            null,
            null,
            null);

    public static PortableLogbookPackageImportResult RequiresResolution(
        PortableLogbookDocument document,
        IReadOnlyList<PortableLogbookPackageReceipt> receipts,
        PortableLogbookImportPlan plan) =>
        new(
            PortableLogbookPackageImportStatus.RequiresResolution,
            document,
            null,
            receipts,
            plan,
            null,
            null,
            null);

    public static PortableLogbookPackageImportResult Imported(
        PortableLogbookPackageImportStatus status,
        PortableLogbookDocument document,
        IReadOnlyList<PortableLogbookWorkbookRow> workbookRows,
        IReadOnlyList<PortableLogbookPackageReceipt> receipts,
        PortableLogbookImportPlan plan,
        PortableLogbookPackageReceipt receipt,
        byte[] encryptedHistoryPackage,
        PortableLogbookWorkbookStorageEnvelope storageEnvelope) =>
        new(
            status,
            document,
            workbookRows,
            receipts,
            plan,
            receipt,
            encryptedHistoryPackage,
            storageEnvelope);
}

public enum PortableLogbookPackageImportStatus
{
    PackageReplay,
    DuplicateOperationsRecorded,
    Applied,
    RequiresResolution
}
