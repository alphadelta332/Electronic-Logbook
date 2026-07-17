using System.Text.Json;

namespace ElectronicLogbook.Updater.Tests;

public sealed class DiagnosticBundleFactoryTests : IDisposable
{
    private readonly string _originalOneDrive =
        Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty;
    private readonly string _originalOneDriveConsumer =
        Environment.GetEnvironmentVariable("OneDriveConsumer") ?? string.Empty;
    private readonly string _originalOneDriveCommercial =
        Environment.GetEnvironmentVariable("OneDriveCommercial") ?? string.Empty;

    [Fact]
    public void CreateRedactsPathsWorkbookFileNamesAndTokens()
    {
        var sourcePath = Path.Combine("C:\\Users\\Pilot\\OneDrive\\Logbooks", "My Private Logbook.xlsm");
        var outputPath = Path.Combine(Path.GetTempPath(), "Updated Private Logbook.xlsm");
        var report = BuildReport(sourcePath, outputPath);
        var progressEvents = new[]
        {
            new UpdaterProgressEvent(
                UpdaterProgressEventTypes.PhaseStarted,
                UpdaterPhaseIds.CopyLogbookData,
                "copying Logbook data",
                40,
                DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
                TimeoutSeconds: 300)
        };
        var exception = new IOException(
            $"Could not replace {sourcePath} with token ghp_abcdefghijklmnopqrstuvwxyz.");

        var bundle = DiagnosticBundleFactory.Create(
            TestRepo.Version,
            report,
            progressEvents,
            exception,
            sourcePath,
            report.MasterPath,
            outputPath,
            DateTimeOffset.Parse("2026-07-17T01:00:00Z"));
        var json = JsonSerializer.Serialize(bundle, JsonDefaults.Web);

        Assert.DoesNotContain(sourcePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(outputPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("My Private Logbook.xlsm", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Updated Private Logbook.xlsm", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATER-FILE-IO", json, StringComparison.Ordinal);
        Assert.Equal(3, bundle.SchemaVersion);
        Assert.Equal(12, bundle.WorkbookStructure.LogbookRows);
        Assert.Single(bundle.Phases);
        Assert.Null(bundle.Phases[0].RecoveryHint);
        Assert.Equal(300, bundle.Phases[0].TimeoutSeconds);
        Assert.NotNull(bundle.Error?.RecoveryHint);
        Assert.Contains("Close Excel", bundle.Error.RecoveryHint, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIncludesRedactedPhaseRecoveryHints()
    {
        var sourcePath = Path.Combine("C:\\Users\\Pilot\\OneDrive\\Logbooks", "My Private Logbook.xlsm");
        var progressEvents = new[]
        {
            new UpdaterProgressEvent(
                UpdaterProgressEventTypes.PhaseFailed,
                UpdaterPhaseIds.OpenSourceWorkbook,
                "source locked",
                null,
                DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
                $"Close {sourcePath} and retry.")
        };

        var bundle = DiagnosticBundleFactory.Create(
            TestRepo.Version,
            report: null,
            progressEvents,
            new InvalidOperationException($"Could not open {sourcePath}."),
            sourcePath,
            masterWorkbookPath: null,
            outputWorkbookPath: null);

        Assert.Single(bundle.Phases);
        Assert.Contains("[redacted-path]", bundle.Phases[0].RecoveryHint, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, bundle.Phases[0].RecoveryHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Save and close", bundle.Error?.RecoveryHint, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCategorisesTemporaryCloudAndLocalPaths()
    {
        var oneDriveRoot = Path.Combine(Path.GetTempPath(), $"OneDrive-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("OneDriveConsumer", string.Empty);
        Environment.SetEnvironmentVariable("OneDriveCommercial", string.Empty);
        Environment.SetEnvironmentVariable("OneDrive", oneDriveRoot);
        var sourcePath = Path.Combine(oneDriveRoot, "Logbook.xlsm");
        var masterPath = Path.Combine(Path.GetTempPath(), "Master.xlsm");
        var outputPath = Path.Combine("C:\\LocalLogbooks", "Output.xlsm");

        var bundle = DiagnosticBundleFactory.Create(
            TestRepo.Version,
            report: null,
            progressEvents: [],
            error: null,
            sourcePath,
            masterPath,
            outputPath);

        Assert.Equal("cloud-synced", bundle.PathCategories.SourceWorkbook);
        Assert.Equal("temporary", bundle.PathCategories.MasterWorkbook);
        Assert.Equal("local", bundle.PathCategories.OutputWorkbook);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OneDrive", _originalOneDrive);
        Environment.SetEnvironmentVariable("OneDriveConsumer", _originalOneDriveConsumer);
        Environment.SetEnvironmentVariable("OneDriveCommercial", _originalOneDriveCommercial);
    }

    private static MigrationReport BuildReport(string sourcePath, string outputPath)
    {
        return new MigrationReport(
            sourcePath,
            Path.Combine(Path.GetTempPath(), "master.xlsm"),
            outputPath,
            "2.0.0",
            TestRepo.Version,
            12,
            new AirportVisitStatsDiagnostics(
                AirportRows: 3000,
                LogbookRows: 12,
                AliasCount: 5,
                KeywordCount: 8,
                LogbookRowsWithDetails: 10,
                SimOnlyRowsSkipped: 1,
                TokensScanned: 100,
                TokensIgnored: 20,
                TokensMatched: 15,
                LogbookRowsWithRecognisedAirports: 9,
                VisitedAirportRows: 7,
                WrittenVisitedAirportRows: 7,
                SavedNonBlankVisitRows: 6,
                TopVisitedAirports: new Dictionary<string, int>
                {
                    ["YSSY"] = 2,
                    ["YMML"] = 1
                }),
            new Dictionary<string, string>
            {
                ["Logbook"] = "fingerprint"
            },
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            "succeeded");
    }
}
