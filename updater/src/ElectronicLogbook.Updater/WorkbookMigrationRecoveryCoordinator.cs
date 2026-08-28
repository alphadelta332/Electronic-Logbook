using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

internal interface IWorkbookMigrationRecoveryClient
{
    Task<HostedWorkbookMigration> BeginWorkbookMigrationAsync(
        string sourceFingerprint,
        string logbookDisplayName,
        CancellationToken cancellationToken = default);

    Task<HostedWorkbookMigration> GetWorkbookMigrationStatusAsync(
        CancellationToken cancellationToken = default);

    Task<PortableWorkbookMigrationRecoveryMaterial> PrepareAndEnrollWorkbookRecoveryAsync(
        HostedWorkbookMigration migration,
        CancellationToken cancellationToken = default);
}

public sealed class WorkbookMigrationRecoveryPreparation : IDisposable
{
    internal WorkbookMigrationRecoveryPreparation(
        HostedWorkbookMigration migration,
        PortableWorkbookMigrationRecoveryMaterial recoveryMaterial)
    {
        Migration = migration;
        RecoveryMaterial = recoveryMaterial;
    }

    public HostedWorkbookMigration Migration { get; }

    public PortableWorkbookMigrationRecoveryMaterial RecoveryMaterial { get; }

    public void Dispose() => RecoveryMaterial.Dispose();
}

public sealed class WorkbookMigrationRecoveryCoordinator
{
    private readonly IWorkbookMigrationRecoveryClient client;

    public WorkbookMigrationRecoveryCoordinator(SupabaseWorkbookConnectionClient client)
        : this((IWorkbookMigrationRecoveryClient)client)
    {
    }

    internal WorkbookMigrationRecoveryCoordinator(IWorkbookMigrationRecoveryClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    public async Task<WorkbookMigrationRecoveryPreparation> PrepareAsync(
        string sourceFingerprint,
        string logbookDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookDisplayName);

        var begun = await client.BeginWorkbookMigrationAsync(
            sourceFingerprint,
            logbookDisplayName,
            cancellationToken);
        var status = await client.GetWorkbookMigrationStatusAsync(cancellationToken);
        EnsureSameHostedMigration(begun, status, sourceFingerprint);
        if (status.Status != HostedWorkbookMigrationStatus.Pending)
        {
            throw new InvalidOperationException(
                "Recovery can be prepared only while the spreadsheet migration is pending.");
        }

        var recoveryMaterial = await client.PrepareAndEnrollWorkbookRecoveryAsync(
            status,
            cancellationToken);
        var expectedCredentialTarget = PortableWorkbookMigrationRecoveryStore.CreateTargetName(
            status.LogbookId,
            status.DeviceId);
        if (!string.Equals(
                recoveryMaterial.CredentialTargetName,
                expectedCredentialTarget,
                StringComparison.Ordinal))
        {
            recoveryMaterial.Dispose();
            throw new InvalidDataException(
                "Temporary recovery keys do not belong to the confirmed spreadsheet migration.");
        }

        return new WorkbookMigrationRecoveryPreparation(status, recoveryMaterial);
    }

    private static void EnsureSameHostedMigration(
        HostedWorkbookMigration begun,
        HostedWorkbookMigration status,
        string sourceFingerprint)
    {
        if (begun.MigrationId != status.MigrationId ||
            begun.AccountId != status.AccountId ||
            begun.LogbookId != status.LogbookId ||
            begun.DeviceId != status.DeviceId ||
            !string.Equals(begun.SourceFingerprint, status.SourceFingerprint, StringComparison.Ordinal) ||
            !string.Equals(status.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The hosted spreadsheet migration changed identity between begin and status confirmation.");
        }
    }
}
