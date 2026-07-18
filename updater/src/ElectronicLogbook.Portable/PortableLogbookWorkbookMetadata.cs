namespace ElectronicLogbook.Portable;

public static class PortableLogbookWorkbookMetadata
{
    public const string LogbookIdName = "PortableLogbookId";
    public const string DeviceIdName = "PortableLogbookDeviceId";
    public const string SchemaVersionName = "PortableLogbookSchemaVersion";
    public const string OperationHistoryPartName = "portable-logbook-history.elogbook";
    public const string ImportLedgerPartName = "portable-logbook-import-ledger.json";
    public const string StorageCustomXmlPartPath = "customXml/portable-logbook-storage.xml";
    public const string CustomXmlNamespace = "urn:electronic-logbook:portable:v1";

    public static IReadOnlyList<PortableLogbookMetadataColumnDefinition> HiddenLogbookColumns { get; } =
    [
        new("PortableEntryId", "Portable Entry ID", PortableLogbookMetadataColumnKind.EntryId),
        new("PortableCurrentRevisionId", "Portable Current Revision ID", PortableLogbookMetadataColumnKind.CurrentRevisionId)
    ];

    public static bool IsPortableMetadataColumn(string workbookColumnName) =>
        HiddenLogbookColumns.Any(column =>
            string.Equals(column.WorkbookColumnName, workbookColumnName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> FilterUserExportColumns(IEnumerable<string> workbookColumnNames)
    {
        ArgumentNullException.ThrowIfNull(workbookColumnNames);
        return workbookColumnNames
            .Where(columnName => !IsPortableMetadataColumn(columnName))
            .ToArray();
    }
}

public sealed record PortableLogbookMetadataColumnDefinition(
    string Id,
    string WorkbookColumnName,
    PortableLogbookMetadataColumnKind Kind);

public enum PortableLogbookMetadataColumnKind
{
    EntryId,
    CurrentRevisionId
}
