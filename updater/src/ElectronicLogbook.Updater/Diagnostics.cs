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
    DateTimeOffset TimestampUtc);

public sealed record DiagnosticError(
    string Code,
    string PhaseId,
    string ExceptionType,
    string Message);

public sealed record DiagnosticWorkbookStructure(
    int LogbookRows,
    int AirportRows,
    int KeywordCount,
    int AliasCount,
    int RouteCount);

public static partial class DiagnosticBundleFactory
{
    public const int CurrentSchemaVersion = 1;

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
                    progressEvent.Message,
                    progressEvent.Percent,
                    progressEvent.TimestampUtc))
                .ToArray(),
            error is null
                ? null
                : new DiagnosticError(
                    ClassifyError(error),
                    lastPhase,
                    error.GetType().Name,
                    RedactSensitiveText(error.Message, sensitivePaths)),
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

        return SensitiveTokenRegex().Replace(redacted, "[redacted-token]");
    }

    [GeneratedRegex(@"(?i)\b(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}\b")]
    private static partial Regex SensitiveTokenRegex();
}
