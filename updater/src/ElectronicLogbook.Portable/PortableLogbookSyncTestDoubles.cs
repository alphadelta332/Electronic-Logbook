namespace ElectronicLogbook.Portable;

public sealed class ManualSyncClock(DateTimeOffset utcNow) : ISyncClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public void Advance(TimeSpan value) => UtcNow += value;

    public void Set(DateTimeOffset utcNow) => UtcNow = utcNow;
}

public sealed class StaticNetworkStatus(NetworkAvailability availability) : INetworkStatus
{
    public NetworkAvailability Availability { get; set; } = availability;

    public ValueTask<NetworkAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Availability);
}

public sealed class InMemorySyncSecureStorage : ISyncSecureStorage
{
    private readonly Dictionary<SyncSecretName, byte[]> values = [];

    public ValueTask SaveAsync(SyncSecretName name, byte[] value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name.Value);
        ArgumentNullException.ThrowIfNull(value);

        values[name] = value.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> LoadAsync(SyncSecretName name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name.Value);
        return ValueTask.FromResult(values.TryGetValue(name, out var value) ? value.ToArray() : null);
    }

    public ValueTask<bool> DeleteAsync(SyncSecretName name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name.Value);
        return ValueTask.FromResult(values.Remove(name));
    }
}

public sealed class InMemoryHostedLogbookLedger : IHostedLogbookLedger
{
    private readonly Dictionary<LogbookId, List<HostedOperationEnvelope>> operationsByLogbook = [];
    private readonly Dictionary<(LogbookId LogbookId, DeviceId DeviceId), long> acknowledgements = [];

    public IReadOnlyDictionary<(LogbookId LogbookId, DeviceId DeviceId), long> Acknowledgements => acknowledgements;

    public ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
        LogbookId logbookId,
        long afterHostedRevision,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        if (afterHostedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterHostedRevision), "Hosted revision cursor cannot be negative.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");
        }

        IEnumerable<HostedOperationEnvelope> operations = operationsByLogbook.TryGetValue(logbookId, out var logbookOperations)
            ? logbookOperations
            : Array.Empty<HostedOperationEnvelope>();
        var page = operations
            .Where(operation => operation.HostedRevision > afterHostedRevision)
            .OrderBy(operation => operation.HostedRevision)
            .Take(Math.Min(pageSize, IHostedLogbookLedger.MaxOperationPageSize))
            .ToArray();
        var throughRevision = page.Length > 0 ? page[^1].HostedRevision : afterHostedRevision;
        var hasMore = operations.Any(operation => operation.HostedRevision > throughRevision);
        return ValueTask.FromResult(new HostedOperationPage(page, throughRevision, hasMore));
    }

    public ValueTask<HostedAppendResult> AppendOperationsAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        IReadOnlyList<HostedOperationUpload> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId.Value);
        ArgumentNullException.ThrowIfNull(operations);

        if (!operationsByLogbook.TryGetValue(logbookId, out var logbookOperations))
        {
            logbookOperations = [];
            operationsByLogbook.Add(logbookId, logbookOperations);
        }

        var accepted = new List<HostedOperationEnvelope>();
        foreach (var upload in operations)
        {
            ValidateUpload(logbookId, deviceId, upload);

            if (!string.Equals(upload.DeviceId.Value, deviceId.Value, StringComparison.Ordinal))
            {
                throw new HostedLedgerException(
                    HostedLedgerFailureReason.DeviceMismatch,
                    "Uploaded operation device does not match the authenticated device.");
            }

            var existing = logbookOperations.SingleOrDefault(operation => operation.RevisionId == upload.RevisionId);
            if (existing is not null)
            {
                if (!OperationPayloadMatches(existing, upload))
                {
                    throw new HostedLedgerException(
                        HostedLedgerFailureReason.OperationReplayRejected,
                        "Operation revision was replayed with different encrypted payload metadata.");
                }

                accepted.Add(existing);
                continue;
            }

            var hostedRevision = logbookOperations.Count == 0
                ? 1
                : logbookOperations[^1].HostedRevision + 1;
            var envelope = new HostedOperationEnvelope(
                hostedRevision,
                upload.RevisionId,
                upload.EntryId,
                upload.DeviceId,
                upload.CreatedAt,
                upload.SchemaVersion,
                upload.PayloadCiphertext,
                upload.PayloadNonce,
                upload.PayloadTag,
                upload.PayloadHash,
                upload.ParentRevisionIds.ToArray());
            logbookOperations.Add(envelope);
            accepted.Add(envelope);
        }

        var throughRevision = logbookOperations.Count == 0 ? 0 : logbookOperations[^1].HostedRevision;
        return ValueTask.FromResult(new HostedAppendResult(accepted, throughRevision));
    }

    public ValueTask RecordAcknowledgementAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        long throughHostedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId.Value);
        if (throughHostedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(throughHostedRevision), "Hosted revision cursor cannot be negative.");
        }

        var hostedHighest = operationsByLogbook.TryGetValue(logbookId, out var operations) && operations.Count > 0
            ? operations[^1].HostedRevision
            : 0;
        if (throughHostedRevision > hostedHighest)
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.CheckpointOutsideHostedHistory,
                "Acknowledgement revision is outside hosted history.");
        }

        var key = (logbookId, deviceId);
        acknowledgements[key] = acknowledgements.TryGetValue(key, out var current)
            ? Math.Max(current, throughHostedRevision)
            : throughHostedRevision;
        return ValueTask.CompletedTask;
    }

    private static void ValidateUpload(LogbookId logbookId, DeviceId deviceId, HostedOperationUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        if (string.IsNullOrWhiteSpace(logbookId.Value)
            || string.IsNullOrWhiteSpace(deviceId.Value)
            || string.IsNullOrWhiteSpace(upload.RevisionId.Value)
            || string.IsNullOrWhiteSpace(upload.EntryId.Value)
            || string.IsNullOrWhiteSpace(upload.DeviceId.Value))
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.InvalidIdentifier,
                "Hosted operation identifiers must be present.");
        }

        if (upload.SchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.UnsupportedSchemaVersion,
                "Hosted operation schema version is not supported.");
        }

        if (upload.PayloadCiphertext.Length > IHostedLogbookLedger.MaxPayloadCiphertextLength)
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.PayloadTooLarge,
                "Hosted operation encrypted payload is too large for the Preview boundary.");
        }

        if (LooksLikePlaintextPayload(upload.PayloadCiphertext))
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.PlaintextPayloadRejected,
                "Hosted operation payload must be encrypted before upload.");
        }

        if (upload.PayloadCiphertext.Length < 16
            || upload.PayloadNonce.Length < 12
            || upload.PayloadTag.Length < 16
            || upload.PayloadHash.Length != 64
            || !upload.PayloadHash.All(IsLowerHex))
        {
            throw new HostedLedgerException(
                HostedLedgerFailureReason.InvalidPayloadEnvelope,
                "Hosted operation encrypted payload metadata is incomplete.");
        }
    }

    private static bool OperationPayloadMatches(HostedOperationEnvelope existing, HostedOperationUpload upload) =>
        string.Equals(existing.PayloadCiphertext, upload.PayloadCiphertext, StringComparison.Ordinal)
        && string.Equals(existing.PayloadNonce, upload.PayloadNonce, StringComparison.Ordinal)
        && string.Equals(existing.PayloadTag, upload.PayloadTag, StringComparison.Ordinal)
        && string.Equals(existing.PayloadHash, upload.PayloadHash, StringComparison.Ordinal);

    private static bool LooksLikePlaintextPayload(string payload)
    {
        var trimmed = payload.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        return trimmed.Contains("\"kind\"", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("\"entry\"", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("\"aircraft", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("\"route\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

public sealed class InMemoryHostedPilotHealthReporter(
    IHostedLogbookLedger ledger,
    ISyncClock clock)
    : IHostedPilotHealthReporter
{
    public HostedPilotHealthSnapshot Snapshot { get; set; } = new(
        HostedPilotQuotaStatus.Ok,
        HostedPilotQuotaStatus.Ok,
        HostedPilotQuotaStatus.Ok,
        ActiveAccountCount: 1,
        ActiveDeviceCount: 1,
        StoredOperationCount: 0,
        EstimatedDatabaseBytes: 0,
        DateTimeOffset.UnixEpoch,
        PaidPlanUpgradeTriggers: []);

    public IReadOnlyList<HostedRedactedSecurityEvent> SecurityEvents { get; set; } = [];

    public ValueTask<HostedPilotHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Snapshot with { CheckedAt = clock.UtcNow });

    public ValueTask<HostedPilotDiagnosticsBundle> CreateRedactedDiagnosticsAsync(
        HostedDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(new HostedPilotDiagnosticsBundle(
            clock.UtcNow,
            new Dictionary<string, string>
            {
                ["supabase_url"] = "[redacted]",
                ["anon_key"] = "[redacted]",
                ["account_id"] = request.AccountId is null ? "[none]" : "[redacted]",
                ["logbook_id"] = request.LogbookId is null ? "[none]" : "[redacted]"
            },
            Snapshot.PaidPlanUpgradeTriggers,
            SecurityEvents,
            ContainsCiphertextPayloads: request.IncludeCiphertextPayloads));
    }

    public ValueTask<HostedPilotLogicalBackup> CreateLogicalBackupAsync(
        HostedLogicalBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationCount = 0;
        if (ledger is InMemoryHostedLogbookLedger inMemoryLedger)
        {
            operationCount = inMemoryLedger.ReadMissingOperationsAsync(
                    request.LogbookId,
                    afterHostedRevision: 0,
                    pageSize: IHostedLogbookLedger.MaxOperationPageSize,
                    cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult()
                .Operations
                .Count;
        }

        return ValueTask.FromResult(new HostedPilotLogicalBackup(
            request.LogbookId,
            clock.UtcNow,
            AccountCount: Snapshot.ActiveAccountCount,
            DeviceCount: Snapshot.ActiveDeviceCount,
            OperationCount: operationCount,
            ContainsCiphertextPayloads: request.IncludeCiphertextPayloads));
    }

    public ValueTask<HostedRestorePlan> ValidateRestoreAsync(
        HostedPilotLogicalBackup backup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var warnings = backup.ContainsCiphertextPayloads
            ? Array.Empty<string>()
            : ["Backup omits ciphertext payloads and can validate metadata only."];
        return ValueTask.FromResult(new HostedRestorePlan(
            backup.LogbookId,
            backup.OperationCount,
            CanRestore: backup.ContainsCiphertextPayloads,
            warnings));
    }
}

public sealed class InMemoryHostedLogbookAuthenticator(
    HostedAccountId accountId,
    DeviceId deviceId,
    ISyncClock clock,
    string invitedEmail = "preview@example.com")
    : IHostedLogbookAuthenticator
{
    private HostedSyncSession? currentSession;
    private HostedSignInStart? pendingSignIn;
    private string? pendingVerificationCode;
    private bool refreshTokenRevoked;

    public HostedAccountStatus AccountStatus { get; set; } = HostedAccountStatus.Invited;

    public HostedDeviceStatus DeviceStatus { get; set; } = HostedDeviceStatus.Active;

    public ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(currentSession);

    public ValueTask<HostedSignInStart> StartEmailSignInAsync(
        string email,
        bool shouldCreateUser = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (shouldCreateUser)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.PublicRegistrationBlocked,
                "Public account registration is disabled during the invitation-only Preview.");
        }

        ThrowIfAccountOrDeviceBlocked();

        if (!string.Equals(email.Trim(), invitedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.InvitationRequired,
                "Sign-in is available only for invited Preview accounts.");
        }

        pendingSignIn = new HostedSignInStart(accountId, MaskEmail(email), clock.UtcNow.AddMinutes(10));
        pendingVerificationCode = "123456";
        return ValueTask.FromResult(pendingSignIn);
    }

    public ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCode);
        ThrowIfAccountOrDeviceBlocked();

        if (pendingSignIn is null || pendingVerificationCode is null)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.VerificationExpired,
                "No pending sign-in request is available.");
        }

        if (pendingSignIn.ExpiresAt <= clock.UtcNow)
        {
            pendingSignIn = null;
            pendingVerificationCode = null;
            throw new HostedSignInException(
                HostedSignInFailureReason.VerificationExpired,
                "The sign-in verification code has expired.");
        }

        if (!string.Equals(verificationCode, pendingVerificationCode, StringComparison.Ordinal))
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.InvalidVerificationCode,
                "The sign-in verification code is not valid.");
        }

        currentSession = new HostedSyncSession(accountId, deviceId, clock.UtcNow.AddHours(1));
        pendingSignIn = null;
        pendingVerificationCode = null;
        refreshTokenRevoked = false;
        AccountStatus = HostedAccountStatus.Active;
        return ValueTask.FromResult(currentSession);
    }

    public ValueTask<HostedSyncSession> ResumeEmailSignInAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfAccountOrDeviceBlocked();
        return currentSession is null
            ? ValueTask.FromException<HostedSyncSession>(new HostedSignInException(
                HostedSignInFailureReason.SignedOut,
                "No verified sign-in is available to resume."))
            : ValueTask.FromResult(currentSession);
    }

    public ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (currentSession is null)
        {
            throw new HostedSignInException(
                HostedSignInFailureReason.SignedOut,
                "Cannot refresh before sign-in.");
        }

        ThrowIfAccountOrDeviceBlocked();

        if (refreshTokenRevoked)
        {
            currentSession = null;
            throw new HostedSignInException(
                HostedSignInFailureReason.RefreshTokenRevoked,
                "The hosted refresh token has been revoked.");
        }

        currentSession = currentSession with { AccessTokenExpiresAt = clock.UtcNow.AddHours(1) };
        return ValueTask.FromResult(currentSession);
    }

    public ValueTask SignOutAsync(CancellationToken cancellationToken = default)
    {
        currentSession = null;
        pendingSignIn = null;
        pendingVerificationCode = null;
        refreshTokenRevoked = true;
        return ValueTask.CompletedTask;
    }

    public void RevokeRefreshToken() => refreshTokenRevoked = true;

    private void ThrowIfAccountOrDeviceBlocked()
    {
        if (AccountStatus is HostedAccountStatus.Disabled)
        {
            currentSession = null;
            pendingSignIn = null;
            pendingVerificationCode = null;
            throw new HostedSignInException(
                HostedSignInFailureReason.AccountDisabled,
                "The hosted account is disabled.");
        }

        if (DeviceStatus is HostedDeviceStatus.Revoked or HostedDeviceStatus.Disabled)
        {
            currentSession = null;
            pendingSignIn = null;
            pendingVerificationCode = null;
            throw new HostedSignInException(
                HostedSignInFailureReason.DeviceRevoked,
                "The hosted device is no longer allowed to sync.");
        }
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" : $"{email[0]}***{email[at..]}";
    }
}

public sealed class RecordingBackgroundSyncScheduler : IBackgroundSyncScheduler
{
    private readonly List<BackgroundSyncRequest> scheduled = [];
    private readonly List<(LogbookId LogbookId, DeviceId DeviceId)> cancelled = [];

    public IReadOnlyList<BackgroundSyncRequest> Scheduled => scheduled;

    public IReadOnlyList<(LogbookId LogbookId, DeviceId DeviceId)> Cancelled => cancelled;

    public ValueTask ScheduleAsync(BackgroundSyncRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        scheduled.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask CancelAsync(LogbookId logbookId, DeviceId deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId.Value);
        cancelled.Add((logbookId, deviceId));
        return ValueTask.CompletedTask;
    }
}

public sealed class RecordingWorkbookSyncBridge(WorkbookSyncSnapshot snapshot) : IWorkbookSyncBridge
{
    private readonly List<WorkbookSyncApplyRequest> applyRequests = [];

    public IReadOnlyList<WorkbookSyncApplyRequest> ApplyRequests => applyRequests;

    public ValueTask<WorkbookSyncSnapshot> ReadSnapshotAsync(
        WorkbookSyncRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(snapshot with
        {
            WorkbookPath = request.WorkbookPath,
            LogbookId = request.LogbookId,
            DeviceId = request.DeviceId
        });

    public ValueTask<WorkbookSyncResult> ApplyAsync(
        WorkbookSyncApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        applyRequests.Add(request);
        return ValueTask.FromResult(new WorkbookSyncResult(
            request.Snapshot.WorkbookPath,
            request.ThroughHostedRevision,
            Applied: request.Snapshot.IsEditable,
            AttentionRequiredReason: request.Snapshot.IsEditable ? null : "Workbook is not editable."));
    }
}
