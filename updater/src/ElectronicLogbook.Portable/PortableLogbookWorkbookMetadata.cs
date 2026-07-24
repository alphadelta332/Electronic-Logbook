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
        new("EntryId", PortableLogbookWorkbookFieldCatalog.EntryIdColumnName, PortableLogbookMetadataColumnKind.EntryId),
        new("PortableCurrentRevisionId", "Portable Current Revision ID", PortableLogbookMetadataColumnKind.CurrentRevisionId)
    ];

    public static bool IsPortableMetadataColumn(string workbookColumnName) =>
        HiddenLogbookColumns.Any(column =>
            string.Equals(column.WorkbookColumnName, workbookColumnName.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> FilterUserExportColumns(IEnumerable<string> workbookColumnNames)
    {
        ArgumentNullException.ThrowIfNull(workbookColumnNames);
        return workbookColumnNames
            .Where(columnName => !IsPortableMetadataColumn(columnName))
            .ToArray();
    }

    public static PortableLogbookMetadataColumnPlan CreateHiddenColumnPlan(IEnumerable<string> workbookColumnNames)
    {
        ArgumentNullException.ThrowIfNull(workbookColumnNames);

        var plannedColumns = workbookColumnNames.ToList();
        var existing = plannedColumns.Select(columnName => columnName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnsToAdd = new List<PortableLogbookMetadataColumnDefinition>();

        foreach (var metadataColumn in HiddenLogbookColumns)
        {
            if (existing.Contains(metadataColumn.WorkbookColumnName))
            {
                continue;
            }

            plannedColumns.Add(metadataColumn.WorkbookColumnName);
            existing.Add(metadataColumn.WorkbookColumnName);
            columnsToAdd.Add(metadataColumn);
        }

        return new PortableLogbookMetadataColumnPlan(
            plannedColumns,
            columnsToAdd,
            HiddenLogbookColumns.Select(column => column.WorkbookColumnName).ToArray());
    }
}

public sealed record PortableLogbookMetadataColumnDefinition(
    string Id,
    string WorkbookColumnName,
    PortableLogbookMetadataColumnKind Kind);

public sealed record PortableLogbookMetadataColumnPlan(
    IReadOnlyList<string> WorkbookColumnNames,
    IReadOnlyList<PortableLogbookMetadataColumnDefinition> ColumnsToAdd,
    IReadOnlyList<string> ColumnsToHide)
{
    public bool RequiresMutation => ColumnsToAdd.Count > 0;
}

public enum PortableLogbookMetadataColumnKind
{
    EntryId,
    CurrentRevisionId
}
