using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public interface IMobileRecoveryEnvelopeService
{
    ValueTask<MobileRecoveryEnvelopeConfiguration> GetConfigurationAsync(
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryEnvelopeEnrollmentResult> EnrollAsync(
        MobileRecoveryEnvelopeEnrollmentRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MobileRecoveryEnvelopeRestoreResult> RestoreAsync(
        MobileRecoveryEnvelopeRestoreRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MobileRecoveryEnvelopeConfiguration(
    string PublicKey,
    string Fingerprint,
    string Algorithm,
    string KeyVersionId);

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
    MobileRecoveryDeviceKey DeviceKey);

public sealed record MobileRecoveryEnvelopeRestoreResult(
    string WrappedKey,
    string Algorithm,
    string KeyVersionId);
