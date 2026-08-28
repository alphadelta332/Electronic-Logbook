using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class StagedWorkbookMigrationRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"StagedWorkbookMigrationRecoveryTests-{Guid.NewGuid():N}");

    public StagedWorkbookMigrationRecoveryTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task RunAfterValidatedStagingAsync_ValidatedArtifacts_PreparesRecoveryBeforeUpload()
    {
        var staging = await CreateStagingAsync();
        var client = new RecordingRecoveryClient();
        var workflow = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));

        var result = await workflow.RunAfterValidatedStagingAsync(
            staging,
            "Migrated Logbook",
            (preparation, _) =>
            {
                Assert.Equal(1, client.PrepareCallCount);
                Assert.Equal(client.Migration.MigrationId, preparation.Migration.MigrationId);
                Assert.Same(client.RecoveryMaterial, preparation.RecoveryMaterial);
                return Task.FromResult("uploaded");
            },
            CancellationToken.None);

        Assert.Equal("uploaded", result);
        Assert.Equal(staging.SourceFingerprint, client.SourceFingerprint);
        Assert.Equal("Migrated Logbook", client.LogbookDisplayName);
        Assert.Throws<ObjectDisposedException>(() =>
            client.RecoveryMaterial.RecoveryKeyPair.ExportPrivateKey());
    }

    [Fact]
    public async Task RunAfterValidatedStagingAsync_UnvalidatedReport_StopsBeforeRecovery()
    {
        var staging = await CreateStagingAsync();
        staging = staging with
        {
            MigrationReport = staging.MigrationReport with { Status = "failed" }
        };
        var client = new RecordingRecoveryClient();
        var workflow = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.RunAfterValidatedStagingAsync(
                staging,
                "Migrated Logbook",
                (_, _) => Task.FromResult("should not run"),
                CancellationToken.None));

        Assert.Contains("validation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.BeginCallCount);
        client.Dispose();
    }

    [Fact]
    public async Task RunAfterValidatedStagingAsync_MissingBackup_StopsBeforeRecovery()
    {
        var staging = await CreateStagingAsync();
        File.Delete(staging.BackupWorkbookPath);
        var client = new RecordingRecoveryClient();
        var workflow = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            workflow.RunAfterValidatedStagingAsync(
                staging,
                "Migrated Logbook",
                (_, _) => Task.FromResult("should not run"),
                CancellationToken.None));

        Assert.Equal(Path.GetFullPath(staging.BackupWorkbookPath), error.FileName);
        Assert.Equal(0, client.BeginCallCount);
        client.Dispose();
    }

    [Fact]
    public async Task RunAfterValidatedStagingAsync_BackupChanged_StopsBeforeRecovery()
    {
        var staging = await CreateStagingAsync();
        await File.AppendAllTextAsync(staging.BackupWorkbookPath, "changed after staging");
        var client = new RecordingRecoveryClient();
        var workflow = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            workflow.RunAfterValidatedStagingAsync(
                staging,
                "Migrated Logbook",
                (_, _) => Task.FromResult("should not run"),
                CancellationToken.None));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.BeginCallCount);
        client.Dispose();
    }

    [Fact]
    public async Task RunAfterValidatedStagingAsync_UploadFails_DisposesScopedRecoveryMaterial()
    {
        var staging = await CreateStagingAsync();
        var client = new RecordingRecoveryClient();
        var workflow = new StagedWorkbookMigrationRecovery(
            new WorkbookMigrationRecoveryCoordinator(client));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.RunAfterValidatedStagingAsync<string>(
                staging,
                "Migrated Logbook",
                (_, _) => throw new InvalidOperationException("upload interrupted"),
                CancellationToken.None));

        Assert.Equal("upload interrupted", error.Message);
        Assert.Throws<ObjectDisposedException>(() =>
            client.RecoveryMaterial.RecoveryKeyPair.ExportPrivateKey());
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
        var originalWorkbook = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            "2.0.3",
            $"Electronic_Logbook_{Guid.NewGuid():N}.xlsm");
        var stagedWorkbook = TestRepo.CreateMinimalWorkbookPackage(
            directory,
            "3.0.0",
            $"Electronic_Logbook_Staged_{Guid.NewGuid():N}.xlsm");
        var backupWorkbook = Path.Combine(
            directory,
            $"Electronic_Logbook_Backup_{Guid.NewGuid():N}.xlsm");
        File.Copy(originalWorkbook, backupWorkbook);
        var fingerprint = await Integrity.Sha256Async(
            originalWorkbook,
            CancellationToken.None);

        return new StagedWorkbookMigration(
            originalWorkbook,
            stagedWorkbook,
            backupWorkbook,
            fingerprint,
            CreateReport(originalWorkbook, stagedWorkbook, "2.0.3", "3.0.0"));
    }

    private static MigrationReport CreateReport(
        string sourceWorkbookPath,
        string stagedWorkbookPath,
        string sourceVersion,
        string outputVersion) =>
        new(
            sourceWorkbookPath,
            "master.xlsm",
            stagedWorkbookPath,
            sourceVersion,
            outputVersion,
            LogbookRows: 0,
            new AirportVisitStatsDiagnostics(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, int>()),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            "validated");

    private sealed class RecordingRecoveryClient : IWorkbookMigrationRecoveryClient, IDisposable
    {
        private HostedWorkbookMigration? migration;

        public int BeginCallCount { get; private set; }
        public int PrepareCallCount { get; private set; }
        public string? SourceFingerprint { get; private set; }
        public string? LogbookDisplayName { get; private set; }
        public HostedWorkbookMigration Migration => migration
            ?? throw new InvalidOperationException("Migration has not started.");
        public PortableWorkbookMigrationRecoveryMaterial RecoveryMaterial { get; private set; } =
            null!;

        public Task<HostedWorkbookMigration> BeginWorkbookMigrationAsync(
            string sourceFingerprint,
            string logbookDisplayName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCallCount++;
            SourceFingerprint = sourceFingerprint;
            LogbookDisplayName = logbookDisplayName;
            migration ??= new HostedWorkbookMigration(
                new WorkbookMigrationId("mig_test"),
                new HostedAccountId("acct_test"),
                new LogbookId("log_test"),
                new DeviceId("dev_test"),
                sourceFingerprint,
                HostedWorkbookMigrationStatus.Pending,
                AttemptCount: 1,
                ExpectedOperationCount: null,
                VerifiedOperationCount: null,
                VerificationReceiptHash: null,
                FailureCode: null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                CompletedAt: null,
                FailedAt: null);
            return Task.FromResult(migration);
        }

        public Task<HostedWorkbookMigration> GetWorkbookMigrationStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Migration);
        }

        public Task<PortableWorkbookMigrationRecoveryMaterial> PrepareAndEnrollWorkbookRecoveryAsync(
            HostedWorkbookMigration hostedMigration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCallCount++;
            RecoveryMaterial = new PortableWorkbookMigrationRecoveryMaterial(
                PortableWorkbookMigrationRecoveryStore.CreateTargetName(
                    hostedMigration.LogbookId,
                    hostedMigration.DeviceId),
                PortableLogbookKey.Generate(),
                PortableWorkbookRecoveryKeyPair.Create(),
                resumed: false);
            return Task.FromResult(RecoveryMaterial);
        }

        public void Dispose()
        {
            RecoveryMaterial?.Dispose();
        }
    }
}
