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

    [Fact]
    public void ExternalUpdaterAirportVisitStatsUsesSameRouteInputsAsWorkbookVba()
    {
        var source = ReadRepoSource("updater", "src", "ElectronicLogbook.Updater", "ExcelWorkbookMigrator.cs");
        var body = ExtractCSharpMethodBody(source, "ReadLogbookRouteSourceValues");

        Assert.Contains("HasColumn((object)logbook, \"From\")", body, StringComparison.Ordinal);
        Assert.Contains("HasColumn((object)logbook, \"Via\")", body, StringComparison.Ordinal);
        Assert.Contains("HasColumn((object)logbook, \"To\")", body, StringComparison.Ordinal);
        Assert.Contains("HasColumn((object)logbook, \"Remarks\")", body, StringComparison.Ordinal);
        Assert.Contains("LogbookRouteText.BuildAirportStatsSource", body, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFlowRefreshesAirportStatsBeforePivotSummaries()
    {
        var source = ReadRepoSource("updater", "src", "ElectronicLogbook.Updater", "ExcelWorkbookMigrator.cs");
        var migrationIndex = source.IndexOf(
            "refreshing airport visit stats",
            StringComparison.Ordinal);
        var pivotIndex = source.IndexOf(
            "refreshing pivot tables",
            StringComparison.Ordinal);

        Assert.True(migrationIndex >= 0, "Migration flow should refresh airport visit stats.");
        Assert.True(pivotIndex >= 0, "Migration flow should refresh pivot summaries.");
        Assert.True(
            migrationIndex < pivotIndex,
            "Airport visit stats must be refreshed before pivot/chart summaries so saved presentation state uses current visits.");
    }

    private static string ReadModAirportsSource()
    {
        return ReadRepoSource("modAirports.bas");
    }

    private static string ReadRepoSource(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find '{Path.Combine(relativePathParts)}' from the test output directory.");
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

    private static string ExtractCSharpMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private static string[] {methodName}(", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            throw new InvalidOperationException($"Could not find C# method '{methodName}'.");
        }

        var openBraceIndex = source.IndexOf('{', methodIndex);
        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException($"Could not find body for C# method '{methodName}'.");
        }

        var depth = 0;
        for (var index = openBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find end of C# method '{methodName}'.");
    }
}
