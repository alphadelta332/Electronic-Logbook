using System.Security.Cryptography;
using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileReplacementRecoveryWorkflowTests
{
    private static readonly LogbookId LogbookId = new("log_recovery");
    private static readonly HostedAccountId AccountId = new("acct_recovery");
    private static readonly DeviceId DeviceId = new("dev_replacement");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T00:00:00Z");

    [Fact]
    public async Task InterruptedLedgerRestoreRetriesWithSameDeviceAndActivatesOnlyAfterDurableCompletion()
    {
        var js = new RecoveryJsRuntime();
        var client = new RecordingRecoveryClient(Session());
        var ledger = new InterruptOnceLedger();
        var workflow = CreateWorkflow(js, client, ledger: ledger);

        var interrupted = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await workflow.RecoverAsync(LogbookId));

        Assert.Equal("RECOVERY_LEDGER_RESTORE_INCOMPLETE", interrupted.ErrorCode);
        Assert.Equal(0, client.CompleteCount);
        var partial = await new BrowserLogbookStore(js).LoadStateV2Async();
        Assert.NotNull(partial?.HostedSync);
        Assert.Equal(DeviceId, partial.HostedSync.DeviceId);
        Assert.Equal(PortableHostedSyncStatus.NeedsAttention, partial.HostedSync.LastStatus);

        var recovered = await workflow.RecoverAsync(LogbookId);

        Assert.Equal(2, client.PrepareCount);
        Assert.All(client.PreparedDeviceIds, id => Assert.Equal(DeviceId, id));
        Assert.Equal(1, client.CompleteCount);
        Assert.Equal(2, ledger.ReadCount);
        Assert.Equal(PortableHostedSyncStatus.Synced, recovered.HostedSync.LastStatus);
        Assert.Equal(DeviceId, recovered.HostedSync.DeviceId);
        Assert.Equal(DeviceId, client.CompletedDeviceId);
    }

    [Fact]
    public async Task ExpiredReplacementCredentialRefreshesBeforeActivation()
    {
        var expired = Session(Now.AddMinutes(-1));
        var refreshed = Session(Now.AddHours(1));
        var authenticator = new RecordingAuthenticator(refreshed);
        var client = new RecordingRecoveryClient(expired);
        var workflow = CreateWorkflow(new RecoveryJsRuntime(), client, authenticator: authenticator);

        var recovered = await workflow.RecoverAsync(LogbookId);

        Assert.Equal(1, authenticator.RefreshCount);
        Assert.Equal(PortableHostedSyncStatus.Synced, recovered.HostedSync.LastStatus);
        Assert.Equal(1, client.CompleteCount);
    }

    [Fact]
    public async Task RevokedDeviceStopsRecoveryBeforeActivationAndPersistsNeedsAttention()
    {
        var js = new RecoveryJsRuntime();
        var authenticator = new RecordingAuthenticator(
            new HostedSignInException(HostedSignInFailureReason.DeviceRevoked, "The replacement device was revoked."));
        var client = new RecordingRecoveryClient(Session(Now.AddMinutes(-1)));
        var workflow = CreateWorkflow(js, client, authenticator: authenticator);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await workflow.RecoverAsync(LogbookId));

        Assert.Equal("RECOVERY_LEDGER_RESTORE_INCOMPLETE", error.ErrorCode);
        Assert.Contains("revoked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.CompleteCount);
        var retained = await new BrowserLogbookStore(js).LoadStateV2Async();
        Assert.Equal(PortableHostedSyncStatus.NeedsAttention, retained?.HostedSync?.LastStatus);
    }

    [Fact]
    public async Task MultipleLogbooksRequireSelectionBeforeDeviceRegistration()
    {
        var client = new RecordingRecoveryClient(Session())
        {
            Memberships = [Membership(LogbookId), Membership(new LogbookId("log_second"))]
        };
        var workflow = CreateWorkflow(new RecoveryJsRuntime(), client);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await workflow.RecoverOnlyLogbookAsync());

        Assert.Equal("RECOVERY_LOGBOOK_SELECTION_REQUIRED", error.ErrorCode);
        Assert.Equal(0, client.PrepareCount);
        Assert.Equal(0, client.CompleteCount);
    }

    [Fact]
    public async Task WrongRecoveryCodePreservesExistingLocalLogbookAndDoesNotActivateDevice()
    {
        var js = new RecoveryJsRuntime { AcceptedRecoveryCode = "correct-code" };
        var store = new BrowserLogbookStore(js);
        var previous = PortableLogbookDocumentV2.CreateAustraliaFirst(
            new LogbookId("log_existing"),
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        await store.SaveStateAsync(new BrowserLogbookStateV2(previous, [], null));
        var client = new RecordingRecoveryClient(Session());
        var workflow = CreateWorkflow(js, client);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await workflow.RecoverOnlyLogbookWithCodeAsync("wrong-code"));

        Assert.Equal("RECOVERY_KEY_IMPORT_FAILED", error.ErrorCode);
        Assert.Equal(0, client.CompleteCount);
        var retained = await store.LoadStateV2Async();
        Assert.Equal(previous.LogbookId, retained?.Document.LogbookId);
        Assert.Null(retained?.HostedSync);
    }

    [Fact]
    public async Task CorrectRecoveryCodeRestoresAndActivatesTheReplacementDevice()
    {
        var js = new RecoveryJsRuntime { AcceptedRecoveryCode = "correct-code" };
        var client = new RecordingRecoveryClient(Session());
        var service = new RecordingRecoveryEnvelopeService();
        var workflow = CreateWorkflow(js, client, recoveryService: service);

        var recovered = await workflow.RecoverOnlyLogbookWithCodeAsync("correct-code");

        Assert.Equal(1, service.RecoveryCodeRestoreCount);
        Assert.Equal(0, service.RestoreCount);
        Assert.Equal(1, client.CompleteCount);
        Assert.Equal(DeviceId, client.CompletedDeviceId);
        Assert.Equal(LogbookId, recovered.Document.LogbookId);
        Assert.Equal(PortableHostedSyncStatus.Synced, recovered.HostedSync.LastStatus);
        var retained = await new BrowserLogbookStore(js).LoadStateV2Async();
        Assert.Equal(LogbookId, retained?.Document.LogbookId);
        Assert.Equal(PortableHostedSyncStatus.Synced, retained?.HostedSync?.LastStatus);
    }

    [Fact]
    public async Task UnavailablePlatformCredentialStopsBeforeRestoreOrLocalMutation()
    {
        var js = new RecoveryJsRuntime { PlatformCredentialAvailable = false };
        var client = new RecordingRecoveryClient(Session());
        var service = new RecordingRecoveryEnvelopeService();
        var workflow = CreateWorkflow(js, client, recoveryService: service);

        var error = await Assert.ThrowsAsync<JSException>(async () =>
            await workflow.RecoverAsync(LogbookId));

        Assert.Contains("unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, service.RestoreCount);
        Assert.Equal(0, client.CompleteCount);
        Assert.Null(await new BrowserLogbookStore(js).LoadStateV2Async());
    }

    [Fact]
    public async Task CompletedWorkbookMigrationRestoresExactEncryptedFlightsBeforeDurableSaveAndActivation()
    {
        var js = new RecoveryJsRuntime();
        var sourceDeviceId = new DeviceId("dev_workbook");
        var clock = new ManualSyncClock(Now);
        var expected = PortableLogbookDocumentV2.CreateAustraliaFirst(
            LogbookId,
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            [
                PortableLogbookOperationV2.Create(
                    LogbookId,
                    new EntryId("ent_first"),
                    new RevisionId("rev_first"),
                    sourceDeviceId,
                    Now.AddMinutes(-2),
                    PortableLogbookWorkbookEntry.Empty with
                    {
                        FlightId = "MIG-001",
                        Year = 2026,
                        Month = 8,
                        Day = 27,
                        Reg = "VH-ABC",
                        From = "YSCN",
                        To = "YMML",
                        SeCommandDay = 1.2m
                    }),
                PortableLogbookOperationV2.Create(
                    LogbookId,
                    new EntryId("ent_second"),
                    new RevisionId("rev_second"),
                    sourceDeviceId,
                    Now.AddMinutes(-1),
                    PortableLogbookWorkbookEntry.Empty with
                    {
                        FlightId = "MIG-002",
                        Year = 2026,
                        Month = 8,
                        Day = 28,
                        Reg = "VH-XYZ",
                        From = "YMML",
                        To = "YSSY",
                        MeDualNight = 2.3m
                    })
            ]);
        var ledger = new InMemoryHostedLogbookLedger();
        var sourceAuthenticator = new RecordingAuthenticator(
            new HostedSyncSession(AccountId, sourceDeviceId, Now.AddHours(1)),
            currentSession: new HostedSyncSession(AccountId, sourceDeviceId, Now.AddHours(1)));
        var seeded = await new MobileHostedSyncWorkflow(
                new BrowserPackageKeyStore(js),
                ledger,
                sourceAuthenticator,
                new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
                clock)
            .SyncAsync(new PortableHostedSyncRequestContext(
                expected,
                new BrowserHostedSyncState(
                    AccountId,
                    LogbookId,
                    sourceDeviceId,
                    0,
                    PortableHostedSyncStatus.Waiting,
                    LedgerCursorVersion: BrowserHostedSyncState.CurrentLedgerCursorVersion),
                BackgroundSyncReason.ManualRefresh));
        Assert.Equal(PortableHostedSyncStatus.Synced, seeded.Status);

        var client = new RecordingRecoveryClient(Session())
        {
            Memberships = [WorkbookMigrationMembership(LogbookId, sourceDeviceId, 2)]
        };
        var service = new RecordingRecoveryEnvelopeService();
        var recovered = await CreateWorkflow(js, client, ledger, recoveryService: service)
            .RecoverOnlyLogbookAsync();

        Assert.Equal(1, service.RestoreCount);
        Assert.Equal(1, client.CompleteCount);
        Assert.Equal(DeviceId, client.CompletedDeviceId);
        Assert.Equal(
            PortableLogbookJson.SerializeV2(expected),
            PortableLogbookJson.SerializeV2(recovered.Document));
        var durable = await new BrowserLogbookStore(js).LoadStateV2Async();
        Assert.Equal(
            PortableLogbookJson.SerializeV2(expected),
            PortableLogbookJson.SerializeV2(Assert.IsType<PortableLogbookDocumentV2>(durable?.Document)));
        Assert.Equal(PortableHostedSyncStatus.Synced, durable?.HostedSync?.LastStatus);
    }

    [Fact]
    public async Task CompletedWorkbookMigrationWithMissingHostedFlightsDoesNotSaveOrActivateAnEmptyLogbook()
    {
        var js = new RecoveryJsRuntime();
        var client = new RecordingRecoveryClient(Session())
        {
            Memberships = [WorkbookMigrationMembership(LogbookId, new DeviceId("dev_workbook"), 1)]
        };
        var service = new RecordingRecoveryEnvelopeService();
        var workflow = CreateWorkflow(js, client, recoveryService: service);

        var error = await Assert.ThrowsAsync<MobileHostedDiagnosticException>(async () =>
            await workflow.RecoverOnlyLogbookAsync());

        Assert.Equal("RECOVERY_MIGRATION_HISTORY_MISMATCH", error.ErrorCode);
        Assert.Contains("not activated", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, service.RestoreCount);
        Assert.Equal(0, client.CompleteCount);
        Assert.Null(await new BrowserLogbookStore(js).LoadStateV2Async());
    }

    private static MobileReplacementRecoveryWorkflow CreateWorkflow(
        RecoveryJsRuntime js,
        RecordingRecoveryClient client,
        IHostedLogbookLedger? ledger = null,
        IHostedLogbookAuthenticator? authenticator = null,
        RecordingRecoveryEnvelopeService? recoveryService = null) =>
        new(
            new BrowserPackageKeyStore(js),
            new BrowserLogbookStore(js),
            client,
            recoveryService ?? new RecordingRecoveryEnvelopeService(),
            ledger ?? new EmptyLedger(),
            authenticator ?? new RecordingAuthenticator(Session()),
            new StaticNetworkStatus(new NetworkAvailability(IsOnline: true)),
            new ManualSyncClock(Now));

    private static HostedSyncSession Session(DateTimeOffset? expiresAt = null) =>
        new(AccountId, DeviceId, expiresAt ?? Now.AddHours(1));

    private static MobileHostedLogbookMembership Membership(LogbookId logbookId) =>
        new(logbookId, "owner", PortableLogbookDocumentV2.CurrentSchemaVersion, 1);

    private static MobileHostedLogbookMembership WorkbookMigrationMembership(
        LogbookId logbookId,
        DeviceId sourceDeviceId,
        int expectedOperationCount) =>
        new(
            logbookId,
            "owner",
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            1,
            new MobileWorkbookMigrationRecoveryExpectation(
                sourceDeviceId,
                expectedOperationCount,
                new string('a', 64),
                new string('b', 64)));

    private sealed class RecordingRecoveryClient(HostedSyncSession session) : IMobileReplacementRecoveryClient
    {
        public IReadOnlyList<MobileHostedLogbookMembership> Memberships { get; set; } = [Membership(LogbookId)];
        public int PrepareCount { get; private set; }
        public int CompleteCount { get; private set; }
        public DeviceId? CompletedDeviceId { get; private set; }
        public List<DeviceId> PreparedDeviceIds { get; } = [];

        public ValueTask<IReadOnlyList<MobileHostedLogbookMembership>> DiscoverActiveLogbooksAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Memberships);

        public ValueTask<MobileReplacementRecoveryContext> PrepareReplacementRecoveryAsync(
            LogbookId logbookId,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            PreparedDeviceIds.Add(session.DeviceId);
            var membership = Memberships.Single(value => value.LogbookId == logbookId);
            return ValueTask.FromResult(new MobileReplacementRecoveryContext(session, membership, "Pixel 8 Pro"));
        }

        public ValueTask CompleteReplacementRecoveryAsync(
            LogbookId logbookId,
            DeviceId deviceId,
            CancellationToken cancellationToken = default)
        {
            CompleteCount++;
            CompletedDeviceId = deviceId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRecoveryEnvelopeService : IMobileRecoveryEnvelopeService
    {
        public int RestoreCount { get; private set; }
        public int RecoveryCodeRestoreCount { get; private set; }

        public ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<MobileRecoverySetupStatus> GetRecoverySetupStatusAsync(MobileRecoverySetupStatusRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(MobileRecoveryEnvelopeEnrollmentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(MobileRecoveryEnvelopeRestoreRequest request, CancellationToken cancellationToken = default)
        {
            RestoreCount++;
            return ValueTask.FromResult(new MobileRecoveryEnvelopeRestoreResult("wrapped-key", "RSA-OAEP-256", "recovery-key-v1"));
        }
        public ValueTask<MobileRecoveryCodeEnrollmentResult> EnrollRecoveryCodeAsync(MobileRecoveryCodeEnrollmentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<MobileRecoveryCodeEnvelopePayload> RestoreWithRecoveryCodeAsync(MobileRecoveryCodeRestoreRequest request, CancellationToken cancellationToken = default)
        {
            RecoveryCodeRestoreCount++;
            return ValueTask.FromResult(new MobileRecoveryCodeEnvelopePayload("ciphertext", "nonce", "salt", MobileRecoveryCodeEnvelope.Algorithm, MobileRecoveryCodeEnvelope.KeyVersionId));
        }
        public ValueTask<MobileRecoveryDeviceActivationResult> ActivateAsync(MobileRecoveryDeviceActivationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuthenticator : IHostedLogbookAuthenticator
    {
        private readonly HostedSyncSession? refreshedSession;
        private readonly HostedSyncSession? currentSession;
        private readonly Exception? refreshError;

        public RecordingAuthenticator(
            HostedSyncSession refreshedSession,
            HostedSyncSession? currentSession = null)
        {
            this.refreshedSession = refreshedSession;
            this.currentSession = currentSession;
        }
        public RecordingAuthenticator(Exception refreshError) => this.refreshError = refreshError;
        public int RefreshCount { get; private set; }

        public ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(currentSession);
        public ValueTask<HostedSignInStart> StartEmailSignInAsync(string email, bool shouldCreateUser = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<HostedSyncSession> CompleteEmailSignInAsync(string verificationCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<HostedSyncSession> ResumeEmailSignInAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return refreshError is null
                ? ValueTask.FromResult(refreshedSession!)
                : ValueTask.FromException<HostedSyncSession>(refreshError);
        }
        public ValueTask SignOutAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private class EmptyLedger : IHostedLogbookLedger
    {
        public virtual ValueTask<HostedOperationPage> ReadMissingOperationsAsync(LogbookId logbookId, long afterHostedRevision, int pageSize, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostedOperationPage([], afterHostedRevision, HasMore: false));
        public ValueTask<HostedAppendResult> AppendOperationsAsync(LogbookId logbookId, DeviceId deviceId, IReadOnlyList<HostedOperationUpload> operations, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostedAppendResult([], 0));
        public ValueTask RecordAcknowledgementAsync(LogbookId logbookId, DeviceId deviceId, long throughHostedRevision, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class InterruptOnceLedger : EmptyLedger
    {
        public int ReadCount { get; private set; }
        public override ValueTask<HostedOperationPage> ReadMissingOperationsAsync(LogbookId logbookId, long afterHostedRevision, int pageSize, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ReadCount == 1
                ? ValueTask.FromException<HostedOperationPage>(new HostedLedgerException(HostedLedgerFailureReason.CheckpointOutsideHostedHistory, "Interrupted hosted read."))
                : base.ReadMissingOperationsAsync(logbookId, afterHostedRevision, pageSize, cancellationToken);
        }
    }

    private sealed class RecoveryJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> storage = [];
        private readonly byte[] packageKey = Enumerable.Repeat((byte)7, 32).ToArray();

        public bool PlatformCredentialAvailable { get; init; } = true;
        public string AcceptedRecoveryCode { get; init; } = "correct-code";

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => Result<TValue>(storage.GetValueOrDefault(Assert.IsType<string>(args![0]))),
                "electronicLogbookStore.save" => Save<TValue>(args),
                "electronicLogbookStore.delete" => Delete<TValue>(args),
                "electronicLogbookKeys.getRecoveryPublicKey" => RecoveryPublicKey<TValue>(),
                "electronicLogbookKeys.importRecoveryEnvelope" => Result<TValue>(true),
                "electronicLogbookKeys.importRecoveryCodeEnvelope" => Result<TValue>(string.Equals(AcceptedRecoveryCode, Assert.IsType<string>(args![1]), StringComparison.Ordinal)),
                "electronicLogbookKeys.hasPackageKey" => Result<TValue>(true),
                "electronicLogbookKeys.encrypt" => Encrypt<TValue>(args),
                "electronicLogbookKeys.decrypt" => Decrypt<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> RecoveryPublicKey<TValue>()
        {
            if (!PlatformCredentialAvailable)
            {
                throw new JSException("Platform credential unavailable.");
            }
            var publicKey = Enumerable.Repeat((byte)4, 294).ToArray();
            return Result<TValue>(new BrowserRecoveryPublicKey(
                Convert.ToBase64String(publicKey),
                Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant(),
                "RSA-OAEP-256"));
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            storage[Assert.IsType<string>(args![0])] = Assert.IsType<string>(args[1]);
            return Result<TValue>(default(TValue));
        }

        private ValueTask<TValue> Delete<TValue>(object?[]? args)
        {
            storage.Remove(Assert.IsType<string>(args![0]));
            return Result<TValue>(default(TValue));
        }

        private ValueTask<TValue> Encrypt<TValue>(object?[]? args)
        {
            var nonce = Assert.IsType<byte[]>(args![1]);
            var plaintext = Assert.IsType<byte[]>(args[2]);
            var additionalData = Assert.IsType<byte[]>(args[3]);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(packageKey, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, additionalData);
            return Result<TValue>(new BrowserPackageCiphertext(ciphertext, tag));
        }

        private ValueTask<TValue> Decrypt<TValue>(object?[]? args)
        {
            var nonce = Assert.IsType<byte[]>(args![1]);
            var ciphertext = Assert.IsType<byte[]>(args[2]);
            var tag = Assert.IsType<byte[]>(args[3]);
            var additionalData = Assert.IsType<byte[]>(args[4]);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(packageKey, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);
            return Result<TValue>(plaintext);
        }

        private static ValueTask<TValue> Result<TValue>(object? value) =>
            new((TValue)value!);
    }
}
