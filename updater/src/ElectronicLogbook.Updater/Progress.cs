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
    public const string CopyPortableStorage = "copy-portable-storage";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class UpdaterProgressEventTypes
{
    public const string PhaseStarted = "phase-started";
    public const string PhaseFailed = "phase-failed";
    public const string UpdateCompleted = "update-completed";
}

public static class UpdaterPhasePolicies
{
    private static readonly IReadOnlyDictionary<string, int> TimeoutSecondsByPhase =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [UpdaterPhaseIds.StartExcel] = 60,
            [UpdaterPhaseIds.OpenSourceWorkbook] = 120,
            [UpdaterPhaseIds.OpenMasterCopy] = 120,
            [UpdaterPhaseIds.PrepareMasterCopy] = 60,
            [UpdaterPhaseIds.ReadSourceValidationData] = 60,
            [UpdaterPhaseIds.CopyLogbookData] = 300,
            [UpdaterPhaseIds.CopyKeywordsData] = 60,
            [UpdaterPhaseIds.CopyRoutesData] = 60,
            [UpdaterPhaseIds.CopyBaseAirportSelections] = 60,
            [UpdaterPhaseIds.CopyNamedPreferences] = 60,
            [UpdaterPhaseIds.RestoreLogbookPresentation] = 180,
            [UpdaterPhaseIds.RefreshAirportVisitStats] = 180,
            [UpdaterPhaseIds.CalculateOutputWorkbook] = 300,
            [UpdaterPhaseIds.RefreshPivotTables] = 180,
            [UpdaterPhaseIds.UpdateHoursOverTimeChart] = 120,
            [UpdaterPhaseIds.ValidatePreservedData] = 120,
            [UpdaterPhaseIds.SaveOutputWorkbook] = 180,
            [UpdaterPhaseIds.CopyPortableStorage] = 60,
            [UpdaterPhaseIds.Completed] = 0,
            [UpdaterPhaseIds.Failed] = 0
        };

    public static IReadOnlyCollection<string> PhaseIds => TimeoutSecondsByPhase.Keys.ToArray();

    public static int? GetTimeoutSeconds(string phaseId)
    {
        return TimeoutSecondsByPhase.TryGetValue(phaseId, out var timeoutSeconds)
            ? timeoutSeconds
            : null;
    }
}

public static class UpdaterPhaseProgress
{
    private static readonly IReadOnlyDictionary<string, int> PercentByPhase =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [UpdaterPhaseIds.StartExcel] = 5,
            [UpdaterPhaseIds.OpenSourceWorkbook] = 10,
            [UpdaterPhaseIds.OpenMasterCopy] = 15,
            [UpdaterPhaseIds.PrepareMasterCopy] = 20,
            [UpdaterPhaseIds.ReadSourceValidationData] = 25,
            [UpdaterPhaseIds.CopyLogbookData] = 40,
            [UpdaterPhaseIds.CopyKeywordsData] = 50,
            [UpdaterPhaseIds.CopyRoutesData] = 58,
            [UpdaterPhaseIds.CopyNamedPreferences] = 64,
            [UpdaterPhaseIds.RestoreLogbookPresentation] = 70,
            [UpdaterPhaseIds.RefreshAirportVisitStats] = 76,
            [UpdaterPhaseIds.CopyBaseAirportSelections] = 80,
            [UpdaterPhaseIds.CalculateOutputWorkbook] = 84,
            [UpdaterPhaseIds.RefreshPivotTables] = 89,
            [UpdaterPhaseIds.UpdateHoursOverTimeChart] = 93,
            [UpdaterPhaseIds.ValidatePreservedData] = 96,
            [UpdaterPhaseIds.SaveOutputWorkbook] = 98,
            [UpdaterPhaseIds.CopyPortableStorage] = 99,
            [UpdaterPhaseIds.Completed] = 100
        };

    public static IReadOnlyCollection<string> PhaseIds => PercentByPhase.Keys.ToArray();

    public static int? GetPercent(string phaseId)
    {
        return PercentByPhase.TryGetValue(phaseId, out var percent)
            ? percent
            : null;
    }
}

public sealed record UpdaterProgressEvent(
    string EventType,
    string PhaseId,
    string Message,
    int? Percent,
    DateTimeOffset TimestampUtc,
    string? RecoveryHint = null,
    int? TimeoutSeconds = null);

public interface IUpdaterProgressSink
{
    void Report(UpdaterProgressEvent progressEvent);
}

public sealed class RecordingUpdaterProgressSink(IUpdaterProgressSink? inner = null) : IUpdaterProgressSink
{
    private readonly List<UpdaterProgressEvent> _events = [];
    private readonly object _syncRoot = new();

    public IReadOnlyList<UpdaterProgressEvent> Events
    {
        get
        {
            lock (_syncRoot)
            {
                return _events.ToArray();
            }
        }
    }

    public void Report(UpdaterProgressEvent progressEvent)
    {
        lock (_syncRoot)
        {
            _events.Add(progressEvent);
        }

        inner?.Report(progressEvent);
    }
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
                if (!string.IsNullOrWhiteSpace(progressEvent.RecoveryHint))
                {
                    Console.Error.WriteLine($"[updater] Recovery: {progressEvent.RecoveryHint}");
                }
                break;
            case UpdaterProgressEventTypes.UpdateCompleted:
                Console.WriteLine($"[updater] {progressEvent.Message}");
                break;
        }
    }
}
