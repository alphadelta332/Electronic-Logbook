using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileLogbookFileExportWorkflow
{
    private const string ExportTimestampHeader = "ExportedAtUtc";

    public static async ValueTask<MobileLogbookFileExportResult> ExportAsync(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        IEnumerable<CustomFieldDefinition> customFields,
        MobileLogbookFileExportRequest request,
        BrowserFileStore fileStore,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(fileStore);
        var file = Create(entries, customFields, request, exportedAt);
        var transfer = await fileStore.ShareLogbookExportOrDownloadAsync(
            file.FileName,
            file.Bytes,
            file.ContentType).ConfigureAwait(false);
        return new MobileLogbookFileExportResult(file, transfer);
    }

    public static MobileLogbookFileExportFile Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        IEnumerable<CustomFieldDefinition> customFields,
        MobileLogbookFileExportRequest request,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(customFields);
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartDate is { } start && request.EndDate is { } end && start > end)
        {
            throw new InvalidOperationException("The start date cannot be later than the end date.");
        }

        var selected = entries.Where(entry =>
        {
            var date = entry.Date ?? throw new InvalidOperationException("A logbook entry does not have a valid date.");
            return (request.StartDate is null || date >= request.StartDate) &&
                   (request.EndDate is null || date <= request.EndDate);
        }).ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("No logbook entries match the selected date range.");
        }

        var utc = exportedAt.ToUniversalTime();
        var timestamp = utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var fields = customFields
            .OrderBy(field => field.Order)
            .Where(field => selected.Any(entry =>
                entry.CustomFields.TryGetValue(field.Id, out var value) && !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        var table = CreateTable(selected, fields, request.CombineDetails, timestamp);
        var extension = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? BrowserFileStore.ExcelWorkbookExtension
            : BrowserFileStore.CsvExtension;
        var contentType = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? BrowserFileStore.ExcelWorkbookContentType
            : BrowserFileStore.CsvContentType;
        var fileName = $"flightlogx-logbook-{utc:yyyyMMdd'T'HHmmss'Z'}{extension}";
        var bytes = request.Format == MobileLogbookFileExportFormat.Xlsx
            ? WriteXlsx(table, utc)
            : WriteCsv(table);
        return new MobileLogbookFileExportFile(fileName, contentType, utc, table, bytes);
    }

    private static MobileLogbookFileExportTable CreateTable(
        IReadOnlyList<PortableLogbookWorkbookEntry> entries,
        IReadOnlyList<CustomFieldDefinition> customFields,
        bool combineDetails,
        string exportedAtUtc)
    {
        var columns = new List<MobileLogbookFileExportColumn>
        {
            Text("Year", entry => entry.Year),
            Text("Month", entry => entry.Month),
            Text("Day", entry => entry.Day),
            Text("Type", entry => entry.Type),
            Text("Reg", entry => entry.Reg),
            Text("Flight ID", entry => entry.FlightId),
            Text("PIC", entry => entry.Pic),
            Text("Other Pilot or Crew", entry => entry.OtherPilotOrCrew)
        };

        if (combineDetails)
        {
            columns.Add(Text("Details", CombinedDetails));
        }
        else
        {
            columns.AddRange([
                Text("From", entry => entry.From),
                Text("To", entry => entry.To),
                Text("Via", entry => entry.Via),
                Text("Remarks", entry => entry.Remarks),
                Flag("FR", entry => entry.FlightReview),
                Flag("IPC", entry => entry.InstrumentProficiencyCheck),
                Flag("OPC", entry => entry.OperatorProficiencyCheck)
            ]);
        }

        columns.AddRange(customFields.Select(field =>
            Text(field.Label, entry => entry.CustomFields.TryGetValue(field.Id, out var value) ? value : null)));
        columns.AddRange([
            Hours("SeIcusDay", entry => entry.SeIcusDay),
            Hours("SeIcusNight", entry => entry.SeIcusNight),
            Hours("SeDualDay", entry => entry.SeDualDay),
            Hours("SeDualNight", entry => entry.SeDualNight),
            Hours("SeCommandDay", entry => entry.SeCommandDay),
            Hours("SeCommandNight", entry => entry.SeCommandNight),
            Hours("MeIcusDay", entry => entry.MeIcusDay),
            Hours("MeIcusNight", entry => entry.MeIcusNight),
            Hours("MeDualDay", entry => entry.MeDualDay),
            Hours("MeDualNight", entry => entry.MeDualNight),
            Hours("MeCommandDay", entry => entry.MeCommandDay),
            Hours("MeCommandNight", entry => entry.MeCommandNight),
            Hours("CopilotDay", entry => entry.CopilotDay),
            Hours("CopilotNight", entry => entry.CopilotNight),
            Hours("IfrIf", entry => entry.IfrIf),
            Hours("IfrSim", entry => entry.IfrSim),
            Count("LandingsDay", entry => entry.LandingsDay),
            Count("LandingsNight", entry => entry.LandingsNight),
            Count("ILS", entry => entry.Ils),
            Count("VOR", entry => entry.Vor),
            Count("RNP", entry => entry.Rnp),
            Count("NDB", entry => entry.Ndb),
            Count("DGA (CDI)", entry => entry.DgaCdi),
            Count("DGA (Azi)", entry => entry.DgaAzi),
            Count("Circling", entry => entry.Circling),
            Hours("TotalHours", entry => MobileLogbookSession.WorkbookFlightTime(entry)),
            Count("TotalApps", entry => WorkbookApproaches(entry)),
            Text(ExportTimestampHeader, _ => exportedAtUtc)
        ]);

        var rows = entries
            .Select(entry => (IReadOnlyList<object?>)columns.Select(column => column.Value(entry)).ToArray())
            .ToArray();
        return new MobileLogbookFileExportTable(columns.Select(column => column.Header).ToArray(), rows);
    }

    private static MobileLogbookFileExportColumn Text(
        string header,
        Func<PortableLogbookWorkbookEntry, object?> value) => new(header, value);

    private static MobileLogbookFileExportColumn Flag(
        string header,
        Func<PortableLogbookWorkbookEntry, bool?> value) => new(header, entry => value(entry));

    private static MobileLogbookFileExportColumn Hours(
        string header,
        Func<PortableLogbookWorkbookEntry, decimal?> value) => new(header, entry => value(entry));

    private static MobileLogbookFileExportColumn Count(
        string header,
        Func<PortableLogbookWorkbookEntry, int?> value) => new(header, entry => value(entry));

    private static int WorkbookApproaches(PortableLogbookWorkbookEntry entry) =>
        (entry.Ils ?? 0) + (entry.Vor ?? 0) + (entry.Rnp ?? 0) + (entry.Ndb ?? 0) +
        (entry.DgaCdi ?? 0) + (entry.DgaAzi ?? 0);

    private static string CombinedDetails(PortableLogbookWorkbookEntry entry)
    {
        var route = new[] { entry.From, entry.Via, entry.To }
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var parts = new List<string>();
        var routeText = string.Join("-", route);
        if (routeText.Length > 0)
        {
            parts.Add(routeText);
        }

        if (!string.IsNullOrWhiteSpace(entry.Remarks))
        {
            parts.Add($"({entry.Remarks.Trim()})");
        }

        var flags = new List<string>();
        if (entry.FlightReview == true) flags.Add("Flight Review");
        if (entry.InstrumentProficiencyCheck == true) flags.Add("IPC");
        if (entry.OperatorProficiencyCheck == true) flags.Add("OPC");
        if (flags.Count > 0)
        {
            parts.Add($"({string.Join("/", flags)})");
        }

        return string.Join(" ", parts);
    }

    private static byte[] WriteCsv(MobileLogbookFileExportTable table)
    {
        var output = new StringBuilder();
        WriteCsvRow(output, table.Headers);
        foreach (var row in table.Rows)
        {
            WriteCsvRow(output, row.Select(FormatCsvValue));
        }

        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static void WriteCsvRow(StringBuilder output, IEnumerable<object?> values)
    {
        output.AppendJoin(',', values.Select(value => EscapeCsv(value?.ToString() ?? string.Empty)));
        output.Append("\r\n");
    }

    private static string FormatCsvValue(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "TRUE" : "FALSE",
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string EscapeCsv(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static byte[] WriteXlsx(MobileLogbookFileExportTable table, DateTimeOffset exportedAtUtc)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                WriteContentTypeDefault(writer, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                WriteContentTypeDefault(writer, "xml", "application/xml");
                WriteContentTypeOverride(writer, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                WriteContentTypeOverride(writer, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                WriteContentTypeOverride(writer, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                WriteContentTypeOverride(writer, "/xl/tables/table1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml");
                WriteContentTypeOverride(writer, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
                WriteContentTypeOverride(writer, "/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml");
                writer.WriteEndElement();
            });
            AddXml(archive, "_rels/.rels", WriteRootRelationships);
            AddXml(archive, "docProps/core.xml", writer => WriteCoreProperties(writer, exportedAtUtc));
            AddXml(archive, "docProps/custom.xml", writer => WriteCustomProperties(writer, exportedAtUtc));
            AddXml(archive, "xl/workbook.xml", WriteWorkbook);
            AddXml(archive, "xl/_rels/workbook.xml.rels", WriteWorkbookRelationships);
            AddXml(archive, "xl/styles.xml", WriteStyles);
            AddXml(archive, "xl/worksheets/sheet1.xml", writer => WriteWorksheet(writer, table));
            AddXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", WriteWorksheetRelationships);
            AddXml(archive, "xl/tables/table1.xml", writer => WriteTable(writer, table));
        }

        return output.ToArray();
    }

    private static void AddXml(ZipArchive archive, string path, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false
        });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void WriteContentTypeDefault(XmlWriter writer, string extension, string contentType)
    {
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", extension);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteContentTypeOverride(XmlWriter writer, string partName, string contentType)
    {
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", partName);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(XmlWriter writer)
    {
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        WriteRelationship(writer, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
        WriteRelationship(writer, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
        WriteRelationship(writer, "rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", "docProps/custom.xml");
        writer.WriteEndElement();
    }

    private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
    {
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    private static void WriteCoreProperties(XmlWriter writer, DateTimeOffset exportedAtUtc)
    {
        writer.WriteStartElement("cp", "coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        writer.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");
        writer.WriteAttributeString("xmlns", "dcterms", null, "http://purl.org/dc/terms/");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteElementString("dc", "title", "http://purl.org/dc/elements/1.1/", "FlightLogX Logbook Export");
        writer.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", "FlightLogX");
        WriteDublinCoreDate(writer, "created", exportedAtUtc);
        WriteDublinCoreDate(writer, "modified", exportedAtUtc);
        writer.WriteEndElement();
    }

    private static void WriteDublinCoreDate(XmlWriter writer, string name, DateTimeOffset value)
    {
        writer.WriteStartElement("dcterms", name, "http://purl.org/dc/terms/");
        writer.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "dcterms:W3CDTF");
        writer.WriteString(value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static void WriteCustomProperties(XmlWriter writer, DateTimeOffset exportedAtUtc)
    {
        writer.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        writer.WriteAttributeString("xmlns", "vt", null, "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        writer.WriteStartElement("property");
        writer.WriteAttributeString("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");
        writer.WriteAttributeString("pid", "2");
        writer.WriteAttributeString("name", ExportTimestampHeader);
        writer.WriteElementString("vt", "lpwstr", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes", exportedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter writer)
    {
        writer.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        writer.WriteStartElement("sheets");
        writer.WriteStartElement("sheet");
        writer.WriteAttributeString("name", "Logbook");
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbookRelationships(XmlWriter writer)
    {
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        WriteRelationship(writer, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml");
        WriteRelationship(writer, "rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
        writer.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter writer)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartElement("styleSheet", ns);
        writer.WriteStartElement("fonts"); writer.WriteAttributeString("count", "2");
        writer.WriteStartElement("font"); writer.WriteElementString("sz", ns, "11"); writer.WriteElementString("name", ns, "Aptos"); writer.WriteEndElement();
        writer.WriteStartElement("font"); writer.WriteElementString("b", ns, string.Empty); writer.WriteStartElement("color", ns); writer.WriteAttributeString("rgb", "FFFFFFFF"); writer.WriteEndElement(); writer.WriteElementString("sz", ns, "11"); writer.WriteElementString("name", ns, "Aptos"); writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("fills"); writer.WriteAttributeString("count", "3");
        WritePatternFill(writer, "none", null); WritePatternFill(writer, "gray125", null); WritePatternFill(writer, "solid", "1F4E78");
        writer.WriteEndElement();
        writer.WriteStartElement("borders"); writer.WriteAttributeString("count", "1"); writer.WriteStartElement("border"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs"); writer.WriteAttributeString("count", "1"); writer.WriteStartElement("xf"); writer.WriteAttributeString("numFmtId", "0"); writer.WriteAttributeString("fontId", "0"); writer.WriteAttributeString("fillId", "0"); writer.WriteAttributeString("borderId", "0"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("cellXfs"); writer.WriteAttributeString("count", "3");
        WriteCellFormat(writer, "0", "0", "0");
        WriteCellFormat(writer, "0", "1", "2");
        WriteCellFormat(writer, "2", "0", "0");
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyles"); writer.WriteAttributeString("count", "1"); writer.WriteStartElement("cellStyle"); writer.WriteAttributeString("name", "Normal"); writer.WriteAttributeString("xfId", "0"); writer.WriteAttributeString("builtinId", "0"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WritePatternFill(XmlWriter writer, string patternType, string? foreground)
    {
        writer.WriteStartElement("fill"); writer.WriteStartElement("patternFill"); writer.WriteAttributeString("patternType", patternType);
        if (foreground is not null) { writer.WriteStartElement("fgColor"); writer.WriteAttributeString("rgb", "FF" + foreground); writer.WriteEndElement(); writer.WriteStartElement("bgColor"); writer.WriteAttributeString("indexed", "64"); writer.WriteEndElement(); }
        writer.WriteEndElement(); writer.WriteEndElement();
    }

    private static void WriteCellFormat(XmlWriter writer, string numFmtId, string fontId, string fillId)
    {
        writer.WriteStartElement("xf"); writer.WriteAttributeString("numFmtId", numFmtId); writer.WriteAttributeString("fontId", fontId); writer.WriteAttributeString("fillId", fillId); writer.WriteAttributeString("borderId", "0"); writer.WriteAttributeString("xfId", "0");
        if (fillId != "0") { writer.WriteAttributeString("applyFill", "1"); writer.WriteAttributeString("applyFont", "1"); }
        if (numFmtId != "0") writer.WriteAttributeString("applyNumberFormat", "1");
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(XmlWriter writer, MobileLogbookFileExportTable table)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartElement("worksheet", ns);
        writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "1"); writer.WriteAttributeString("topLeftCell", "A2"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement();
        writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("sheetData");
        writer.WriteStartElement("row"); writer.WriteAttributeString("r", "1");
        for (var index = 0; index < table.Headers.Count; index++) WriteXlsxCell(writer, 1, index + 1, table.Headers[index], 1);
        writer.WriteEndElement();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", (rowIndex + 2).ToString(CultureInfo.InvariantCulture));
            for (var columnIndex = 0; columnIndex < table.Rows[rowIndex].Count; columnIndex++)
            {
                var value = table.Rows[rowIndex][columnIndex];
                WriteXlsxCell(writer, rowIndex + 2, columnIndex + 1, value, value is decimal ? 2 : 0);
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A1:{ColumnName(table.Headers.Count)}{table.Rows.Count + 1}"); writer.WriteEndElement();
        writer.WriteStartElement("tableParts"); writer.WriteAttributeString("count", "1"); writer.WriteStartElement("tablePart"); writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorksheetRelationships(XmlWriter writer)
    {
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        WriteRelationship(writer, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table", "../tables/table1.xml");
        writer.WriteEndElement();
    }

    private static void WriteTable(XmlWriter writer, MobileLogbookFileExportTable table)
    {
        writer.WriteStartElement("table", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteAttributeString("id", "1");
        writer.WriteAttributeString("name", "LogbookExport");
        writer.WriteAttributeString("displayName", "LogbookExport");
        writer.WriteAttributeString("ref", $"A1:{ColumnName(table.Headers.Count)}{table.Rows.Count + 1}");
        writer.WriteAttributeString("totalsRowShown", "0");
        writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A1:{ColumnName(table.Headers.Count)}{table.Rows.Count + 1}"); writer.WriteEndElement();
        writer.WriteStartElement("tableColumns"); writer.WriteAttributeString("count", table.Headers.Count.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < table.Headers.Count; index++)
        {
            writer.WriteStartElement("tableColumn");
            writer.WriteAttributeString("id", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("name", table.Headers[index]);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("tableStyleInfo"); writer.WriteAttributeString("name", "TableStyleMedium2"); writer.WriteAttributeString("showFirstColumn", "0"); writer.WriteAttributeString("showLastColumn", "0"); writer.WriteAttributeString("showRowStripes", "1"); writer.WriteAttributeString("showColumnStripes", "0"); writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteXlsxCell(XmlWriter writer, int row, int column, object? value, int style)
    {
        writer.WriteStartElement("c"); writer.WriteAttributeString("r", $"{ColumnName(column)}{row}");
        if (style > 0) writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        switch (value)
        {
            case null:
                break;
            case bool flag:
                writer.WriteAttributeString("t", "b"); writer.WriteElementString("v", flag ? "1" : "0");
                break;
            case decimal number:
                writer.WriteElementString("v", number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case int number:
                writer.WriteElementString("v", number.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is"); writer.WriteStartElement("t");
                var text = value.ToString() ?? string.Empty;
                if (text.Length != text.Trim().Length) writer.WriteAttributeString("xml", "space", null, "preserve");
                writer.WriteString(text); writer.WriteEndElement(); writer.WriteEndElement();
                break;
        }
        writer.WriteEndElement();
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name;
    }
}

public enum MobileLogbookFileExportFormat { Xlsx, Csv }

public sealed record MobileLogbookFileExportRequest(
    MobileLogbookFileExportFormat Format,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool CombineDetails = true);

public sealed record MobileLogbookFileExportTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record MobileLogbookFileExportFile(
    string FileName,
    string ContentType,
    DateTimeOffset ExportedAt,
    MobileLogbookFileExportTable Table,
    byte[] Bytes);

public sealed record MobileLogbookFileExportResult(
    MobileLogbookFileExportFile File,
    BrowserFileTransferResult Transfer);

internal sealed record MobileLogbookFileExportColumn(
    string Header,
    Func<PortableLogbookWorkbookEntry, object?> Value);
