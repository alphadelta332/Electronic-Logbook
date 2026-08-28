namespace ElectronicLogbook.Portable;

public interface IHostedLogbookLedger
{
    public const int MaxOperationPageSize = 200;
    public const int MaxPayloadCiphertextLength = 262_144;

    ValueTask<HostedOperationPage> ReadMissingOperationsAsync(
        LogbookId logbookId,
        long afterHostedRevision,
        int pageSize,
        CancellationToken cancellationToken = default);

    ValueTask<HostedAppendResult> AppendOperationsAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        IReadOnlyList<HostedOperationUpload> operations,
        CancellationToken cancellationToken = default);

    ValueTask RecordAcknowledgementAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        long throughHostedRevision,
        CancellationToken cancellationToken = default);
}

public interface IHostedPilotHealthReporter
{
    ValueTask<HostedPilotHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default);

    ValueTask<HostedPilotDiagnosticsBundle> CreateRedactedDiagnosticsAsync(
        HostedDiagnosticsRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<HostedPilotLogicalBackup> CreateLogicalBackupAsync(
        HostedLogicalBackupRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<HostedRestorePlan> ValidateRestoreAsync(
        HostedPilotLogicalBackup backup,
        CancellationToken cancellationToken = default);
}

public interface IHostedLogbookAuthenticator
{
    ValueTask<HostedSyncSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    ValueTask<HostedSignInStart> StartEmailSignInAsync(
        string email,
        bool shouldCreateUser = false,
        CancellationToken cancellationToken = default);

    ValueTask<HostedSyncSession> CompleteEmailSignInAsync(
        string verificationCode,
        CancellationToken cancellationToken = default);

    ValueTask<HostedSyncSession> ResumeEmailSignInAsync(CancellationToken cancellationToken = default);

    ValueTask<HostedSyncSession> RefreshAsync(CancellationToken cancellationToken = default);

    ValueTask SignOutAsync(CancellationToken cancellationToken = default);
}

public interface ISyncSecureStorage
{
    ValueTask SaveAsync(SyncSecretName name, byte[] value, CancellationToken cancellationToken = default);

    ValueTask<byte[]?> LoadAsync(SyncSecretName name, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(SyncSecretName name, CancellationToken cancellationToken = default);
}

public interface IBackgroundSyncScheduler
{
    ValueTask ScheduleAsync(BackgroundSyncRequest request, CancellationToken cancellationToken = default);

    ValueTask CancelAsync(LogbookId logbookId, DeviceId deviceId, CancellationToken cancellationToken = default);
}

public interface IWorkbookSyncBridge
{
    ValueTask<WorkbookSyncSnapshot> ReadSnapshotAsync(
        WorkbookSyncRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbookSyncResult> ApplyAsync(
        WorkbookSyncApplyRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISyncClock
{
    DateTimeOffset UtcNow { get; }
}

public interface INetworkStatus
{
    ValueTask<NetworkAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
}

public sealed record HostedOperationUpload(
    RevisionId RevisionId,
    EntryId EntryId,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    string PayloadCiphertext,
    string PayloadNonce,
    string PayloadTag,
    string PayloadHash,
    IReadOnlyList<RevisionId> ParentRevisionIds);

public sealed record HostedOperationEnvelope(
    long HostedRevision,
    RevisionId RevisionId,
    EntryId EntryId,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    string PayloadCiphertext,
    string PayloadNonce,
    string PayloadTag,
    string PayloadHash,
    IReadOnlyList<RevisionId> ParentRevisionIds);

public sealed record HostedOperationPage(
    IReadOnlyList<HostedOperationEnvelope> Operations,
    long ThroughHostedRevision,
    bool HasMore);

public sealed record HostedAppendResult(
    IReadOnlyList<HostedOperationEnvelope> AcceptedOperations,
    long ThroughHostedRevision);

public sealed record HostedSyncSession(
    HostedAccountId AccountId,
    DeviceId DeviceId,
    DateTimeOffset AccessTokenExpiresAt);

public sealed record HostedSignInStart(
    HostedAccountId AccountId,
    string DeliveryHint,
    DateTimeOffset ExpiresAt);

public enum HostedWorkbookMigrationStatus
{
    Pending,
    Completed,
    Failed
}

public sealed record HostedWorkbookMigration(
    WorkbookMigrationId MigrationId,
    HostedAccountId AccountId,
    LogbookId LogbookId,
    DeviceId DeviceId,
    string SourceFingerprint,
    HostedWorkbookMigrationStatus Status,
    int AttemptCount,
    int? ExpectedOperationCount,
    int? VerifiedOperationCount,
    string? VerificationReceiptHash,
    string? FailureCode,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt);

public sealed record HostedAccountId(string Value);

public sealed record SyncSecretName(string Value);

public enum HostedAccountStatus
{
    Invited,
    Active,
    Disabled
}

public enum HostedDeviceStatus
{
    Active,
    Revoked,
    Disabled
}

public enum HostedSignInFailureReason
{
    InvitationRequired,
    AccountRecoveryRequired,
    VerificationExpired,
    InvalidVerificationCode,
    RefreshTokenRevoked,
    AccountDisabled,
    DeviceRevoked,
    SignedOut,
    PublicRegistrationBlocked,
    WorkbookMigrationRequired
}

public sealed class HostedSignInException(
    HostedSignInFailureReason reason,
    string message)
    : Exception(message)
{
    public HostedSignInFailureReason Reason { get; } = reason;
}

public enum HostedLedgerFailureReason
{
    InvalidIdentifier,
    DeviceMismatch,
    UnsupportedSchemaVersion,
    PlaintextPayloadRejected,
    PayloadTooLarge,
    InvalidPayloadEnvelope,
    OperationReplayRejected,
    CheckpointOutsideHostedHistory
}

public sealed class HostedLedgerException(
    HostedLedgerFailureReason reason,
    string message)
    : Exception(message)
{
    public HostedLedgerFailureReason Reason { get; } = reason;
}

public enum HostedPilotQuotaStatus
{
    Ok,
    NearLimit,
    UpgradeRequired
}

public sealed record HostedPilotHealthSnapshot(
    HostedPilotQuotaStatus DatabaseSizeStatus,
    HostedPilotQuotaStatus AuthUserStatus,
    HostedPilotQuotaStatus StorageStatus,
    int ActiveAccountCount,
    int ActiveDeviceCount,
    int StoredOperationCount,
    long EstimatedDatabaseBytes,
    DateTimeOffset CheckedAt,
    IReadOnlyList<string> PaidPlanUpgradeTriggers);

public sealed record HostedDiagnosticsRequest(
    HostedAccountId? AccountId,
    LogbookId? LogbookId,
    bool IncludeCiphertextPayloads = false);

public sealed record HostedPilotDiagnosticsBundle(
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> RedactedConfiguration,
    IReadOnlyList<string> HealthWarnings,
    IReadOnlyList<HostedRedactedSecurityEvent> SecurityEvents,
    bool ContainsCiphertextPayloads);

public sealed record HostedRedactedSecurityEvent(
    DateTimeOffset CreatedAt,
    string EventType,
    string Severity,
    IReadOnlyDictionary<string, string> RedactedDetails);

public sealed record HostedLogicalBackupRequest(
    LogbookId LogbookId,
    bool IncludeCiphertextPayloads);

public sealed record HostedPilotLogicalBackup(
    LogbookId LogbookId,
    DateTimeOffset ExportedAt,
    int AccountCount,
    int DeviceCount,
    int OperationCount,
    bool ContainsCiphertextPayloads);

public sealed record HostedRestorePlan(
    LogbookId LogbookId,
    int OperationCount,
    bool CanRestore,
    IReadOnlyList<string> Warnings);

public sealed record BackgroundSyncRequest(
    LogbookId LogbookId,
    DeviceId DeviceId,
    BackgroundSyncReason Reason,
    DateTimeOffset NotBefore);

public enum BackgroundSyncReason
{
    LocalEdit,
    LaunchOrResume,
    NetworkRestored,
    Retry,
    ManualRefresh
}

public sealed record WorkbookSyncRequest(
    string WorkbookPath,
    LogbookId LogbookId,
    DeviceId DeviceId);

public sealed record WorkbookSyncSnapshot(
    string WorkbookPath,
    LogbookId LogbookId,
    DeviceId DeviceId,
    IReadOnlyList<RevisionId> LocalRevisionIds,
    long LastAcknowledgedHostedRevision,
    bool IsEditable);

public sealed record WorkbookSyncApplyRequest(
    WorkbookSyncSnapshot Snapshot,
    IReadOnlyList<HostedOperationEnvelope> RemoteOperations,
    long ThroughHostedRevision);

public sealed record WorkbookSyncResult(
    string WorkbookPath,
    long LastAcknowledgedHostedRevision,
    bool Applied,
    string? AttentionRequiredReason = null);

public sealed record NetworkAvailability(
    bool IsOnline,
    bool IsMetered = false);

public sealed class SystemSyncClock : ISyncClock
{
    public static SystemSyncClock Instance { get; } = new();

    private SystemSyncClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
