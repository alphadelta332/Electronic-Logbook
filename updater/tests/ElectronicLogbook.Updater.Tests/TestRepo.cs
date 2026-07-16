namespace ElectronicLogbook.Updater.Tests;

using System.IO.Compression;
using System.Text;

internal static class TestRepo
{
    public static string Version => File.ReadAllText(FindFile("version.txt")).Trim();

    public static string FindFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file: {Path.Combine(relativeParts)}");
    }

    public static string CreateMinimalWorkbookPackage(
        string directory,
        string version,
        string? fileName = null,
        bool includeAirportsTable = true)
    {
        var path = Path.Combine(directory, fileName ?? $"{Guid.NewGuid():N}.xlsm");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddEntry(
            archive,
            "xl/workbook.xml",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Backend" sheetId="1" r:id="rId1"/>
                <sheet name="Logbook" sheetId="2" r:id="rId2"/>
                <sheet name="Lists" sheetId="3" r:id="rId3"/>
              </sheets>
              <definedNames>
                <definedName name="LogbookVersion">'Backend'!$A$1</definedName>
                <definedName name="GitHubBranch">'Backend'!$A$2</definedName>
                <definedName name="DateAfterExport">'Backend'!$A$3</definedName>
                <definedName name="RoutesBuilt">'Backend'!$A$4</definedName>
                <definedName name="RoutesDirty">'Backend'!$A$5</definedName>
                <definedName name="RoutesDefinitionVersion">'Backend'!$A$6</definedName>
                <definedName name="suppressWarningsUntil">'Backend'!$A$7</definedName>
              </definedNames>
            </workbook>
            """);
        AddEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
            </Relationships>
            """);
        AddEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>{{version}}</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
        AddWorksheetWithTables(archive, "xl/worksheets/sheet2.xml", ["Logbook"]);
        AddWorksheetWithTables(
            archive,
            "xl/worksheets/sheet3.xml",
            includeAirportsTable ? ["Keywords", "Airports"] : ["Keywords"]);
        AddTable(archive, "xl/tables/table1.xml", "Logbook");
        AddTable(archive, "xl/tables/table2.xml", "Keywords");
        if (includeAirportsTable)
        {
            AddTable(archive, "xl/tables/table3.xml", "Airports");
        }

        return path;
    }

    private static void AddWorksheetWithTables(
        ZipArchive archive,
        string worksheetPath,
        IReadOnlyList<string> tableNames)
    {
        var tableParts = string.Join(
            Environment.NewLine,
            tableNames.Select((_, index) => $"""    <tablePart r:id="rId{index + 1}"/>"""));
        AddEntry(
            archive,
            worksheetPath,
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData/>
              <tableParts count="{{tableNames.Count}}">
            {{tableParts}}
              </tableParts>
            </worksheet>
            """);

        var firstTableNumber = tableNames[0] == "Logbook" ? 1 : 2;
        var relationships = string.Join(
            Environment.NewLine,
            tableNames.Select((_, index) =>
                $"""  <Relationship Id="rId{index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table{firstTableNumber + index}.xml"/>"""));
        var relationshipPath = worksheetPath.Replace(
            "worksheets/",
            "worksheets/_rels/",
            StringComparison.Ordinal);
        AddEntry(
            archive,
            $"{relationshipPath}.rels",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            {{relationships}}
            </Relationships>
            """);
    }

    private static void AddTable(ZipArchive archive, string path, string name)
    {
        AddEntry(
            archive,
            path,
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   id="1" name="{{name}}" displayName="{{name}}" ref="A1:A2">
              <autoFilter ref="A1:A2"/>
              <tableColumns count="1">
                <tableColumn id="1" name="Column1"/>
              </tableColumns>
            </table>
            """);
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }
}
