using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileWorkbookMigrationReaderTests
{
    [Fact]
    public void InspectReadsCurrentMasterWorkbookWithoutChangingIt()
    {
        var repoRoot = FindRepoRoot();
        var workbookPath = Path.Combine(repoRoot, "Electronic_Logbook_Master.xlsm");
        var before = SHA256.HashData(File.ReadAllBytes(workbookPath));
        var bytes = File.ReadAllBytes(workbookPath);

        var plan = MobileWorkbookMigrationReader.Inspect(
            new BrowserFile(Path.GetFileName(workbookPath), "application/vnd.ms-excel.sheet.macroEnabled.12", bytes),
            new LogbookId("log_read_only_contract"));

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workbookPath)));
        Assert.Equal(File.ReadAllText(Path.Combine(repoRoot, "version.txt")).Trim(), plan.WorkbookVersion);
        Assert.Equal(PortableLogbookCustomFieldSet.WorkbookCustomFieldCount, plan.CustomFieldDefinitions.Count);
        Assert.True(plan.CachedTotalsMatch);
    }

    [Fact]
    public void InspectReadsWorkbookIdentityRowsCustomFieldsOverridesAndTotalsWithoutChangingSourceBytes()
    {
        var file = CreateWorkbook();
        var before = SHA256.HashData(file.Bytes);

        var plan = MobileWorkbookMigrationReader.Inspect(file, new LogbookId("log_target"));

        Assert.Equal(before, SHA256.HashData(file.Bytes));
        Assert.Equal("Disposable-Logbook.xlsm", plan.SourceFileName);
        Assert.Equal("2.0.7", plan.WorkbookVersion);
        Assert.Equal(new LogbookId("log_workbook_source"), plan.EmbeddedWorkbookLogbookId);
        Assert.Equal(new LogbookId("log_target"), plan.TargetLogbookId);
        Assert.Equal(["Role", "Employer", "Exercise", "Note"], plan.CustomFieldDefinitions.Select(field => field.Label));
        Assert.Equal(new DateOnly(2026, 6, 30), plan.CurrencyOverrideDates.FlightReview);
        Assert.Equal(new DateOnly(2026, 7, 31), plan.CurrencyOverrideDates.InstrumentProficiencyCheck);
        Assert.Null(plan.CurrencyOverrideDates.OperatorProficiencyCheck);

        Assert.Equal(2, plan.Rows.Count);
        var first = plan.Rows[0];
        Assert.Equal(2, first.SourceRowNumber);
        Assert.Equal(new EntryId("ent_source_1"), first.SourceEntryId);
        Assert.Equal(new DateOnly(2026, 7, 24), first.Entry.Date);
        Assert.Equal("VH-ABC", first.Entry.Reg);
        Assert.Equal("Captain", first.Entry.CustomFields[new CustomFieldId("cf_workbook_1")]);
        Assert.Equal(1.2m, first.Entry.SeCommandDay);
        Assert.Equal(0.3m, first.Entry.IfrSim);
        Assert.Equal(2, first.Entry.Ils);
        Assert.Equal(1, first.Entry.Circling);

        Assert.Equal(new MobileWorkbookMigrationTotals(2, 2.0m, 0.3m, 2.3m, 1, 1, 3, 1), plan.CalculatedTotals);
        Assert.Equal(new MobileWorkbookMigrationCachedTotals(2.0m, 0.3m, 1m, 1m, 3m), plan.CachedWorkbookTotals);
        Assert.True(plan.CachedTotalsMatch);
        Assert.Equal(64, plan.SourceSha256.Length);
        Assert.Equal(64, plan.EntryValuesSha256.Length);
    }

    [Fact]
    public void InspectFlagsCachedWorkbookTotalsThatDoNotMatchVisibleRows()
    {
        var plan = MobileWorkbookMigrationReader.Inspect(
            CreateWorkbook(cachedFlightHours: 99m),
            new LogbookId("log_target"));

        Assert.False(plan.CachedTotalsMatch);
        var current = PortableLogbookDocumentV2.CreateAustraliaFirst(
            plan.TargetLogbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MobileWorkbookMigrationWorkflow.CreateCandidate(
                plan,
                current,
                new DeviceId("dev_android"),
                DateTimeOffset.Parse("2026-08-25T00:00:00Z")));
        Assert.Contains("cached totals", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectRejectsNonWorkbookExtensionsBeforeReadingBytes()
    {
        var workbook = CreateWorkbook();
        var file = workbook with { FileName = "renamed.zip" };

        var exception = Assert.Throws<BrowserFileStoreException>(() =>
            MobileWorkbookMigrationReader.Inspect(file, new LogbookId("log_target")));

        Assert.Contains(".xlsm or .xlsx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareWithAppMatchesExactDataIndependentOfEntryAndCustomValueOrder()
    {
        var logbookId = new LogbookId("log_compare");
        var fields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Role", "Employer", "Exercise", "Note"]);
        var first = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 20,
            Reg = "VH-ONE",
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [fields[0].Id] = "Captain",
                [fields[1].Id] = "Alpha"
            },
            SeCommandDay = 1.1m,
            LandingsDay = 1
        };
        var second = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 21,
            Reg = "VH-TWO",
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [fields[1].Id] = "Bravo",
                [fields[0].Id] = "First Officer"
            },
            SeDualNight = 0.9m,
            LandingsNight = 1
        };
        var overrides = new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 7, 31), null, null);
        var plan = CreatePlan(logbookId, logbookId, fields, overrides, first, second);

        var comparison = MobileWorkbookMigrationWorkflow.CompareWithApp(
            plan,
            logbookId,
            [second, first],
            fields.Reverse(),
            overrides);

        Assert.True(comparison.EmbeddedIdentityMatches);
        Assert.True(comparison.EntryCountMatches);
        Assert.True(comparison.EntryValuesMatch);
        Assert.True(comparison.CustomFieldsMatch);
        Assert.True(comparison.CurrencyOverrideDatesMatch);
        Assert.True(comparison.TotalsMatch);
        Assert.True(comparison.IsExactDataMatch);
        Assert.Equal(2, comparison.AppEntryCount);
        Assert.Empty(comparison.WorkbookOnlyRows);
        Assert.Empty(comparison.AppOnlyEntries);
        Assert.Empty(comparison.CustomFieldDifferences);
    }

    [Fact]
    public void CompareWithAppReportsEachMaterialDifferenceWithoutChangingEitherSide()
    {
        var appLogbookId = new LogbookId("log_app");
        var workbookFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Role", "Employer", "Exercise", "Note"]);
        var workbookEntry = PortableLogbookWorkbookEntry.Empty with { Reg = "VH-WORKBOOK", SeCommandDay = 1.2m };
        var secondWorkbookEntry = PortableLogbookWorkbookEntry.Empty with { Reg = "VH-SECOND", SeDualDay = 0.5m };
        var plan = CreatePlan(
            appLogbookId,
            new LogbookId("log_other"),
            workbookFields,
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 7, 31), null, null),
            workbookEntry,
            secondWorkbookEntry);
        var appEntry = workbookEntry with { Reg = "VH-APP", SeCommandDay = 2.4m };
        var appFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Position", "Employer", "Exercise", "Note"]);

        var comparison = MobileWorkbookMigrationWorkflow.CompareWithApp(
            plan,
            appLogbookId,
            [appEntry],
            appFields,
            PortableLogbookCurrencyOverrideDates.Empty);

        Assert.False(comparison.EmbeddedIdentityMatches);
        Assert.False(comparison.EntryCountMatches);
        Assert.False(comparison.EntryValuesMatch);
        Assert.False(comparison.CustomFieldsMatch);
        Assert.False(comparison.CurrencyOverrideDatesMatch);
        Assert.False(comparison.TotalsMatch);
        Assert.False(comparison.IsExactDataMatch);
        Assert.Equal(2, comparison.WorkbookOnlyRows.Count);
        Assert.Single(comparison.AppOnlyEntries);
        var fieldDifference = Assert.Single(comparison.CustomFieldDifferences);
        Assert.Equal(1, fieldDifference.Order);
        Assert.Equal("Role", fieldDifference.WorkbookLabel);
        Assert.Equal("Position", fieldDifference.AppLabel);
        Assert.Equal("VH-WORKBOOK", workbookEntry.Reg);
        Assert.Equal("VH-APP", appEntry.Reg);
    }

    private static MobileWorkbookMigrationPlan CreatePlan(
        LogbookId targetLogbookId,
        LogbookId embeddedLogbookId,
        IReadOnlyList<CustomFieldDefinition> fields,
        PortableLogbookCurrencyOverrideDates overrides,
        params PortableLogbookWorkbookEntry[] entries)
    {
        var rows = entries
            .Select((entry, index) => new MobileWorkbookMigrationRow(index + 2, null, entry))
            .ToArray();
        var totals = MobileWorkbookMigrationTotals.Calculate(entries);
        return new MobileWorkbookMigrationPlan(
            "Disposable.xlsm",
            new string('a', 64),
            "3.0.0",
            embeddedLogbookId,
            targetLogbookId,
            fields,
            overrides,
            rows,
            totals,
            MobileWorkbookMigrationCachedTotals.Empty,
            MobileWorkbookMigrationReader.ComputeEntryValuesSha256(entries));
    }

    private static BrowserFile CreateWorkbook(decimal cachedFlightHours = 2.0m)
    {
        var columns = new[] { PortableLogbookWorkbookFieldCatalog.EntryIdColumnName }
            .Concat(PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames)
            .Concat(["TotalHours", "TotalApps"])
            .ToArray();
        columns[16] = "Role";
        columns[17] = "Employer";
        columns[18] = "Exercise";
        columns[19] = "Note";
        var totalHoursIndex = Array.IndexOf(columns, "TotalHours");
        var totalAppsIndex = Array.IndexOf(columns, "TotalApps");
        var ifrSimIndex = Array.IndexOf(columns, "IfrSim");
        var landingsDayIndex = Array.IndexOf(columns, "LandingsDay");
        var landingsNightIndex = Array.IndexOf(columns, "LandingsNight");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "xl/workbook.xml", new XDocument(
                new XElement(Ns + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(Ns + "sheets",
                        Sheet("Logbook", 1, "rId1"),
                        Sheet("Admin", 2, "rId2"),
                        Sheet("Metadata", 3, "rId3")),
                    new XElement(Ns + "definedNames",
                        DefinedName("LogbookVersion", "'Admin'!$A$1"),
                        DefinedName("FROverride", "'Admin'!$A$2"),
                        DefinedName("IPCOverride", "'Admin'!$A$3"),
                        DefinedName("OPCOverride", "'Admin'!$A$4"),
                        DefinedName(PortableLogbookWorkbookMetadata.LogbookIdName, "'Metadata'!$A$1")))));
            AddXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                new XElement(PackageRelNs + "Relationships",
                    Relationship("rId1", "worksheets/sheet1.xml"),
                    Relationship("rId2", "worksheets/sheet2.xml"),
                    Relationship("rId3", "worksheets/sheet3.xml"))));
            AddXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", new XDocument(
                new XElement(PackageRelNs + "Relationships",
                    new XElement(PackageRelNs + "Relationship",
                        new XAttribute("Id", "rIdTable"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table"),
                        new XAttribute("Target", "../tables/table1.xml")))));
            var tableColumns = columns
                .Select((name, index) => new XElement(
                    Ns + "tableColumn",
                    new XAttribute("id", index + 1),
                    new XAttribute("name", name)));
            AddXml(archive, "xl/tables/table1.xml", new XDocument(
                new XElement(
                    Ns + "table",
                    new XAttribute("id", 1),
                    new XAttribute("name", "Logbook"),
                    new XAttribute("displayName", "Logbook"),
                    new XAttribute("ref", $"A1:{ColumnName(columns.Length)}4"),
                    new XAttribute("totalsRowCount", 1),
                    new XElement(
                        Ns + "tableColumns",
                        new XAttribute("count", columns.Length),
                        tableColumns))));

            var row2 = EntryValues(
                "ent_source_1", "2026", "7", "24", "C172", "VH-ABC", "AD332", "Alex", "Jamie", "YSBK", "YSCN", "YWOL", "Training", "TRUE", "FALSE", "FALSE",
                "Captain", "Airline", "IFR", "First", seCommandDay: "1.2", ifrSim: "0.3", landingsDay: "1", ils: "2", circling: "1");
            var row3 = EntryValues(
                string.Empty, "2026", "Jul", "25", "C172", "VH-XYZ", "AD333", "Alex", string.Empty, "YSCN", "YSBK", string.Empty, "Return", "FALSE", "FALSE", "FALSE",
                "PIC", "Airline", "Night", "Second", seDualNight: "0.8", landingsNight: "1", rnp: "1");
            var totals = new Dictionary<int, string>
            {
                [totalHoursIndex] = cachedFlightHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [ifrSimIndex] = "0.3",
                [landingsDayIndex] = "1",
                [landingsNightIndex] = "1",
                [totalAppsIndex] = "3"
            };
            AddXml(archive, "xl/worksheets/sheet1.xml", Worksheet(
                Row(1, columns.Select((value, index) => InlineCell(index + 1, 1, value))),
                Row(2, row2.Select((value, index) => InlineCell(index + 1, 2, value))),
                Row(3, row3.Select((value, index) => InlineCell(index + 1, 3, value))),
                Row(4, totals.Select(pair => NumberCell(pair.Key + 1, 4, pair.Value)))));
            AddXml(archive, "xl/worksheets/sheet2.xml", Worksheet(
                Row(1, [InlineCell(1, 1, "2.0.7")]),
                Row(2, [NumberCell(1, 2, DateSerial(new DateOnly(2026, 6, 30)))]),
                Row(3, [NumberCell(1, 3, DateSerial(new DateOnly(2026, 7, 31)))]),
                Row(4, [NumberCell(1, 4, "0")])));
            AddXml(archive, "xl/worksheets/sheet3.xml", Worksheet(
                Row(1, [InlineCell(1, 1, "log_workbook_source")])));
        }

        return new BrowserFile(
            "Disposable-Logbook.xlsm",
            "application/vnd.ms-excel.sheet.macroEnabled.12",
            output.ToArray());
    }

    private static string[] EntryValues(
        string entryId,
        string year,
        string month,
        string day,
        string type,
        string reg,
        string flightId,
        string pic,
        string otherCrew,
        string from,
        string to,
        string via,
        string remarks,
        string fr,
        string ipc,
        string opc,
        string custom1,
        string custom2,
        string custom3,
        string custom4,
        string? seCommandDay = null,
        string? seDualNight = null,
        string? ifrSim = null,
        string? landingsDay = null,
        string? landingsNight = null,
        string? ils = null,
        string? rnp = null,
        string? circling = null)
    {
        var values = Enumerable.Repeat(string.Empty, PortableLogbookWorkbookFieldCatalog.PilotEnteredFields.Count + 3).ToArray();
        values[0] = entryId;
        var byId = PortableLogbookWorkbookFieldCatalog.PilotEnteredFields
            .Select((field, index) => (field.Id, Index: index + 1))
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        void Set(string key, string? value) => values[byId[key]] = value ?? string.Empty;
        Set("dateYear", year); Set("dateMonth", month); Set("dateDay", day); Set("type", type); Set("reg", reg);
        Set("flightId", flightId); Set("pic", pic); Set("otherPilotOrCrew", otherCrew); Set("from", from); Set("to", to);
        Set("via", via); Set("remarks", remarks); Set("fr", fr); Set("ipc", ipc); Set("opc", opc);
        Set("custom1", custom1); Set("custom2", custom2); Set("custom3", custom3); Set("custom4", custom4);
        Set("seCommandDay", seCommandDay); Set("seDualNight", seDualNight); Set("ifrSim", ifrSim);
        Set("landingsDay", landingsDay); Set("landingsNight", landingsNight); Set("ils", ils); Set("rnp", rnp); Set("circling", circling);
        return values;
    }

    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static XElement Sheet(string name, int id, string relationshipId) =>
        new(Ns + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(RelNs + "id", relationshipId));

    private static XElement DefinedName(string name, string reference) =>
        new(Ns + "definedName", new XAttribute("name", name), reference);

    private static XElement Relationship(string id, string target) =>
        new(PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
            new XAttribute("Target", target));

    private static XDocument Worksheet(params XElement[] rows) =>
        new(new XElement(Ns + "worksheet", new XElement(Ns + "sheetData", rows)));

    private static XElement Row(int number, IEnumerable<XElement> cells) =>
        new(Ns + "row", new XAttribute("r", number), cells);

    private static XElement InlineCell(int column, int row, string value) =>
        new(Ns + "c",
            new XAttribute("r", $"{ColumnName(column)}{row}"),
            new XAttribute("t", "inlineStr"),
            new XElement(Ns + "is", new XElement(Ns + "t", value)));

    private static XElement NumberCell(int column, int row, string value) =>
        new(Ns + "c", new XAttribute("r", $"{ColumnName(column)}{row}"), new XElement(Ns + "v", value));

    private static string DateSerial(DateOnly value) =>
        value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        document.Save(stream);
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

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.txt")) &&
                File.Exists(Path.Combine(directory.FullName, "Electronic_Logbook_Master.xlsm")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
