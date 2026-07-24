namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;
using System.IO.Compression;
using System.Xml.Linq;

public sealed class WorkbookFaithfulLogbookFieldContractTests
{
    private static readonly XNamespace Spreadsheet =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void MasterWorkbookLogbookTableDefinesCanonicalEntryIdAndPilotEnteredColumns()
    {
        var workbookPath = TestRepo.FindFile("Electronic_Logbook_Master.xlsm");
        var columns = ReadLogbookTableColumnNames(workbookPath);
        var expectedPilotColumns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames;

        Assert.Equal(PortableLogbookWorkbookFieldCatalog.EntryIdColumnName, columns[0]);
        Assert.Equal(
            expectedPilotColumns,
            columns
                .Skip(2)
                .Take(expectedPilotColumns.Count));
        Assert.Equal(44, expectedPilotColumns.Count);
        Assert.DoesNotContain("Portable Entry ID", columns);

        var calculatedColumns = columns
            .Where(column => !string.Equals(column, PortableLogbookWorkbookFieldCatalog.EntryIdColumnName, StringComparison.Ordinal) &&
                !expectedPilotColumns.Contains(column, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(PortableLogbookWorkbookFieldCatalog.CalculatedProjectionColumnNames, calculatedColumns);
    }

    [Fact]
    public void WorkbookFaithfulCatalogDoesNotContainAbandonedV1CollapsedFields()
    {
        var fieldIds = PortableLogbookWorkbookFieldCatalog.PilotEnteredFields
            .Select(field => field.Id)
            .ToHashSet(StringComparer.Ordinal);
        var columnNames = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var abandonedId in new[]
        {
            "date",
            "aircraftType",
            "registration",
            "flightNumber",
            "multiPilot",
            "pilotInCommand",
            "coPilot",
            "dual",
            "instructor",
            "day",
            "night",
            "takeoffsDay",
            "takeoffsNight",
            "ifrApproaches",
            "holding"
        })
        {
            Assert.DoesNotContain(abandonedId, fieldIds);
        }

        foreach (var abandonedColumnName in new[]
        {
            "Date",
            "Aircraft Type",
            "Flight Number",
            "Multi-Pilot",
            "Co-Pilot",
            "Dual",
            "Instructor",
            "Night",
            "Takeoffs Day",
            "Takeoffs Night",
            "IFR Approaches",
            "Holding"
        })
        {
            Assert.DoesNotContain(abandonedColumnName, columnNames);
        }
    }

    private static IReadOnlyList<string> ReadLogbookTableColumnNames(string workbookPath)
    {
        using var stream = new FileStream(
            workbookPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var document = ReadXml(entry);
            var root = document.Root;
            if (root is null)
            {
                continue;
            }

            var name = (string?)root.Attribute("name");
            var displayName = (string?)root.Attribute("displayName");
            if (!string.Equals(name, "Logbook", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(displayName, "Logbook", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return root
                .Descendants(Spreadsheet + "tableColumn")
                .Select(column => (string?)column.Attribute("name") ?? string.Empty)
                .ToArray();
        }

        throw new InvalidDataException("Workbook package does not contain a Logbook table.");
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
