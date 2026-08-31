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
    public void PreviewAndLegacyPilotBranchesUsePreviewRuntimeWithoutRepeatingDevelopmentWarning()
    {
        var bootSource = ReadVbaSource("modBoot.bas");
        var updateSource = ReadVbaSource("modUpdate.bas");

        Assert.Contains("Private Const LEGACY_PREVIEW_GITHUB_BRANCH As String = \"pilot\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("IsPreviewUpdateBranch = (branchName = \"preview\" Or branchName = LEGACY_PREVIEW_GITHUB_BRANCH)", bootSource, StringComparison.Ordinal);
        Assert.Contains("GitHubSourceBranch = LEGACY_PREVIEW_GITHUB_BRANCH", bootSource, StringComparison.Ordinal);
        Assert.Contains("RequiresDevelopmentWizardWarning = Not IsStableUpdateBranch(branchName) And", bootSource, StringComparison.Ordinal);
        Assert.Contains("Not IsPreviewUpdateBranch(branchName)", bootSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"preview\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbookUpdaterPreview", bootSource, StringComparison.Ordinal);

        Assert.Contains("Private Const LEGACY_PREVIEW_GITHUB_BRANCH As String = \"pilot\"", updateSource, StringComparison.Ordinal);
        Assert.Contains("IsPreviewUpdateBranch = (branchName = \"preview\" Or branchName = LEGACY_PREVIEW_GITHUB_BRANCH)", updateSource, StringComparison.Ordinal);
        Assert.Contains("GitHubSourceBranch = LEGACY_PREVIEW_GITHUB_BRANCH", updateSource, StringComparison.Ordinal);
        Assert.Contains("WorkbookUpdateChannelArgument = \"preview\"", updateSource, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbookUpdaterPreview", updateSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewVersion300OffersTheFlightLogXMoveBeforeStartingTheUpdater()
    {
        var workbookSource = ReadVbaSource("ThisWorkbook.cls");
        var bootSource = ReadVbaSource("modBoot.bas");
        var updateSource = ReadVbaSource("modUpdate.bas");

        Assert.Contains("modBoot.CheckForUpdate", workbookSource, StringComparison.Ordinal);
        AssertPreviewMigrationOffer(bootSource, "RunWizardUpdate remoteVer");
        AssertPreviewMigrationOffer(updateSource, "RunUpdate remoteVer");
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
    [InlineData("preview", "preview")]
    [InlineData(" PREVIEW ", "preview")]
    [InlineData("pilot", "preview")]
    [InlineData(" PILOT ", "preview")]
    [InlineData("main", null)]
    [InlineData("dev", null)]
    [InlineData("hotfix", null)]
    [InlineData("", null)]
    public void WorkbookMigrationCanonicalisesPreviewAndAcceptsLegacyPilot(
        string sourceChannel,
        string? expected)
    {
        Assert.Equal(expected, ExcelWorkbookMigrator.CanonicalPreviewUpdateChannel(sourceChannel));
    }

    [Fact]
    public void WizardUsesPreviewChannelAndAcceptsLegacyPilotInput()
    {
        var source = ReadRepoSource(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs"));

        Assert.Contains("\"preview\" => UpdateChannel.Preview", source, StringComparison.Ordinal);
        Assert.Contains("\"pilot\" => UpdateChannel.Preview", source, StringComparison.Ordinal);
        Assert.Contains("UpdateChannel.Preview => \"Preview\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateChannel.Preview => LegacyPreviewGitHubBranch", source, StringComparison.Ordinal);
        Assert.Contains("Preview version:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyPilotWizardPublicationIsProtectedAndKeepsThe203BridgeAsset()
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

    private static void AssertPreviewMigrationOffer(string source, string acceptedAction)
    {
        var normalizedSource = source.ReplaceLineEndings("\n");

        Assert.Contains("Private Const PREVIEW_MIGRATION_VERSION As String = \"3.0.0\"", source, StringComparison.Ordinal);
        Assert.Contains("IsPreviewMigrationOffer = IsPreviewUpdateBranch(GetGitHubBranch()) And", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeVersionText(remoteVer) = PREVIEW_MIGRATION_VERSION", source, StringComparison.Ordinal);
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
