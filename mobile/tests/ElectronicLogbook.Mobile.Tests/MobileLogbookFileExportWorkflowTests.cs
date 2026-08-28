using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
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

    [Fact]
    public void CreateCsvMatchesGoldenWorkbookContractAndUsesUtcTimestamp()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields.Reverse(),
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Csv,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                CombineDetails: true),
            ExportedAt);

        var actual = Encoding.UTF8.GetString(result.Bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        var golden = File.ReadAllText(Path.Combine("Fixtures", "logbook-export-golden.csv"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(golden.TrimEnd('\n'), actual.TrimEnd('\n'));
        Assert.Equal("flightlogx-logbook-20260825T230405Z.csv", result.FileName);
        Assert.Equal(BrowserFileStore.CsvContentType, result.ContentType);
        Assert.Equal(DateTimeOffset.Parse("2026-08-25T23:04:05Z"), result.ExportedAt);
        Assert.False(result.Bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(2, result.Table.Rows.Count);
        Assert.DoesNotContain("Unused", result.Table.Headers);
    }

    [Fact]
    public void CreateXlsxContainsGoldenRowsExportMetadataAndValidOpenXmlParts()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries(),
            Fields,
            new MobileLogbookFileExportRequest(
                MobileLogbookFileExportFormat.Xlsx,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 21),
                CombineDetails: true),
            ExportedAt);

        using var stream = new MemoryStream(result.Bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var requiredParts = new[]
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "docProps/core.xml",
            "docProps/custom.xml",
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
        Assert.Equal(3, rows.Length);
        Assert.Equal(result.Table.Headers, ReadRow(rows[0], spreadsheet));
        Assert.Equal("VH-ONE", ReadCell(rows[1], spreadsheet, "E2"));
        Assert.Equal("YSBK-DCT-YSCN (Training, \"check\") (Flight Review/IPC)", ReadCell(rows[1], spreadsheet, "I2"));
        Assert.Equal("1.2", ReadCell(rows[1], spreadsheet, "AK2"));
        Assert.Equal("2", ReadCell(rows[1], spreadsheet, "AL2"));
        Assert.Equal("2026-08-25T23:04:05.000Z", ReadCell(rows[1], spreadsheet, "AM2"));

        var custom = ReadXml(archive, "docProps/custom.xml");
        Assert.Contains("ExportedAtUtc", custom.ToString(), StringComparison.Ordinal);
        Assert.Contains("2026-08-25T23:04:05.000Z", custom.ToString(), StringComparison.Ordinal);
        Assert.Equal("flightlogx-logbook-20260825T230405Z.xlsx", result.FileName);
        Assert.Equal(BrowserFileStore.ExcelWorkbookContentType, result.ContentType);
    }

    [Fact]
    public void SeparateDetailsKeepsVbaHeaderOrderAndNullableValueTypes()
    {
        var result = MobileLogbookFileExportWorkflow.Create(
            Entries().Skip(2).Take(1),
            Fields,
            new MobileLogbookFileExportRequest(MobileLogbookFileExportFormat.Csv, CombineDetails: false),
            ExportedAt);

        Assert.Equal(
            ["From", "To", "Via", "Remarks", "FR", "IPC", "OPC"],
            result.Table.Headers.Skip(8).Take(7));
        Assert.Equal("Employer", result.Table.Headers[15]);
        Assert.DoesNotContain("Role", result.Table.Headers);
        Assert.DoesNotContain("Unused", result.Table.Headers);
        Assert.Equal("SeIcusDay", result.Table.Headers[16]);
        Assert.Equal("TotalHours", result.Table.Headers[^3]);
        Assert.Equal("TotalApps", result.Table.Headers[^2]);
        Assert.Equal("ExportedAtUtc", result.Table.Headers[^1]);
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
                [PortableLogbookWorkbookEntry.Empty with { Reg = "VH-NODATE" }], Fields,
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

    private static IReadOnlyList<PortableLogbookWorkbookEntry> Entries() =>
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
    ];

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static IReadOnlyList<string> ReadRow(XElement row, XNamespace spreadsheet) =>
        row.Elements(spreadsheet + "c").Select(cell => ReadCellValue(cell, spreadsheet)).ToArray();

    private static string ReadCell(XElement row, XNamespace spreadsheet, string reference) =>
        ReadCellValue(row.Elements(spreadsheet + "c").Single(cell => (string?)cell.Attribute("r") == reference), spreadsheet);

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
