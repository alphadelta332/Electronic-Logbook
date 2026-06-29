using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed record ReleaseManifest(
    string Version,
    string Tag,
    string Commit,
    IReadOnlyList<ReleaseAsset> Assets);

public sealed record ReleaseAsset(string Name, long Size, string Sha256);

public sealed record VerifiedRelease(
    ReleaseManifest Manifest,
    string MasterWorkbookPath,
    string DownloadDirectory);

public sealed record MigrationRequest(
    string SourcePath,
    string MasterPath,
    string OutputPath,
    ReleaseManifest? Manifest);

public sealed record MigrationReport(
    string SourcePath,
    string MasterPath,
    string OutputPath,
    string SourceVersion,
    string OutputVersion,
    int LogbookRows,
    AirportVisitStatsDiagnostics AirportVisitStats,
    IReadOnlyDictionary<string, string> PreservedFingerprints,
    DateTimeOffset CompletedAtUtc,
    string Status);

public sealed record AirportVisitStatsDiagnostics(
    int AirportRows,
    int LogbookRows,
    int AliasCount,
    int KeywordCount,
    int LogbookRowsWithDetails,
    int SimOnlyRowsSkipped,
    int TokensScanned,
    int TokensIgnored,
    int TokensMatched,
    int LogbookRowsWithRecognisedAirports,
    int VisitedAirportRows,
    int WrittenVisitedAirportRows,
    int SavedNonBlankVisitRows,
    IReadOnlyDictionary<string, int> TopVisitedAirports);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
