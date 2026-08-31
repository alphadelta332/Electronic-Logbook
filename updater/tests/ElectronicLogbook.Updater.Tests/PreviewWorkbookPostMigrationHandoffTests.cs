using System.IO.Compression;
using System.Xml.Linq;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PreviewWorkbookPostMigrationHandoffTests : IDisposable
{
    private static readonly DateTimeOffset CompletedAt =
        DateTimeOffset.Parse("2026-08-29T08:15:30+10:00");

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PreviewWorkbookPostMigrationHandoffTests-{Guid.NewGuid():N}");

    public PreviewWorkbookPostMigrationHandoffTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task InstallAsync_StampsVerifiedCompletionThenAtomicallyInstallsAndRetainsUntouchedBackup()
    {
        var staging = await CreateStagingAsync();
        var hostedResult = CompletedHostedResult(staging.SourceFingerprint);
        var untouchedBackupFingerprint = await Integrity.Sha256Async(
            staging.BackupWorkbookPath,
            CancellationToken.None);

        var result = await new PreviewWorkbookPostMigrationHandoff().InstallAsync(
            staging,
            hostedResult);

        Assert.Equal(Path.GetFullPath(staging.OriginalWorkbookPath), result.FinalWorkbookPath);
        Assert.Equal(staging.BackupWorkbookPath, result.UntouchedBackupWorkbookPath);
        Assert.True(File.Exists(result.UntouchedBackupWorkbookPath));
        Assert.True(File.Exists(result.InstallationRollbackBackupPath));
        Assert.False(File.Exists(staging.StagedWorkbookPath));
        Assert.Equal(
            TestRepo.Version,
            WorkbookPackageValidator.ValidateWorkbookPackage(result.FinalWorkbookPath));
        Assert.Equal(
            untouchedBackupFingerprint,
            await Integrity.Sha256Async(
                result.UntouchedBackupWorkbookPath,
                CancellationToken.None));
        Assert.Equal(
            new WorkbookMigrationStamp(
                PreviewWorkbookPostMigrationHandoff.CompletedStatus,
                CompletedAt,
                hostedResult.Migration.MigrationId),
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                result.FinalWorkbookPath));
    }

    [Fact]
    public async Task InstallAsync_StampFailureLeavesOriginalAndUntouchedBackupUnchanged()
    {
        var staging = await CreateStagingAsync();
        var sourceFingerprint = await Integrity.Sha256Async(
            staging.OriginalWorkbookPath,
            CancellationToken.None);
        var backupFingerprint = await Integrity.Sha256Async(
            staging.BackupWorkbookPath,
            CancellationToken.None);
        var replaceCalled = false;
        var handoff = new PreviewWorkbookPostMigrationHandoff(
            (_, _) => throw new IOException("stamp write failed"),
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp,
            (_, _, _, _, _) =>
            {
                replaceCalled = true;
                throw new InvalidOperationException("Replacement must not run.");
            });

        var error = await Assert.ThrowsAsync<IOException>(() =>
            handoff.InstallAsync(
                staging,
                CompletedHostedResult(staging.SourceFingerprint)));

        Assert.Contains("stamp write failed", error.Message, StringComparison.Ordinal);
        Assert.False(replaceCalled);
        Assert.Equal(
            sourceFingerprint,
            await Integrity.Sha256Async(staging.OriginalWorkbookPath, CancellationToken.None));
        Assert.Equal(
            backupFingerprint,
            await Integrity.Sha256Async(staging.BackupWorkbookPath, CancellationToken.None));
        Assert.Null(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                staging.OriginalWorkbookPath));
    }

    [Fact]
    public async Task InstallAsync_InstallationFailureCanRetrySameCompletedMigrationAndStamp()
    {
        var staging = await CreateStagingAsync();
        var hostedResult = CompletedHostedResult(staging.SourceFingerprint);
        var sourceFingerprint = await Integrity.Sha256Async(
            staging.OriginalWorkbookPath,
            CancellationToken.None);
        var backupFingerprint = await Integrity.Sha256Async(
            staging.BackupWorkbookPath,
            CancellationToken.None);
        var failingHandoff = new PreviewWorkbookPostMigrationHandoff(
            PortableLogbookWorkbookPackageStorage.EnsureWorkbookMigrationStamp,
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp,
            (_, _, _, _, _) => throw new IOException("installation failed"));

        var error = await Assert.ThrowsAsync<IOException>(() =>
            failingHandoff.InstallAsync(staging, hostedResult));

        Assert.Contains("installation failed", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            sourceFingerprint,
            await Integrity.Sha256Async(staging.OriginalWorkbookPath, CancellationToken.None));
        Assert.Equal(
            backupFingerprint,
            await Integrity.Sha256Async(staging.BackupWorkbookPath, CancellationToken.None));
        Assert.NotNull(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                staging.StagedWorkbookPath));

        var retry = await new PreviewWorkbookPostMigrationHandoff().InstallAsync(
            staging,
            hostedResult);

        Assert.Equal(staging.OriginalWorkbookPath, retry.FinalWorkbookPath);
        Assert.NotNull(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                retry.FinalWorkbookPath));
        Assert.True(File.Exists(staging.BackupWorkbookPath));
    }

    [Fact]
    public async Task InstallAsync_PostReplaceStampValidationFailureRestoresOriginalAndFreshStageCanRetryCompletedMigration()
    {
        var staging = await CreateStagingAsync();
        var hostedResult = CompletedHostedResult(staging.SourceFingerprint);
        var sourceFingerprint = await Integrity.Sha256Async(
            staging.OriginalWorkbookPath,
            CancellationToken.None);
        WorkbookMigrationStamp? ReadStampExceptFromInstalledSource(string path) =>
            string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(staging.OriginalWorkbookPath),
                StringComparison.OrdinalIgnoreCase)
                ? null
                : PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(path);
        var handoff = new PreviewWorkbookPostMigrationHandoff(
            PortableLogbookWorkbookPackageStorage.EnsureWorkbookMigrationStamp,
            ReadStampExceptFromInstalledSource,
            static (source, staged, finalVersion, backupVersion, validation) =>
                WorkbookHandoff.ReplaceSourceWithUpdated(
                    source,
                    staged,
                    finalVersion,
                    backupVersion,
                    PhysicalWorkbookFileSystem.Instance,
                    validation));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handoff.InstallAsync(staging, hostedResult));

        Assert.Contains("installed workbook", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "2.0.3",
            WorkbookPackageValidator.ValidateWorkbookPackage(staging.OriginalWorkbookPath));
        Assert.Equal(
            sourceFingerprint,
            await Integrity.Sha256Async(staging.OriginalWorkbookPath, CancellationToken.None));
        Assert.True(File.Exists(staging.BackupWorkbookPath));
        Assert.False(File.Exists(staging.StagedWorkbookPath));

        var retryStaged = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            TestRepo.Version,
            $"RetryStaged_{Guid.NewGuid():N}.xlsm");
        var retryStaging = staging with
        {
            StagedWorkbookPath = retryStaged,
            MigrationReport = staging.MigrationReport with { OutputPath = retryStaged }
        };
        var retry = await new PreviewWorkbookPostMigrationHandoff().InstallAsync(
            retryStaging,
            hostedResult with { ResumedCompletedMigration = true });

        Assert.Equal(TestRepo.Version, WorkbookPackageValidator.ValidateWorkbookPackage(retry.FinalWorkbookPath));
        Assert.NotNull(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                retry.FinalWorkbookPath));
    }

    [Fact]
    public async Task InstallAsync_SourceChangedAfterHostedCompletionFailsBeforeStampOrInstall()
    {
        var staging = await CreateStagingAsync();
        var hostedResult = CompletedHostedResult(staging.SourceFingerprint);
        using (var archive = ZipFile.Open(staging.OriginalWorkbookPath, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("customXml/source-changed.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("changed after hosted verification");
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreviewWorkbookPostMigrationHandoff().InstallAsync(staging, hostedResult));

        Assert.Contains("changed after hosted migration", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                staging.StagedWorkbookPath));
        Assert.Equal(
            "2.0.3",
            WorkbookPackageValidator.ValidateWorkbookPackage(staging.OriginalWorkbookPath));
        Assert.True(File.Exists(staging.BackupWorkbookPath));
    }

    [Fact]
    public async Task InstallAsync_PendingHostedMigrationFailsBeforeStampOrInstall()
    {
        var staging = await CreateStagingAsync();
        var hostedResult = CompletedHostedResult(staging.SourceFingerprint) with
        {
            Migration = CompletedHostedResult(staging.SourceFingerprint).Migration with
            {
                Status = HostedWorkbookMigrationStatus.Pending,
                CompletedAt = null
            }
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreviewWorkbookPostMigrationHandoff().InstallAsync(staging, hostedResult));

        Assert.Contains("completion has not been exactly verified", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(
                staging.StagedWorkbookPath));
        Assert.Equal(
            "2.0.3",
            WorkbookPackageValidator.ValidateWorkbookPackage(staging.OriginalWorkbookPath));
    }

    [Fact]
    public void WorkbookMigrationStamp_RoundTripsThroughHiddenDefinedNamesAndIsIdempotent()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            TestRepo.Version,
            "stamp-metadata.xlsm");
        var stamp = new WorkbookMigrationStamp(
            PreviewWorkbookPostMigrationHandoff.CompletedStatus,
            CompletedAt,
            new WorkbookMigrationId("mig_stamp"));

        var first = PortableLogbookWorkbookPackageStorage.EnsureWorkbookMigrationStamp(
            workbook,
            stamp);
        var second = PortableLogbookWorkbookPackageStorage.EnsureWorkbookMigrationStamp(
            workbook,
            stamp);

        Assert.True(first.WorkbookMutated);
        Assert.False(second.WorkbookMutated);
        Assert.Equal(stamp, PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp(workbook));
        using var archive = ZipFile.OpenRead(workbook);
        using var stream = archive.GetEntry("xl/workbook.xml")!.Open();
        var document = XDocument.Load(stream);
        var stampNames = document
            .Descendants()
            .Where(element => element.Name.LocalName == "definedName")
            .Where(element => ((string?)element.Attribute("name"))?.StartsWith(
                "FlightLogXMigration",
                StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(3, stampNames.Length);
        Assert.All(
            stampNames,
            name => Assert.Equal("1", (string?)name.Attribute("hidden")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<StagedWorkbookMigration> CreateStagingAsync()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            "2.0.3",
            $"Source_{Guid.NewGuid():N}.xlsm");
        var staged = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            TestRepo.Version,
            $"Staged_{Guid.NewGuid():N}.xlsm");
        var backup = Path.Combine(directory, $"Backup_{Guid.NewGuid():N}.xlsm");
        File.Copy(source, backup);
        var fingerprint = await Integrity.Sha256Async(source, CancellationToken.None);
        return new StagedWorkbookMigration(
            source,
            staged,
            backup,
            fingerprint,
            new MigrationReport(
                source,
                "master.xlsm",
                staged,
                "2.0.3",
                TestRepo.Version,
                1,
                EmptyAirportStats(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                "validated"));
    }

    private static PreviewWorkbookHostedMigrationResult CompletedHostedResult(
        string sourceFingerprint)
    {
        var migrationId = new WorkbookMigrationId("mig_complete");
        var logbookId = new LogbookId("log_complete");
        var deviceId = new DeviceId("dev_complete");
        var receipt = new PortableWorkbookMigrationReceipt(
            sourceFingerprint,
            logbookId,
            deviceId,
            1,
            "entry-values-hash",
            "custom-fields-hash",
            "currency-overrides-hash",
            "document-hash",
            new PortableWorkbookMigrationTotals(1, 1.2m, 0m, 1.2m, 1, 0, 0, 0),
            "receipt-hash");
        var migration = new HostedWorkbookMigration(
            migrationId,
            new HostedAccountId("acct_complete"),
            logbookId,
            deviceId,
            sourceFingerprint,
            HostedWorkbookMigrationStatus.Completed,
            1,
            1,
            1,
            receipt.VerificationReceiptSha256,
            null,
            CompletedAt.AddMinutes(-5),
            CompletedAt,
            CompletedAt,
            null);
        return new PreviewWorkbookHostedMigrationResult(
            migration,
            receipt,
            1,
            ResumedCompletedMigration: false);
    }

    private static AirportVisitStatsDiagnostics EmptyAirportStats() =>
        new(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new Dictionary<string, int>());
}
