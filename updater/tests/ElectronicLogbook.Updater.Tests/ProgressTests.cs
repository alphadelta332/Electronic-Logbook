namespace ElectronicLogbook.Updater.Tests;

public sealed class ProgressTests
{
    [Fact]
    public void RecordingProgressSinkStoresEventsAndForwardsToInnerSink()
    {
        var inner = new CapturingProgressSink();
        var sink = new RecordingUpdaterProgressSink(inner);
        var progressEvent = new UpdaterProgressEvent(
            UpdaterProgressEventTypes.PhaseFailed,
            UpdaterPhaseIds.SaveOutputWorkbook,
            "save failed",
            null,
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            "Close Excel and retry.");

        sink.Report(progressEvent);

        var recorded = Assert.Single(sink.Events);
        Assert.Equal(progressEvent, recorded);
        Assert.Equal(progressEvent, Assert.Single(inner.Events));
    }

    [Fact]
    public void PhasePoliciesDefineTimeoutsForStableMigrationPhases()
    {
        var expectedPhaseIds = new[]
        {
            UpdaterPhaseIds.StartExcel,
            UpdaterPhaseIds.OpenSourceWorkbook,
            UpdaterPhaseIds.OpenMasterCopy,
            UpdaterPhaseIds.PrepareMasterCopy,
            UpdaterPhaseIds.ReadSourceValidationData,
            UpdaterPhaseIds.CopyLogbookData,
            UpdaterPhaseIds.CopyKeywordsData,
            UpdaterPhaseIds.CopyRoutesData,
            UpdaterPhaseIds.CopyBaseAirportSelections,
            UpdaterPhaseIds.CopyNamedPreferences,
            UpdaterPhaseIds.RestoreLogbookPresentation,
            UpdaterPhaseIds.RefreshAirportVisitStats,
            UpdaterPhaseIds.CalculateOutputWorkbook,
            UpdaterPhaseIds.RefreshPivotTables,
            UpdaterPhaseIds.UpdateHoursOverTimeChart,
            UpdaterPhaseIds.ValidatePreservedData,
            UpdaterPhaseIds.SaveOutputWorkbook,
            UpdaterPhaseIds.CopyPortableStorage,
            UpdaterPhaseIds.Completed,
            UpdaterPhaseIds.Failed
        };

        foreach (var phaseId in expectedPhaseIds)
        {
            Assert.NotNull(UpdaterPhasePolicies.GetTimeoutSeconds(phaseId));
        }
        Assert.Equal(
            expectedPhaseIds.Order(StringComparer.Ordinal),
            UpdaterPhasePolicies.PhaseIds.Order(StringComparer.Ordinal));
        Assert.Null(UpdaterPhasePolicies.GetTimeoutSeconds("unknown-phase"));
    }

    [Fact]
    public void PhaseProgressDefinesPercentsForWizardMigrationPhases()
    {
        var expectedPhaseIds = new[]
        {
            UpdaterPhaseIds.StartExcel,
            UpdaterPhaseIds.OpenSourceWorkbook,
            UpdaterPhaseIds.OpenMasterCopy,
            UpdaterPhaseIds.PrepareMasterCopy,
            UpdaterPhaseIds.ReadSourceValidationData,
            UpdaterPhaseIds.CopyLogbookData,
            UpdaterPhaseIds.CopyKeywordsData,
            UpdaterPhaseIds.CopyRoutesData,
            UpdaterPhaseIds.CopyNamedPreferences,
            UpdaterPhaseIds.RestoreLogbookPresentation,
            UpdaterPhaseIds.RefreshAirportVisitStats,
            UpdaterPhaseIds.CopyBaseAirportSelections,
            UpdaterPhaseIds.CalculateOutputWorkbook,
            UpdaterPhaseIds.RefreshPivotTables,
            UpdaterPhaseIds.UpdateHoursOverTimeChart,
            UpdaterPhaseIds.ValidatePreservedData,
            UpdaterPhaseIds.SaveOutputWorkbook,
            UpdaterPhaseIds.CopyPortableStorage,
            UpdaterPhaseIds.Completed
        };

        var lastPercent = -1;
        foreach (var phaseId in expectedPhaseIds)
        {
            var percent = UpdaterPhaseProgress.GetPercent(phaseId);

            Assert.NotNull(percent);
            Assert.InRange(percent.Value, lastPercent + 1, 100);
            lastPercent = percent.Value;
        }

        Assert.Null(UpdaterPhaseProgress.GetPercent(UpdaterPhaseIds.Failed));
        Assert.Null(UpdaterPhaseProgress.GetPercent("unknown-phase"));
        Assert.Equal(
            expectedPhaseIds.Order(StringComparer.Ordinal),
            UpdaterPhaseProgress.PhaseIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ConsoleProgressSinkWritesRecoveryHintForFailures()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var sink = new ConsoleUpdaterProgressSink();

            sink.Report(new UpdaterProgressEvent(
                UpdaterProgressEventTypes.PhaseFailed,
                UpdaterPhaseIds.SaveOutputWorkbook,
                "save failed",
                null,
                DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
                "Close Excel and retry."));

            var text = error.ToString();
            Assert.Contains("Failed at save-output-workbook", text, StringComparison.Ordinal);
            Assert.Contains("Recovery: Close Excel and retry.", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed class CapturingProgressSink : IUpdaterProgressSink
    {
        public List<UpdaterProgressEvent> Events { get; } = [];

        public void Report(UpdaterProgressEvent progressEvent)
        {
            Events.Add(progressEvent);
        }
    }
}
