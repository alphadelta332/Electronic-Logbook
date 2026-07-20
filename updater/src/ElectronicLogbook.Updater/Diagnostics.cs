using System.Text.RegularExpressions;

namespace ElectronicLogbook.Updater;

public sealed record DiagnosticBundle(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string? SourceVersion,
    string? OutputVersion,
    string Status,
    DiagnosticPathCategories PathCategories,
    IReadOnlyList<DiagnosticPhaseEvent> Phases,
    DiagnosticError? Error,
    DiagnosticWorkbookStructure WorkbookStructure);

public sealed record DiagnosticPathCategories(
    string SourceWorkbook,
    string MasterWorkbook,
    string OutputWorkbook);

public sealed record DiagnosticPhaseEvent(
    string EventType,
    string PhaseId,
    string Message,
    int? Percent,
    DateTimeOffset TimestampUtc,
    string? RecoveryHint,
    int? TimeoutSeconds);

public sealed record DiagnosticError(
    string Code,
    string PhaseId,
    string ExceptionType,
    string Message,
    string? RecoveryHint);

public sealed record DiagnosticWorkbookStructure(
    int LogbookRows,
    int AirportRows,
    int KeywordCount,
    int AliasCount,
    int RouteCount);

public static partial class DiagnosticBundleFactory
{
    public const int CurrentSchemaVersion = 3;

    public static DiagnosticBundle Create(
        string applicationVersion,
        MigrationReport? report,
        IReadOnlyList<UpdaterProgressEvent> progressEvents,
        Exception? error,
        string? sourceWorkbookPath,
        string? masterWorkbookPath,
        string? outputWorkbookPath,
        DateTimeOffset? createdAtUtc = null)
    {
        var sensitivePaths = new[]
        {
            sourceWorkbookPath,
            masterWorkbookPath,
            outputWorkbookPath
        };
        var lastPhase = progressEvents.LastOrDefault()?.PhaseId ?? UpdaterPhaseIds.Failed;

        return new DiagnosticBundle(
            CurrentSchemaVersion,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            applicationVersion,
            report?.SourceVersion,
            report?.OutputVersion,
            report?.Status ?? (error is null ? "unknown" : "failed"),
            new DiagnosticPathCategories(
                CategorisePath(sourceWorkbookPath),
                CategorisePath(masterWorkbookPath),
                CategorisePath(outputWorkbookPath)),
            progressEvents
                .Select(progressEvent => new DiagnosticPhaseEvent(
                    progressEvent.EventType,
                    progressEvent.PhaseId,
                    RedactSensitiveText(progressEvent.Message, sensitivePaths),
                    progressEvent.Percent,
                    progressEvent.TimestampUtc,
                    RedactOptionalSensitiveText(progressEvent.RecoveryHint, sensitivePaths),
                    progressEvent.TimeoutSeconds))
                .ToArray(),
            error is null
                ? null
                : new DiagnosticError(
                    ClassifyError(error),
                    lastPhase,
                    error.GetType().Name,
                    RedactSensitiveText(error.Message, sensitivePaths),
                    RedactSensitiveText(GetRecoveryHint(lastPhase, error), sensitivePaths)),
            new DiagnosticWorkbookStructure(
                report?.LogbookRows ?? 0,
                report?.AirportVisitStats.AirportRows ?? 0,
                report?.AirportVisitStats.KeywordCount ?? 0,
                report?.AirportVisitStats.AliasCount ?? 0,
                report?.AirportVisitStats.TopVisitedAirports.Count ?? 0));
    }

    private static string CategorisePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "unknown";
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var tempPath = Path.GetFullPath(Path.GetTempPath());
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (CloudStoragePath.IsLikelyCloudSynced(fullPath))
            {
                return "cloud-synced";
            }

            return fullPath.StartsWith(tempPath, comparison)
                ? "temporary"
                : "local";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ClassifyError(Exception error)
    {
        return error switch
        {
            OperationCanceledException => "UPDATER-CANCELLED",
            InvalidDataException => "UPDATER-VALIDATION",
            IOException => "UPDATER-FILE-IO",
            UnauthorizedAccessException => "UPDATER-FILE-ACCESS",
            _ => "UPDATER-UNEXPECTED"
        };
    }

    public static string GetRecoveryHint(string phaseId, Exception error)
    {
        if (error is OperationCanceledException)
        {
            return "No recovery action is required. Re-run the updater when ready.";
        }

        if (error is IOException or UnauthorizedAccessException)
        {
            return "Close Excel, wait for any cloud sync to finish, confirm the workbook is writable, then re-run the updater.";
        }

        return phaseId switch
        {
            UpdaterPhaseIds.OpenSourceWorkbook =>
                "Save and close the source workbook, wait for cloud sync to finish, then re-run the updater.",
            UpdaterPhaseIds.OpenMasterCopy =>
                "Retry after the updater can access the downloaded or selected master workbook.",
            UpdaterPhaseIds.ReadSourceValidationData =>
                "Keep the original workbook unchanged and contact support with the diagnostic bundle.",
            UpdaterPhaseIds.CopyLogbookData or
            UpdaterPhaseIds.CopyKeywordsData or
            UpdaterPhaseIds.CopyRoutesData or
            UpdaterPhaseIds.CopyBaseAirportSelections or
            UpdaterPhaseIds.CopyNamedPreferences =>
                "Keep the original workbook unchanged and contact support with the diagnostic bundle before retrying.",
            UpdaterPhaseIds.ValidatePreservedData =>
                "Do not use the updated workbook. Keep the original workbook and contact support with the diagnostic bundle.",
            UpdaterPhaseIds.SaveOutputWorkbook =>
                "Close Excel, check write access and available disk space, then re-run the updater.",
            UpdaterPhaseIds.CopyPortableStorage =>
                "Do not use the updated workbook. Keep the original workbook and contact support with the diagnostic bundle.",
            _ =>
                "Keep the original workbook and any updater backup, then re-run the updater or contact support with the diagnostic bundle."
        };
    }

    private static string RedactSensitiveText(
        string message,
        IEnumerable<string?> sensitivePaths)
    {
        var redacted = message;
        foreach (var path in sensitivePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            redacted = redacted.Replace(path!, "[redacted-path]", StringComparison.OrdinalIgnoreCase);
            var fileName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                redacted = redacted.Replace(fileName, "[redacted-file]", StringComparison.OrdinalIgnoreCase);
            }
        }

        redacted = RecoveryCodeLineRegex().Replace(redacted, "Recovery code: [redacted-recovery-code]");

        return SensitiveTokenRegex().Replace(redacted, "[redacted-token]");
    }

    private static string? RedactOptionalSensitiveText(
        string? message,
        IEnumerable<string?> sensitivePaths)
    {
        return string.IsNullOrWhiteSpace(message)
            ? null
            : RedactSensitiveText(message, sensitivePaths);
    }

    [GeneratedRegex(@"(?i)\b(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}\b")]
    private static partial Regex SensitiveTokenRegex();

    [GeneratedRegex(@"(?i)\bRecovery code:\s*[A-Za-z0-9_-](?:[A-Za-z0-9_-]|\s){42,}")]
    private static partial Regex RecoveryCodeLineRegex();
}
