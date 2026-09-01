using System.Security.Cryptography;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PreviewWorkbookHostedMigrationTests : IDisposable
{
    public enum HostedInterruptionPoint
    {
        Begin,
        Status,
        PrepareRecovery,
        AppendOperations,
        AppendConfiguration,
        ReadOperations,
        ReadConfiguration,
        Complete
    }

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PreviewWorkbookHostedMigrationTests-{Guid.NewGuid():N}");

    public PreviewWorkbookHostedMigrationTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task RunAsync_PreparesRecoveryUploadsReadsBackExactlyThenCompletes()
    {
        var staging = await CreateStagingAsync();
        var events = new List<string>();
        using var client = new RecordingHostedClient(events);
        var ledger = new RecordingLedger(events);
        var session = Session(client.AccountId);
        var workflow = new PreviewWorkbookHostedMigration(
            client,
            session,
            _ => ledger,
            (_, migration) => CreatePayload(migration));

        var result = await workflow.RunAsync(staging, "FlightLogX Logbook");

        Assert.Equal(HostedWorkbookMigrationStatus.Completed, result.Migration.Status);
        Assert.Equal(1, result.VerifiedFlightCount);
        Assert.False(result.ResumedCompletedMigration);
        Assert.Equal(1, client.PrepareCallCount);
        Assert.Equal(1, client.CompleteCallCount);
        Assert.True(events.IndexOf("prepare-recovery") < events.IndexOf("append-operation"));
        Assert.True(events.IndexOf("read-configuration") < events.IndexOf("complete"));
        Assert.Throws<ObjectDisposedException>(() =>
            client.LastRecoveryMaterial!.RecoveryKeyPair.ExportPrivateKey());
    }

    [Fact]
    public async Task RunAsync_ExactReadbackMismatchDoesNotCompleteAndRetryReusesMigrationAndKeys()
    {
        var staging = await CreateStagingAsync();
        var events = new List<string>();
        using var client = new RecordingHostedClient(events);
        var firstLedger = new RecordingLedger(events) { AddUnexpectedReadbackOperation = true };
        var firstWorkflow = new PreviewWorkbookHostedMigration(
            client,
            Session(client.AccountId),
            _ => firstLedger,
            (_, migration) => CreatePayload(migration));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            firstWorkflow.RunAsync(staging, "FlightLogX Logbook"));
        var firstMigration = client.Migration;
        var firstKey = client.LastRecoveryMaterial!.LogbookKey;

        Assert.Contains("operation set", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.CompleteCallCount);

        var retryLedger = new RecordingLedger(events);
        var retryWorkflow = new PreviewWorkbookHostedMigration(
            client,
            Session(client.AccountId),
            _ => retryLedger,
            (_, migration) => CreatePayload(migration));
        var retry = await retryWorkflow.RunAsync(staging, "FlightLogX Logbook");

        Assert.Equal(firstMigration.MigrationId, retry.Migration.MigrationId);
        Assert.Equal(firstKey, client.LastRecoveryMaterial!.LogbookKey);
        Assert.True(client.LastRecoveryMaterial.Resumed);
        Assert.Equal(1, client.CompleteCallCount);
    }

    [Theory]
    [InlineData(HostedInterruptionPoint.Begin)]
    [InlineData(HostedInterruptionPoint.Status)]
    [InlineData(HostedInterruptionPoint.PrepareRecovery)]
    [InlineData(HostedInterruptionPoint.AppendOperations)]
    [InlineData(HostedInterruptionPoint.AppendConfiguration)]
    [InlineData(HostedInterruptionPoint.ReadOperations)]
    [InlineData(HostedInterruptionPoint.ReadConfiguration)]
    [InlineData(HostedInterruptionPoint.Complete)]
    public async Task RunAsync_LostNetworkResponseAtEveryHostedBoundary_RetryReusesMigrationKeysAndData(
        HostedInterruptionPoint interruptionPoint)
    {
        var staging = await CreateStagingAsync();
        var events = new List<string>();
        var interruption = new OneShotNetworkInterruption(interruptionPoint);
        using var client = new RecordingHostedClient(events, interruption);
        var ledger = new RecordingLedger(events, interruption);
        var firstWorkflow = new PreviewWorkbookHostedMigration(
            client,
            Session(client.AccountId),
            _ => ledger,
            (_, migration) => CreatePayload(migration));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            firstWorkflow.RunAsync(staging, "FlightLogX Logbook"));
        var firstMigration = client.Migration;

        Assert.True(interruption.HasInterrupted);
        Assert.True(File.Exists(staging.OriginalWorkbookPath));
        Assert.True(File.Exists(staging.BackupWorkbookPath));

        var retryWorkflow = new PreviewWorkbookHostedMigration(
            client,
            Session(client.AccountId),
            _ => ledger,
            (_, migration) => CreatePayload(migration));
        var retry = await retryWorkflow.RunAsync(staging, "FlightLogX Logbook");

        Assert.Equal(firstMigration.MigrationId, retry.Migration.MigrationId);
        Assert.Equal(HostedWorkbookMigrationStatus.Completed, retry.Migration.Status);
        Assert.Equal(1, client.CompleteCallCount);
        Assert.Equal(1, ledger.StoredOperationCount);
        Assert.Equal(1, client.StoredConfigurationCount);
        var expectedRecoveryPreparations = interruptionPoint is
            HostedInterruptionPoint.Begin or
            HostedInterruptionPoint.Status or
            HostedInterruptionPoint.Complete
                ? 1
                : 2;
        Assert.Equal(expectedRecoveryPreparations, client.PrepareCallCount);
        Assert.Equal(expectedRecoveryPreparations, client.ObservedLogbookKeys.Count);
        Assert.Equal(expectedRecoveryPreparations, client.ObservedRecoveryFingerprints.Count);
        Assert.Single(client.ObservedLogbookKeys.Distinct());
        Assert.Single(client.ObservedRecoveryFingerprints.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunAsync_CompletedRetryVerifiesStoredReceiptWithoutRecoveryOrUpload()
    {
        var staging = await CreateStagingAsync();
        var events = new List<string>();
        using var client = new RecordingHostedClient(events);
        var ledgerCreateCount = 0;
        var workflow = new PreviewWorkbookHostedMigration(
            client,
            Session(client.AccountId),
            _ =>
            {
                ledgerCreateCount++;
                return new RecordingLedger(events);
            },
            (_, migration) => CreatePayload(migration));

        var first = await workflow.RunAsync(staging, "FlightLogX Logbook");
        var retry = await workflow.RunAsync(staging, "FlightLogX Logbook");

        Assert.Equal(first.VerifiedReceipt, retry.VerifiedReceipt);
        Assert.True(retry.ResumedCompletedMigration);
        Assert.Equal(1, ledgerCreateCount);
        Assert.Equal(1, client.PrepareCallCount);
        Assert.Equal(1, client.CompleteCallCount);
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
            "3.0.0",
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
                "3.0.0",
                1,
                EmptyAirportStats(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                "validated"));
    }

    private static WorkbookMigrationPayload CreatePayload(HostedWorkbookMigration migration) =>
        WorkbookMigrationPayloadConverter.ConvertRows(
            [new PortableLogbookWorkbookRowV2(
                null,
                null,
                PortableLogbookWorkbookEntry.Empty with
                {
                    Year = 2026,
                    Month = 8,
                    Day = 29,
                    Type = "C172",
                    Reg = "VH-ABC",
                    From = "YSBK",
                    To = "YSCN",
                    SeCommandDay = 1.2m,
                    LandingsDay = 1
                })],
            [],
            PortableLogbookCurrencyOverrideDates.Empty,
            migration);

    private static SupabaseWorkbookSession Session(HostedAccountId accountId) =>
        new(
            accountId,
            new PortableHostedCredential(
                "access-token",
                "refresh-token",
                DateTimeOffset.UtcNow.AddHours(1)),
            "preview@example.com");

    private static AirportVisitStatsDiagnostics EmptyAirportStats() =>
        new(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new Dictionary<string, int>());

    private sealed class RecordingHostedClient : IWorkbookMigrationHostedClient, IDisposable
    {
        private readonly List<string> events;
        private readonly OneShotNetworkInterruption? interruption;
        private readonly PortableLogbookKey retainedKey = PortableLogbookKey.Generate();
        private readonly byte[] retainedRecoveryPrivateKey;
        private HostedConfigurationRevisionEnvelope? configuration;

        public RecordingHostedClient(
            List<string> events,
            OneShotNetworkInterruption? interruption = null)
        {
            this.events = events;
            this.interruption = interruption;
            using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();
            retainedRecoveryPrivateKey = recoveryKeyPair.ExportPrivateKey();
        }

        public HostedAccountId AccountId { get; } = new("acct_test");
        public HostedWorkbookMigration Migration { get; private set; } = null!;
        public PortableWorkbookMigrationRecoveryMaterial? LastRecoveryMaterial { get; private set; }
        public int PrepareCallCount { get; private set; }
        public int CompleteCallCount { get; private set; }
        public int StoredConfigurationCount => configuration is null ? 0 : 1;
        public List<PortableLogbookKey> ObservedLogbookKeys { get; } = [];
        public List<string> ObservedRecoveryFingerprints { get; } = [];

        public Task<HostedWorkbookMigration> BeginWorkbookMigrationAsync(
            string sourceFingerprint,
            string logbookDisplayName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("begin");
            Migration ??= new HostedWorkbookMigration(
                new WorkbookMigrationId("mig_test"),
                AccountId,
                new LogbookId("log_test"),
                new DeviceId("dev_test"),
                sourceFingerprint,
                HostedWorkbookMigrationStatus.Pending,
                1,
                null,
                null,
                null,
                null,
                DateTimeOffset.Parse("2026-08-29T01:02:03Z"),
                DateTimeOffset.Parse("2026-08-29T01:02:03Z"),
                null,
                null);
            interruption?.ThrowAfter(HostedInterruptionPoint.Begin);
            return Task.FromResult(Migration);
        }

        public Task<HostedWorkbookMigration> GetWorkbookMigrationStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("status");
            interruption?.ThrowAfter(HostedInterruptionPoint.Status);
            return Task.FromResult(Migration);
        }

        public Task<PortableWorkbookMigrationRecoveryMaterial> PrepareAndEnrollWorkbookRecoveryAsync(
            HostedWorkbookMigration migration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("prepare-recovery");
            PrepareCallCount++;
            LastRecoveryMaterial = new PortableWorkbookMigrationRecoveryMaterial(
                PortableWorkbookMigrationRecoveryStore.CreateTargetName(
                    migration.LogbookId,
                    migration.DeviceId),
                retainedKey,
                PortableWorkbookRecoveryKeyPair.Import(retainedRecoveryPrivateKey),
                resumed: PrepareCallCount > 1);
            ObservedLogbookKeys.Add(LastRecoveryMaterial.LogbookKey);
            ObservedRecoveryFingerprints.Add(LastRecoveryMaterial.RecoveryKeyPair.Fingerprint);
            try
            {
                interruption?.ThrowAfter(HostedInterruptionPoint.PrepareRecovery);
            }
            catch
            {
                LastRecoveryMaterial.Dispose();
                throw;
            }
            return Task.FromResult(LastRecoveryMaterial);
        }

        public Task<HostedWorkbookMigration> CompleteWorkbookMigrationAsync(
            WorkbookMigrationId migrationId,
            int expectedOperationCount,
            string verificationReceiptHash,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("complete");
            CompleteCallCount++;
            Migration = Migration with
            {
                Status = HostedWorkbookMigrationStatus.Completed,
                ExpectedOperationCount = expectedOperationCount,
                VerifiedOperationCount = expectedOperationCount,
                VerificationReceiptHash = verificationReceiptHash,
                CompletedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            interruption?.ThrowAfter(HostedInterruptionPoint.Complete);
            return Task.FromResult(Migration);
        }

        public Task<HostedConfigurationRevisionEnvelope> AppendWorkbookConfigurationRevisionAsync(
            HostedWorkbookMigration migration,
            HostedConfigurationRevisionUpload revision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("append-configuration");
            configuration ??= new HostedConfigurationRevisionEnvelope(
                1,
                revision.RevisionId,
                revision.DeviceId,
                revision.CreatedAt,
                revision.SchemaVersion,
                revision.PayloadCiphertext,
                revision.PayloadNonce,
                revision.PayloadTag,
                revision.PayloadHash);
            interruption?.ThrowAfter(HostedInterruptionPoint.AppendConfiguration);
            return Task.FromResult(configuration);
        }

        public Task<HostedConfigurationRevisionPage> ReadWorkbookConfigurationRevisionsAsync(
            HostedWorkbookMigration migration,
            long afterHostedRevision = 0,
            int pageSize = IHostedLogbookLedger.MaxOperationPageSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("read-configuration");
            var page = new HostedConfigurationRevisionPage(
                configuration is null ? [] : [configuration],
                configuration?.HostedRevision ?? afterHostedRevision,
                false);
            interruption?.ThrowAfter(HostedInterruptionPoint.ReadConfiguration);
            return Task.FromResult(page);
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(retainedRecoveryPrivateKey);
    }

    private sealed class RecordingLedger : IHostedLogbookLedger
    {
        private readonly InMemoryHostedLogbookLedger inner = new();
        private readonly List<string> events;
        private readonly OneShotNetworkInterruption? interruption;
        private readonly HashSet<RevisionId> storedOperationIds = [];

        public RecordingLedger(
            List<string> events,
            OneShotNetworkInterruption? interruption = null)
        {
            this.events = events;
            this.interruption = interruption;
        }

        public bool AddUnexpectedReadbackOperation { get; init; }
        public int StoredOperationCount => storedOperationIds.Count;

        public async ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations,
            CancellationToken cancellationToken = default)
        {
            events.Add("append-operation");
            var result = await inner.AppendOperationsAsync(
                logbookId,
                deviceId,
                operations,
                cancellationToken);
            storedOperationIds.UnionWith(operations.Select(operation => operation.RevisionId));
            interruption?.ThrowAfter(HostedInterruptionPoint.AppendOperations);
            return result;
        }

        public async ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            events.Add("read-operation");
            var page = await inner.ReadMissingOperationsAsync(
                logbookId,
                afterHostedRevision,
                pageSize,
                cancellationToken);
            interruption?.ThrowAfter(HostedInterruptionPoint.ReadOperations);
            if (!AddUnexpectedReadbackOperation || page.Operations.Count == 0)
            {
                return page;
            }

            var unexpected = page.Operations[0] with
            {
                HostedRevision = page.ThroughHostedRevision + 1,
                RevisionId = new RevisionId("rev_unexpected")
            };
            return new HostedOperationPage(
                [.. page.Operations, unexpected],
                unexpected.HostedRevision,
                false);
        }

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class OneShotNetworkInterruption(HostedInterruptionPoint point)
    {
        public bool HasInterrupted { get; private set; }

        public void ThrowAfter(HostedInterruptionPoint currentPoint)
        {
            if (HasInterrupted || currentPoint != point)
            {
                return;
            }

            HasInterrupted = true;
            throw new HttpRequestException(
                $"Hosted connection interrupted after {currentPoint} completed remotely.");
        }
    }
}
