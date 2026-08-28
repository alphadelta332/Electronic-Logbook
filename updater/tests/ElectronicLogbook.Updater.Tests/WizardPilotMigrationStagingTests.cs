namespace ElectronicLogbook.Updater.Tests;

public sealed class WizardPilotMigrationStagingTests
{
    [Fact]
    public void PilotUpdateStagesValidatedArtifactsWithoutReplacingOriginalWorkbook()
    {
        var wizard = File.ReadAllText(TestRepo.FindFile(
            "updater/src/ElectronicLogbook.Updater.Wizard/MainWindow.xaml.cs"));
        var start = wizard.IndexOf(
            "private async Task StartUpdateAsync()",
            StringComparison.Ordinal);
        var end = wizard.IndexOf(
            "private HandoffRecoveryResult RecoverPendingHandoffForWizard()",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "StartUpdateAsync could not be isolated.");
        var updateFlow = wizard[start..end];

        Assert.Contains("_context.Channel == UpdateChannel.Pilot", updateFlow, StringComparison.Ordinal);
        Assert.Contains("new WorkbookMigrationStager(progressSink)", updateFlow, StringComparison.Ordinal);
        Assert.Contains("await stager.StageAsync(migrationRequest", updateFlow, StringComparison.Ordinal);
        Assert.Contains("if (pilotStaging is not null)", updateFlow, StringComparison.Ordinal);
        Assert.Contains("The original workbook remains unchanged", updateFlow, StringComparison.Ordinal);
        Assert.Contains("else if (_context.UseInPlaceSwap)", updateFlow, StringComparison.Ordinal);
        var stagingIndex = updateFlow.IndexOf("await stager.StageAsync", StringComparison.Ordinal);
        var handoffGateIndex = updateFlow.IndexOf("else if (_context.UseInPlaceSwap)", StringComparison.Ordinal);
        var replacementIndex = updateFlow.IndexOf(
            "WorkbookHandoff.ReplaceSourceWithUpdated",
            StringComparison.Ordinal);
        Assert.True(
            stagingIndex >= 0 && handoffGateIndex > stagingIndex && replacementIndex > handoffGateIndex,
            "Pilot staging must complete before the non-pilot replacement branch.");
    }
}
