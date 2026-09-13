using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookFileExportWorkflowTests
{
    private static readonly DateTimeOffset ExportedAt = DateTimeOffset.Parse("2026-08-26T09:04:05+10:00");
    private static readonly IReadOnlyList<CustomFieldDefinition> Fields =
    [
        new(new CustomFieldId("cf_role"), "Role", 1),
        new(new CustomFieldId("cf_employer"), "Employer", 2),
        new(new CustomFieldId("cf_unused"), "Unused", 3)
    ];
    private static readonly IReadOnlyList<CustomFieldDefinition> CanonicalFields =
    [
        new(new CustomFieldId("cf_workbook_1"), "Custom 1", 1),
        new(new CustomFieldId("cf_workbook_2"), "Custom 2", 2),
        new(new CustomFieldId("cf_workbook_3"), "Custom 3", 3),
        new(new CustomFieldId("cf_workbook_4"), "Custom 4", 4)
    ];

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, "logbook-export-full-golden.csv")]
    [InlineData(MobileLogbookFileExportLayout.Compact, "logbook-export-compact-golden.csv")]
    public void CreateCsvMatchesGoldenWorkbookContractAndUsesUtcTimestamp(
        MobileLogbookFileExportLayout layout,
        string fixtureName)
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields.Reverse(),
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Csv,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                Layout: layout),
            ExportedAt);

        var actual = Encoding.UTF8.GetString(result.Bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        var golden = File.ReadAllText(Path.Combine("Fixtures", fixtureName))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(golden.TrimEnd('\n'), actual.TrimEnd('\n'));
        Assert.Equal("flightlogx-logbook-20260825T230405Z.csv", result.FileName);
        Assert.Equal(BrowserFileStore.CsvContentType, result.ContentType);
        Assert.Equal(DateTimeOffset.Parse("2026-08-25T23:04:05Z"), result.ExportedAt);
        Assert.False(result.Bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(2, result.Table.Rows.Count);
        var customColumnStart = layout == MobileLogbookFileExportLayout.Full ? 16 : 10;
        Assert.Equal(["Role", "Employer", "Unused", "Custom 4"], result.Table.Headers.Skip(customColumnStart).Take(4));
    }

    [Fact]
    public void CreateXlsxPopulatesCanonicalTemplateRowsFormulasRangesAndMetadata()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                Layout: MobileLogbookFileExportLayout.Compact),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var requiredParts = new[]
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "docProps/core.xml",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
            "xl/styles.xml",
            "xl/worksheets/sheet1.xml",
            "xl/worksheets/_rels/sheet1.xml.rels",
            "xl/tables/table1.xml"
        };
        Assert.All(requiredParts, part => Assert.NotNull(archive.GetEntry(part)));

        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = worksheet.Descendants(spreadsheet + "row").ToArray();
        Assert.Equal(9, rows.Length);
        Assert.Empty(worksheet.Root!.Elements(spreadsheet + "autoFilter"));
        var firstDataRow = rows.Single(row => (int?)row.Attribute("r") == 6);
        var secondDataRow = rows.Single(row => (int?)row.Attribute("r") == 7);
        var totalsRow = rows.Single(row => (int?)row.Attribute("r") == 8);
        var grandTotalRow = rows.Single(row => (int?)row.Attribute("r") == 9);
        Assert.Equal("entry_002", ReadCell(firstDataRow, spreadsheet, "B6"));
        Assert.Equal(
            new DateTime(2026, 8, 20).ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ReadCell(firstDataRow, spreadsheet, "C6"));
        Assert.Equal("VH-ONE", ReadCell(firstDataRow, spreadsheet, "H6"));
        Assert.Equal("YSBK-DCT-YSCN (Training, \"check\") (Flight Review/IPC)", ReadCell(firstDataRow, spreadsheet, "L6"));
        Assert.Equal("Captain", ReadCell(firstDataRow, spreadsheet, "M6"));
        Assert.Equal("Alpha", ReadCell(secondDataRow, spreadsheet, "N7"));
        Assert.Equal("1.2", ReadCell(firstDataRow, spreadsheet, "AP6"));
        Assert.Equal("2", ReadCell(firstDataRow, spreadsheet, "AQ6"));
        Assert.Equal("1.2", ReadCell(firstDataRow, spreadsheet, "BB6"));
        Assert.Equal("3.1", ReadCell(secondDataRow, spreadsheet, "BB7"));
        Assert.Equal("3.1", ReadCell(totalsRow, spreadsheet, "AP8"));
        Assert.Equal("4", ReadCell(totalsRow, spreadsheet, "AQ8"));
        Assert.Equal("3.5", ReadCell(grandTotalRow, spreadsheet, "K9"));

        var table = ReadXml(archive, "xl/tables/table1.xml");
        Assert.Single(table.Root!.Elements(spreadsheet + "autoFilter"));
        Assert.Equal("B5:BF8", table.Root.Attribute("ref")?.Value);
        Assert.Equal("B5:BF7", table.Root.Element(spreadsheet + "autoFilter")?.Attribute("ref")?.Value);
        Assert.Equal(
            ["Role", "Employer", "Unused", "Custom 4"],
            table.Root.Element(spreadsheet + "tableColumns")!
                .Elements(spreadsheet + "tableColumn")
                .Skip(11)
                .Take(4)
                .Select(column => column.Attribute("name")?.Value));

        var workbook = ReadXml(archive, "xl/workbook.xml");
        Assert.Contains(
            workbook.Descendants(spreadsheet + "definedName"),
            name => name.Attribute("name")?.Value == "LogbookTotals" && name.Value == "Logbook!$I$8:$K$9");
        var calculation = workbook.Root!.Element(spreadsheet + "calcPr")!;
        Assert.Equal("auto", calculation.Attribute("calcMode")?.Value);
        Assert.Equal("1", calculation.Attribute("fullCalcOnLoad")?.Value);
        Assert.Equal("1", calculation.Attribute("forceFullCalc")?.Value);

        Assert.Null(archive.GetEntry("docProps/custom.xml"));
        var core = ReadXml(archive, "docProps/core.xml");
        Assert.Contains("FlightLogX Logbook Export", core.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, core.Descendants().Count(element => element.Value == "2026-08-25T23:04:05.000Z"));
        Assert.Equal("flightlogx-logbook-20260825T230405Z.xlsx", result.FileName);
        Assert.Equal(BrowserFileStore.ExcelWorkbookContentType, result.ContentType);

        using var documentStream = new MemoryStream(result.Bytes, writable: false);
        using var document = SpreadsheetDocument.Open(documentStream, false);
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                Environment.NewLine,
                validationErrors.Select(error => $"{error.Part?.Uri}: {error.Description} at {error.Path?.XPath}")));
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, "Full")]
    [InlineData(MobileLogbookFileExportLayout.Compact, "Compact")]
    public void CreateXlsxMatchesCanonicalTemplateStructure(MobileLogbookFileExportLayout layout, string templateLayout)
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            CanonicalEntries(),
            CanonicalFields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                layout),
            ExportedAt);

        using var expected = OpenEmbeddedTemplate(templateLayout);
        using var actual = new MemoryStream(result.Bytes, writable: false);

        Assert.Equal(
            ReadNormalizedWorkbookStructure(expected),
            ReadNormalizedWorkbookStructure(actual));
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, "BL")]
    [InlineData(MobileLogbookFileExportLayout.Compact, "BF")]
    public void CreateXlsxSortsByDateThenEntryIdOmitsDeletedRowsAndWritesHiddenEntryIds(
        MobileLogbookFileExportLayout layout,
        string endColumn)
    {
        var date = new DateOnly(2026, 8, 20);
        var entries = new[]
        {
            Materialized("entry_deleted", PortableLogbookWorkbookEntry.Empty with
            {
                Year = date.Year, Month = date.Month, Day = date.Day, Reg = "VH-DELETED"
            }) with { IsDeleted = true },
            Materialized("entry_z", PortableLogbookWorkbookEntry.Empty with
            {
                Year = date.Year, Month = date.Month, Day = date.Day, Reg = "VH-ZED"
            }),
            Materialized("entry_a", PortableLogbookWorkbookEntry.Empty with
            {
                Year = date.Year, Month = date.Month, Day = date.Day, Reg = "VH-ALPHA"
            })
        };

        var result = MobileLogbookFileExportWorkflow.Create(
            entries,
            [],
            new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Xlsx, Layout: layout),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var rows = worksheet.Descendants(spreadsheet + "row")
            .ToDictionary(row => (int)row.Attribute("r")!);
        Assert.Equal("entry_a", ReadCell(rows[6], spreadsheet, "B6"));
        Assert.Equal("VH-ALPHA", ReadCell(rows[6], spreadsheet, "H6"));
        Assert.Equal("entry_z", ReadCell(rows[7], spreadsheet, "B7"));
        Assert.Equal("VH-ZED", ReadCell(rows[7], spreadsheet, "H7"));
        Assert.DoesNotContain(worksheet.Descendants(spreadsheet + "c"), cell =>
            ReadCellValue(cell, spreadsheet) == "VH-DELETED");
        Assert.Contains(
            worksheet.Descendants(spreadsheet + "col"),
            column =>
                (int?)column.Attribute("min") <= 2 &&
                (int?)column.Attribute("max") >= 2 &&
                (string?)column.Attribute("hidden") == "1");

        var table = ReadXml(archive, "xl/tables/table1.xml");
        Assert.Equal($"B5:{endColumn}8", table.Root!.Attribute("ref")?.Value);

        using var validationStream = new MemoryStream(result.Bytes, writable: false);
        using var document = SpreadsheetDocument.Open(validationStream, false);
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                Environment.NewLine,
                validationErrors.Select(error => $"{error.Part?.Uri}: {error.Description} at {error.Path?.XPath}")));
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, 20, 20, "BL", 1)]
    [InlineData(MobileLogbookFileExportLayout.Full, 20, 21, "BL", 2)]
    [InlineData(MobileLogbookFileExportLayout.Compact, 20, 20, "BF", 1)]
    [InlineData(MobileLogbookFileExportLayout.Compact, 20, 21, "BF", 2)]
    public void CreateXlsxExpandsCanonicalTableForOneOrMultipleDateRangeRows(
        MobileLogbookFileExportLayout layout,
        int startDay,
        int endDay,
        string endColumn,
        int expectedDataRows)
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, startDay),
                new DateOnly(2026, 8, endDay),
                layout),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var totalsRow = 6 + expectedDataRows;
        var lastDataRow = 5 + expectedDataRows;

        Assert.Equal(7 + expectedDataRows, worksheet.Descendants(spreadsheet + "row").Count());
        Assert.Equal($"B5:{endColumn}{totalsRow}", table.Root!.Attribute("ref")?.Value);
        Assert.Equal(
            $"B5:{endColumn}{lastDataRow}",
            table.Root.Element(spreadsheet + "autoFilter")?.Attribute("ref")?.Value);
        Assert.Equal(
            Enumerable.Range(0, expectedDataRows).Select(index => (20 + index).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            worksheet.Descendants(spreadsheet + "row")
                .Where(row => (int?)row.Attribute("r") is >= 6 && (int?)row.Attribute("r") <= lastDataRow)
                .Select(row => ReadCell(row, spreadsheet, $"F{row.Attribute("r")!.Value}")));
        AssertSanitizedPackage(archive);
        AssertOpenXmlValid(result.Bytes);
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, "S", "T", "U", "V")]
    [InlineData(MobileLogbookFileExportLayout.Compact, "M", "N", "O", "P")]
    public void CreateXlsxWritesAllCustomSlotsAndPreservesLegacyNumberFormats(
        MobileLogbookFileExportLayout layout,
        string custom1Column,
        string custom2Column,
        string custom3Column,
        string custom4Column)
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            CanonicalEntries(),
            CanonicalFields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                layout),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var rows = worksheet.Descendants(spreadsheet + "row").ToDictionary(row => (int)row.Attribute("r")!);
        var customColumns = new[] { custom1Column, custom2Column, custom3Column, custom4Column };

        Assert.Equal(["1.1", "2.2", "3.3", "4"], customColumns.Select(column => ReadCell(rows[6], spreadsheet, $"{column}6")));
        Assert.All(customColumns, column => Assert.Equal(string.Empty, ReadCell(rows[7], spreadsheet, $"{column}7")));

        var styles = ReadXml(archive, "xl/styles.xml");
        var cellFormats = styles.Root!.Element(spreadsheet + "cellXfs")!.Elements(spreadsheet + "xf").ToArray();
        Assert.Equal(
            ["165", "165", "165", "1"],
            customColumns.Select(column =>
            {
                var styleIndex = int.Parse(
                    rows[6].Elements(spreadsheet + "c").Single(cell => (string?)cell.Attribute("r") == $"{column}6")
                        .Attribute("s")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                return cellFormats[styleIndex].Attribute("numFmtId")!.Value;
            }));
        Assert.Contains(
            styles.Descendants(spreadsheet + "numFmt"),
            format => format.Attribute("numFmtId")?.Value == "165" && format.Attribute("formatCode")?.Value == "0.0");
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full, "S", "V")]
    [InlineData(MobileLogbookFileExportLayout.Compact, "M", "P")]
    public void CreateXlsxUsesExplicitCustomNumberFormatsForValuesAndTotals(
        MobileLogbookFileExportLayout layout,
        string firstCustomColumn,
        string fourthCustomColumn)
    {
        var explicitFields = CanonicalFields
            .Select(field => field.Order switch
            {
                1 => field with { NumberFormat = CustomFieldNumberFormat.WholeNumbers },
                4 => field with { NumberFormat = CustomFieldNumberFormat.Decimals },
                _ => field
            })
            .ToArray();
        var result = MobileLogbookFileExportWorkflow.Create(
            CanonicalEntries(),
            explicitFields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                layout),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var styles = ReadXml(archive, "xl/styles.xml");

        Assert.Equal("1", ReadNumberFormatId(worksheet, styles, spreadsheet, $"{firstCustomColumn}6"));
        Assert.Equal("165", ReadNumberFormatId(worksheet, styles, spreadsheet, $"{fourthCustomColumn}6"));
        Assert.Equal("1", ReadNumberFormatId(worksheet, styles, spreadsheet, $"{firstCustomColumn}8"));
        Assert.Equal("165", ReadNumberFormatId(worksheet, styles, spreadsheet, $"{fourthCustomColumn}8"));
        Assert.Equal("1.1", ReadCell(
            worksheet.Descendants(spreadsheet + "row").Single(row => (int?)row.Attribute("r") == 6),
            spreadsheet,
            $"{firstCustomColumn}6"));
        Assert.Equal("4", ReadCell(
            worksheet.Descendants(spreadsheet + "row").Single(row => (int?)row.Attribute("r") == 6),
            spreadsheet,
            $"{fourthCustomColumn}6"));
        AssertOpenXmlValid(result.Bytes);
    }

    [Theory]
    [InlineData(MobileLogbookFileExportLayout.Full)]
    [InlineData(MobileLogbookFileExportLayout.Compact)]
    public void CreateXlsxPreservesFormulaDefinitionsAndCachesEveryKeyTotal(MobileLogbookFileExportLayout layout)
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                layout),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var rows = worksheet.Descendants(spreadsheet + "row").ToDictionary(row => (int)row.Attribute("r")!);
        var columns = TableColumnReferences(table, spreadsheet);
        var calculatedColumns = table.Descendants(spreadsheet + "tableColumn")
            .Where(column => column.Element(spreadsheet + "calculatedColumnFormula") is not null)
            .Select(column => column.Attribute("name")!.Value)
            .ToArray();

        Assert.All(calculatedColumns, name =>
        {
            Assert.NotNull(FindCell(rows[6], spreadsheet, $"{columns[name]}6").Element(spreadsheet + "f"));
            Assert.NotNull(FindCell(rows[7], spreadsheet, $"{columns[name]}7").Element(spreadsheet + "f"));
        });
        Assert.Equal("1.2", ReadCell(rows[6], spreadsheet, $"{columns["TotalHours"]}6"));
        Assert.Equal("1.9", ReadCell(rows[7], spreadsheet, $"{columns["TotalHours"]}7"));
        Assert.Equal("2", ReadCell(rows[6], spreadsheet, $"{columns["TotalApps"]}6"));
        Assert.Equal("2", ReadCell(rows[7], spreadsheet, $"{columns["TotalApps"]}7"));
        Assert.Equal("1.2", ReadCell(rows[6], spreadsheet, $"{columns["CumTotalHours"]}6"));
        Assert.Equal("3.1", ReadCell(rows[7], spreadsheet, $"{columns["CumTotalHours"]}7"));
        Assert.Equal("3.1", ReadCell(rows[8], spreadsheet, $"{columns["TotalHours"]}8"));
        Assert.Equal("4", ReadCell(rows[8], spreadsheet, $"{columns["TotalApps"]}8"));
        Assert.Equal("3.5", ReadCell(rows[9], spreadsheet, "K9"));
    }

    [Theory]
    [InlineData("Full")]
    [InlineData("Compact")]
    public void EmbeddedCanonicalTemplatesAreSanitizedAndValid(string layout)
    {
        using var template = OpenEmbeddedTemplate(layout);
        using var archive = new ZipArchive(template, ZipArchiveMode.Read, leaveOpen: true);
        AssertSanitizedPackage(archive);

        var workbook = ReadXml(archive, "xl/workbook.xml");
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal("Logbook", workbook.Descendants(spreadsheet + "sheet").Single().Attribute("name")?.Value);
        Assert.Equal(
            ["LogbookFilterHeaders", "LogbookHeaders", "LogbookSumTotals", "LogbookTotals"],
            workbook.Descendants(spreadsheet + "definedName")
                .Select(name => name.Attribute("name")?.Value)
                .OrderBy(name => name, StringComparer.Ordinal));

        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var sharedStrings = ReadSharedStrings(archive, spreadsheet);
        var dataCells = worksheet.Descendants(spreadsheet + "row")
            .Single(row => (int?)row.Attribute("r") == 6)
            .Elements(spreadsheet + "c")
            .Where(cell => cell.Element(spreadsheet + "f") is null);
        Assert.All(
            dataCells,
            cell => Assert.True(
                string.IsNullOrEmpty(ReadCellValue(cell, spreadsheet, sharedStrings)),
                $"Template data cell {cell.Attribute("r")?.Value} contains a non-formula value."));
        var grandTotalRow = worksheet.Descendants(spreadsheet + "row")
            .Single(row => (int?)row.Attribute("r") == 8);
        Assert.Equal("Total Aeronautical Experience", ReadCell(grandTotalRow, spreadsheet, "J8", sharedStrings));
        Assert.Equal(
            "SUBTOTAL(109,Logbook[TotalHours])+SUBTOTAL(109,Logbook[IfrSim])",
            grandTotalRow.Elements(spreadsheet + "c")
                .Single(cell => cell.Attribute("r")?.Value == "K8")
                .Element(spreadsheet + "f")?.Value);

        template.Position = 0;
        using var document = SpreadsheetDocument.Open(template, false);
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                Environment.NewLine,
                validationErrors.Select(error => $"{error.Part?.Uri}: {error.Description} at {error.Path?.XPath}")));
    }

    [Fact]
    public void FullCsvIsTheDefaultAndKeepsEveryUserFacingCanonicalColumn()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries().Skip(2).Take(1),
            Fields,
            new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv),
            ExportedAt);

        Assert.Equal(
            ["Date", "Year", "Month", "Day", "Type", "Reg", "Flight ID", "PIC", "Other Pilot or Crew", "From", "To", "Via", "Remarks", "FR", "IPC", "OPC"],
            result.Table.Headers.Take(16));
        Assert.Equal(["Role", "Employer", "Unused", "Custom 4"], result.Table.Headers.Skip(16).Take(4));
        Assert.Equal("SeIcusDay", result.Table.Headers[20]);
        Assert.Equal("TotalHours", result.Table.Headers[^2]);
        Assert.Equal("TotalApps", result.Table.Headers[^1]);
        Assert.DoesNotContain("Details", result.Table.Headers);
        Assert.DoesNotContain("EntryID", result.Table.Headers);
        Assert.DoesNotContain("ExportedAtUtc", result.Table.Headers);
        Assert.DoesNotContain(result.Table.Headers, header => header.StartsWith("Cum", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2026, 8, 21), result.Table.Rows[0][0]);
        Assert.StartsWith("Date,Year,Month,Day", Encoding.UTF8.GetString(result.Bytes), StringComparison.Ordinal);
        Assert.Contains("21/08/2026,2026,8,21", Encoding.UTF8.GetString(result.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void CompactCsvCombinesDetailsButKeepsAllFourCustomSlots()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries().Skip(1).Take(1),
            Fields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Csv,
                Layout: MobileLogbookFileExportLayout.Compact),
            ExportedAt);

        Assert.Equal(
            ["Date", "Year", "Month", "Day", "Type", "Reg", "Flight ID", "PIC", "Other Pilot or Crew", "Details"],
            result.Table.Headers.Take(10));
        Assert.Equal(["Role", "Employer", "Unused", "Custom 4"], result.Table.Headers.Skip(10).Take(4));
        Assert.DoesNotContain("From", result.Table.Headers);
        Assert.DoesNotContain("Remarks", result.Table.Headers);
        Assert.Equal(
            "YSBK-DCT-YSCN (Training, \"check\") (Flight Review/IPC)",
            result.Table.Rows[0][9]);
    }

    [Fact]
    public void CreateNormalizesBrowserFloatingPointNoiseInWorkbookHours()
    {
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 22,
            FlightId = "FLOAT-NOISE",
            SeCommandDay = 2.2999999999999998m,
            IfrIf = 1.234567m
        };

        var result = MobileLogbookFileExportWorkflow.Create(
            Materialize([entry]),
            [],
            new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv),
            ExportedAt);

        Assert.Equal(2.3m, result.Table.Rows[0][result.Table.Headers.ToList().IndexOf("SeCommandDay")]);
        Assert.Equal(1.234567m, result.Table.Rows[0][result.Table.Headers.ToList().IndexOf("IfrIf")]);
        Assert.DoesNotContain("2.2999999999999998", Encoding.UTF8.GetString(result.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsInvalidRangeMissingDatesAndNoMatches()
    {
        var backwards = Assert.Throws<InvalidOperationException>(() =>
            MobileLogbookFileExportWorkflow.Create(
                Entries(), Fields,
                new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv, new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 20)),
                ExportedAt));
        Assert.Contains("later", backwards.Message, StringComparison.OrdinalIgnoreCase);

        var missingDate = Assert.Throws<InvalidOperationException>(() =>
            MobileLogbookFileExportWorkflow.Create(
                Materialize([PortableLogbookWorkbookEntry.Empty with { Reg = "VH-NODATE" }]), Fields,
                new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv),
                ExportedAt));
        Assert.Contains("valid date", missingDate.Message, StringComparison.OrdinalIgnoreCase);

        var noMatches = Assert.Throws<InvalidOperationException>(() =>
            MobileLogbookFileExportWorkflow.Create(
                Entries(), Fields,
                new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv, new DateOnly(2030, 1, 1)),
                ExportedAt));
        Assert.Contains("No logbook entries", noMatches.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsyncUsesNormalShareOrDownloadFlow()
    {
        var js = new RecordingJsRuntime();
        var result = await MobileLogbookFileExportWorkflow.ExportAsync(
            Entries().Take(1),
            Fields,
            new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv),
            new BrowserFileStore(js),
            ExportedAt);

        Assert.Equal(
            ["electronicLogbookFiles.nativeShareOrDownload", "electronicLogbookFiles.canShare", "electronicLogbookFiles.download"],
            js.Calls.Select(call => call.Identifier));
        Assert.Same(result.File.Bytes, js.Calls[^1].Arguments[1]);
        Assert.Equal(BrowserFileStore.CsvContentType, js.Calls[^1].Arguments[2]);
    }

    private static IReadOnlyList<PortableLogbookMaterializedEntryV2> Entries() =>
        Materialize(
        [
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026, Month = 8, Day = 19, Reg = "VH-OUTSIDE"
            },
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026, Month = 8, Day = 20, Type = "C172", Reg = "VH-ONE", FlightId = "F1",
                Pic = "Alex", OtherPilotOrCrew = "Bob", From = " YSBK ", Via = "DCT", To = "YSCN",
                Remarks = "Training, \"check\"", FlightReview = true, InstrumentProficiencyCheck = true,
                CustomFields = new Dictionary<CustomFieldId, string?> { [new("cf_role")] = "Captain" },
                SeCommandDay = 1.2m, IfrIf = 0.3m, IfrSim = 0.4m, LandingsDay = 1, Ils = 2, Circling = 1
            },
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026, Month = 8, Day = 21, Type = "PA44", Reg = "VH-TWO", FlightId = "F2",
                Pic = "Alex", From = "YSCN", To = "YSSY",
                CustomFields = new Dictionary<CustomFieldId, string?> { [new("cf_employer")] = "Alpha" },
                SeDualNight = 0.8m, MeCommandNight = 1.1m, Vor = 1, DgaCdi = 1
            }
        ]);

    private static IReadOnlyList<PortableLogbookMaterializedEntryV2> CanonicalEntries()
    {
        var entries = Entries().ToArray();
        entries[1] = entries[1] with
        {
            Entry = entries[1].Entry! with
            {
                CustomFields = new Dictionary<CustomFieldId, string?>
                {
                    [CanonicalFields[0].Id] = "1.1",
                    [CanonicalFields[1].Id] = "2.2",
                    [CanonicalFields[2].Id] = "3.3",
                    [CanonicalFields[3].Id] = "4"
                }
            }
        };
        return entries;
    }

    private static IReadOnlyList<PortableLogbookMaterializedEntryV2> Materialize(
        IReadOnlyList<PortableLogbookWorkbookEntry> entries) =>
        entries.Select((entry, index) => Materialized($"entry_{index + 1:D3}", entry)).ToArray();

    private static PortableLogbookMaterializedEntryV2 Materialized(
        string entryId,
        PortableLogbookWorkbookEntry entry) =>
        new(
            new EntryId(entryId),
            new RevisionId($"revision_{entryId}"),
            IsDeleted: false,
            entry,
            []);

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static IReadOnlyDictionary<string, string> TableColumnReferences(
        XDocument table,
        XNamespace spreadsheet)
    {
        var (startColumn, _, _, _) = ParseRange(table.Root!.Attribute("ref")!.Value);
        var firstColumn = ColumnNumber(startColumn);
        return table.Root.Element(spreadsheet + "tableColumns")!
            .Elements(spreadsheet + "tableColumn")
            .Select((column, index) => new
            {
                Name = column.Attribute("name")!.Value,
                Reference = ColumnName(firstColumn + index)
            })
            .ToDictionary(item => item.Name, item => item.Reference, StringComparer.Ordinal);
    }

    private static void AssertSanitizedPackage(ZipArchive archive)
    {
        var partNames = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.DoesNotContain(partNames, name =>
            name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("xl/webextensions/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "docProps/custom.xml", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertOpenXmlValid(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                Environment.NewLine,
                validationErrors.Select(error => $"{error.Part?.Uri}: {error.Description} at {error.Path?.XPath}")));
    }

    private static Stream OpenEmbeddedTemplate(string layout)
    {
        var resourceName = $"ElectronicLogbook.Mobile.Templates.LogbookExport{layout}.xlsx";
        return typeof(MobileLogbookFileExportWorkflow).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded export template not found: {resourceName}");
    }

    private static string ReadNormalizedWorkbookStructure(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var styles = ReadXml(archive, "xl/styles.xml");
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var sharedStrings = ReadSharedStrings(archive, spreadsheet);

        var tableReference = table.Root!.Attribute("ref")!.Value;
        var (startColumn, headerRow, endColumn, totalsRow) = ParseRange(tableReference);
        var tableColumns = table.Root.Element(spreadsheet + "tableColumns")!
            .Elements(spreadsheet + "tableColumn")
            .Select(column => new
            {
                Name = column.Attribute("name")?.Value,
                TotalsFunction = column.Attribute("totalsRowFunction")?.Value,
                TotalsLabel = column.Attribute("totalsRowLabel")?.Value,
                Formula = column.Element(spreadsheet + "calculatedColumnFormula")?.Value,
                TotalsFormula = column.Element(spreadsheet + "totalsRowFormula")?.Value
            })
            .ToArray();
        var firstDataRow = headerRow + 1;

        var signature = new
        {
            SheetName = workbook.Descendants(spreadsheet + "sheet").Single().Attribute("name")?.Value,
            DefinedNames = workbook.Descendants(spreadsheet + "definedName")
                .Select(name => $"{name.Attribute("name")?.Value}={NormalizeDefinedName(name.Value, totalsRow)}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Table = new
            {
                Start = $"{startColumn}{headerRow}",
                ColumnCount = ColumnNumber(endColumn) - ColumnNumber(startColumn) + 1,
                HasTotals = table.Root.Attribute("totalsRowCount")?.Value == "1",
                HasAutoFilter = table.Root.Element(spreadsheet + "autoFilter") is not null,
                Style = table.Root.Element(spreadsheet + "tableStyleInfo")?.Attributes()
                    .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
                    .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}")
                    .ToArray(),
                Columns = tableColumns
            },
            View = worksheet.Descendants(spreadsheet + "sheetView").Select(view => new
            {
                ShowGridLines = view.Attribute("showGridLines")?.Value ?? "1",
                Pane = view.Element(spreadsheet + "pane")?.Attributes()
                    .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
                    .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}")
                    .ToArray() ?? []
            }).FirstOrDefault(),
            Columns = worksheet.Descendants(spreadsheet + "cols")
                .Elements(spreadsheet + "col")
                .Select(column => string.Join(
                    ";",
                    column.Attributes()
                        .Where(attribute => attribute.Name.LocalName is "min" or "max" or "width" or "hidden" or "style" or "customWidth")
                        .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
                        .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}")))
                .ToArray(),
            HeaderRows = worksheet.Descendants(spreadsheet + "row")
                .Where(row => (int?)row.Attribute("r") is >= 1 and <= 5)
                .Select(row => new
                {
                    Row = row.Attribute("r")?.Value,
                    Height = row.Attribute("ht")?.Value,
                    Hidden = row.Attribute("hidden")?.Value,
                    Style = row.Attribute("s")?.Value,
                    Cells = row.Elements(spreadsheet + "c")
                        .Select(cell => new
                        {
                            Reference = cell.Attribute("r")?.Value,
                            Style = cell.Attribute("s")?.Value,
                            Value = ReadCellValue(cell, spreadsheet, sharedStrings)
                        })
                        .ToArray()
                })
                .ToArray(),
            MergedHeadings = worksheet.Descendants(spreadsheet + "mergeCell")
                .Select(merge => merge.Attribute("ref")?.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            DataStylesAndFormulas = ReadTableRowStructure(
                worksheet,
                spreadsheet,
                tableColumns.Select(column => column.Name ?? string.Empty).ToArray(),
                startColumn,
                firstDataRow),
            TotalsStylesAndFormulas = ReadTableRowStructure(
                worksheet,
                spreadsheet,
                tableColumns.Select(column => column.Name ?? string.Empty).ToArray(),
                startColumn,
                totalsRow),
            GrandTotal = worksheet.Descendants(spreadsheet + "row")
                .Where(row => (int?)row.Attribute("r") == totalsRow + 1)
                .Elements(spreadsheet + "c")
                .Where(cell => new[] { $"J{totalsRow + 1}", $"K{totalsRow + 1}" }
                    .Contains(cell.Attribute("r")?.Value, StringComparer.Ordinal))
                .Select(cell => new
                {
                    Reference = Regex.Replace(cell.Attribute("r")!.Value, "[0-9]+$", "{GrandTotalRow}"),
                    Style = cell.Attribute("s")?.Value,
                    Value = cell.Element(spreadsheet + "f") is null
                        ? ReadCellValue(cell, spreadsheet, sharedStrings)
                        : "{CachedFormulaValue}",
                    Formula = cell.Element(spreadsheet + "f")?.Value
                })
                .ToArray(),
            StyleCounts = new
            {
                NumberFormats = styles.Root!.Element(spreadsheet + "numFmts")?.Elements().Count() ?? 0,
                Fonts = styles.Root.Element(spreadsheet + "fonts")?.Elements().Count() ?? 0,
                Fills = styles.Root.Element(spreadsheet + "fills")?.Elements().Count() ?? 0,
                Borders = styles.Root.Element(spreadsheet + "borders")?.Elements().Count() ?? 0,
                CellFormats = styles.Root.Element(spreadsheet + "cellXfs")?.Elements().Count() ?? 0
            }
        };

        return JsonSerializer.Serialize(signature, new JsonSerializerOptions { WriteIndented = true });
    }

    private static IReadOnlyList<object> ReadTableRowStructure(
        XDocument worksheet,
        XNamespace spreadsheet,
        IReadOnlyList<string> tableColumns,
        string startColumn,
        int rowNumber)
    {
        var cells = worksheet.Descendants(spreadsheet + "row")
            .Single(row => (int?)row.Attribute("r") == rowNumber)
            .Elements(spreadsheet + "c")
            .ToDictionary(cell => cell.Attribute("r")!.Value, StringComparer.Ordinal);
        var start = ColumnNumber(startColumn);

        return tableColumns.Select((name, index) =>
        {
            var reference = $"{ColumnName(start + index)}{rowNumber}";
            cells.TryGetValue(reference, out var cell);
            return (object)new
            {
                Column = name,
                Style = cell?.Attribute("s")?.Value,
                Formula = cell?.Element(spreadsheet + "f")?.Value
            };
        }).ToArray();
    }

    private static string NormalizeDefinedName(string value, int totalsRow) =>
        Regex.Replace(
            Regex.Replace(value, $@"\${totalsRow + 1}(?![0-9])", "${GrandTotalRow}"),
            $@"\${totalsRow}(?![0-9])",
            "${TotalsRow}");

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive, XNamespace spreadsheet)
    {
        if (archive.GetEntry("xl/sharedStrings.xml") is null)
        {
            return [];
        }

        return ReadXml(archive, "xl/sharedStrings.xml")
            .Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ReadNumberFormatId(
        XDocument worksheet,
        XDocument styles,
        XNamespace spreadsheet,
        string cellReference)
    {
        var cell = worksheet.Descendants(spreadsheet + "c")
            .Single(candidate => candidate.Attribute("r")?.Value == cellReference);
        var styleIndex = int.Parse(
            cell.Attribute("s")?.Value ?? throw new InvalidOperationException($"Cell {cellReference} has no style."),
            System.Globalization.CultureInfo.InvariantCulture);
        return styles.Root!.Element(spreadsheet + "cellXfs")!.Elements(spreadsheet + "xf")
            .ElementAt(styleIndex)
            .Attribute("numFmtId")!.Value;
    }

    private static string ReadCellValue(
        XElement cell,
        XNamespace spreadsheet,
        IReadOnlyList<string> sharedStrings)
    {
        if ((string?)cell.Attribute("t") == "s" &&
            int.TryParse(cell.Element(spreadsheet + "v")?.Value, out var sharedStringIndex))
        {
            return sharedStrings[sharedStringIndex];
        }

        return ReadCellValue(cell, spreadsheet);
    }

    private static (string StartColumn, int StartRow, string EndColumn, int EndRow) ParseRange(string reference)
    {
        var match = Regex.Match(reference, "^(?<start>[A-Z]+)(?<startRow>[0-9]+):(?<end>[A-Z]+)(?<endRow>[0-9]+)$");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unsupported worksheet range: {reference}");
        }

        return (
            match.Groups["start"].Value,
            int.Parse(match.Groups["startRow"].Value, System.Globalization.CultureInfo.InvariantCulture),
            match.Groups["end"].Value,
            int.Parse(match.Groups["endRow"].Value, System.Globalization.CultureInfo.InvariantCulture));
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

    private static IReadOnlyList<string> ReadRow(XElement row, XNamespace spreadsheet) =>
        row.Elements(spreadsheet + "c").Select(cell => ReadCellValue(cell, spreadsheet)).ToArray();

    private static string ReadCell(XElement row, XNamespace spreadsheet, string reference) =>
        ReadCellValue(row.Elements(spreadsheet + "c").Single(cell => (string?)cell.Attribute("r") == reference), spreadsheet);

    private static XElement FindCell(XElement row, XNamespace spreadsheet, string reference) =>
        row.Elements(spreadsheet + "c").Single(cell => (string?)cell.Attribute("r") == reference);

    private static string ReadCell(
        XElement row,
        XNamespace spreadsheet,
        string reference,
        IReadOnlyList<string> sharedStrings) =>
        ReadCellValue(
            row.Elements(spreadsheet + "c").Single(cell => (string?)cell.Attribute("r") == reference),
            spreadsheet,
            sharedStrings);

    private static string ReadCellValue(XElement cell, XNamespace spreadsheet)
    {
        if ((string?)cell.Attribute("t") == "inlineStr")
        {
            return string.Concat(cell.Descendants(spreadsheet + "t").Select(value => value.Value));
        }
        if ((string?)cell.Attribute("t") == "b")
        {
            return cell.Element(spreadsheet + "v")?.Value == "1" ? "TRUE" : "FALSE";
        }
        return cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<JsCall> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new JsCall(identifier, args ?? []));
            return new ValueTask<TValue>(default(TValue)!);
        }
    }

    private sealed record JsCall(string Identifier, IReadOnlyList<object?> Arguments);
}
