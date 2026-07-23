namespace ElectronicLogbook.Updater.Tests;

public sealed class VbaBranchModeTests
{
    [Fact]
    public void HotfixBranchUsesDevelopmentWorkbookBehaviour()
    {
        var source = ReadVbaSource("modLogbook.bas");

        Assert.Contains("branchValue = \"dev\" Or branchValue = \"hotfix\"", source, StringComparison.Ordinal);
        Assert.Contains("WorkbookBranchDisablesDevelopmentPrompts(branchValue)", source, StringComparison.Ordinal);
        Assert.Contains("WorkbookProtectionDisabledByBranch = WorkbookBranchDisablesProtection(branchValue)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HotfixBranchUsesBranchMasterAndHotfixWizardChannel()
    {
        var bootSource = ReadVbaSource("modBoot.bas");
        var updateSource = ReadVbaSource("modUpdate.bas");

        Assert.Contains("branchName = \"hotfix\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"hotfix\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("commandLine = commandLine & \" --channel \" & WorkbookUpdateChannelArgument()", bootSource, StringComparison.Ordinal);

        Assert.Contains("branchName = \"hotfix\"", updateSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"hotfix\"", updateSource, StringComparison.Ordinal);
        Assert.Contains("commandLine = commandLine & \" --channel \" & WorkbookUpdateChannelArgument()", updateSource, StringComparison.Ordinal);
    }

    private static string ReadVbaSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} from the test output directory.");
    }
}
