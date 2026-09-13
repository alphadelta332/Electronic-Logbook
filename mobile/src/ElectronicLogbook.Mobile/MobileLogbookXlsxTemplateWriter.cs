using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

internal static class MobileLogbookXlsxTemplateWriter
{
    private const int FirstCustomFieldOrder = 1;
    private const int LastCustomFieldOrder = 4;
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Spreadsheet = SpreadsheetNamespace;

    public static byte[] Write(
        IReadOnlyList<PortableLogbookMaterializedEntryV2> entries,
        IReadOnlyList<CustomFieldDefinition> customFields,
        MobileLogbookFileExportLayout layout,
        DateTimeOffset exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(customFields);
        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "The export layout is not supported.");
        }

        var compact = layout == MobileLogbookFileExportLayout.Compact;
        var layoutName = compact ? "Compact" : "Full";
        var resourceName = $"ElectronicLogbook.Mobile.Templates.LogbookExport{layoutName}.xlsx";
        using var template = typeof(MobileLogbookXlsxTemplateWriter).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded logbook export template not found: {resourceName}");
        using var output = new MemoryStream();
        template.CopyTo(output);
        output.Position = 0;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
            var table = ReadXml(archive, "xl/tables/table1.xml");
            var workbook = ReadXml(archive, "xl/workbook.xml");
            var coreProperties = ReadXml(archive, "docProps/core.xml");

            PopulateWorksheetAndTable(worksheet, table, entries, customFields, compact);
            UpdateWorkbook(workbook, entries.Count, compact);
            UpdateCoreProperties(coreProperties, exportedAtUtc);

            WriteXml(archive, "xl/worksheets/sheet1.xml", worksheet);
            WriteXml(archive, "xl/tables/table1.xml", table);
            WriteXml(archive, "xl/workbook.xml", workbook);
            WriteXml(archive, "docProps/core.xml", coreProperties);
        }

        return output.ToArray();
    }

    private static void PopulateWorksheetAndTable(
        XDocument worksheet,
        XDocument table,
        IReadOnlyList<PortableLogbookMaterializedEntryV2> entries,
        IReadOnlyList<CustomFieldDefinition> customFields,
        bool compact)
    {
        var tableRoot = table.Root ?? throw new InvalidDataException("The export template table is missing its root element.");
        var (startColumn, headerRow, endColumn, templateTotalsRow) = ParseRange(
            tableRoot.Attribute("ref")?.Value
            ?? throw new InvalidDataException("The export template table has no range."));
        var firstDataRow = headerRow + 1;
        var lastDataRow = firstDataRow + entries.Count - 1;
        var totalsRow = lastDataRow + 1;
        var grandTotalRow = totalsRow + 1;
        var tableColumns = tableRoot.Element(Spreadsheet + "tableColumns")?
            .Elements(Spreadsheet + "tableColumn")
            .ToArray()
            ?? throw new InvalidDataException("The export template table has no columns.");
        var originalColumnNames = tableColumns
            .Select(column => column.Attribute("name")?.Value
                ?? throw new InvalidDataException("An export template table column has no name."))
            .ToArray();
        var expectedEndColumn = ColumnName(ColumnNumber(startColumn) + tableColumns.Length - 1);
        if (!string.Equals(expectedEndColumn, endColumn, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The export template table column count does not match its range.");
        }

        var worksheetRoot = worksheet.Root
            ?? throw new InvalidDataException("The export template worksheet is missing its root element.");
        var sheetData = worksheetRoot.Element(Spreadsheet + "sheetData")
            ?? throw new InvalidDataException("The export template worksheet has no sheet data.");
        var templateDataRow = FindRow(sheetData, firstDataRow);
        var templateTotals = FindRow(sheetData, templateTotalsRow);
        var templateGrandTotal = FindRow(sheetData, templateTotalsRow + 1);
        var customNumberStyles = ReadCustomNumberStyles(
            templateDataRow,
            templateTotals,
            originalColumnNames,
            startColumn,
            firstDataRow,
            templateTotalsRow);
        var customFieldsByOrder = customFields
            .Where(field => field.IsActive && field.Order is >= FirstCustomFieldOrder and <= LastCustomFieldOrder)
            .GroupBy(field => field.Order)
            .ToDictionary(group => group.Key, group => group.First());

        UpdateCustomFieldHeaders(sheetData, tableColumns, originalColumnNames, startColumn, headerRow, customFieldsByOrder);
        var formulaValues = BuildFormulaValues(entries);
        foreach (var row in sheetData.Elements(Spreadsheet + "row")
                     .Where(row => RowNumber(row) >= firstDataRow)
                     .ToArray())
        {
            row.Remove();
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var rowNumber = firstDataRow + index;
            var row = CloneRow(templateDataRow, rowNumber, startColumn, endColumn);
            for (var columnIndex = 0; columnIndex < originalColumnNames.Length; columnIndex++)
            {
                var name = originalColumnNames[columnIndex];
                var cell = FindCell(row, ColumnName(ColumnNumber(startColumn) + columnIndex), rowNumber);
                ApplyCustomNumberStyle(cell, name, customFieldsByOrder, customNumberStyles, totalsRow: false);
                if (tableColumns[columnIndex].Element(Spreadsheet + "calculatedColumnFormula") is not null)
                {
                    SetFormulaCachedValue(cell, formulaValues[index][name]);
                }
                else
                {
                    SetCellValue(cell, DirectValue(name, entries[index], customFieldsByOrder, compact));
                }
            }
            sheetData.Add(row);
        }

        var totals = CloneRow(templateTotals, totalsRow, startColumn, endColumn);
        for (var columnIndex = 0; columnIndex < originalColumnNames.Length; columnIndex++)
        {
            var cell = FindCell(totals, ColumnName(ColumnNumber(startColumn) + columnIndex), totalsRow);
            if (cell.Element(Spreadsheet + "f") is null)
            {
                continue;
            }

            var name = originalColumnNames[columnIndex];
            ApplyCustomNumberStyle(cell, name, customFieldsByOrder, customNumberStyles, totalsRow: true);
            if (TryGetCustomFieldOrder(name, out var customOrder) &&
                customFieldsByOrder.TryGetValue(customOrder, out var customField) &&
                !string.Equals(customField.Label, name, StringComparison.Ordinal))
            {
                cell.Element(Spreadsheet + "f")!.Value =
                    $"SUBTOTAL(109,{ColumnName(ColumnNumber(startColumn) + columnIndex)}{firstDataRow}:" +
                    $"{ColumnName(ColumnNumber(startColumn) + columnIndex)}{lastDataRow})";
            }

            var cachedTotal = string.Equals(name, "Other Pilot or Crew", StringComparison.Ordinal)
                ? entries.Select((_, index) => NumericValue(formulaValues[index]["TotalHours"])).Sum()
                : entries.Select((entry, index) => tableColumns[columnIndex].Element(Spreadsheet + "calculatedColumnFormula") is not null
                        ? NumericValue(formulaValues[index][name])
                        : NumericValue(DirectValue(name, entry, customFieldsByOrder, compact)))
                    .Sum();
            SetFormulaCachedValue(cell, cachedTotal);
        }
        sheetData.Add(totals);

        var grandTotal = CloneRow(templateGrandTotal, grandTotalRow, startColumn, endColumn);
        var grandTotalFormula = grandTotal.Elements(Spreadsheet + "c")
            .Single(cell => cell.Element(Spreadsheet + "f") is not null);
        var grandTotalValue = entries.Sum(entry =>
            (MobileLogbookFileExportWorkflow.NormalizeHours(
                MobileLogbookSession.WorkbookFlightTime(entry.Entry!)) ?? 0m) +
            (MobileLogbookFileExportWorkflow.NormalizeHours(entry.Entry!.IfrSim) ?? 0m));
        SetFormulaCachedValue(grandTotalFormula, grandTotalValue);
        sheetData.Add(grandTotal);

        var tableReference = $"{startColumn}{headerRow}:{endColumn}{totalsRow}";
        var filterReference = $"{startColumn}{headerRow}:{endColumn}{lastDataRow}";
        tableRoot.SetAttributeValue("ref", tableReference);
        tableRoot.Element(Spreadsheet + "autoFilter")?.SetAttributeValue("ref", filterReference);
        tableRoot.Element(Spreadsheet + "sortState")?.SetAttributeValue(
            "ref",
            $"{ColumnName(ColumnNumber(startColumn) + 1)}{firstDataRow}:{endColumn}{lastDataRow}");
        worksheetRoot.Element(Spreadsheet + "dimension")?.SetAttributeValue("ref", $"A1:{endColumn}{grandTotalRow}");
    }

    private static void UpdateCustomFieldHeaders(
        XElement sheetData,
        IReadOnlyList<XElement> tableColumns,
        IReadOnlyList<string> originalColumnNames,
        string startColumn,
        int headerRow,
        IReadOnlyDictionary<int, CustomFieldDefinition> customFieldsByOrder)
    {
        var header = FindRow(sheetData, headerRow);
        for (var index = 0; index < originalColumnNames.Count; index++)
        {
            if (!TryGetCustomFieldOrder(originalColumnNames[index], out var order) ||
                !customFieldsByOrder.TryGetValue(order, out var definition))
            {
                continue;
            }

            tableColumns[index].SetAttributeValue("name", definition.Label);
            var cell = FindCell(header, ColumnName(ColumnNumber(startColumn) + index), headerRow);
            SetCellValue(cell, definition.Label);
        }
    }

    private static IReadOnlyDictionary<CustomFieldNumberFormat, CustomNumberStyles> ReadCustomNumberStyles(
        XElement templateDataRow,
        XElement templateTotalsRow,
        IReadOnlyList<string> originalColumnNames,
        string startColumn,
        int dataRowNumber,
        int totalsRowNumber)
    {
        var decimalColumnIndex = FindCustomFieldColumnIndex(originalColumnNames, FirstCustomFieldOrder);
        var wholeColumnIndex = FindCustomFieldColumnIndex(originalColumnNames, LastCustomFieldOrder);
        var startColumnNumber = ColumnNumber(startColumn);

        return new Dictionary<CustomFieldNumberFormat, CustomNumberStyles>
        {
            [CustomFieldNumberFormat.Decimals] = new(
                ReadStyle(templateDataRow, ColumnName(startColumnNumber + decimalColumnIndex), dataRowNumber),
                ReadStyle(templateTotalsRow, ColumnName(startColumnNumber + decimalColumnIndex), totalsRowNumber)),
            [CustomFieldNumberFormat.WholeNumbers] = new(
                ReadStyle(templateDataRow, ColumnName(startColumnNumber + wholeColumnIndex), dataRowNumber),
                ReadStyle(templateTotalsRow, ColumnName(startColumnNumber + wholeColumnIndex), totalsRowNumber))
        };
    }

    private static int FindCustomFieldColumnIndex(IReadOnlyList<string> columnNames, int order)
    {
        for (var index = 0; index < columnNames.Count; index++)
        {
            if (TryGetCustomFieldOrder(columnNames[index], out var candidateOrder) && candidateOrder == order)
            {
                return index;
            }
        }

        throw new InvalidDataException($"The export template is missing Custom {order}.");
    }

    private static string ReadStyle(XElement row, string column, int rowNumber) =>
        FindCell(row, column, rowNumber).Attribute("s")?.Value
        ?? throw new InvalidDataException($"The export template cell {column}{rowNumber} has no style.");

    private static void ApplyCustomNumberStyle(
        XElement cell,
        string columnName,
        IReadOnlyDictionary<int, CustomFieldDefinition> customFieldsByOrder,
        IReadOnlyDictionary<CustomFieldNumberFormat, CustomNumberStyles> styles,
        bool totalsRow)
    {
        if (!TryGetCustomFieldOrder(columnName, out var order) ||
            !customFieldsByOrder.TryGetValue(order, out var definition) ||
            definition.NumberFormat is not { } explicitFormat)
        {
            return;
        }

        var selected = styles[explicitFormat];
        cell.SetAttributeValue("s", totalsRow ? selected.Totals : selected.Data);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> BuildFormulaValues(
        IReadOnlyList<PortableLogbookMaterializedEntryV2> entries)
    {
        var values = entries
            .Select(entry => (Dictionary<string, object>)new(StringComparer.Ordinal)
            {
                ["Date"] = entry.Entry!.Date!.Value.ToDateTime(TimeOnly.MinValue).ToOADate(),
                ["TotalHours"] = MobileLogbookFileExportWorkflow.NormalizeHours(
                    MobileLogbookSession.WorkbookFlightTime(entry.Entry)) ?? 0m,
                ["TotalApps"] = WorkbookApproaches(entry.Entry)
            })
            .ToArray();

        var cumulativeHours = 0m;
        for (var index = 0; index < entries.Count; index++)
        {
            cumulativeHours += NumericValue(values[index]["TotalHours"]);
            values[index]["CumTotalHours"] = cumulativeHours;
        }

        var reverseTotals = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["CumLandingsDay"] = 0m,
            ["CumLandingsNight"] = 0m,
            ["CumILS"] = 0m,
            ["CumVOR"] = 0m,
            ["CumRNP"] = 0m,
            ["CumNDB"] = 0m,
            ["CumDgaCdi"] = 0m,
            ["CumDgaAzi"] = 0m,
            ["CumCirc"] = 0m,
            ["CumTotalApps"] = 0m,
            ["Cum2D"] = 0m,
            ["Cum3D"] = 0m,
            ["CumCDI"] = 0m,
            ["CumAzi"] = 0m
        };
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index].Entry!;
            AddReverse("CumLandingsDay", entry.LandingsDay ?? 0);
            AddReverse("CumLandingsNight", entry.LandingsNight ?? 0);
            AddReverse("CumILS", entry.Ils ?? 0);
            AddReverse("CumVOR", entry.Vor ?? 0);
            AddReverse("CumRNP", entry.Rnp ?? 0);
            AddReverse("CumNDB", entry.Ndb ?? 0);
            AddReverse("CumDgaCdi", entry.DgaCdi ?? 0);
            AddReverse("CumDgaAzi", entry.DgaAzi ?? 0);
            AddReverse("CumCirc", entry.Circling ?? 0);
            AddReverse("CumTotalApps", WorkbookApproaches(entry));
            AddReverse("Cum2D", (entry.Vor ?? 0) + (entry.Rnp ?? 0) + (entry.Ndb ?? 0) + (entry.DgaCdi ?? 0) + (entry.DgaAzi ?? 0));
            AddReverse("Cum3D", entry.Ils ?? 0);
            AddReverse("CumCDI", (entry.Ils ?? 0) + (entry.Vor ?? 0) + (entry.Rnp ?? 0) + (entry.DgaCdi ?? 0));
            AddReverse("CumAzi", (entry.Ndb ?? 0) + (entry.DgaAzi ?? 0));

            void AddReverse(string name, decimal amount)
            {
                reverseTotals[name] += amount;
                values[index][name] = reverseTotals[name];
            }
        }

        return values;
    }

    private static object? DirectValue(
        string name,
        PortableLogbookMaterializedEntryV2 materialized,
        IReadOnlyDictionary<int, CustomFieldDefinition> customFieldsByOrder,
        bool compact)
    {
        var entry = materialized.Entry!;
        if (TryGetCustomFieldOrder(name, out var customOrder))
        {
            if (!customFieldsByOrder.TryGetValue(customOrder, out var definition) ||
                !entry.CustomFields.TryGetValue(definition.Id, out var customValue) ||
                string.IsNullOrWhiteSpace(customValue))
            {
                return null;
            }

            return decimal.TryParse(customValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : customValue;
        }

        return name switch
        {
            "EntryID" => materialized.EntryId.Value,
            "Year" => entry.Year,
            "Month" => entry.Month,
            "Day" => entry.Day,
            "Type" => entry.Type,
            "Reg" => entry.Reg,
            "Flight ID" => entry.FlightId,
            "PIC" => entry.Pic,
            "Other Pilot or Crew" => entry.OtherPilotOrCrew,
            "From" => entry.From,
            "To" => entry.To,
            "Via" => entry.Via,
            "Remarks" => entry.Remarks,
            "FR" => entry.FlightReview,
            "IPC" => entry.InstrumentProficiencyCheck,
            "OPC" => entry.OperatorProficiencyCheck,
            "Details" when compact => MobileLogbookFileExportWorkflow.CombinedDetails(entry),
            "SeIcusDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeIcusDay),
            "SeIcusNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeIcusNight),
            "SeDualDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeDualDay),
            "SeDualNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeDualNight),
            "SeCommandDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeCommandDay),
            "SeCommandNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.SeCommandNight),
            "MeIcusDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeIcusDay),
            "MeIcusNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeIcusNight),
            "MeDualDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeDualDay),
            "MeDualNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeDualNight),
            "MeCommandDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeCommandDay),
            "MeCommandNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.MeCommandNight),
            "CopilotDay" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.CopilotDay),
            "CopilotNight" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.CopilotNight),
            "IfrIf" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.IfrIf),
            "IfrSim" => MobileLogbookFileExportWorkflow.NormalizeHours(entry.IfrSim),
            "LandingsDay" => entry.LandingsDay,
            "LandingsNight" => entry.LandingsNight,
            "ILS" => entry.Ils,
            "VOR" => entry.Vor,
            "RNP" => entry.Rnp,
            "NDB" => entry.Ndb,
            "DGA (CDI)" => entry.DgaCdi,
            "DGA (Azi)" => entry.DgaAzi,
            "Circling" => entry.Circling,
            _ => null
        };
    }

    private static void UpdateWorkbook(XDocument workbook, int entryCount, bool compact)
    {
        var totalsRow = 6 + entryCount;
        var grandTotalRow = totalsRow + 1;
        var names = workbook.Descendants(Spreadsheet + "definedName")
            .ToDictionary(name => name.Attribute("name")?.Value ?? string.Empty, StringComparer.Ordinal);
        names["LogbookSumTotals"].Value = compact
            ? $"Logbook!$M${totalsRow}:$AQ${totalsRow}"
            : $"Logbook!$S${totalsRow}:$AW${totalsRow}";
        names["LogbookTotals"].Value = $"Logbook!$I${totalsRow}:$K${grandTotalRow}";

        var calc = workbook.Root?.Element(Spreadsheet + "calcPr")
            ?? throw new InvalidDataException("The export template workbook has no calculation settings.");
        calc.SetAttributeValue("calcMode", "auto");
        calc.SetAttributeValue("fullCalcOnLoad", "1");
        calc.SetAttributeValue("forceFullCalc", "1");
    }

    private static void UpdateCoreProperties(XDocument coreProperties, DateTimeOffset exportedAtUtc)
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace dcterms = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var root = coreProperties.Root
            ?? throw new InvalidDataException("The export template core properties are missing their root element.");
        root.SetElementValue(dc + "title", "FlightLogX Logbook Export");
        root.SetElementValue(dc + "creator", "FlightLogX");
        root.SetElementValue(cp + "lastModifiedBy", "FlightLogX");
        var timestamp = exportedAtUtc.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        foreach (var name in new[] { "created", "modified" })
        {
            var element = root.Element(dcterms + name);
            if (element is null)
            {
                element = new XElement(dcterms + name);
                root.Add(element);
            }
            element.SetAttributeValue(xsi + "type", "dcterms:W3CDTF");
            element.Value = timestamp;
        }
    }

    private static XElement CloneRow(XElement source, int rowNumber, string startColumn, string endColumn)
    {
        var start = ColumnNumber(startColumn);
        var end = ColumnNumber(endColumn);
        var clone = new XElement(source);
        clone.SetAttributeValue("r", rowNumber);
        foreach (var cell in clone.Elements(Spreadsheet + "c").ToArray())
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
            var columnNumber = ColumnNumber(column);
            if (columnNumber < start || columnNumber > end)
            {
                cell.Remove();
                continue;
            }
            cell.SetAttributeValue("r", $"{column}{rowNumber}");
        }
        return clone;
    }

    private static XElement FindRow(XElement sheetData, int rowNumber) =>
        sheetData.Elements(Spreadsheet + "row").Single(row => RowNumber(row) == rowNumber);

    private static int RowNumber(XElement row) =>
        int.Parse(row.Attribute("r")?.Value ?? "0", CultureInfo.InvariantCulture);

    private static XElement FindCell(XElement row, string column, int rowNumber) =>
        row.Elements(Spreadsheet + "c")
            .Single(cell => string.Equals(cell.Attribute("r")?.Value, $"{column}{rowNumber}", StringComparison.Ordinal));

    private static void SetCellValue(XElement cell, object? value)
    {
        cell.Attribute("t")?.Remove();
        cell.Elements(Spreadsheet + "f").Remove();
        cell.Elements(Spreadsheet + "v").Remove();
        cell.Elements(Spreadsheet + "is").Remove();
        switch (value)
        {
            case null:
                return;
            case bool flag:
                cell.SetAttributeValue("t", "b");
                cell.Add(new XElement(Spreadsheet + "v", flag ? "1" : "0"));
                return;
            case decimal number:
                cell.Add(new XElement(Spreadsheet + "v", number.ToString("G29", CultureInfo.InvariantCulture)));
                return;
            case double number:
                cell.Add(new XElement(Spreadsheet + "v", number.ToString("R", CultureInfo.InvariantCulture)));
                return;
            case int number:
                cell.Add(new XElement(Spreadsheet + "v", number.ToString(CultureInfo.InvariantCulture)));
                return;
            default:
                var text = value.ToString() ?? string.Empty;
                var textElement = new XElement(Spreadsheet + "t", text);
                if (text.Length != text.Trim().Length)
                {
                    textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                cell.SetAttributeValue("t", "inlineStr");
                cell.Add(new XElement(Spreadsheet + "is", textElement));
                return;
        }
    }

    private static void SetFormulaCachedValue(XElement cell, object value)
    {
        cell.Attribute("t")?.Remove();
        cell.Elements(Spreadsheet + "v").Remove();
        var formula = cell.Element(Spreadsheet + "f")
            ?? throw new InvalidDataException($"Expected formula cell {cell.Attribute("r")?.Value} is missing its formula.");
        formula.AddAfterSelf(new XElement(Spreadsheet + "v", FormatNumeric(value)));
    }

    private static string FormatNumeric(object value) => value switch
    {
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"Unsupported cached formula value type: {value.GetType().Name}.")
    };

    private static decimal NumericValue(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        double number => (decimal)number,
        _ => 0m
    };

    private static int WorkbookApproaches(PortableLogbookWorkbookEntry entry) =>
        (entry.Ils ?? 0) + (entry.Vor ?? 0) + (entry.Rnp ?? 0) + (entry.Ndb ?? 0) +
        (entry.DgaCdi ?? 0) + (entry.DgaAzi ?? 0);

    private static bool TryGetCustomFieldOrder(string name, out int order)
    {
        order = 0;
        return name.StartsWith("Custom ", StringComparison.Ordinal) &&
               int.TryParse(name.AsSpan("Custom ".Length), NumberStyles.None, CultureInfo.InvariantCulture, out order) &&
               order is >= FirstCustomFieldOrder and <= LastCustomFieldOrder;
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"The export template is missing {path}.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"The export template is missing {path}.");
        using var stream = entry.Open();
        stream.SetLength(0);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false
        });
        document.Save(writer);
    }

    private static (string StartColumn, int StartRow, string EndColumn, int EndRow) ParseRange(string reference)
    {
        var separator = reference.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == reference.Length - 1)
        {
            throw new InvalidDataException($"Unsupported export template range: {reference}.");
        }
        var start = ParseCellReference(reference[..separator]);
        var end = ParseCellReference(reference[(separator + 1)..]);
        return (start.Column, start.Row, end.Column, end.Row);
    }

    private static (string Column, int Row) ParseCellReference(string reference)
    {
        var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var rowText = new string(reference.SkipWhile(char.IsLetter).ToArray());
        if (column.Length == 0 || !int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out var row))
        {
            throw new InvalidDataException($"Unsupported export template cell reference: {reference}.");
        }
        return (column, row);
    }

    private static int ColumnNumber(string name)
    {
        var result = 0;
        foreach (var character in name)
        {
            result = (result * 26) + character - 'A' + 1;
        }
        return result;
    }

    private static string ColumnName(int number)
    {
        var result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + (number % 26)) + result;
            number /= 26;
        }
        return result;
    }

    private sealed record CustomNumberStyles(string Data, string Totals);
}
