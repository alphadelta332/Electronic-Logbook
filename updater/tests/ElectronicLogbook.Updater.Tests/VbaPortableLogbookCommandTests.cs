namespace ElectronicLogbook.Updater.Tests;

public sealed class VbaPortableLogbookCommandTests
{
    [Fact]
    public void ModLogbookExposesPortableStatusCommand()
    {
        var source = ReadModLogbookSource();

        Assert.Contains("Public Sub ShowPortableLogbookStatus()", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE_LOGBOOK_UPDATER_EXE_NAME", source, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookUpdaterPath", source, StringComparison.Ordinal);
        Assert.Contains("portable status --workbook", source, StringComparison.Ordinal);
        Assert.Contains("QuoteCommandArgument(ThisWorkbook.FullName)", source, StringComparison.Ordinal);
        Assert.Contains("CreateObject(\"WScript.Shell\")", source, StringComparison.Ordinal);
        Assert.Contains("Run(commandLine, 0, True)", source, StringComparison.Ordinal);
        Assert.Contains("BuildUserFacingErrorMessage(", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-STATUS-E001", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewEntryCommandRepairAssignsPortableStatusButton()
    {
        var source = ReadModLogbookSource();

        Assert.Contains("portablelogbookstatus", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InStr(labelText, \"portable\")", source, StringComparison.Ordinal);
        Assert.Contains("InStr(labelText, \"status\")", source, StringComparison.Ordinal);
        Assert.Contains("actionName = \"ShowPortableLogbookStatus\"", source, StringComparison.Ordinal);
    }

    private static string ReadModLogbookSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "modLogbook.bas");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find modLogbook.bas from the test output directory.");
    }
}
