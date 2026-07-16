namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookOutputNamerTests
{
    [Theory]
    [InlineData("Electronic Logbook", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated_Updated", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated_20260716-131723", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated_Updated_20260716-131723", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated_Staged_20260716-131723", "Electronic Logbook")]
    [InlineData("Electronic Logbook_Updated_Working", "Electronic Logbook")]
    [InlineData("Updated Training Logbook", "Updated Training Logbook")]
    public void StripUpdaterSuffixesRemovesOnlyUpdaterGeneratedSuffixes(
        string input,
        string expected)
    {
        Assert.Equal(expected, WorkbookOutputNamer.StripUpdaterSuffixes(input));
    }

    [Fact]
    public void BuildDefaultOutputPathUsesCleanBaseName()
    {
        var source = Path.Combine(
            Path.GetTempPath(),
            "Electronic Logbook_Updated_Updated_20260716-131723.xlsm");

        var output = WorkbookOutputNamer.BuildDefaultOutputPath(source);

        Assert.Equal(Path.GetDirectoryName(source), Path.GetDirectoryName(output));
        Assert.Matches(
            @"Electronic Logbook_Updated_\d{8}-\d{6}\.xlsm$",
            Path.GetFileName(output));
    }
}
