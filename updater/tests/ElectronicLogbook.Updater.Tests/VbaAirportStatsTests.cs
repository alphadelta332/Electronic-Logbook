namespace ElectronicLogbook.Updater.Tests;

public sealed class VbaAirportStatsTests
{
    [Fact]
    public void AirportVisitStatsCountsViaColumnTokens()
    {
        var source = ReadModAirportsSource();
        var body = ExtractVbaProcedureBody(source, "AccumulateAirportVisits", "End Sub");

        Assert.Contains("AirportTableColumnExists(tblLog, \"Via\")", body, StringComparison.Ordinal);
        Assert.Contains("viaCol = tblLog.ListColumns(\"Via\").Index", body, StringComparison.Ordinal);
        Assert.Contains("tblLog.DataBodyRange.Cells(rowIndex, viaCol).Value", body, StringComparison.Ordinal);
        Assert.Contains("TokeniseAirportDetails(viaText)", body, StringComparison.Ordinal);
        Assert.Contains("If Not rowMatches.Exists(icao) Then rowMatches.Add icao, True", body, StringComparison.Ordinal);
    }

    private static string ReadModAirportsSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "modAirports.bas");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find modAirports.bas from the test output directory.");
    }

    private static string ExtractVbaProcedureBody(string source, string procedureName, string terminator)
    {
        var procedureIndex = source.IndexOf(
            $"Private Sub {procedureName}",
            StringComparison.Ordinal);
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Private Function {procedureName}",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Public Sub {procedureName}",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Public Function {procedureName}",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            throw new InvalidOperationException($"Could not find VBA procedure '{procedureName}'.");
        }

        var terminatorIndex = source.IndexOf(terminator, procedureIndex, StringComparison.Ordinal);
        if (terminatorIndex < 0)
        {
            throw new InvalidOperationException($"Could not find end of VBA procedure '{procedureName}'.");
        }

        return source.Substring(procedureIndex, terminatorIndex - procedureIndex + terminator.Length);
    }
}
