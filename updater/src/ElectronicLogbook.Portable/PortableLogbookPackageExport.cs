namespace ElectronicLogbook.Portable;

public static class PortableLogbookPackageExport
{
    public static PortableLogbookPackageExportResult ExportPackage(
        PortableLogbookDocument currentDocument,
        IEnumerable<PortableLogbookMaterializedEntry> knownEntries,
        IEnumerable<PortableLogbookWorkbookRow> currentRows,
        DeviceId deviceId,
        PortableLogbookKey key,
        IEnumerable<PortableLogbookPackageReceipt> importReceipts,
        DateTimeOffset exportedAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(knownEntries);
        ArgumentNullException.ThrowIfNull(currentRows);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(importReceipts);

        var projection = PortableLogbookWorkbookProjection.Reconcile(
            knownEntries,
            currentRows,
            currentDocument.LogbookId,
            deviceId,
            exportedAt,
            idFactory);
        var workingCopy = PortableLogbookWorkingCopy.FromProjection(projection, exportedAt);
        var exportedDocument = projection.Operations.Count == 0
            ? currentDocument
            : PortableLogbookDocument.CreateAustraliaFirst(
                currentDocument.LogbookId,
                currentDocument.CustomFieldDefinitions,
                currentDocument.Operations.Concat(projection.Operations));
        var packageBytes = PortableLogbookPackage.Write(exportedDocument, key);
        var storageEnvelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            exportedDocument,
            packageBytes,
            importReceipts);

        return new PortableLogbookPackageExportResult(
            exportedDocument,
            projection,
            workingCopy,
            packageBytes,
            storageEnvelope,
            exportedAt);
    }
}

public sealed record PortableLogbookPackageExportResult(
    PortableLogbookDocument Document,
    PortableLogbookProjectionResult Projection,
    PortableLogbookWorkingCopyState WorkingCopyBeforeExport,
    byte[] PackageBytes,
    PortableLogbookWorkbookStorageEnvelope StorageEnvelope,
    DateTimeOffset ExportedAt);
