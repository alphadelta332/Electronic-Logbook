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

    [Fact]
    public void PilotBranchUsesBranchMasterWithoutRepeatingDevelopmentWarning()
    {
        var bootSource = ReadVbaSource("modBoot.bas");
        var updateSource = ReadVbaSource("modUpdate.bas");

        Assert.Contains("IsPilotUpdateBranch = (LCase$(Trim$(branchName)) = \"pilot\")", bootSource, StringComparison.Ordinal);
        Assert.Contains("RequiresDevelopmentWizardWarning = Not IsStableUpdateBranch(branchName) And", bootSource, StringComparison.Ordinal);
        Assert.Contains("Not IsPilotUpdateBranch(branchName)", bootSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"pilot\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbookUpdaterPilot", bootSource, StringComparison.Ordinal);

        Assert.Contains("IsPilotUpdateBranch = (LCase$(Trim$(branchName)) = \"pilot\")", updateSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"pilot\"", updateSource, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbookUpdaterPilot", updateSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PilotVersion300OffersTheFlightLogXMoveBeforeStartingTheUpdater()
    {
        var workbookSource = ReadVbaSource("ThisWorkbook.cls");
        var bootSource = ReadVbaSource("modBoot.bas");
        var updateSource = ReadVbaSource("modUpdate.bas");

        Assert.Contains("modBoot.CheckForUpdate", workbookSource, StringComparison.Ordinal);
        AssertPilotMigrationOffer(bootSource, "RunWizardUpdate remoteVer");
        AssertPilotMigrationOffer(updateSource, "RunUpdate remoteVer");
    }

    [Fact]
    public void CompletedMigrationWarnsOncePerExcelSessionBeforeAddingALocalEntry()
    {
        var source = ReadVbaSource("modLogbook.bas");
        var normalizedSource = source.ReplaceLineEndings("\n");

        Assert.Contains(
            "Private Const FLIGHTLOGX_MIGRATION_STATUS_NAME As String = \"FlightLogXMigrationStatus\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Private Const FLIGHTLOGX_MIGRATION_COMPLETED_STATUS As String = \"Moved to FlightLogX\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Private mFlightLogXLocalEditWarningShown As Boolean", source, StringComparison.Ordinal);
        Assert.Contains("If mFlightLogXLocalEditWarningShown Then Exit Sub", source, StringComparison.Ordinal);
        Assert.Contains("mFlightLogXLocalEditWarningShown = True", source, StringComparison.Ordinal);
        Assert.Contains(
            "Changes you make here stay only in this spreadsheet and are not sent to FlightLogX.",
            source,
            StringComparison.Ordinal);
        Assert.Contains("You can continue editing after closing this message.", source, StringComparison.Ordinal);

        var warningIndex = normalizedSource.IndexOf(
            "    WarnIfPostMigrationWorkbookEditStaysLocal\n",
            StringComparison.Ordinal);
        var preAddSaveIndex = normalizedSource.IndexOf(
            "    If Not TrySaveWorkbookBeforeAdd(ThisWorkbook) Then",
            StringComparison.Ordinal);
        Assert.True(warningIndex >= 0, "The Add action should check the completed migration stamp.");
        Assert.True(
            preAddSaveIndex > warningIndex,
            "The local-only warning must appear on the add attempt before the workbook is saved or changed.");
    }

    [Theory]
    [InlineData("pilot", true)]
    [InlineData(" PILOT ", true)]
    [InlineData("main", false)]
    [InlineData("dev", false)]
    [InlineData("hotfix", false)]
    [InlineData("", false)]
    public void WorkbookMigrationOnlyRetainsTheControlledPilotChannel(string sourceChannel, bool expected)
    {
        Assert.Equal(expected, ExcelWorkbookMigrator.ShouldPreservePilotUpdateChannel(sourceChannel));
    }

    [Fact]
    public void WizardRecognisesAndDisplaysPilotChannel()
    {
        var source = ReadRepoSource(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs"));

        Assert.Contains("\"pilot\" => UpdateChannel.Pilot", source, StringComparison.Ordinal);
        Assert.Contains("UpdateChannel.Pilot => \"Pilot\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateChannel.Pilot => \"pilot\"", source, StringComparison.Ordinal);
        Assert.Contains("Pilot version:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PilotWizardPublicationIsProtectedAndKeepsThe203BridgeAsset()
    {
        var pilotWorkflow = ReadRepoSource(Path.Combine(
            ".github",
            "workflows",
            "publish-pilot-wizard.yml"));
        var developmentWorkflow = ReadRepoSource(Path.Combine(
            ".github",
            "workflows",
            "publish-dev-wizard.yml"));

        Assert.Contains("- pilot", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("environment: pilot", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("ELECTRONIC_LOGBOOK_PILOT_SUPABASE_URL", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("ELECTRONIC_LOGBOOK_PILOT_SUPABASE_ANON_KEY", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("$tag = \"dev-wizard-$shortSha\"", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("pilot-wizard-channel.txt", pilotWorkflow, StringComparison.Ordinal);
        Assert.Contains("Preserving pilot channel bridge release", developmentWorkflow, StringComparison.Ordinal);
    }

    private static string ReadVbaSource(string fileName)
    {
        return ReadRepoSource(fileName);
    }

    private static void AssertPilotMigrationOffer(string source, string acceptedAction)
    {
        var normalizedSource = source.ReplaceLineEndings("\n");

        Assert.Contains("Private Const PILOT_MIGRATION_VERSION As String = \"3.0.0\"", source, StringComparison.Ordinal);
        Assert.Contains("IsPilotMigrationOffer = IsPilotUpdateBranch(GetGitHubBranch()) And", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeVersionText(remoteVer) = PILOT_MIGRATION_VERSION", source, StringComparison.Ordinal);
        Assert.Contains("Your logbook is ready to move to FlightLogX.", source, StringComparison.Ordinal);
        Assert.Contains("in this workbook to the FlightLogX app.", source, StringComparison.Ordinal);
        Assert.Contains("Nothing will change if you choose No.", source, StringComparison.Ordinal);
        Assert.Contains(
            $"If MsgBox(msg, vbYesNo + vbInformation, title) = vbYes Then\n            {acceptedAction}\n        End If",
            normalizedSource,
            StringComparison.Ordinal);
    }

    private static string ReadRepoSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from the test output directory.");
    }
}
