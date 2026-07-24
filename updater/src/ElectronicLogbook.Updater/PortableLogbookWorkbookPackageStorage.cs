using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class PortableLogbookWorkbookPackageStorage
{
    private static readonly XNamespace ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace DocumentRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PortableNamespace = PortableLogbookWorkbookMetadata.CustomXmlNamespace;
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void WriteEnvelope(string workbookPath, PortableLogbookWorkbookStorageEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(envelope);

        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        WriteXmlEntry(archive, PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath, CreateEnvelopeXml(envelope));
        EnsureContentType(archive);
        EnsureCustomXmlRelationship(archive);
    }

    public static PortableLogbookWorkbookStorageEnvelope? ReadEnvelope(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var packageStream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var encodedJson = document.Root?.Element(PortableNamespace + "json")?.Value;
        if (string.IsNullOrWhiteSpace(encodedJson))
        {
            throw new InvalidDataException("Portable logbook workbook storage part is missing its JSON payload.");
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedJson));
        return PortableLogbookWorkbookStorage.Deserialize(json);
    }

    public static PortableLogbookWorkbookStorageState? OpenState(string workbookPath, PortableLogbookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(key);

        var envelope = ReadEnvelope(workbookPath);
        return envelope is null
            ? null
            : PortableLogbookWorkbookStorage.OpenEnvelope(envelope, key);
    }

    public static PortableLogbookWorkbookStorageStateV2? OpenStateV2(string workbookPath, PortableLogbookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(key);

        var envelope = ReadEnvelope(workbookPath);
        return envelope is null
            ? null
            : PortableLogbookWorkbookStorage.OpenEnvelopeV2(envelope, key);
    }

    public static bool CopyEnvelope(string sourceWorkbookPath, string destinationWorkbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationWorkbookPath);

        var envelope = ReadEnvelope(sourceWorkbookPath);
        if (envelope is null)
        {
            return false;
        }

        WriteEnvelope(destinationWorkbookPath, envelope);
        return true;
    }

    public static PortableLogbookWorkbookIdentity? ReadWorkbookIdentityMetadata(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var packageStream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var logbookId = ReadDefinedNameCellValue(archive, PortableLogbookWorkbookMetadata.LogbookIdName);
        var deviceId = ReadDefinedNameCellValue(archive, PortableLogbookWorkbookMetadata.DeviceIdName);
        var schemaVersionText = ReadDefinedNameCellValue(archive, PortableLogbookWorkbookMetadata.SchemaVersionName);
        if (string.IsNullOrWhiteSpace(logbookId) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            !int.TryParse(
                schemaVersionText,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var schemaVersion))
        {
            return null;
        }

        return new PortableLogbookWorkbookIdentity(
            new LogbookId(logbookId),
            new DeviceId(deviceId),
            schemaVersion);
    }

    public static bool CopyWorkbookIdentityMetadata(string sourceWorkbookPath, string destinationWorkbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationWorkbookPath);

        var identity = ReadWorkbookIdentityMetadata(sourceWorkbookPath);
        if (identity is null)
        {
            return false;
        }

        EnsureWorkbookIdentityMetadata(
            destinationWorkbookPath,
            identity.LogbookId,
            identity.DeviceId,
            identity.SchemaVersion);
        return true;
    }

    public static PortableLogbookWorkbookMetadataPackageResult EnsureHiddenMetadataColumns(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var root = tableDocument.Root
            ?? throw new InvalidDataException("Logbook table XML is invalid.");
        var tableColumns = root.Element(SpreadsheetNamespace + "tableColumns")
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");
        var existingColumnNames = tableColumns
            .Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (string?)column.Attribute("name") ?? string.Empty)
            .ToArray();
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(existingColumnNames);
        if (plan.RequiresMutation)
        {
            ApplyHiddenColumnPlanToTable(root, tableColumns, plan);
            WriteXmlEntry(archive, tableEntry.FullName, tableDocument);
        }

        if (!TryParseTableReference((string?)root.Attribute("ref"), out var startColumn, out var startRow, out _, out _))
        {
            throw new InvalidDataException("Logbook table reference is invalid.");
        }

        var hiddenColumnIndexes = plan.WorkbookColumnNames
            .Select((columnName, index) => PortableLogbookWorkbookMetadata.IsPortableMetadataColumn(columnName)
                ? index + 1
                : 0)
            .Where(index => index > 0)
            .ToArray();
        var absoluteHiddenColumnIndexes = hiddenColumnIndexes
            .Select(index => startColumn + index - 1)
            .ToArray();
        var worksheetEntryName = FindWorksheetEntryForTable(archive, tableEntry.FullName);
        if (worksheetEntryName is not null && hiddenColumnIndexes.Length > 0)
        {
            HideWorksheetColumns(archive, worksheetEntryName, absoluteHiddenColumnIndexes);
            WriteHiddenMetadataWorksheetCells(
                archive,
                worksheetEntryName,
                startColumn,
                startRow,
                startRow,
                plan.WorkbookColumnNames,
                hiddenColumnIndexes,
                [],
                []);
        }

        return new PortableLogbookWorkbookMetadataPackageResult(
            plan.WorkbookColumnNames,
            plan.ColumnsToAdd.Select(column => column.WorkbookColumnName).ToArray(),
            absoluteHiddenColumnIndexes);
    }

    public static PortableLogbookWorkbookMetadataWriteResult WriteHiddenMetadataColumnValues(
        string workbookPath,
        IEnumerable<PortableLogbookWorkbookRow> rows,
        IEnumerable<CustomFieldDefinition>? customFieldDefinitions = null,
        bool writeVisiblePayloadCells = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(rows);

        var workbookRows = rows.ToArray();
        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var root = tableDocument.Root
            ?? throw new InvalidDataException("Logbook table XML is invalid.");
        var tableColumns = root.Element(SpreadsheetNamespace + "tableColumns")
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");
        var existingColumnNames = tableColumns
            .Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (string?)column.Attribute("name") ?? string.Empty)
            .ToArray();
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(existingColumnNames);
        if (plan.RequiresMutation)
        {
            ApplyHiddenColumnPlanToTable(root, tableColumns, plan);
        }

        if (!TryParseTableReference((string?)root.Attribute("ref"), out var startColumn, out var startRow, out _, out var endRow))
        {
            throw new InvalidDataException("Logbook table reference is invalid.");
        }

        var requiredEndRow = startRow + workbookRows.Length;
        if (requiredEndRow > endRow &&
            TryResizeTableReference((string?)root.Attribute("ref"), plan.WorkbookColumnNames.Count, requiredEndRow, out var resizedRef))
        {
            root.SetAttributeValue("ref", resizedRef);
            root.Element(SpreadsheetNamespace + "autoFilter")?.SetAttributeValue("ref", resizedRef);
            endRow = requiredEndRow;
        }

        WriteXmlEntry(archive, tableEntry.FullName, tableDocument);

        var worksheetEntryName = FindWorksheetEntryForTable(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Workbook package does not contain the Logbook worksheet.");
        var metadataColumnIndexes = PortableLogbookWorkbookMetadata.HiddenLogbookColumns
            .Select(column => plan.WorkbookColumnNames
                .Select((name, index) => new { name, index })
                .FirstOrDefault(candidate => string.Equals(
                    candidate.name.Trim(),
                    column.WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                ?.index + 1 ?? 0)
            .ToArray();
        if (metadataColumnIndexes.Any(index => index <= 0))
        {
            throw new InvalidDataException("Logbook table does not contain portable metadata columns.");
        }

        var absoluteMetadataColumnIndexes = metadataColumnIndexes
            .Select(index => startColumn + index - 1)
            .ToArray();
        HideWorksheetColumns(archive, worksheetEntryName, absoluteMetadataColumnIndexes);
        WriteHiddenMetadataWorksheetCells(
            archive,
            worksheetEntryName,
            startColumn,
            startRow,
            endRow,
            plan.WorkbookColumnNames,
            metadataColumnIndexes,
            workbookRows,
            customFieldDefinitions ?? [],
            writeVisiblePayloadCells);

        return new PortableLogbookWorkbookMetadataWriteResult(
            Path.GetFullPath(workbookPath),
            workbookRows.Length,
            absoluteMetadataColumnIndexes);
    }

    public static PortableLogbookWorkbookMetadataWriteResult WriteHiddenMetadataColumnValuesV2(
        string workbookPath,
        IEnumerable<PortableLogbookWorkbookRowV2> rows,
        IEnumerable<CustomFieldDefinition>? customFieldDefinitions = null,
        bool writeVisiblePayloadCells = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(rows);

        var workbookRows = rows.ToArray();
        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var root = tableDocument.Root
            ?? throw new InvalidDataException("Logbook table XML is invalid.");
        var tableColumns = root.Element(SpreadsheetNamespace + "tableColumns")
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");
        var existingColumnNames = tableColumns
            .Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (string?)column.Attribute("name") ?? string.Empty)
            .ToArray();
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(existingColumnNames);
        if (plan.RequiresMutation)
        {
            ApplyHiddenColumnPlanToTable(root, tableColumns, plan);
        }

        if (!TryParseTableReference((string?)root.Attribute("ref"), out var startColumn, out var startRow, out _, out var endRow))
        {
            throw new InvalidDataException("Logbook table reference is invalid.");
        }

        var requiredEndRow = startRow + workbookRows.Length;
        if (requiredEndRow > endRow &&
            TryResizeTableReference((string?)root.Attribute("ref"), plan.WorkbookColumnNames.Count, requiredEndRow, out var resizedRef))
        {
            root.SetAttributeValue("ref", resizedRef);
            root.Element(SpreadsheetNamespace + "autoFilter")?.SetAttributeValue("ref", resizedRef);
            endRow = requiredEndRow;
        }

        WriteXmlEntry(archive, tableEntry.FullName, tableDocument);

        var worksheetEntryName = FindWorksheetEntryForTable(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Workbook package does not contain the Logbook worksheet.");
        var metadataColumnIndexes = PortableLogbookWorkbookMetadata.HiddenLogbookColumns
            .Select(column => plan.WorkbookColumnNames
                .Select((name, index) => new { name, index })
                .FirstOrDefault(candidate => string.Equals(
                    candidate.name.Trim(),
                    column.WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                ?.index + 1 ?? 0)
            .ToArray();
        if (metadataColumnIndexes.Any(index => index <= 0))
        {
            throw new InvalidDataException("Logbook table does not contain portable metadata columns.");
        }

        var absoluteMetadataColumnIndexes = metadataColumnIndexes
            .Select(index => startColumn + index - 1)
            .ToArray();
        HideWorksheetColumns(archive, worksheetEntryName, absoluteMetadataColumnIndexes);
        WriteHiddenMetadataWorksheetCellsV2(
            archive,
            worksheetEntryName,
            startColumn,
            startRow,
            endRow,
            plan.WorkbookColumnNames,
            metadataColumnIndexes,
            workbookRows,
            customFieldDefinitions ?? [],
            writeVisiblePayloadCells);

        return new PortableLogbookWorkbookMetadataWriteResult(
            Path.GetFullPath(workbookPath),
            workbookRows.Length,
            absoluteMetadataColumnIndexes);
    }

    public static IReadOnlyList<PortableLogbookWorkbookRow> ReadCurrentRows(
        string workbookPath,
        IEnumerable<CustomFieldDefinition>? customFieldDefinitions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var packageStream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var tableRoot = tableDocument.Root
            ?? throw new InvalidDataException("Logbook table XML is invalid.");
        if (!TryParseTableReference((string?)tableRoot.Attribute("ref"), out var startColumn, out var startRow, out _, out var endRow))
        {
            throw new InvalidDataException("Logbook table reference is invalid.");
        }

        var columnNames = tableRoot
            .Element(SpreadsheetNamespace + "tableColumns")
            ?.Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (string?)column.Attribute("name") ?? string.Empty)
            .ToArray()
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");
        var worksheetEntryName = FindWorksheetEntryForTable(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Workbook package does not contain the Logbook worksheet.");
        var worksheet = ReadXmlEntry(archive, worksheetEntryName)
            ?? throw new InvalidDataException("Logbook worksheet part is invalid.");
        var sharedStrings = ReadSharedStrings(archive);
        var cells = worksheet
            .Descendants(SpreadsheetNamespace + "c")
            .Select(cell => new
            {
                Reference = (string?)cell.Attribute("r") ?? string.Empty,
                Value = ReadCellText(cell, sharedStrings)
            })
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Reference))
            .ToDictionary(cell => cell.Reference, cell => cell.Value, StringComparer.OrdinalIgnoreCase);
        var fieldsByColumnName = BuildFieldsByWorkbookColumnName();
        var customFieldsByLabel = (customFieldDefinitions ?? [])
            .ToDictionary(field => field.Label, StringComparer.OrdinalIgnoreCase);
        var usesRealWorkbookSchema = columnNames.Any(IsRealWorkbookSchemaColumn);
        var rows = new List<PortableLogbookWorkbookRow>();

        for (var rowNumber = startRow + 1; rowNumber <= endRow; rowNumber++)
        {
            var rawFieldValues = new Dictionary<PortableLogbookFieldDefinition, string?>();
            var rawCustomValues = new Dictionary<CustomFieldDefinition, string?>();
            var rawWorkbookValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            EntryId? entryId = null;
            RevisionId? currentRevisionId = null;
            var rowHasUserData = false;

            for (var index = 0; index < columnNames.Length; index++)
            {
                var columnName = columnNames[index].Trim();
                var cellReference = $"{ColumnName(startColumn + index)}{rowNumber}";
                cells.TryGetValue(cellReference, out var cellText);
                rawWorkbookValues[columnName] = cellText;
                if (string.Equals(
                    columnName,
                    PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    entryId = string.IsNullOrWhiteSpace(cellText) ? null : new EntryId(cellText.Trim());
                    continue;
                }

                if (string.Equals(
                    columnName,
                    PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    currentRevisionId = string.IsNullOrWhiteSpace(cellText) ? null : new RevisionId(cellText.Trim());
                    continue;
                }

                if (fieldsByColumnName.TryGetValue(columnName, out var field))
                {
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        rowHasUserData = true;
                    }

                    if (ShouldIgnoreDirectWorkbookField(field, columnName, cellText, usesRealWorkbookSchema))
                    {
                        continue;
                    }

                    rawFieldValues[field] = cellText;
                    continue;
                }

                if (customFieldsByLabel.TryGetValue(columnName, out var customField))
                {
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        rowHasUserData = true;
                    }

                    rawCustomValues[customField] = cellText;
                }
            }

            if (!rowHasUserData || !LooksLikeWorkbookFlightEntry(rawFieldValues, rawWorkbookValues, usesRealWorkbookSchema))
            {
                continue;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in rawFieldValues)
            {
                try
                {
                    values[pair.Key.Id] = ConvertWorkbookCellValue(pair.Key, pair.Value);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new InvalidDataException(
                        $"Unable to read Logbook row {rowNumber}, column '{pair.Key.WorkbookColumnName}' as {pair.Key.Kind}. Value: '{pair.Value}'.",
                        ex);
                }
            }

            if (usesRealWorkbookSchema)
            {
                ApplyRealWorkbookDerivedValues(values, rawWorkbookValues);
            }
            var customValues = rawCustomValues.ToDictionary(
                pair => pair.Key.Id,
                pair => string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value);

            rows.Add(new PortableLogbookWorkbookRow(
                entryId,
                currentRevisionId,
                PortableLogbookEntryFields.FromFieldValues(values, customValues)));
        }

        return rows;
    }

    public static IReadOnlyList<PortableLogbookWorkbookRowV2> ReadCurrentRowsV2(
        string workbookPath,
        IEnumerable<CustomFieldDefinition>? customFieldDefinitions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var packageStream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var tableRoot = tableDocument.Root
            ?? throw new InvalidDataException("Logbook table XML is invalid.");
        if (!TryParseTableReference((string?)tableRoot.Attribute("ref"), out var startColumn, out var startRow, out _, out var endRow))
        {
            throw new InvalidDataException("Logbook table reference is invalid.");
        }

        var columnNames = tableRoot
            .Element(SpreadsheetNamespace + "tableColumns")
            ?.Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (string?)column.Attribute("name") ?? string.Empty)
            .ToArray()
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");
        var worksheetEntryName = FindWorksheetEntryForTable(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Workbook package does not contain the Logbook worksheet.");
        var worksheet = ReadXmlEntry(archive, worksheetEntryName)
            ?? throw new InvalidDataException("Logbook worksheet part is invalid.");
        var sharedStrings = ReadSharedStrings(archive);
        var cells = worksheet
            .Descendants(SpreadsheetNamespace + "c")
            .Select(cell => new
            {
                Reference = (string?)cell.Attribute("r") ?? string.Empty,
                Value = ReadCellText(cell, sharedStrings)
            })
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Reference))
            .ToDictionary(cell => cell.Reference, cell => cell.Value, StringComparer.OrdinalIgnoreCase);
        var fieldsByColumnName = PortableLogbookWorkbookFieldCatalog.ByWorkbookColumnName;
        var customFieldsByLabel = (customFieldDefinitions ?? [])
            .ToDictionary(field => field.Label, StringComparer.OrdinalIgnoreCase);
        var rows = new List<PortableLogbookWorkbookRowV2>();

        for (var rowNumber = startRow + 1; rowNumber <= endRow; rowNumber++)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var customValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            EntryId? entryId = null;
            RevisionId? currentRevisionId = null;
            var rowHasUserData = false;

            for (var index = 0; index < columnNames.Length; index++)
            {
                var columnName = columnNames[index].Trim();
                var cellReference = $"{ColumnName(startColumn + index)}{rowNumber}";
                cells.TryGetValue(cellReference, out var cellText);
                if (string.Equals(
                    columnName,
                    PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    entryId = string.IsNullOrWhiteSpace(cellText) ? null : new EntryId(cellText.Trim());
                    continue;
                }

                if (string.Equals(
                    columnName,
                    PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    currentRevisionId = string.IsNullOrWhiteSpace(cellText) ? null : new RevisionId(cellText.Trim());
                    continue;
                }

                if (fieldsByColumnName.TryGetValue(columnName, out var field))
                {
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        rowHasUserData = true;
                    }

                    values[field.Id] = ConvertWorkbookCellValue(field, cellText);
                    continue;
                }

                if (customFieldsByLabel.TryGetValue(columnName, out var customField))
                {
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        rowHasUserData = true;
                    }

                    customValues[$"custom{customField.Order}"] = string.IsNullOrWhiteSpace(cellText) ? null : cellText;
                }
            }

            if (!rowHasUserData || !LooksLikeWorkbookFlightEntryV2(values))
            {
                continue;
            }

            foreach (var pair in customValues)
            {
                values[pair.Key] = pair.Value;
            }

            rows.Add(new PortableLogbookWorkbookRowV2(
                entryId,
                currentRevisionId,
                PortableLogbookWorkbookEntryFields.FromFieldValues(values, (customFieldDefinitions ?? []).ToArray())));
        }

        return rows;
    }

    public static IReadOnlyList<CustomFieldDefinition> ReadWorkbookCustomFieldDefinitions(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var packageStream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var tableEntry = FindLogbookTableEntry(archive)
            ?? throw new InvalidDataException("Workbook package does not contain a Logbook table.");
        var tableDocument = ReadXmlEntry(archive, tableEntry.FullName)
            ?? throw new InvalidDataException("Logbook table part is invalid.");
        var columnNames = tableDocument.Root?
            .Element(SpreadsheetNamespace + "tableColumns")
            ?.Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => ((string?)column.Attribute("name") ?? string.Empty).Trim())
            .Where(name => name.StartsWith("Custom ", StringComparison.OrdinalIgnoreCase))
            .Take(PortableLogbookCustomFieldSet.WorkbookCustomFieldCount)
            .ToArray()
            ?? throw new InvalidDataException("Logbook table does not contain tableColumns.");

        return columnNames.Length == PortableLogbookCustomFieldSet.WorkbookCustomFieldCount
            ? PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(columnNames)
            : [];
    }

    private static bool LooksLikeWorkbookFlightEntry(
        IReadOnlyDictionary<PortableLogbookFieldDefinition, string?> rawFieldValues,
        IReadOnlyDictionary<string, string?> rawWorkbookValues,
        bool usesRealWorkbookSchema)
    {
        var hasDate = rawFieldValues.Any(pair =>
            pair.Key.Id == "date" &&
            !string.IsNullOrWhiteSpace(pair.Value));
        if (!hasDate)
        {
            return false;
        }

        var entryIdentityFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "aircraftType",
            "registration",
            "from",
            "to"
        };
        var loggedTimeFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "multiPilot",
            "pilotInCommand",
            "coPilot",
            "dual",
            "instructor",
            "instrumentSimulated"
        };

        var hasEntryIdentity = rawFieldValues.Any(pair =>
            entryIdentityFields.Contains(pair.Key.Id) &&
            !string.IsNullOrWhiteSpace(pair.Value));
        var hasLoggedTime = rawFieldValues.Any(pair =>
            loggedTimeFields.Contains(pair.Key.Id) &&
            decimal.TryParse(
                pair.Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) &&
            value > 0) ||
            usesRealWorkbookSchema &&
            SumWorkbookDecimal(
                rawWorkbookValues,
                "SeIcusDay",
                "SeIcusNight",
                "SeDualDay",
                "SeDualNight",
                "SeCommandDay",
                "SeCommandNight",
                "MeIcusDay",
                "MeIcusNight",
                "MeDualDay",
                "MeDualNight",
                "MeCommandDay",
                "MeCommandNight",
                "CopilotDay",
                "CopilotNight",
                "TotalHours") > 0;

        return hasEntryIdentity && hasLoggedTime;
    }

    private static bool LooksLikeWorkbookFlightEntryV2(IReadOnlyDictionary<string, object?> values)
    {
        var hasDate =
            values.TryGetValue("dateYear", out var year) && year is not null &&
            values.TryGetValue("dateMonth", out var month) && month is not null &&
            values.TryGetValue("dateDay", out var day) && day is not null;
        if (!hasDate)
        {
            return false;
        }

        var hasEntryIdentity =
            HasTextValue(values, "type") ||
            HasTextValue(values, "reg") ||
            HasTextValue(values, "from") ||
            HasTextValue(values, "to");
        var loggedTimeFieldIds = new[]
        {
            "seIcusDay",
            "seIcusNight",
            "seDualDay",
            "seDualNight",
            "seCommandDay",
            "seCommandNight",
            "meIcusDay",
            "meIcusNight",
            "meDualDay",
            "meDualNight",
            "meCommandDay",
            "meCommandNight",
            "copilotDay",
            "copilotNight",
            "ifrIf",
            "ifrSim"
        };
        var hasLoggedTime = loggedTimeFieldIds.Any(fieldId =>
            values.TryGetValue(fieldId, out var value) &&
            value is decimal decimalValue &&
            decimalValue > 0);

        return hasEntryIdentity && hasLoggedTime;
    }

    private static bool HasTextValue(IReadOnlyDictionary<string, object?> values, string fieldId) =>
        values.TryGetValue(fieldId, out var value) &&
        value is string text &&
        !string.IsNullOrWhiteSpace(text);

    private static bool ShouldIgnoreDirectWorkbookField(
        PortableLogbookFieldDefinition field,
        string columnName,
        string? cellText,
        bool usesRealWorkbookSchema) =>
        usesRealWorkbookSchema &&
        ((field.Id == "pilotInCommand" &&
            string.Equals(columnName, "PIC", StringComparison.OrdinalIgnoreCase) &&
            !CanParseWorkbookDecimal(cellText)) ||
        (field.Id == "day" &&
            string.Equals(columnName, "Day", StringComparison.OrdinalIgnoreCase)) ||
        (field.Id == "instrumentSimulated" &&
            string.Equals(columnName, "IfrSim", StringComparison.OrdinalIgnoreCase)));

    private static bool IsRealWorkbookSchemaColumn(string columnName) =>
        string.Equals(columnName, "SeCommandDay", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(columnName, "MeCommandDay", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(columnName, "Other Pilot or Crew", StringComparison.OrdinalIgnoreCase);

    private static void ApplyRealWorkbookDerivedValues(
        IDictionary<string, object?> values,
        IReadOnlyDictionary<string, string?> rawWorkbookValues)
    {
        var command = SumWorkbookDecimal(
            rawWorkbookValues,
            "SeCommandDay",
            "SeCommandNight",
            "MeCommandDay",
            "MeCommandNight");
        var icus = SumWorkbookDecimal(rawWorkbookValues, "SeIcusDay", "SeIcusNight", "MeIcusDay", "MeIcusNight");
        var coPilot = SumWorkbookDecimal(rawWorkbookValues, "CopilotDay", "CopilotNight");
        var dual = SumWorkbookDecimal(rawWorkbookValues, "SeDualDay", "SeDualNight", "MeDualDay", "MeDualNight");
        SetDecimalIfPositive(
            values,
            "pilotInCommand",
            command);
        SetDecimalIfPositive(values, "multiPilot", icus);
        SetDecimalIfPositive(values, "coPilot", coPilot);
        SetDecimalIfPositive(values, "dual", dual);
        SetDecimalIfPositive(
            values,
            "day",
            SumWorkbookDecimal(
                rawWorkbookValues,
                "SeIcusDay",
                "SeDualDay",
                "SeCommandDay",
                "MeIcusDay",
                "MeDualDay",
                "MeCommandDay",
                "CopilotDay"));
        SetDecimalIfPositive(
            values,
            "night",
            SumWorkbookDecimal(
                rawWorkbookValues,
                "SeIcusNight",
                "SeDualNight",
                "SeCommandNight",
                "MeIcusNight",
                "MeDualNight",
                "MeCommandNight",
                "CopilotNight"));
        SetCountIfPositive(values, "ifrApproaches", SumWorkbookCount(rawWorkbookValues, "TotalApps"));

        if (command + icus + coPilot + dual <= 0)
        {
            SetDecimalIfPositive(values, "pilotInCommand", SumWorkbookDecimal(rawWorkbookValues, "TotalHours"));
        }

        NormalizeInstrumentAndFlightTime(values);

        if (string.IsNullOrWhiteSpace(GetValueString(values, "to")))
        {
            var from = GetValueString(values, "from");
            if (!string.IsNullOrWhiteSpace(from))
            {
                values["to"] = from;
            }
        }

        if (values.TryGetValue("details", out var details) && details is string text)
        {
            values["details"] = text.Trim();
        }
    }

    private static void NormalizeInstrumentAndFlightTime(IDictionary<string, object?> values)
    {
        var flightTime =
            GetDecimalValue(values, "multiPilot") +
            GetDecimalValue(values, "pilotInCommand") +
            GetDecimalValue(values, "coPilot") +
            GetDecimalValue(values, "dual") +
            GetDecimalValue(values, "instructor");
        var actualInstrument = GetDecimalValue(values, "instrumentActual");
        if (actualInstrument <= flightTime)
        {
            return;
        }

        if (flightTime <= 0)
        {
            values["pilotInCommand"] = actualInstrument;
            return;
        }

        values["instrumentActual"] = flightTime;
    }

    private static void SetDecimalIfPositive(IDictionary<string, object?> values, string fieldId, decimal value)
    {
        if (value > 0)
        {
            values[fieldId] = value;
        }
    }

    private static void SetCountIfPositive(IDictionary<string, object?> values, string fieldId, int value)
    {
        if (value > 0)
        {
            values[fieldId] = value;
        }
    }

    private static decimal SumWorkbookDecimal(IReadOnlyDictionary<string, string?> values, params string[] columnNames) =>
        columnNames.Sum(columnName => TryReadWorkbookDecimal(values, columnName, out var value) ? value : 0);

    private static int SumWorkbookCount(IReadOnlyDictionary<string, string?> values, params string[] columnNames) =>
        Convert.ToInt32(
            SumWorkbookDecimal(values, columnNames),
            System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryReadWorkbookDecimal(
        IReadOnlyDictionary<string, string?> values,
        string columnName,
        out decimal value)
    {
        value = 0;
        return values.TryGetValue(columnName, out var text) &&
            decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    private static bool CanParseWorkbookDecimal(string? value) =>
        decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);

    private static string? GetWorkbookText(IReadOnlyDictionary<string, string?> values, string columnName) =>
        values.TryGetValue(columnName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? GetValueString(IDictionary<string, object?> values, string fieldId) =>
        values.TryGetValue(fieldId, out var value) && value is not null
            ? value.ToString()
            : null;

    private static decimal GetDecimalValue(IDictionary<string, object?> values, string fieldId) =>
        values.TryGetValue(fieldId, out var value) && value is not null
            ? Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    private static IReadOnlyDictionary<string, PortableLogbookFieldDefinition> BuildFieldsByWorkbookColumnName()
    {
        var fields = new Dictionary<string, PortableLogbookFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in PortableLogbookFields.RawFlightFields)
        {
            fields.TryAdd(field.WorkbookColumnName, field);
        }

        AddFieldAlias(fields, "aircraftType", "Type");
        AddFieldAlias(fields, "flightNumber", "Flight ID");
        AddFieldAlias(fields, "route", "Via");
        AddFieldAlias(fields, "details", "Remarks");
        AddFieldAlias(fields, "instrumentActual", "IfrIf");
        AddFieldAlias(fields, "instrumentSimulated", "IfrSim");
        return fields;
    }

    private static void AddFieldAlias(
        Dictionary<string, PortableLogbookFieldDefinition> fields,
        string fieldId,
        string workbookColumnName)
    {
        var field = PortableLogbookFields.ById[fieldId];
        fields.TryAdd(workbookColumnName, field);
    }

    public static PortableLogbookWorkbookIdentityPackageResult EnsureWorkbookIdentityMetadata(
        string workbookPath,
        LogbookId logbookId,
        DeviceId deviceId,
        int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(logbookId);
        ArgumentNullException.ThrowIfNull(deviceId);

        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var workbook = ReadXmlEntry(archive, "xl/workbook.xml")
            ?? throw new InvalidDataException("Workbook package does not contain xl/workbook.xml.");
        var workbookRelationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("Workbook package does not contain workbook relationships.");
        var root = workbook.Root
            ?? throw new InvalidDataException("Workbook XML is invalid.");
        var definedNames = root.Element(SpreadsheetNamespace + "definedNames");
        if (definedNames is null)
        {
            definedNames = new XElement(SpreadsheetNamespace + "definedNames");
            var sheets = root.Element(SpreadsheetNamespace + "sheets");
            if (sheets is not null)
            {
                sheets.AddAfterSelf(definedNames);
            }
            else
            {
                root.Add(definedNames);
            }
        }

        var metadataSheet = FindWorkbookMetadataSheet(root, workbookRelationships, definedNames)
            ?? throw new InvalidDataException("Workbook package does not contain a metadata sheet for portable identity values.");
        var metadataWorksheet = ReadXmlEntry(archive, metadataSheet.WorksheetEntryName)
            ?? throw new InvalidDataException("Workbook metadata worksheet part is invalid.");
        var values = new (string Name, string Value)[]
        {
            (PortableLogbookWorkbookMetadata.LogbookIdName, logbookId.Value),
            (PortableLogbookWorkbookMetadata.DeviceIdName, deviceId.Value),
            (PortableLogbookWorkbookMetadata.SchemaVersionName, schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        var nextMetadataRow = FindNextMetadataRow(workbook, metadataWorksheet, metadataSheet.SheetName);
        var namesAdded = new List<string>();
        var cellsWritten = new List<string>();

        foreach (var (name, value) in values)
        {
            var definedName = FindDefinedName(definedNames, name);
            string cellReference;
            var parsedCellReference = string.Empty;
            var existingReferenceIsUsable = false;
            if (definedName is not null &&
                TryParseSingleCellReference(definedName.Value, out var definedSheetName, out parsedCellReference) &&
                string.Equals(definedSheetName, metadataSheet.SheetName, StringComparison.OrdinalIgnoreCase))
            {
                existingReferenceIsUsable = true;
            }

            if (definedName is null ||
                !existingReferenceIsUsable)
            {
                definedName ??= new XElement(
                    SpreadsheetNamespace + "definedName",
                    new XAttribute("name", name));
                if (definedName.Parent is null)
                {
                    definedNames.Add(definedName);
                    namesAdded.Add(name);
                }

                cellReference = $"{metadataSheet.MetadataColumnName}{nextMetadataRow}";
                nextMetadataRow++;
                definedName.Value = CreateSingleCellReference(metadataSheet.SheetName, cellReference);
            }
            else
            {
                cellReference = parsedCellReference;
            }

            definedName.SetAttributeValue("hidden", "1");
            UpsertInlineStringCell(metadataWorksheet, cellReference, value);
            cellsWritten.Add(cellReference);
        }

        WriteXmlEntry(archive, "xl/workbook.xml", workbook);
        WriteXmlEntry(archive, metadataSheet.WorksheetEntryName, metadataWorksheet);

        return new PortableLogbookWorkbookIdentityPackageResult(
            logbookId,
            deviceId,
            schemaVersion,
            namesAdded,
            cellsWritten);
    }

    private static XDocument CreateEnvelopeXml(PortableLogbookWorkbookStorageEnvelope envelope)
    {
        var json = PortableLogbookWorkbookStorage.Serialize(envelope);
        return new XDocument(
            new XElement(
                PortableNamespace + "portableLogbookStorage",
                new XAttribute("version", PortableLogbookWorkbookStorage.CurrentStorageVersion),
                new XElement(PortableNamespace + "json", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)))));
    }

    private static ZipArchiveEntry? FindLogbookTableEntry(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var document = ReadXmlEntry(archive, entry.FullName);
            var root = document?.Root;
            if (root is null)
            {
                continue;
            }

            var name = (string?)root.Attribute("name");
            var displayName = (string?)root.Attribute("displayName");
            if (string.Equals(name, "Logbook", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(displayName, "Logbook", StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static void ApplyHiddenColumnPlanToTable(
        XElement tableRoot,
        XElement tableColumns,
        PortableLogbookMetadataColumnPlan plan)
    {
        var maxId = tableColumns
            .Elements(SpreadsheetNamespace + "tableColumn")
            .Select(column => (int?)column.Attribute("id") ?? 0)
            .DefaultIfEmpty(0)
            .Max();
        foreach (var columnName in plan.ColumnsToAdd.Select(column => column.WorkbookColumnName))
        {
            maxId++;
            tableColumns.Add(new XElement(
                SpreadsheetNamespace + "tableColumn",
                new XAttribute("id", maxId),
                new XAttribute("name", columnName)));
        }

        tableColumns.SetAttributeValue("count", plan.WorkbookColumnNames.Count);
        var tableRef = (string?)tableRoot.Attribute("ref");
        if (TryResizeTableReference(tableRef, plan.WorkbookColumnNames.Count, out var resizedRef))
        {
            tableRoot.SetAttributeValue("ref", resizedRef);
            tableRoot.Element(SpreadsheetNamespace + "autoFilter")?.SetAttributeValue("ref", resizedRef);
        }
    }

    private static string? FindWorksheetEntryForTable(ZipArchive archive, string tableEntryName)
    {
        var normalisedTablePath = NormalisePackagePath(tableEntryName);
        foreach (var relationshipsEntry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith("xl/worksheets/_rels/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationships = ReadXmlEntry(archive, relationshipsEntry.FullName);
            var worksheetEntryName = ResolveWorksheetEntryNameFromRelationships(relationshipsEntry.FullName);
            foreach (var relationship in relationships?.Root?.Elements(RelationshipsNamespace + "Relationship") ?? [])
            {
                var target = (string?)relationship.Attribute("Target");
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                var resolved = NormalisePackagePath(ResolveRelationshipTarget(worksheetEntryName, target));
                if (string.Equals(resolved, normalisedTablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return worksheetEntryName;
                }
            }
        }

        return null;
    }

    private static string ResolveWorksheetEntryNameFromRelationships(string relationshipsEntryName)
    {
        var fileName = Path.GetFileNameWithoutExtension(relationshipsEntryName);
        return $"xl/worksheets/{fileName}";
    }

    private static XElement? FindDefinedName(XElement definedNames, string name) =>
        definedNames
            .Elements(SpreadsheetNamespace + "definedName")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("name"),
                name,
                StringComparison.OrdinalIgnoreCase));

    private static string? ReadDefinedNameCellValue(ZipArchive archive, string name)
    {
        var workbook = ReadXmlEntry(archive, "xl/workbook.xml");
        var workbookRelationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels");
        if (workbook?.Root is null || workbookRelationships?.Root is null)
        {
            return null;
        }

        var definedName = workbook
            .Descendants(SpreadsheetNamespace + "definedName")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("name"),
                name,
                StringComparison.OrdinalIgnoreCase));
        if (definedName is null ||
            !TryParseSingleCellReference(definedName.Value, out var sheetName, out var cellReference))
        {
            return null;
        }

        var worksheetEntryName = FindWorksheetEntryForSheet(workbook, workbookRelationships, sheetName);
        if (worksheetEntryName is null)
        {
            return null;
        }

        var worksheet = ReadXmlEntry(archive, worksheetEntryName);
        var cell = worksheet?
            .Descendants(SpreadsheetNamespace + "c")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("r"),
                cellReference,
                StringComparison.OrdinalIgnoreCase));
        return ReadCellText(cell);
    }

    private static string? FindWorksheetEntryForSheet(
        XDocument workbook,
        XDocument workbookRelationships,
        string sheetName)
    {
        var sheet = workbook
            .Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("name"),
                sheetName,
                StringComparison.OrdinalIgnoreCase));
        var relationshipId = (string?)sheet?.Attribute(DocumentRelationshipsNamespace + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return null;
        }

        var relationship = workbookRelationships
            .Descendants(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("Id"),
                relationshipId,
                StringComparison.Ordinal));
        var target = (string?)relationship?.Attribute("Target");
        return string.IsNullOrWhiteSpace(target)
            ? null
            : ResolveRelationshipTarget("xl/workbook.xml", target);
    }

    private static string? ReadCellText(XElement? cell) => ReadCellText(cell, []);

    private static string? ReadCellText(XElement? cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell is null)
        {
            return null;
        }

        if (string.Equals((string?)cell.Attribute("t"), "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return cell
                .Descendants(SpreadsheetNamespace + "t")
                .FirstOrDefault()
                ?.Value;
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (string.Equals((string?)cell.Attribute("t"), "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStrings = ReadXmlEntry(archive, "xl/sharedStrings.xml");
        if (sharedStrings?.Root is null)
        {
            return [];
        }

        return sharedStrings
            .Root
            .Elements(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static object? ConvertWorkbookCellValue(PortableLogbookFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return field.Kind switch
        {
            PortableLogbookFieldKind.Date => ConvertWorkbookDateCellValue(field, text),
            PortableLogbookFieldKind.DecimalHours => decimal.Parse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture),
            PortableLogbookFieldKind.Count => Convert.ToInt32(
                decimal.Parse(
                    text,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture),
            _ => text
        };
    }

    private static object? ConvertWorkbookCellValue(PortableLogbookWorkbookFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return field.Kind switch
        {
            PortableLogbookWorkbookFieldKind.Boolean => ParseWorkbookBoolean(text),
            PortableLogbookWorkbookFieldKind.DecimalHours => decimal.Parse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture),
            PortableLogbookWorkbookFieldKind.Count => Convert.ToInt32(
                decimal.Parse(
                    text,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture),
            _ => text
        };
    }

    private static bool ParseWorkbookBoolean(string text)
    {
        if (bool.TryParse(text, out var boolean))
        {
            return boolean;
        }

        if (string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "N", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "No", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new FormatException($"Workbook boolean value '{text}' is not recognised.");
    }

    private static DateOnly ConvertWorkbookDateCellValue(PortableLogbookFieldDefinition field, string value)
    {
        if (DateOnly.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date))
        {
            return date;
        }

        if (double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var serialDate))
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
        }

        throw new ArgumentException($"Field '{field.Id}' cannot be converted to DateOnly.");
    }

    private static WorkbookMetadataSheet? FindWorkbookMetadataSheet(
        XElement workbookRoot,
        XDocument workbookRelationships,
        XElement definedNames)
    {
        var logbookVersion = FindDefinedName(definedNames, "LogbookVersion");
        if (logbookVersion is not null &&
            TryParseSingleCellReference(logbookVersion.Value, out var metadataSheetName, out var metadataCellReference) &&
            TryParseCellReference(metadataCellReference, out var metadataColumn, out _))
        {
            var sheet = FindSheet(workbookRoot, metadataSheetName);
            var worksheetEntryName = FindWorksheetEntryName(workbookRelationships, sheet);
            if (worksheetEntryName is not null)
            {
                return new WorkbookMetadataSheet(metadataSheetName, worksheetEntryName, ColumnName(metadataColumn));
            }
        }

        var backendSheet = FindSheet(workbookRoot, "Backend");
        var backendWorksheetEntryName = FindWorksheetEntryName(workbookRelationships, backendSheet);
        return backendWorksheetEntryName is null
            ? null
            : new WorkbookMetadataSheet((string?)backendSheet?.Attribute("name") ?? "Backend", backendWorksheetEntryName, "A");
    }

    private static XElement? FindSheet(XElement workbookRoot, string sheetName) =>
        workbookRoot
            .Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(sheet => string.Equals(
                (string?)sheet.Attribute("name"),
                sheetName,
                StringComparison.OrdinalIgnoreCase));

    private static string? FindWorksheetEntryName(XDocument workbookRelationships, XElement? sheet)
    {
        var relationshipId = (string?)sheet?.Attribute(DocumentRelationshipsNamespace + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return null;
        }

        var relationship = workbookRelationships
            .Descendants(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate.Attribute("Id"),
                relationshipId,
                StringComparison.Ordinal));
        var target = (string?)relationship?.Attribute("Target");
        return string.IsNullOrWhiteSpace(target)
            ? null
            : ResolveRelationshipTarget("xl/workbook.xml", target);
    }

    private static int FindNextMetadataRow(
        XDocument workbook,
        XDocument metadataWorksheet,
        string metadataSheetName)
    {
        var rowsFromDefinedNames = workbook
            .Descendants(SpreadsheetNamespace + "definedName")
            .Select(definedName => TryParseSingleCellReference(definedName.Value, out var sheetName, out var cellReference) &&
                    string.Equals(sheetName, metadataSheetName, StringComparison.OrdinalIgnoreCase) &&
                    TryParseCellReference(cellReference, out _, out var row)
                ? row
                : 0);
        var rowsFromWorksheetCells = metadataWorksheet
            .Descendants(SpreadsheetNamespace + "c")
            .Select(cell => TryParseCellReference((string?)cell.Attribute("r") ?? string.Empty, out _, out var row) ? row : 0);
        return rowsFromDefinedNames
            .Concat(rowsFromWorksheetCells)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private sealed record WorkbookMetadataSheet(
        string SheetName,
        string WorksheetEntryName,
        string MetadataColumnName);

    private static string CreateSingleCellReference(string sheetName, string cellReference) =>
        $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'!${new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant()}${new string(cellReference.SkipWhile(char.IsLetter).ToArray())}";

    private static bool TryParseSingleCellReference(string value, out string sheetName, out string cellReference)
    {
        sheetName = string.Empty;
        cellReference = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf('!');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        sheetName = value[..separatorIndex].Trim();
        if (sheetName.StartsWith("'", StringComparison.Ordinal) &&
            sheetName.EndsWith("'", StringComparison.Ordinal) &&
            sheetName.Length >= 2)
        {
            sheetName = sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        cellReference = value[(separatorIndex + 1)..].Replace("$", string.Empty, StringComparison.Ordinal);
        return TryParseCellReference(cellReference, out _, out _);
    }

    private static void UpsertInlineStringCell(XDocument worksheet, string cellReference, string value)
    {
        var root = worksheet.Root
            ?? throw new InvalidDataException("Worksheet XML is invalid.");
        if (!TryParseCellReference(cellReference, out _, out var rowNumber))
        {
            throw new InvalidDataException($"Cell reference '{cellReference}' is invalid.");
        }

        var sheetData = root.Element(SpreadsheetNamespace + "sheetData");
        if (sheetData is null)
        {
            sheetData = new XElement(SpreadsheetNamespace + "sheetData");
            root.Add(sheetData);
        }

        var row = sheetData
            .Elements(SpreadsheetNamespace + "row")
            .FirstOrDefault(element => ((uint?)element.Attribute("r") ?? 0) == rowNumber);
        if (row is null)
        {
            row = new XElement(SpreadsheetNamespace + "row", new XAttribute("r", rowNumber));
            sheetData.Add(row);
        }

        var cell = row
            .Elements(SpreadsheetNamespace + "c")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("r"),
                cellReference,
                StringComparison.OrdinalIgnoreCase));
        if (cell is null)
        {
            cell = new XElement(SpreadsheetNamespace + "c", new XAttribute("r", cellReference));
            row.Add(cell);
        }

        cell.SetAttributeValue("t", "inlineStr");
        cell.Elements().Remove();
        cell.Add(new XElement(
            SpreadsheetNamespace + "is",
            new XElement(SpreadsheetNamespace + "t", value)));
    }

    private static string ResolveRelationshipTarget(string sourceEntryName, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return target[1..];
        }

        var sourceDirectory = Path.GetDirectoryName(sourceEntryName)?.Replace('\\', '/') ?? string.Empty;
        var combined = string.IsNullOrWhiteSpace(sourceDirectory)
            ? target
            : sourceDirectory + "/" + target;
        return NormalisePackagePath(combined);
    }

    private static string NormalisePackagePath(string path)
    {
        var parts = new Stack<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.Pop();
                }

                continue;
            }

            parts.Push(part);
        }

        return string.Join("/", parts.Reverse());
    }

    private static void HideWorksheetColumns(ZipArchive archive, string worksheetEntryName, IReadOnlyList<int> columnIndexes)
    {
        var worksheet = ReadXmlEntry(archive, worksheetEntryName)
            ?? throw new InvalidDataException("Logbook worksheet part is invalid.");
        var root = worksheet.Root
            ?? throw new InvalidDataException("Logbook worksheet XML is invalid.");
        var cols = root.Element(SpreadsheetNamespace + "cols");
        if (cols is null)
        {
            cols = new XElement(SpreadsheetNamespace + "cols");
            var sheetData = root.Element(SpreadsheetNamespace + "sheetData");
            if (sheetData is not null)
            {
                sheetData.AddBeforeSelf(cols);
            }
            else
            {
                root.AddFirst(cols);
            }
        }

        foreach (var columnIndex in columnIndexes)
        {
            foreach (var existing in cols.Elements(SpreadsheetNamespace + "col").Where(column =>
                ((uint?)column.Attribute("min") ?? 0) == columnIndex &&
                ((uint?)column.Attribute("max") ?? 0) == columnIndex).ToArray())
            {
                existing.Remove();
            }

            cols.Add(new XElement(
                SpreadsheetNamespace + "col",
                new XAttribute("min", columnIndex),
                new XAttribute("max", columnIndex),
                new XAttribute("width", "0"),
                new XAttribute("hidden", "1"),
                new XAttribute("customWidth", "1")));
        }

        WriteXmlEntry(archive, worksheetEntryName, worksheet);
    }

    private static bool TryResizeTableReference(string? reference, int columnCount, out string resizedReference)
    {
        resizedReference = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var parts = reference.Split(':', 2);
        if (parts.Length != 2 ||
            !TryParseCellReference(parts[0], out var startColumn, out var startRow) ||
            !TryParseCellReference(parts[1], out _, out var endRow))
        {
            return false;
        }

        var endColumn = startColumn + columnCount - 1;
        resizedReference = $"{ColumnName(startColumn)}{startRow}:{ColumnName(endColumn)}{endRow}";
        return true;
    }

    private static bool TryResizeTableReference(
        string? reference,
        int columnCount,
        int endRow,
        out string resizedReference)
    {
        resizedReference = string.Empty;
        if (!TryParseTableReference(reference, out var startColumn, out var startRow, out _, out _))
        {
            return false;
        }

        var endColumn = startColumn + columnCount - 1;
        resizedReference = $"{ColumnName(startColumn)}{startRow}:{ColumnName(endColumn)}{endRow}";
        return true;
    }

    private static bool TryParseTableReference(
        string? reference,
        out int startColumn,
        out int startRow,
        out int endColumn,
        out int endRow)
    {
        startColumn = 0;
        startRow = 0;
        endColumn = 0;
        endRow = 0;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var parts = reference.Split(':', 2);
        return parts.Length == 2 &&
            TryParseCellReference(parts[0], out startColumn, out startRow) &&
            TryParseCellReference(parts[1], out endColumn, out endRow);
    }

    private static bool TryParseCellReference(string value, out int column, out int row)
    {
        column = 0;
        row = 0;
        var letters = new string(value.TakeWhile(char.IsLetter).ToArray());
        var digits = new string(value.SkipWhile(char.IsLetter).ToArray());
        if (letters.Length == 0 ||
            digits.Length == 0 ||
            !int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out row))
        {
            return false;
        }

        foreach (var character in letters.ToUpperInvariant())
        {
            column = (column * 26) + character - 'A' + 1;
        }

        return column > 0 && row > 0;
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }

    private static void WriteHiddenMetadataWorksheetCells(
        ZipArchive archive,
        string worksheetEntryName,
        int tableStartColumn,
        int tableStartRow,
        int tableEndRow,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<int> metadataColumnIndexes,
        IReadOnlyList<PortableLogbookWorkbookRow> rows,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        bool writeVisiblePayloadCells = true)
    {
        var worksheet = ReadXmlEntry(archive, worksheetEntryName)
            ?? throw new InvalidDataException("Logbook worksheet part is invalid.");
        var entryIdColumn = tableStartColumn + metadataColumnIndexes[0] - 1;
        var revisionIdColumn = tableStartColumn + metadataColumnIndexes[1] - 1;
        var fieldsByColumnName = writeVisiblePayloadCells
            ? BuildFieldsByWorkbookColumnName()
            : new Dictionary<string, PortableLogbookFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        var customFieldsByLabel = writeVisiblePayloadCells
            ? customFieldDefinitions.ToDictionary(
                field => field.Label,
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CustomFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        UpsertInlineStringCell(
            worksheet,
            $"{ColumnName(entryIdColumn)}{tableStartRow}",
            PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName);
        UpsertInlineStringCell(
            worksheet,
            $"{ColumnName(revisionIdColumn)}{tableStartRow}",
            PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName);

        var lastDataRow = tableStartRow + rows.Count;
        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = tableStartRow + 1 + index;
            if (writeVisiblePayloadCells)
            {
                WriteWorkbookRowPayloadCells(
                    worksheet,
                    tableStartColumn,
                    rowNumber,
                    columnNames,
                    fieldsByColumnName,
                    customFieldsByLabel,
                    rows[index].Entry);
            }

            UpsertInlineStringCell(worksheet, $"{ColumnName(entryIdColumn)}{rowNumber}", rows[index].EntryId?.Value ?? string.Empty);
            UpsertInlineStringCell(
                worksheet,
                $"{ColumnName(revisionIdColumn)}{rowNumber}",
                rows[index].CurrentRevisionId?.Value ?? string.Empty);
        }

        for (var rowNumber = lastDataRow + 1; rowNumber <= tableEndRow; rowNumber++)
        {
            if (writeVisiblePayloadCells)
            {
                RemoveWorkbookRowPayloadCells(worksheet, tableStartColumn, rowNumber, columnNames, fieldsByColumnName, customFieldsByLabel);
            }

            RemoveCell(worksheet, $"{ColumnName(entryIdColumn)}{rowNumber}");
            RemoveCell(worksheet, $"{ColumnName(revisionIdColumn)}{rowNumber}");
        }

        WriteXmlEntry(archive, worksheetEntryName, worksheet);
    }

    private static void WriteHiddenMetadataWorksheetCellsV2(
        ZipArchive archive,
        string worksheetEntryName,
        int tableStartColumn,
        int tableStartRow,
        int tableEndRow,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<int> metadataColumnIndexes,
        IReadOnlyList<PortableLogbookWorkbookRowV2> rows,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        bool writeVisiblePayloadCells = true)
    {
        var worksheet = ReadXmlEntry(archive, worksheetEntryName)
            ?? throw new InvalidDataException("Logbook worksheet part is invalid.");
        var entryIdColumn = tableStartColumn + metadataColumnIndexes[0] - 1;
        var revisionIdColumn = tableStartColumn + metadataColumnIndexes[1] - 1;
        var fieldsByColumnName = writeVisiblePayloadCells
            ? PortableLogbookWorkbookFieldCatalog.ByWorkbookColumnName
            : new Dictionary<string, PortableLogbookWorkbookFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        var customFieldsByLabel = writeVisiblePayloadCells
            ? customFieldDefinitions.ToDictionary(
                field => field.Label,
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CustomFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        UpsertInlineStringCell(
            worksheet,
            $"{ColumnName(entryIdColumn)}{tableStartRow}",
            PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName);
        UpsertInlineStringCell(
            worksheet,
            $"{ColumnName(revisionIdColumn)}{tableStartRow}",
            PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName);

        var lastDataRow = tableStartRow + rows.Count;
        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = tableStartRow + 1 + index;
            if (writeVisiblePayloadCells)
            {
                WriteWorkbookRowPayloadCellsV2(
                    worksheet,
                    tableStartColumn,
                    rowNumber,
                    columnNames,
                    fieldsByColumnName,
                    customFieldsByLabel,
                    rows[index].Entry);
            }

            UpsertInlineStringCell(worksheet, $"{ColumnName(entryIdColumn)}{rowNumber}", rows[index].EntryId?.Value ?? string.Empty);
            UpsertInlineStringCell(
                worksheet,
                $"{ColumnName(revisionIdColumn)}{rowNumber}",
                rows[index].CurrentRevisionId?.Value ?? string.Empty);
        }

        for (var rowNumber = lastDataRow + 1; rowNumber <= tableEndRow; rowNumber++)
        {
            if (writeVisiblePayloadCells)
            {
                RemoveWorkbookRowPayloadCellsV2(worksheet, tableStartColumn, rowNumber, columnNames, fieldsByColumnName, customFieldsByLabel);
            }

            RemoveCell(worksheet, $"{ColumnName(entryIdColumn)}{rowNumber}");
            RemoveCell(worksheet, $"{ColumnName(revisionIdColumn)}{rowNumber}");
        }

        WriteXmlEntry(archive, worksheetEntryName, worksheet);
    }

    private static void WriteWorkbookRowPayloadCells(
        XDocument worksheet,
        int tableStartColumn,
        int rowNumber,
        IReadOnlyList<string> columnNames,
        IReadOnlyDictionary<string, PortableLogbookFieldDefinition> fieldsByColumnName,
        IReadOnlyDictionary<string, CustomFieldDefinition> customFieldsByLabel,
        PortableLogbookEntry entry)
    {
        var values = PortableLogbookEntryFields.ToFieldValues(entry);
        for (var index = 0; index < columnNames.Count; index++)
        {
            var columnName = columnNames[index].Trim();
            var reference = $"{ColumnName(tableStartColumn + index)}{rowNumber}";
            if (fieldsByColumnName.TryGetValue(columnName, out var field))
            {
                WriteOrRemoveInlineStringCell(worksheet, reference, FormatWorkbookCellValue(values[field.Id]));
            }
            else if (customFieldsByLabel.TryGetValue(columnName, out var customField))
            {
                entry.CustomFields.TryGetValue(customField.Id, out var customValue);
                WriteOrRemoveInlineStringCell(worksheet, reference, customValue);
            }
        }
    }

    private static void WriteWorkbookRowPayloadCellsV2(
        XDocument worksheet,
        int tableStartColumn,
        int rowNumber,
        IReadOnlyList<string> columnNames,
        IReadOnlyDictionary<string, PortableLogbookWorkbookFieldDefinition> fieldsByColumnName,
        IReadOnlyDictionary<string, CustomFieldDefinition> customFieldsByLabel,
        PortableLogbookWorkbookEntry entry)
    {
        var values = PortableLogbookWorkbookEntryFields.ToFieldValues(entry);
        for (var index = 0; index < columnNames.Count; index++)
        {
            var columnName = columnNames[index].Trim();
            var reference = $"{ColumnName(tableStartColumn + index)}{rowNumber}";
            if (fieldsByColumnName.TryGetValue(columnName, out var field))
            {
                WriteOrRemoveInlineStringCell(worksheet, reference, FormatWorkbookCellValue(values[field.Id]));
            }
            else if (customFieldsByLabel.TryGetValue(columnName, out var customField))
            {
                entry.CustomFields.TryGetValue(customField.Id, out var customValue);
                WriteOrRemoveInlineStringCell(worksheet, reference, customValue);
            }
        }
    }

    private static void RemoveWorkbookRowPayloadCells(
        XDocument worksheet,
        int tableStartColumn,
        int rowNumber,
        IReadOnlyList<string> columnNames,
        IReadOnlyDictionary<string, PortableLogbookFieldDefinition> fieldsByColumnName,
        IReadOnlyDictionary<string, CustomFieldDefinition> customFieldsByLabel)
    {
        for (var index = 0; index < columnNames.Count; index++)
        {
            var columnName = columnNames[index].Trim();
            if (fieldsByColumnName.ContainsKey(columnName) || customFieldsByLabel.ContainsKey(columnName))
            {
                RemoveCell(worksheet, $"{ColumnName(tableStartColumn + index)}{rowNumber}");
            }
        }
    }

    private static void RemoveWorkbookRowPayloadCellsV2(
        XDocument worksheet,
        int tableStartColumn,
        int rowNumber,
        IReadOnlyList<string> columnNames,
        IReadOnlyDictionary<string, PortableLogbookWorkbookFieldDefinition> fieldsByColumnName,
        IReadOnlyDictionary<string, CustomFieldDefinition> customFieldsByLabel)
    {
        for (var index = 0; index < columnNames.Count; index++)
        {
            var columnName = columnNames[index].Trim();
            if (fieldsByColumnName.ContainsKey(columnName) || customFieldsByLabel.ContainsKey(columnName))
            {
                RemoveCell(worksheet, $"{ColumnName(tableStartColumn + index)}{rowNumber}");
            }
        }
    }

    private static void WriteOrRemoveInlineStringCell(XDocument worksheet, string cellReference, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveCell(worksheet, cellReference);
            return;
        }

        UpsertInlineStringCell(worksheet, cellReference, value);
    }

    private static string? FormatWorkbookCellValue(object? value) =>
        value switch
        {
            null => null,
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int intValue => intValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static void RemoveCell(XDocument worksheet, string cellReference)
    {
        foreach (var cell in worksheet
            .Descendants(SpreadsheetNamespace + "c")
            .Where(element => string.Equals(
                (string?)element.Attribute("r"),
                cellReference,
                StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            cell.Remove();
        }
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer);
    }

    private static void EnsureContentType(ZipArchive archive)
    {
        var document = ReadXmlEntry(archive, "[Content_Types].xml") ?? new XDocument(
            new XElement(
                ContentTypesNamespace + "Types",
                new XElement(
                    ContentTypesNamespace + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(
                    ContentTypesNamespace + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml"))));
        var root = document.Root ?? throw new InvalidDataException("Workbook content types part is invalid.");
        var partName = "/" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath;
        var hasOverride = root
            .Elements(ContentTypesNamespace + "Override")
            .Any(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase));
        if (!hasOverride)
        {
            root.Add(new XElement(
                ContentTypesNamespace + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", "application/xml")));
        }

        WriteXmlEntry(archive, "[Content_Types].xml", document);
    }

    private static void EnsureCustomXmlRelationship(ZipArchive archive)
    {
        var document = ReadXmlEntry(archive, "_rels/.rels") ?? new XDocument(new XElement(RelationshipsNamespace + "Relationships"));
        var root = document.Root ?? throw new InvalidDataException("Workbook relationships part is invalid.");
        var target = PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath;
        var relationship = root
            .Elements(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Id"), "rIdPortableLogbookStorage", StringComparison.Ordinal));
        if (relationship is null)
        {
            root.Add(new XElement(
                RelationshipsNamespace + "Relationship",
                new XAttribute("Id", "rIdPortableLogbookStorage"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"),
                new XAttribute("Target", target)));
        }
        else
        {
            relationship.SetAttributeValue("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml");
            relationship.SetAttributeValue("Target", target);
        }

        WriteXmlEntry(archive, "_rels/.rels", document);
    }

    private static XDocument? ReadXmlEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}

public sealed record PortableLogbookWorkbookMetadataPackageResult(
    IReadOnlyList<string> WorkbookColumnNames,
    IReadOnlyList<string> ColumnsAdded,
    IReadOnlyList<int> HiddenColumnIndexes)
{
    public bool WorkbookMutated => ColumnsAdded.Count > 0 || HiddenColumnIndexes.Count > 0;
}

public sealed record PortableLogbookWorkbookMetadataWriteResult(
    string WorkbookPath,
    int RowCount,
    IReadOnlyList<int> AbsoluteMetadataColumnIndexes);

public sealed record PortableLogbookWorkbookIdentityPackageResult(
    LogbookId LogbookId,
    DeviceId DeviceId,
    int SchemaVersion,
    IReadOnlyList<string> NamesAdded,
    IReadOnlyList<string> CellsWritten)
{
    public bool WorkbookMutated => NamesAdded.Count > 0 || CellsWritten.Count > 0;
}

public sealed record PortableLogbookWorkbookIdentity(
    LogbookId LogbookId,
    DeviceId DeviceId,
    int SchemaVersion);
