namespace ElectronicLogbook.Updater.Tests;

public sealed class WizardPilotMigrationStagingTests
{
    [Fact]
    public void PilotUpdateShowsPlainLanguageWorkbookSummaryBeforeStagingAndGoogleSignIn()
    {
        var wizard = File.ReadAllText(TestRepo.FindFile(
            "updater/src/ElectronicLogbook.Updater.Wizard/MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(TestRepo.FindFile(
            "updater/src/ElectronicLogbook.Updater.Wizard/MainWindow.xaml"));
        var panelStart = xaml.IndexOf("x:Name=\"PreMigrationSummaryPanel\"", StringComparison.Ordinal);
        var panelEnd = xaml.IndexOf("</Border>", panelStart, StringComparison.Ordinal);
        Assert.True(panelStart >= 0 && panelEnd > panelStart, "Pre-migration summary panel could not be isolated.");
        var panel = xaml[panelStart..panelEnd];

        Assert.Contains("WorkbookPreMigrationInspector.Inspect(source)", wizard, StringComparison.Ordinal);
        Assert.Contains("Flights:", wizard, StringComparison.Ordinal);
        Assert.Contains("Logged hours:", wizard, StringComparison.Ordinal);
        Assert.Contains("Date range:", wizard, StringComparison.Ordinal);
        Assert.Contains("Check these workbook warnings:", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logbook id", panel, StringComparison.OrdinalIgnoreCase);

        var summaryIndex = wizard.IndexOf("WorkbookPreMigrationInspector.Inspect(source)", StringComparison.Ordinal);
        var stagingIndex = wizard.IndexOf("await stager.StageAsync", StringComparison.Ordinal);
        var signInIndex = wizard.IndexOf("await connectionClient.SignInWithGoogleAsync", StringComparison.Ordinal);
        Assert.True(
            summaryIndex >= 0 && stagingIndex > summaryIndex && signInIndex > stagingIndex,
            "The customer summary must be prepared before staging and Google sign-in.");
    }

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
        Assert.Contains("await stager.StageAsync(", updateFlow, StringComparison.Ordinal);
        Assert.Contains("await connectionClient.SignInWithGoogleAsync", updateFlow, StringComparison.Ordinal);
        Assert.Contains("new PilotWorkbookHostedMigration(", updateFlow, StringComparison.Ordinal);
        Assert.Contains("await hostedMigration.RunAsync(", updateFlow, StringComparison.Ordinal);
        Assert.Contains("Signed in as {pilotAccountEmail}", updateFlow, StringComparison.Ordinal);
        Assert.Contains("new PilotWorkbookPostMigrationHandoff()", updateFlow, StringComparison.Ordinal);
        Assert.Contains("await postMigrationHandoff.InstallAsync(", updateFlow, StringComparison.Ordinal);
        Assert.Contains("Migration Complete", updateFlow, StringComparison.Ordinal);
        Assert.Contains("Moved to FlightLogX", updateFlow, StringComparison.Ordinal);
        Assert.Contains("if (pilotStaging is not null)", updateFlow, StringComparison.Ordinal);
        Assert.Contains("The original workbook remains unchanged", updateFlow, StringComparison.Ordinal);
        Assert.Contains("else if (_context.UseInPlaceSwap)", updateFlow, StringComparison.Ordinal);
        var stagingIndex = updateFlow.IndexOf("await stager.StageAsync", StringComparison.Ordinal);
        var googleSignInIndex = updateFlow.IndexOf(
            "await connectionClient.SignInWithGoogleAsync",
            StringComparison.Ordinal);
        var hostedMigrationIndex = updateFlow.IndexOf(
            "await hostedMigration.RunAsync",
            StringComparison.Ordinal);
        var pilotHandoffIndex = updateFlow.IndexOf(
            "await postMigrationHandoff.InstallAsync",
            StringComparison.Ordinal);
        var handoffGateIndex = updateFlow.IndexOf("else if (_context.UseInPlaceSwap)", StringComparison.Ordinal);
        var replacementIndex = updateFlow.IndexOf(
            "WorkbookHandoff.ReplaceSourceWithUpdated",
            StringComparison.Ordinal);
        Assert.True(
            stagingIndex >= 0
            && googleSignInIndex > stagingIndex
            && hostedMigrationIndex > googleSignInIndex
            && pilotHandoffIndex > hostedMigrationIndex
            && handoffGateIndex > pilotHandoffIndex
            && replacementIndex > handoffGateIndex,
            "Pilot staging and Google sign-in must complete before hosted migration, pilot handoff, and the non-pilot replacement branch.");
    }

    [Fact]
    public void PilotUpdateUsesStageAwareFailureCopyAndTreatsOnlyUserCancellationAsCancelled()
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

        Assert.Contains(
            "catch (OperationCanceledException ex) when (_updateCts?.IsCancellationRequested == true)",
            updateFlow,
            StringComparison.Ordinal);
        Assert.Contains("PilotWorkbookMigrationStage.PreparingWorkbook", updateFlow, StringComparison.Ordinal);
        Assert.Contains("PilotWorkbookMigrationStage.SigningIn", updateFlow, StringComparison.Ordinal);
        Assert.Contains("PilotWorkbookMigrationStage.MovingToFlightLogX", updateFlow, StringComparison.Ordinal);
        Assert.Contains("PilotWorkbookMigrationStage.InstallingWorkbook", updateFlow, StringComparison.Ordinal);
        Assert.Contains("PilotWorkbookMigrationFailurePresenter.Create(", updateFlow, StringComparison.Ordinal);
        Assert.Contains("pilotHostedMigration is not null", updateFlow, StringComparison.Ordinal);
        Assert.Contains("pilotFailure?.CustomerMessage", updateFlow, StringComparison.Ordinal);
        Assert.Contains("Migration stopped safely", updateFlow, StringComparison.Ordinal);
    }
}
