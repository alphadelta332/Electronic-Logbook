namespace ElectronicLogbook.Updater;

public sealed class StagedWorkbookMigrationRecovery
{
    private readonly WorkbookMigrationRecoveryCoordinator recoveryCoordinator;

    public StagedWorkbookMigrationRecovery(
        WorkbookMigrationRecoveryCoordinator recoveryCoordinator)
    {
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        this.recoveryCoordinator = recoveryCoordinator;
    }

    public async Task<TResult> RunAfterValidatedStagingAsync<TResult>(
        StagedWorkbookMigration staging,
        string logbookDisplayName,
        Func<WorkbookMigrationRecoveryPreparation, CancellationToken, Task<TResult>> continueWithEncryptedUpload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookDisplayName);
        ArgumentNullException.ThrowIfNull(continueWithEncryptedUpload);

        await ValidateStagingAsync(staging, cancellationToken);

        using var recoveryPreparation = await recoveryCoordinator.PrepareAsync(
            staging.SourceFingerprint,
            logbookDisplayName,
            cancellationToken);

        return await continueWithEncryptedUpload(recoveryPreparation, cancellationToken);
    }

    public async Task<TResult> RunAfterValidatedStagingOrCompletionAsync<TResult>(
        StagedWorkbookMigration staging,
        string logbookDisplayName,
        Func<WorkbookMigrationRecoveryState, CancellationToken, Task<TResult>> continueWithHostedMigration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentException.ThrowIfNullOrWhiteSpace(logbookDisplayName);
        ArgumentNullException.ThrowIfNull(continueWithHostedMigration);

        await ValidateStagingAsync(staging, cancellationToken);

        using var recoveryState = await recoveryCoordinator.BeginOrResumeAsync(
            staging.SourceFingerprint,
            logbookDisplayName,
            cancellationToken);

        return await continueWithHostedMigration(recoveryState, cancellationToken);
    }

    private static async Task ValidateStagingAsync(
        StagedWorkbookMigration staging,
        CancellationToken cancellationToken)
    {
        var migrationReport = staging.MigrationReport;
        var originalWorkbookPath = Path.GetFullPath(staging.OriginalWorkbookPath);
        var stagedWorkbookPath = Path.GetFullPath(staging.StagedWorkbookPath);
        var backupWorkbookPath = Path.GetFullPath(staging.BackupWorkbookPath);
        var reportedSourcePath = Path.GetFullPath(migrationReport.SourcePath);
        var reportedOutputPath = Path.GetFullPath(migrationReport.OutputPath);

        if (!string.Equals(migrationReport.Status, "validated", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Hosted migration recovery cannot start before source migration validation succeeds.");
        }

        if (!string.Equals(originalWorkbookPath, reportedSourcePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(stagedWorkbookPath, reportedOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The validated migration report does not belong to the staged workbook artifacts.");
        }

        if (string.Equals(originalWorkbookPath, backupWorkbookPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stagedWorkbookPath, backupWorkbookPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(originalWorkbookPath, stagedWorkbookPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The original, staged, and backup workbooks must use separate paths.");
        }

        WorkbookPackageValidator.ValidateWorkbookPackage(
            originalWorkbookPath,
            migrationReport.SourceVersion);
        WorkbookPackageValidator.ValidateWorkbookPackage(
            stagedWorkbookPath,
            migrationReport.OutputVersion);
        WorkbookPackageValidator.ValidateWorkbookPackage(
            backupWorkbookPath,
            migrationReport.SourceVersion);

        var originalFingerprint = await Integrity.Sha256Async(
            originalWorkbookPath,
            cancellationToken);
        var backupFingerprint = await Integrity.Sha256Async(
            backupWorkbookPath,
            cancellationToken);
        if (!string.Equals(
                staging.SourceFingerprint,
                originalFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                staging.SourceFingerprint,
                backupFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The original workbook or its timestamped backup changed after staging.");
        }
    }
}
