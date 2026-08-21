using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookSyncContractsTests
{
    [Fact]
    public async Task PortableHostedSyncUploadsLocalOperationsPullsRemoteOperationsAndAcknowledgesCheckpoint()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var androidDeviceId = new DeviceId("dev_android");
        var workbookDeviceId = new DeviceId("dev_workbook");
        var key = PortableLogbookKey.Generate();
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            androidDeviceId,
            clock);
        await authenticator.StartEmailSignInAsync("pilot@example.com");
        await authenticator.CompleteEmailSignInAsync("123456");

        var remoteOperation = CreateWorkbookOperation(logbookId, "ent_remote", "rev_remote", workbookDeviceId);
        await ledger.AppendOperationsAsync(
            logbookId,
            workbookDeviceId,
            [HostedOperationCipher.Encrypt(remoteOperation, key)]);

        var localOperation = CreateWorkbookOperation(logbookId, "ent_local", "rev_local", androidDeviceId);
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [localOperation]);
        var sync = new PortableHostedLogbookSync(
            ledger,
            authenticator,
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
            clock);

        var result = await sync.SyncAsync(new PortableHostedSyncRequest(document, key, LastAcknowledgedHostedRevision: 0));
        var idempotentRetry = await sync.SyncAsync(new PortableHostedSyncRequest(
            result.Document,
            key,
            result.LastAcknowledgedHostedRevision));

        Assert.Equal(PortableHostedSyncStatus.Synced, result.Status);
        Assert.Equal(2, result.LastAcknowledgedHostedRevision);
        Assert.Equal(1, result.UploadedOperationCount);
        Assert.Equal(1, result.DownloadedOperationCount);
        Assert.Contains(result.Document.Operations, operation => operation.RevisionId == localOperation.RevisionId);
        Assert.Contains(result.Document.Operations, operation => operation.RevisionId == remoteOperation.RevisionId);
        Assert.Equal(2, ledger.Acknowledgements[(logbookId, androidDeviceId)]);
        var hostedOperations = await ledger.ReadMissingOperationsAsync(logbookId, afterHostedRevision: 0, pageSize: 10);
        Assert.Equal(PortableHostedSyncStatus.Synced, idempotentRetry.Status);
        Assert.Equal(2, hostedOperations.Operations.Count);
        Assert.DoesNotContain("\"entry\"", hostedOperations.Operations[0].PayloadCiphertext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortableHostedSyncReportsOfflineAndSigningInWithoutUploadingLocalWrites()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [CreateWorkbookOperation(logbookId, "ent_local", "rev_local", new DeviceId("dev_android"))]);
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);

        var offline = await new PortableHostedLogbookSync(
                ledger,
                authenticator,
                new StaticNetworkStatus(new NetworkAvailability(IsOnline: false)),
                clock)
            .SyncAsync(new PortableHostedSyncRequest(document, PortableLogbookKey.Generate(), LastAcknowledgedHostedRevision: 0));
        var signingIn = await new PortableHostedLogbookSync(
                ledger,
                authenticator,
                new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
                clock)
            .SyncAsync(new PortableHostedSyncRequest(document, PortableLogbookKey.Generate(), LastAcknowledgedHostedRevision: 0));

        Assert.Equal(PortableHostedSyncStatus.Offline, offline.Status);
        Assert.Equal(1, offline.PendingLocalOperationCount);
        Assert.Equal(PortableHostedSyncStatus.SigningIn, signingIn.Status);
        Assert.Equal(1, signingIn.PendingLocalOperationCount);
        var hostedOperations = await ledger.ReadMissingOperationsAsync(logbookId, afterHostedRevision: 0, pageSize: 10);
        Assert.Empty(hostedOperations.Operations);
    }

    [Fact]
    public async Task PendingWorkbookRecoversHostedHistoryBeforeActivationAndThenUploadsRealisticLocalRows()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-21T10:00:00Z"));
        var ledger = new ActivationGuardedHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var androidDeviceId = new DeviceId("dev_android");
        var workbookDeviceId = new DeviceId("dev_workbook");
        var key = PortableLogbookKey.Generate();
        var remoteOperations = Enumerable.Range(1, 23)
            .Select(index => HostedOperationCipher.Encrypt(
                CreateWorkbookOperation(
                    logbookId,
                    $"ent_remote_{index:D3}",
                    $"rev_remote_{index:D3}",
                    androidDeviceId),
                key))
            .ToArray();
        await ledger.SeedAsync(logbookId, androidDeviceId, remoteOperations);
        ledger.IsActive = false;

        var localOperations = Enumerable.Range(1, 321)
            .Select(index => CreateWorkbookOperation(
                logbookId,
                $"ent_local_{index:D3}",
                $"rev_local_{index:D3}",
                workbookDeviceId))
            .ToArray();
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            localOperations);
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            workbookDeviceId,
            clock);
        await authenticator.StartEmailSignInAsync("pilot@example.com");
        await authenticator.CompleteEmailSignInAsync("123456");
        var sync = new PortableHostedLogbookSync(
            ledger,
            authenticator,
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
            clock);

        var recovery = await sync.SyncAsync(new PortableHostedSyncRequest(
            document,
            key,
            LastAcknowledgedHostedRevision: 0,
            UploadLocalOperations: false));

        Assert.Equal(PortableHostedSyncStatus.Synced, recovery.Status);
        Assert.Equal(23, recovery.LastAcknowledgedHostedRevision);
        Assert.Equal(0, recovery.UploadedOperationCount);
        Assert.Equal(23, recovery.DownloadedOperationCount);
        Assert.Equal(321, recovery.PendingLocalOperationCount);
        Assert.Equal(0, ledger.AppendAttemptCount);
        Assert.Equal(23, ledger.Acknowledgements[(logbookId, workbookDeviceId)]);

        ledger.IsActive = true;
        var upload = await sync.SyncAsync(new PortableHostedSyncRequest(
            recovery.Document,
            key,
            recovery.LastAcknowledgedHostedRevision));

        Assert.Equal(PortableHostedSyncStatus.Synced, upload.Status);
        Assert.Equal(321, upload.UploadedOperationCount);
        Assert.Equal(344, upload.Document.Operations.Count);
        Assert.Equal(344, upload.LastAcknowledgedHostedRevision);
        Assert.Equal(344, ledger.Acknowledgements[(logbookId, workbookDeviceId)]);
        Assert.Equal(1, ledger.AppendAttemptCount);
    }

    [Fact]
    public void HostedOperationCipherRejectsWrongKeysAndTamperedCiphertext()
    {
        var operation = CreateWorkbookOperation(new LogbookId("log_sync"), "ent_001", "rev_001", new DeviceId("dev_android"));
        var key = PortableLogbookKey.Generate();
        var upload = HostedOperationCipher.Encrypt(operation, key);
        var envelope = new HostedOperationEnvelope(
            HostedRevision: 1,
            upload.RevisionId,
            upload.EntryId,
            upload.DeviceId,
            upload.CreatedAt,
            upload.SchemaVersion,
            upload.PayloadCiphertext,
            upload.PayloadNonce,
            upload.PayloadTag,
            upload.PayloadHash,
            upload.ParentRevisionIds);

        var wrongKeyError = Assert.Throws<HostedOperationCipherException>(() =>
            HostedOperationCipher.Decrypt(envelope, PortableLogbookKey.Generate()));
        var tamperedHashError = Assert.Throws<HostedOperationCipherException>(() =>
            HostedOperationCipher.Decrypt(envelope with { PayloadHash = repeat('f', 64) }, key));

        Assert.Contains("authentication failed", wrongKeyError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hash", tamperedHashError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostedOperationCipherProducesStableEnvelopeForIdempotentRetries()
    {
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var operation = CreateWorkbookOperation(logbookId, "ent_001", "rev_001", deviceId);
        var otherRevision = CreateWorkbookOperation(logbookId, "ent_002", "rev_002", deviceId);
        var changedPayload = CreateWorkbookOperation(logbookId, "ent_changed", "rev_001", deviceId);
        var key = PortableLogbookKey.Generate();

        var first = HostedOperationCipher.Encrypt(operation, key);
        var retry = HostedOperationCipher.Encrypt(operation, key);
        var other = HostedOperationCipher.Encrypt(otherRevision, key);
        var changed = HostedOperationCipher.Encrypt(changedPayload, key);

        Assert.Equal(first.PayloadNonce, retry.PayloadNonce);
        Assert.Equal(first.PayloadCiphertext, retry.PayloadCiphertext);
        Assert.Equal(first.PayloadTag, retry.PayloadTag);
        Assert.Equal(first.PayloadHash, retry.PayloadHash);
        Assert.NotEqual(first.PayloadNonce, other.PayloadNonce);
        Assert.NotEqual(first.PayloadNonce, changed.PayloadNonce);
    }

    [Fact]
    public async Task PortableHostedSyncReportsNeedsAttentionForRevokedHostedDevice()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            deviceId,
            clock);
        await authenticator.StartEmailSignInAsync("pilot@example.com");
        await authenticator.CompleteEmailSignInAsync("123456");
        authenticator.DeviceStatus = HostedDeviceStatus.Revoked;
        clock.Advance(TimeSpan.FromHours(1));
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            logbookId,
            [new CustomFieldDefinition(new CustomFieldId("cf_workbook_1"), "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [CreateWorkbookOperation(logbookId, "ent_local", "rev_local", deviceId)]);

        var result = await new PortableHostedLogbookSync(
                new InMemoryHostedLogbookLedger(),
                authenticator,
                new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
                clock)
            .SyncAsync(new PortableHostedSyncRequest(document, PortableLogbookKey.Generate(), LastAcknowledgedHostedRevision: 0));

        Assert.Equal(PortableHostedSyncStatus.NeedsAttention, result.Status);
        Assert.Equal(1, result.PendingLocalOperationCount);
        Assert.Contains("device", result.AttentionRequiredReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InMemoryLedgerAppendsPagesAndAcknowledgesEncryptedOperations()
    {
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var upload = new HostedOperationUpload(
            new RevisionId("rev_001"),
            new EntryId("ent_001"),
            deviceId,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            PayloadCiphertext: repeat('a', 32),
            PayloadNonce: repeat('b', 16),
            PayloadTag: repeat('c', 32),
            PayloadHash: repeat('0', 64),
            ParentRevisionIds: []);

        var appended = await ledger.AppendOperationsAsync(logbookId, deviceId, [upload]);
        var idempotentRetry = await ledger.AppendOperationsAsync(logbookId, deviceId, [upload]);
        var firstPage = await ledger.ReadMissingOperationsAsync(logbookId, afterHostedRevision: 0, pageSize: 1);
        await ledger.RecordAcknowledgementAsync(logbookId, deviceId, firstPage.ThroughHostedRevision);
        await ledger.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision: 0);

        Assert.Equal(1, appended.ThroughHostedRevision);
        Assert.Equal(1, idempotentRetry.ThroughHostedRevision);
        var envelope = Assert.Single(firstPage.Operations);
        Assert.Equal(1, envelope.HostedRevision);
        Assert.Equal(repeat('a', 32), envelope.PayloadCiphertext);
        Assert.Equal(firstPage.ThroughHostedRevision, ledger.Acknowledgements[(logbookId, deviceId)]);
    }

    [Fact]
    public async Task InMemoryLedgerRejectsPlaintextReplayDeviceMismatchAndImpossibleCheckpoints()
    {
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var upload = CreateUpload("rev_001", deviceId);

        await ledger.AppendOperationsAsync(logbookId, deviceId, [upload]);
        var replayError = await Assert.ThrowsAsync<HostedLedgerException>(async () =>
            await ledger.AppendOperationsAsync(logbookId, deviceId, [upload with { PayloadHash = repeat('1', 64) }]));
        var plaintextError = await Assert.ThrowsAsync<HostedLedgerException>(async () =>
            await ledger.AppendOperationsAsync(logbookId, deviceId, [CreateUpload("rev_002", deviceId) with { PayloadCiphertext = "{\"entry\":\"plaintext\"}" }]));
        var mismatchError = await Assert.ThrowsAsync<HostedLedgerException>(async () =>
            await ledger.AppendOperationsAsync(logbookId, deviceId, [CreateUpload("rev_003", new DeviceId("dev_other"))]));
        var checkpointError = await Assert.ThrowsAsync<HostedLedgerException>(async () =>
            await ledger.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision: 2));

        Assert.Equal(HostedLedgerFailureReason.OperationReplayRejected, replayError.Reason);
        Assert.Equal(HostedLedgerFailureReason.PlaintextPayloadRejected, plaintextError.Reason);
        Assert.Equal(HostedLedgerFailureReason.DeviceMismatch, mismatchError.Reason);
        Assert.Equal(HostedLedgerFailureReason.CheckpointOutsideHostedHistory, checkpointError.Reason);
    }

    [Fact]
    public async Task InMemoryLedgerClampsMissingOperationPagesToPilotBound()
    {
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var uploads = Enumerable.Range(1, IHostedLogbookLedger.MaxOperationPageSize + 1)
            .Select(index => CreateUpload($"rev_{index:D3}", deviceId))
            .ToArray();

        await ledger.AppendOperationsAsync(logbookId, deviceId, uploads);
        var page = await ledger.ReadMissingOperationsAsync(
            logbookId,
            afterHostedRevision: 0,
            pageSize: IHostedLogbookLedger.MaxOperationPageSize + 100);

        Assert.Equal(IHostedLogbookLedger.MaxOperationPageSize, page.Operations.Count);
        Assert.Equal(IHostedLogbookLedger.MaxOperationPageSize, page.ThroughHostedRevision);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task HealthDiagnosticsAndLogicalBackupAreRedactedByDefault()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var reporter = new InMemoryHostedPilotHealthReporter(ledger, clock)
        {
            Snapshot = new HostedPilotHealthSnapshot(
                HostedPilotQuotaStatus.NearLimit,
                HostedPilotQuotaStatus.Ok,
                HostedPilotQuotaStatus.Ok,
                ActiveAccountCount: 3,
                ActiveDeviceCount: 4,
                StoredOperationCount: 1,
                EstimatedDatabaseBytes: 420_000_000,
                CheckedAt: DateTimeOffset.UnixEpoch,
                PaidPlanUpgradeTriggers: ["database near free-tier limit"])
        };

        await ledger.AppendOperationsAsync(logbookId, new DeviceId("dev_android"), [CreateUpload("rev_001", new DeviceId("dev_android"))]);
        var health = await reporter.GetHealthAsync();
        var diagnostics = await reporter.CreateRedactedDiagnosticsAsync(new HostedDiagnosticsRequest(
            new HostedAccountId("acct_private"),
            logbookId));
        var backup = await reporter.CreateLogicalBackupAsync(new HostedLogicalBackupRequest(
            logbookId,
            IncludeCiphertextPayloads: true));
        var restorePlan = await reporter.ValidateRestoreAsync(backup);

        Assert.Equal(HostedPilotQuotaStatus.NearLimit, health.DatabaseSizeStatus);
        Assert.Equal("[redacted]", diagnostics.RedactedConfiguration["supabase_url"]);
        Assert.Equal("[redacted]", diagnostics.RedactedConfiguration["account_id"]);
        Assert.False(diagnostics.ContainsCiphertextPayloads);
        Assert.Equal(1, backup.OperationCount);
        Assert.True(backup.ContainsCiphertextPayloads);
        Assert.True(restorePlan.CanRestore);
    }

    [Fact]
    public async Task PlatformTestDoublesCoverAuthStorageSchedulerNetworkClockAndWorkbookBridge()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var storage = new InMemorySyncSecureStorage();
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var scheduler = new RecordingBackgroundSyncScheduler();
        var bridge = new RecordingWorkbookSyncBridge(new WorkbookSyncSnapshot(
            "placeholder.xlsm",
            new LogbookId("log_sync"),
            new DeviceId("dev_workbook"),
            LocalRevisionIds: [],
            LastAcknowledgedHostedRevision: 0,
            IsEditable: true));

        await storage.SaveAsync(new SyncSecretName("refresh-token"), new byte[] { 1, 2, 3 });
        var signInStart = await authenticator.StartEmailSignInAsync("pilot@example.com");
        var session = await authenticator.CompleteEmailSignInAsync("123456");
        await scheduler.ScheduleAsync(new BackgroundSyncRequest(
            new LogbookId("log_sync"),
            session.DeviceId,
            BackgroundSyncReason.LocalEdit,
            clock.UtcNow));
        var snapshot = await bridge.ReadSnapshotAsync(new WorkbookSyncRequest(
            "Electronic_Logbook_Master.xlsm",
            new LogbookId("log_sync"),
            new DeviceId("dev_workbook")));
        var applyResult = await bridge.ApplyAsync(new WorkbookSyncApplyRequest(snapshot, RemoteOperations: [], ThroughHostedRevision: 4));

        Assert.Equal("p***@example.com", signInStart.DeliveryHint);
        Assert.True((await network.GetAvailabilityAsync()).IsOnline);
        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.LoadAsync(new SyncSecretName("refresh-token")));
        Assert.Equal(clock.UtcNow.AddHours(1), session.AccessTokenExpiresAt);
        Assert.Single(scheduler.Scheduled);
        Assert.Equal("Electronic_Logbook_Master.xlsm", snapshot.WorkbookPath);
        Assert.True(applyResult.Applied);
        Assert.Equal(4, applyResult.LastAcknowledgedHostedRevision);
    }

    [Fact]
    public async Task InMemoryAuthenticatorBlocksPublicRegistrationAndUnknownEmails()
    {
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z")));

        var createUserError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.StartEmailSignInAsync("new@example.com", shouldCreateUser: true));
        var unknownEmailError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.StartEmailSignInAsync("unknown@example.com"));

        Assert.Equal(HostedSignInFailureReason.PublicRegistrationBlocked, createUserError.Reason);
        Assert.Equal(HostedSignInFailureReason.InvitationRequired, unknownEmailError.Reason);
        Assert.Null(await authenticator.GetCurrentSessionAsync());
    }

    [Fact]
    public async Task InMemoryAuthenticatorRefreshSignOutAndRevocationAreExplicit()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);

        await authenticator.StartEmailSignInAsync("pilot@example.com");
        var session = await authenticator.CompleteEmailSignInAsync("123456");
        clock.Advance(TimeSpan.FromMinutes(30));
        var refreshed = await authenticator.RefreshAsync();
        authenticator.RevokeRefreshToken();
        var revokedError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.RefreshAsync());
        await authenticator.StartEmailSignInAsync("pilot@example.com");
        await authenticator.CompleteEmailSignInAsync("123456");
        await authenticator.SignOutAsync();
        var signedOutError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.RefreshAsync());

        Assert.Equal(session.AccessTokenExpiresAt.AddMinutes(30), refreshed.AccessTokenExpiresAt);
        Assert.Equal(HostedSignInFailureReason.RefreshTokenRevoked, revokedError.Reason);
        Assert.Equal(HostedSignInFailureReason.SignedOut, signedOutError.Reason);
        Assert.Null(await authenticator.GetCurrentSessionAsync());
    }

    [Fact]
    public async Task InMemoryAuthenticatorRejectsExpiredCodesDisabledAccountsAndRevokedDevices()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);

        await authenticator.StartEmailSignInAsync("pilot@example.com");
        clock.Advance(TimeSpan.FromMinutes(11));
        var expiredError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.CompleteEmailSignInAsync("123456"));

        authenticator.AccountStatus = HostedAccountStatus.Disabled;
        var disabledError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.StartEmailSignInAsync("pilot@example.com"));

        authenticator.AccountStatus = HostedAccountStatus.Invited;
        authenticator.DeviceStatus = HostedDeviceStatus.Revoked;
        var revokedDeviceError = await Assert.ThrowsAsync<HostedSignInException>(async () =>
            await authenticator.StartEmailSignInAsync("pilot@example.com"));

        Assert.Equal(HostedSignInFailureReason.VerificationExpired, expiredError.Reason);
        Assert.Equal(HostedSignInFailureReason.AccountDisabled, disabledError.Reason);
        Assert.Equal(HostedSignInFailureReason.DeviceRevoked, revokedDeviceError.Reason);
    }

    private static HostedOperationUpload CreateUpload(string revisionId, DeviceId deviceId) =>
        new(
            new RevisionId(revisionId),
            new EntryId($"ent_{revisionId}"),
            deviceId,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            PayloadCiphertext: repeat('a', 32),
            PayloadNonce: repeat('b', 16),
            PayloadTag: repeat('c', 32),
            PayloadHash: repeat('0', 64),
            ParentRevisionIds: []);

    private static PortableLogbookOperationV2 CreateWorkbookOperation(
        LogbookId logbookId,
        string entryId,
        string revisionId,
        DeviceId deviceId)
    {
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 8,
            Day = 6,
            Type = "DA40",
            Reg = "VH-ELB",
            From = "YSBK",
            To = "YSCN",
            SeCommandDay = 1.1m,
            LandingsDay = 1
        };

        return PortableLogbookOperationV2.Create(
            logbookId,
            new EntryId(entryId),
            new RevisionId(revisionId),
            deviceId,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            entry);
    }

    private static string repeat(char value, int count) => new(value, count);

    private sealed class ActivationGuardedHostedLogbookLedger : IHostedLogbookLedger
    {
        private readonly InMemoryHostedLogbookLedger inner = new();

        public bool IsActive { get; set; } = true;

        public int AppendAttemptCount { get; private set; }

        public IReadOnlyDictionary<(LogbookId LogbookId, DeviceId DeviceId), long> Acknowledgements =>
            inner.Acknowledgements;

        public ValueTask<HostedAppendResult> SeedAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations) =>
            inner.AppendOperationsAsync(logbookId, deviceId, operations);

        public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
            LogbookId logbookId,
            long afterHostedRevision,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.ReadMissingOperationsAsync(logbookId, afterHostedRevision, pageSize, cancellationToken);

        public ValueTask<HostedAppendResult> AppendOperationsAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            IReadOnlyList<HostedOperationUpload> operations,
            CancellationToken cancellationToken = default)
        {
            AppendAttemptCount++;
            if (!IsActive)
            {
                throw new HostedLedgerException(
                    HostedLedgerFailureReason.DeviceMismatch,
                    "Device is not active for current account.");
            }

            return inner.AppendOperationsAsync(logbookId, deviceId, operations, cancellationToken);
        }

        public ValueTask RecordAcknowledgementAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            long throughHostedRevision,
            CancellationToken cancellationToken = default) =>
            inner.RecordAcknowledgementAsync(logbookId, deviceId, throughHostedRevision, cancellationToken);
    }
}
