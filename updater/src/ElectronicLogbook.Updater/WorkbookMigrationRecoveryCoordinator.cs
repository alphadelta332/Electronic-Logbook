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

internal interface IWorkbookMigrationHostedClient :
    IWorkbookMigrationRecoveryClient,
    IWorkbookMigrationConfigurationClient
{
    Task<HostedWorkbookMigration> CompleteWorkbookMigrationAsync(
        WorkbookMigrationId migrationId,
        int expectedOperationCount,
        string verificationReceiptHash,
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

public sealed class WorkbookMigrationRecoveryState : IDisposable
{
    private PortableWorkbookMigrationRecoveryMaterial? recoveryMaterial;

    internal WorkbookMigrationRecoveryState(
        HostedWorkbookMigration migration,
        PortableWorkbookMigrationRecoveryMaterial? recoveryMaterial)
    {
        Migration = migration;
        this.recoveryMaterial = recoveryMaterial;
    }

    public HostedWorkbookMigration Migration { get; }

    public PortableWorkbookMigrationRecoveryMaterial? RecoveryMaterial => recoveryMaterial;

    public bool IsAlreadyCompleted =>
        Migration.Status == HostedWorkbookMigrationStatus.Completed;

    internal PortableWorkbookMigrationRecoveryMaterial TakeRecoveryMaterial()
    {
        var material = recoveryMaterial
            ?? throw new InvalidOperationException(
                "The spreadsheet migration returned no temporary recovery keys.");
        recoveryMaterial = null;
        return material;
    }

    public void Dispose() => recoveryMaterial?.Dispose();
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
        using var state = await BeginOrResumeAsync(
            sourceFingerprint,
            logbookDisplayName,
            cancellationToken);
        if (state.IsAlreadyCompleted)
        {
            throw new InvalidOperationException(
                "The spreadsheet migration is already complete and no longer needs temporary recovery keys.");
        }

        var recoveryMaterial = state.TakeRecoveryMaterial();
        return new WorkbookMigrationRecoveryPreparation(
            state.Migration,
            recoveryMaterial);
    }

    public async Task<WorkbookMigrationRecoveryState> BeginOrResumeAsync(
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
        if (status.Status == HostedWorkbookMigrationStatus.Completed)
        {
            PortableWorkbookMigrationRecoveryStore.Delete(
                PortableWorkbookMigrationRecoveryStore.CreateTargetName(
                    status.LogbookId,
                    status.DeviceId));
            return new WorkbookMigrationRecoveryState(status, recoveryMaterial: null);
        }
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

        return new WorkbookMigrationRecoveryState(status, recoveryMaterial);
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
