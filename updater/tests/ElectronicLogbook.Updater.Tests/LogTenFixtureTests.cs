namespace ElectronicLogbook.Updater.Tests;

public sealed class LogTenFixtureTests
{
    private static readonly string[] RequiredDynamicHeaders =
    [
        "Date",
        "Aircraft ID",
        "Aircraft Type",
        "From",
        "To",
        "Total Time",
        "Simulator",
        "PIC/P1 Crew",
        "Day Ldg",
        "Night Ldg",
        "Approach 1",
        "Approach 2"
    ];

    [Theory]
    [InlineData("dynamic-clean.csv", 1)]
    [InlineData("dynamic-blocking.csv", 3)]
    [InlineData("dynamic-simulator.csv", 1)]
    [InlineData("dynamic-duplicates.csv", 2)]
    [InlineData("dynamic-locale-sensitive.tsv", 1)]
    [InlineData("dynamic-malformed.csv", 3)]
    public void DynamicLogTenFixturesUseRequiredImporterHeaders(string fileName, int expectedDataRows)
    {
        var lines = ReadFixtureLines(fileName);
        var delimiter = fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var headers = lines[0]
            .Split(delimiter)
            .Select(header => header.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in RequiredDynamicHeaders)
        {
            Assert.Contains(header, headers);
        }

        Assert.Equal(expectedDataRows, lines.Skip(1).Count(line => !string.IsNullOrWhiteSpace(line)));
    }

    [Fact]
    public void DefaultFullExportFixtureUsesLegacyLogTenHeader()
    {
        var headers = ReadFixtureLines("default-full-export-detected.csv")[0]
            .Split(',')
            .Select(header => header.Trim())
            .ToArray();

        Assert.Contains("flight_flightdate", headers);
    }

    [Fact]
    public void DynamicExportDocumentationListsRequiredHeaders()
    {
        var documentation = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "docs",
            "logten-dynamic-export.md")));

        foreach (var header in RequiredDynamicHeaders)
        {
            Assert.Contains($"`{header}`", documentation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SimulatorFixturePinsSimulatorOnlyShape()
    {
        var row = ReadFixtureLines("dynamic-simulator.csv")[1].Split(',');

        Assert.Equal("", row[2]);
        Assert.Equal("0", row[5]);
        Assert.Equal("1.2", row[6]);
    }

    [Fact]
    public void DuplicateFixtureContainsTwoIdenticalRows()
    {
        var lines = ReadFixtureLines("dynamic-duplicates.csv");

        Assert.Equal(lines[1], lines[2]);
    }

    [Fact]
    public void LocaleSensitiveFixtureUsesTabDelimiterAndDurationValues()
    {
        var lines = ReadFixtureLines("dynamic-locale-sensitive.tsv");

        Assert.Contains('\t', lines[0]);
        Assert.Contains("1:15", lines[1]);
        Assert.Contains("0:35", lines[1]);
        Assert.Contains("\"Quoted, comma-containing remarks\"", lines[1]);
    }

    private static string[] ReadFixtureLines(string fileName) =>
        File.ReadAllLines(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "testdata",
            "logten",
            fileName)));
}
