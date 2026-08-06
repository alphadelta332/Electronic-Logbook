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
            .Take(pageSize)
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
            if (!string.Equals(upload.DeviceId.Value, deviceId.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Uploaded operation device does not match the authenticated device.");
            }

            var existing = logbookOperations.SingleOrDefault(operation => operation.RevisionId == upload.RevisionId);
            if (existing is not null)
            {
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

        acknowledgements[(logbookId, deviceId)] = throughHostedRevision;
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryHostedLogbookAuthenticator(
    HostedAccountId accountId,
    DeviceId deviceId,
    ISyncClock clock)
    : IHostedLogbookAuthenticator
{
    private HostedSyncSession? currentSession;
    private HostedSignInStart? pendingSignIn;

    public ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(currentSession);

    public ValueTask<HostedSignInStart> StartEmailSignInAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        pendingSignIn = new HostedSignInStart(accountId, MaskEmail(email), clock.UtcNow.AddMinutes(10));
        return ValueTask.FromResult(pendingSignIn);
    }

    public ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCode);
        if (pendingSignIn is null || pendingSignIn.ExpiresAt <= clock.UtcNow)
        {
            throw new InvalidOperationException("No pending sign-in request is available.");
        }

        currentSession = new HostedSyncSession(accountId, deviceId, clock.UtcNow.AddHours(1));
        pendingSignIn = null;
        return ValueTask.FromResult(currentSession);
    }

    public ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (currentSession is null)
        {
            throw new InvalidOperationException("Cannot refresh before sign-in.");
        }

        currentSession = currentSession with { AccessTokenExpiresAt = clock.UtcNow.AddHours(1) };
        return ValueTask.FromResult(currentSession);
    }

    public ValueTask SignOutAsync(CancellationToken cancellationToken = default)
    {
        currentSession = null;
        pendingSignIn = null;
        return ValueTask.CompletedTask;
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
