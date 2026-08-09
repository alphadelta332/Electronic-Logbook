using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public interface IMobileRecoveryEnvelopeService
{
    ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoverySetupStatus> GetRecoverySetupStatusAsync(
        MobileRecoverySetupStatusRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(
        MobileRecoveryEnvelopeEnrollmentRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(
        MobileRecoveryEnvelopeRestoreRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryCodeEnrollmentResult> EnrollRecoveryCodeAsync(
        MobileRecoveryCodeEnrollmentRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryCodeEnvelopePayload> RestoreWithRecoveryCodeAsync(
        MobileRecoveryCodeRestoreRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryDeviceActivationResult> ActivateAsync(
        MobileRecoveryDeviceActivationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MobileRecoveryEnvelopeConfiguration(
    string PublicKey,
    string Fingerprint,
    string Algorithm,
    string KeyVersionId);

public sealed record MobileRecoverySetupStatusRequest(LogbookId LogbookId, DeviceId DeviceId);

public sealed record MobileRecoverySetupStatus(bool ManagedEnvelopeConfigured, bool RecoveryCodeConfigured);

public sealed record MobileRecoveryDeviceKey(
    string PublicKey,
    string Fingerprint,
    string Algorithm);

public sealed record MobileRecoveryEnvelopeEnrollmentRequest(
    LogbookId LogbookId,
    DeviceId DeviceId,
    MobileRecoveryDeviceKey DeviceKey,
    string WrappedPackageKey,
    string IngressKeyVersionId);

public sealed record MobileRecoveryEnvelopeEnrollmentResult(
    bool Enrolled,
    string KeyVersionId);

public sealed record MobileRecoveryEnvelopeRestoreRequest(
    LogbookId LogbookId,
    DeviceId DeviceId,
    MobileRecoveryDeviceKey DeviceKey,
    string PlatformLabel);

public sealed record MobileRecoveryEnvelopeRestoreResult(
    string WrappedKey,
    string Algorithm,
    string KeyVersionId);

public sealed record MobileRecoveryCodeEnrollmentRequest(
    LogbookId LogbookId,
    DeviceId DeviceId,
    MobileRecoveryCodeEnvelopePayload Envelope);

public sealed record MobileRecoveryCodeEnrollmentResult(bool Enrolled);

public sealed record MobileRecoveryCodeRestoreRequest(
    LogbookId LogbookId,
    DeviceId DeviceId,
    string PlatformLabel,
    MobileRecoveryDeviceKey DeviceKey);

public sealed record MobileRecoveryDeviceActivationRequest(
    LogbookId LogbookId,
    DeviceId DeviceId);

public sealed record MobileRecoveryDeviceActivationResult(bool Activated);
