using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public interface IMobileReplacementRecoveryClient
{
    ValueTask<IReadOnlyList<MobileHostedLogbookMembership>> DiscoverActiveLogbooksAsync(
        CancellationToken cancellationToken = default);

    ValueTask<MobileReplacementRecoveryContext> PrepareReplacementRecoveryAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default);

    ValueTask CompleteReplacementRecoveryAsync(
        LogbookId logbookId,
        DeviceId deviceId,
        CancellationToken cancellationToken = default);
}

public interface IMobileReplacementRecoveryWorkflow
{
    ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookAsync(
        CancellationToken cancellationToken = default);

    ValueTask<MobileReplacementRecoveryResult> RecoverOnlyLogbookWithCodeAsync(
        string recoveryCode,
        CancellationToken cancellationToken = default);

    ValueTask<MobileReplacementRecoveryResult> RecoverAsync(
        LogbookId logbookId,
        CancellationToken cancellationToken = default);
}

public sealed record MobileReplacementRecoveryContext(
    HostedSyncSession Session,
    MobileHostedLogbookMembership Membership,
    string PlatformLabel);

public sealed record MobileReplacementRecoveryResult(
    PortableLogbookDocumentV2 Document,
    BrowserHostedSyncState HostedSync);
