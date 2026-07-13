namespace ElectronicLogbook.Updater;

public static class UpdaterPhaseIds
{
    public const string StartExcel = "start-excel";
    public const string OpenSourceWorkbook = "open-source-workbook";
    public const string OpenMasterCopy = "open-master-copy";
    public const string PrepareMasterCopy = "prepare-master-copy";
    public const string ReadSourceValidationData = "read-source-validation-data";
    public const string CopyLogbookData = "copy-logbook-data";
    public const string CopyKeywordsData = "copy-keywords-data";
    public const string CopyRoutesData = "copy-routes-data";
    public const string CopyBaseAirportSelections = "copy-base-airport-selections";
    public const string CopyNamedPreferences = "copy-named-preferences";
    public const string RestoreLogbookPresentation = "restore-logbook-presentation";
    public const string RefreshAirportVisitStats = "refresh-airport-visit-stats";
    public const string CalculateOutputWorkbook = "calculate-output-workbook";
    public const string RefreshPivotTables = "refresh-pivot-tables";
    public const string UpdateHoursOverTimeChart = "update-hours-over-time-chart";
    public const string ValidatePreservedData = "validate-preserved-data";
    public const string SaveOutputWorkbook = "save-output-workbook";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class UpdaterProgressEventTypes
{
    public const string PhaseStarted = "phase-started";
    public const string PhaseFailed = "phase-failed";
    public const string UpdateCompleted = "update-completed";
}

public sealed record UpdaterProgressEvent(
    string EventType,
    string PhaseId,
    string Message,
    int? Percent,
    DateTimeOffset TimestampUtc);

public interface IUpdaterProgressSink
{
    void Report(UpdaterProgressEvent progressEvent);
}

public sealed class ConsoleUpdaterProgressSink : IUpdaterProgressSink
{
    public void Report(UpdaterProgressEvent progressEvent)
    {
        switch (progressEvent.EventType)
        {
            case UpdaterProgressEventTypes.PhaseStarted:
                Console.WriteLine($"[updater] {progressEvent.Message}...");
                break;
            case UpdaterProgressEventTypes.PhaseFailed:
                Console.Error.WriteLine($"[updater] Failed at {progressEvent.PhaseId}: {progressEvent.Message}");
                break;
            case UpdaterProgressEventTypes.UpdateCompleted:
                Console.WriteLine($"[updater] {progressEvent.Message}");
                break;
        }
    }
}
