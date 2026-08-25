using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileWorkbookMigrationReader
{
    private const int MaxXmlEntryBytes = 32 * 1024 * 1024;
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace DocumentRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static MobileWorkbookMigrationPlan Inspect(BrowserFile file, LogbookId targetLogbookId)
    {
        ArgumentNullException.ThrowIfNull(file);
        BrowserFileStore.ValidateWorkbookFile(file);
        if (string.IsNullOrWhiteSpace(targetLogbookId.Value))
        {
            throw new ArgumentException("Target app logbook identity is required.", nameof(targetLogbookId));
        }

        try
        {
            using var stream = new MemoryStream(file.Bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var tablePart = FindLogbookTablePart(archive);
            var table = ReadXmlEntry(archive, tablePart);
            var tableRoot = table.Root ?? throw new InvalidDataException("Logbook table XML is invalid.");
            var tableReference = ParseTableReference((string?)tableRoot.Attribute("ref"));
            var columnNames = tableRoot
                .Element(SpreadsheetNamespace + "tableColumns")?
                .Elements(SpreadsheetNamespace + "tableColumn")
                .Select(column => ((string?)column.Attribute("name") ?? string.Empty).Trim())
                .ToArray()
                ?? throw new InvalidDataException("Logbook table does not contain column definitions.");
            if (columnNames.Length != tableReference.ColumnCount)
            {
                throw new InvalidDataException("Logbook table column count does not match its worksheet range.");
            }

            var worksheetPart = FindWorksheetPartForTable(archive, tablePart);
            var worksheet = ReadXmlEntry(archive, worksheetPart);
            var sharedStrings = ReadSharedStrings(archive);
            var cells = ReadWorksheetCells(worksheet, sharedStrings);
            var customFields = ReadCustomFields(columnNames);
            var rows = ReadRows(tableRoot, tableReference, columnNames, customFields, cells);
            var calculatedTotals = MobileWorkbookMigrationTotals.Calculate(rows.Select(row => row.Entry));
            var cachedTotals = ReadCachedTotals(tableRoot, tableReference, columnNames, cells);
            var currencyOverrides = ReadCurrencyOverrides(archive, sharedStrings);
            var entryValuesSha256 = ComputeEntryValuesSha256(rows);
            var sourceSha256 = Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant();

            return new MobileWorkbookMigrationPlan(
                file.FileName,
                sourceSha256,
                ReadDefinedNameValue(archive, sharedStrings, "LogbookVersion"),
                ReadLogbookId(archive, sharedStrings),
                targetLogbookId,
                customFields,
                currencyOverrides,
                rows,
                calculatedTotals,
                cachedTotals,
                entryValuesSha256);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            throw new InvalidDataException("Selected file is not a readable Electronic Logbook workbook.", ex);
        }
    }

    private static string FindLogbookTablePart(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var table = ReadXmlEntry(archive, entry.FullName).Root;
            if (string.Equals((string?)table?.Attribute("name"), "Logbook", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)table?.Attribute("displayName"), "Logbook", StringComparison.OrdinalIgnoreCase))
            {
                return entry.FullName;
            }
        }

        throw new InvalidDataException("Workbook does not contain the required Logbook table.");
    }

    private static string FindWorksheetPartForTable(ZipArchive archive, string tablePart)
    {
        foreach (var relationshipsEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/worksheets/_rels/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationships = ReadXmlEntry(archive, relationshipsEntry.FullName);
            var worksheetPart = "xl/worksheets/" + Path.GetFileNameWithoutExtension(relationshipsEntry.Name);
            foreach (var relationship in relationships.Descendants(RelationshipsNamespace + "Relationship"))
            {
                var target = (string?)relationship.Attribute("Target");
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                var resolved = ResolveRelationshipTarget(worksheetPart, target);
                if (string.Equals(resolved, tablePart, StringComparison.OrdinalIgnoreCase))
                {
                    return worksheetPart;
                }
            }
        }

        throw new InvalidDataException("Workbook does not contain the worksheet used by the Logbook table.");
    }

    private static IReadOnlyList<CustomFieldDefinition> ReadCustomFields(IReadOnlyList<string> columnNames)
    {
        var pilotFields = PortableLogbookWorkbookFieldCatalog.PilotEnteredFields;
        var pilotStartIndex = columnNames
            .Select((name, index) => (name, index))
            .Where(item => string.Equals(item.name, pilotFields[0].WorkbookColumnName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        var customIndexes = pilotFields
            .Select((field, index) => (field, index))
            .Where(item => item.field.Id.StartsWith("custom", StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        var labels = pilotStartIndex >= 0 && customIndexes.All(index => pilotStartIndex + index < columnNames.Count)
            ? customIndexes.Select(index => columnNames[pilotStartIndex + index]).ToArray()
            : [];

        if (labels.Length != PortableLogbookCustomFieldSet.WorkbookCustomFieldCount ||
            labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Logbook table does not contain four readable custom-field columns.");
        }

        return PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(labels);
    }

    private static IReadOnlyList<MobileWorkbookMigrationRow> ReadRows(
        XElement tableRoot,
        TableReference reference,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<CustomFieldDefinition> customFields,
        IReadOnlyDictionary<string, string?> cells)
    {
        var dataEndRow = reference.EndRow - ReadTotalsRowCount(tableRoot);
        var customFieldIndexes = PortableLogbookWorkbookFieldCatalog.PilotEnteredFields
            .Select((field, index) => (field, index))
            .Where(item => item.field.Id.StartsWith("custom", StringComparison.Ordinal))
            .ToDictionary(item => item.index, item => customFields.Single(field => field.Order == item.index - 14));
        var pilotStartIndex = Array.FindIndex(
            columnNames.ToArray(),
            name => string.Equals(name, PortableLogbookWorkbookFieldCatalog.PilotEnteredFields[0].WorkbookColumnName, StringComparison.OrdinalIgnoreCase));
        var rows = new List<MobileWorkbookMigrationRow>();

        for (var rowNumber = reference.StartRow + 1; rowNumber <= dataEndRow; rowNumber++)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            EntryId? sourceEntryId = null;
            var rowHasUserData = false;
            for (var index = 0; index < columnNames.Count; index++)
            {
                var columnName = columnNames[index];
                var cellReference = $"{ColumnName(reference.StartColumn + index)}{rowNumber}";
                cells.TryGetValue(cellReference, out var rawValue);
                if (string.Equals(columnName, PortableLogbookWorkbookFieldCatalog.EntryIdColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = new EntryId(rawValue?.Trim() ?? string.Empty);
                    sourceEntryId = EntryId.IsValid(candidate) ? candidate : null;
                    continue;
                }

                if (pilotStartIndex >= 0 && customFieldIndexes.TryGetValue(index - pilotStartIndex, out var customField))
                {
                    if (!string.IsNullOrWhiteSpace(rawValue))
                    {
                        rowHasUserData = true;
                    }

                    values[$"custom{customField.Order}"] = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
                    continue;
                }

                if (!PortableLogbookWorkbookFieldCatalog.ByWorkbookColumnName.TryGetValue(columnName, out var field) ||
                    field.Id.StartsWith("custom", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    rowHasUserData = true;
                }

                try
                {
                    values[field.Id] = ConvertWorkbookCellValue(field, rawValue);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new InvalidDataException(
                        $"Unable to read Logbook row {rowNumber}, column '{columnName}'. Value: '{rawValue}'.",
                        ex);
                }
            }

            if (!rowHasUserData || !LooksLikeFlightEntry(values))
            {
                continue;
            }

            var entry = PortableLogbookWorkbookEntryFields.FromFieldValues(values, customFields);
            rows.Add(new MobileWorkbookMigrationRow(rowNumber, sourceEntryId, entry));
        }

        return rows;
    }

    private static object? ConvertWorkbookCellValue(PortableLogbookWorkbookFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (field.Id == "dateMonth")
        {
            return ConvertWorkbookMonth(text);
        }

        if (field.Id == "dateDay")
        {
            return ConvertWorkbookDay(text);
        }

        return field.Kind switch
        {
            PortableLogbookWorkbookFieldKind.Boolean => ParseWorkbookBoolean(text),
            PortableLogbookWorkbookFieldKind.DecimalHours => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture),
            PortableLogbookWorkbookFieldKind.Count => Convert.ToInt32(decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            _ => text
        };
    }

    private static int ConvertWorkbookMonth(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericMonth) &&
            numericMonth is >= 1 and <= 12)
        {
            return numericMonth;
        }

        if (DateTime.TryParseExact(text, ["MMM", "MMMM"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var namedMonth))
        {
            return namedMonth.Month;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial >= 32)
        {
            return DateTime.FromOADate(serial).Month;
        }

        throw new FormatException($"Workbook month value '{text}' is not recognised.");
    }

    private static int ConvertWorkbookDay(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericDay) &&
            numericDay is >= 1 and <= 31)
        {
            return numericDay;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial >= 1)
        {
            return DateTime.FromOADate(serial).Day;
        }

        throw new FormatException($"Workbook day value '{text}' is not recognised.");
    }

    private static bool ParseWorkbookBoolean(string text) => text.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "Y" or "1" => true,
        "FALSE" or "NO" or "N" or "0" => false,
        _ => throw new FormatException($"Workbook boolean value '{text}' is not recognised.")
    };

    private static bool LooksLikeFlightEntry(IReadOnlyDictionary<string, object?> values)
    {
        var hasDate = values.GetValueOrDefault("dateYear") is not null &&
                      values.GetValueOrDefault("dateMonth") is not null &&
                      values.GetValueOrDefault("dateDay") is not null;
        var hasIdentity = HasText(values, "type") || HasText(values, "reg") || HasText(values, "from") || HasText(values, "to");
        var hasLoggedTime = PortableLogbookWorkbookFieldCatalog.PilotEnteredFields
            .Where(field => field.Kind == PortableLogbookWorkbookFieldKind.DecimalHours)
            .Any(field => values.GetValueOrDefault(field.Id) is decimal amount && amount > 0);
        return hasDate && hasIdentity && hasLoggedTime;
    }

    private static bool HasText(IReadOnlyDictionary<string, object?> values, string key) =>
        values.GetValueOrDefault(key) is string text && !string.IsNullOrWhiteSpace(text);

    private static MobileWorkbookMigrationCachedTotals ReadCachedTotals(
        XElement tableRoot,
        TableReference reference,
        IReadOnlyList<string> columnNames,
        IReadOnlyDictionary<string, string?> cells)
    {
        if (ReadTotalsRowCount(tableRoot) == 0)
        {
            return MobileWorkbookMigrationCachedTotals.Empty;
        }

        decimal? ReadDecimal(string columnName)
        {
            var index = columnNames
                .Select((name, columnIndex) => (name, columnIndex))
                .Where(item => string.Equals(item.name, columnName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.columnIndex)
                .DefaultIfEmpty(-1)
                .First();
            if (index < 0 || !cells.TryGetValue($"{ColumnName(reference.StartColumn + index)}{reference.EndRow}", out var text) ||
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            return value;
        }

        return new MobileWorkbookMigrationCachedTotals(
            ReadDecimal("TotalHours"),
            ReadDecimal("IfrSim"),
            ReadDecimal("LandingsDay"),
            ReadDecimal("LandingsNight"),
            ReadDecimal("TotalApps"));
    }

    private static PortableLogbookCurrencyOverrideDates ReadCurrencyOverrides(
        ZipArchive archive,
        IReadOnlyList<string> sharedStrings) =>
        new(
            ReadDefinedNameDate(archive, sharedStrings, "FROverride"),
            ReadDefinedNameDate(archive, sharedStrings, "IPCOverride"),
            ReadDefinedNameDate(archive, sharedStrings, "OPCOverride"));

    private static DateOnly? ReadDefinedNameDate(ZipArchive archive, IReadOnlyList<string> sharedStrings, string name)
    {
        var value = ReadDefinedNameValue(archive, sharedStrings, name);
        if (string.IsNullOrWhiteSpace(value) || value == "0")
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException)
            {
            }
        }

        throw new InvalidDataException($"Workbook named value '{name}' is not a readable date.");
    }

    private static LogbookId? ReadLogbookId(ZipArchive archive, IReadOnlyList<string> sharedStrings)
    {
        var value = ReadDefinedNameValue(archive, sharedStrings, PortableLogbookWorkbookMetadata.LogbookIdName);
        return string.IsNullOrWhiteSpace(value) ? null : new LogbookId(value.Trim());
    }

    private static string? ReadDefinedNameValue(ZipArchive archive, IReadOnlyList<string> sharedStrings, string name)
    {
        var workbook = ReadXmlEntry(archive, "xl/workbook.xml");
        var definedName = workbook
            .Descendants(SpreadsheetNamespace + "definedName")
            .FirstOrDefault(candidate => string.Equals((string?)candidate.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
        if (definedName is null || !TryParseSingleCellReference(definedName.Value, out var sheetName, out var cellReference))
        {
            return null;
        }

        var relationshipId = workbook
            .Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(sheet => string.Equals((string?)sheet.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(DocumentRelationshipsNamespace + "id")
            ?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return null;
        }

        var relationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels");
        var target = relationships
            .Descendants(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(relationship => string.Equals((string?)relationship.Attribute("Id"), relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")
            ?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var worksheet = ReadXmlEntry(archive, ResolveRelationshipTarget("xl/workbook.xml", target));
        return ReadWorksheetCells(worksheet, sharedStrings).GetValueOrDefault(cellReference);
    }

    private static IReadOnlyDictionary<string, string?> ReadWorksheetCells(XDocument worksheet, IReadOnlyList<string> sharedStrings) =>
        worksheet
            .Descendants(SpreadsheetNamespace + "c")
            .Select(cell => new
            {
                Reference = ((string?)cell.Attribute("r") ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal),
                Value = ReadCellText(cell, sharedStrings)
            })
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Reference))
            .GroupBy(cell => cell.Reference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

    private static string? ReadCellText(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(element => element.Value));
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex) &&
            sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        if (archive.GetEntry("xl/sharedStrings.xml") is null)
        {
            return [];
        }

        return ReadXmlEntry(archive, "xl/sharedStrings.xml")
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static XDocument ReadXmlEntry(ZipArchive archive, string entryName)
    {
        var normalizedName = NormalizePackagePath(entryName);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(NormalizePackagePath(candidate.FullName), normalizedName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Workbook package part '{normalizedName}' is missing.");
        if (entry.Length > MaxXmlEntryBytes)
        {
            throw new InvalidDataException($"Workbook package part '{normalizedName}' is too large to inspect safely.");
        }

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static int ReadTotalsRowCount(XElement tableRoot) =>
        int.TryParse((string?)tableRoot.Attribute("totalsRowCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? Math.Max(0, count)
            : 0;

    private static TableReference ParseTableReference(string? value)
    {
        var parts = value?.Split(':', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } ||
            !TryParseCellReference(parts[0], out var startColumn, out var startRow) ||
            !TryParseCellReference(parts[1], out var endColumn, out var endRow) ||
            startColumn > endColumn || startRow >= endRow)
        {
            throw new InvalidDataException("Logbook table range is invalid.");
        }

        return new TableReference(startColumn, startRow, endColumn, endRow);
    }

    private static bool TryParseSingleCellReference(string value, out string sheetName, out string cellReference)
    {
        sheetName = string.Empty;
        cellReference = string.Empty;
        var separatorIndex = value.LastIndexOf('!');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        sheetName = value[..separatorIndex].Trim();
        if (sheetName.StartsWith("'", StringComparison.Ordinal) && sheetName.EndsWith("'", StringComparison.Ordinal))
        {
            sheetName = sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        cellReference = value[(separatorIndex + 1)..].Replace("$", string.Empty, StringComparison.Ordinal);
        return TryParseCellReference(cellReference, out _, out _);
    }

    private static bool TryParseCellReference(string value, out int column, out int row)
    {
        column = 0;
        row = 0;
        var letters = value.TakeWhile(char.IsLetter).ToArray();
        var digits = value.SkipWhile(char.IsLetter).ToArray();
        if (letters.Length == 0 || digits.Length == 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out row) || row <= 0)
        {
            return false;
        }

        foreach (var letter in letters)
        {
            var upper = char.ToUpperInvariant(letter);
            if (upper is < 'A' or > 'Z')
            {
                return false;
            }

            column = checked((column * 26) + (upper - 'A' + 1));
        }

        return column > 0;
    }

    private static string ColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            column--;
            name.Insert(0, (char)('A' + (column % 26)));
            column /= 26;
        }

        return name.ToString();
    }

    private static string ResolveRelationshipTarget(string sourcePart, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizePackagePath(target);
        }

        var sourceDirectory = sourcePart.Contains('/')
            ? sourcePart[..(sourcePart.LastIndexOf('/') + 1)]
            : string.Empty;
        return NormalizePackagePath(sourceDirectory + target);
    }

    private static string NormalizePackagePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException("Workbook relationship target escapes the package root.");
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    public static string ComputeEntryValuesSha256(IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        var canonical = JsonSerializer.Serialize(
            entries.ToArray(),
            PortableLogbookJson.SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string ComputeEntryValuesSha256(IEnumerable<MobileWorkbookMigrationRow> rows) =>
        ComputeEntryValuesSha256(rows.Select(row => row.Entry));

    private sealed record TableReference(int StartColumn, int StartRow, int EndColumn, int EndRow)
    {
        public int ColumnCount => EndColumn - StartColumn + 1;
    }
}

public sealed record MobileWorkbookMigrationPlan(
    string SourceFileName,
    string SourceSha256,
    string? WorkbookVersion,
    LogbookId? EmbeddedWorkbookLogbookId,
    LogbookId TargetLogbookId,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    PortableLogbookCurrencyOverrideDates CurrencyOverrideDates,
    IReadOnlyList<MobileWorkbookMigrationRow> Rows,
    MobileWorkbookMigrationTotals CalculatedTotals,
    MobileWorkbookMigrationCachedTotals CachedWorkbookTotals,
    string EntryValuesSha256)
{
    public bool CachedTotalsMatch => CachedWorkbookTotals.Matches(CalculatedTotals);
}

public sealed record MobileWorkbookMigrationRow(
    int SourceRowNumber,
    EntryId? SourceEntryId,
    PortableLogbookWorkbookEntry Entry);

public sealed record MobileWorkbookMigrationTotals(
    int EntryCount,
    decimal FlightHours,
    decimal SimulatorHours,
    decimal LoggedHours,
    int DayLandings,
    int NightLandings,
    int InstrumentApproaches,
    int Circling)
{
    public static MobileWorkbookMigrationTotals Calculate(IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        var materialized = entries.ToArray();
        var flightHours = materialized.Sum(MobileLogbookSession.WorkbookFlightTime);
        var simulatorHours = materialized.Sum(entry => entry.IfrSim ?? 0);
        return new MobileWorkbookMigrationTotals(
            materialized.Length,
            flightHours,
            simulatorHours,
            flightHours + simulatorHours,
            materialized.Sum(entry => entry.LandingsDay ?? 0),
            materialized.Sum(entry => entry.LandingsNight ?? 0),
            materialized.Sum(entry =>
                entry.Ils.GetValueOrDefault() +
                entry.Vor.GetValueOrDefault() +
                entry.Rnp.GetValueOrDefault() +
                entry.Ndb.GetValueOrDefault() +
                entry.DgaCdi.GetValueOrDefault() +
                entry.DgaAzi.GetValueOrDefault()),
            materialized.Sum(entry => entry.Circling ?? 0));
    }
}

public sealed record MobileWorkbookMigrationCachedTotals(
    decimal? FlightHours,
    decimal? SimulatorHours,
    decimal? DayLandings,
    decimal? NightLandings,
    decimal? InstrumentApproaches)
{
    public static MobileWorkbookMigrationCachedTotals Empty { get; } = new(null, null, null, null, null);

    public bool Matches(MobileWorkbookMigrationTotals totals) =>
        MatchesIfPresent(FlightHours, totals.FlightHours) &&
        MatchesIfPresent(SimulatorHours, totals.SimulatorHours) &&
        MatchesIfPresent(DayLandings, totals.DayLandings) &&
        MatchesIfPresent(NightLandings, totals.NightLandings) &&
        MatchesIfPresent(InstrumentApproaches, totals.InstrumentApproaches);

    private static bool MatchesIfPresent(decimal? cached, decimal calculated) =>
        cached is null || cached.Value == calculated;
}
