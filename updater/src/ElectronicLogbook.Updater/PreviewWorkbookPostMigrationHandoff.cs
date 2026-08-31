using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed record PreviewWorkbookPostMigrationHandoffResult(
    string FinalWorkbookPath,
    string UntouchedBackupWorkbookPath,
    string InstallationRollbackBackupPath,
    WorkbookMigrationStamp Stamp);

public sealed class PreviewWorkbookPostMigrationHandoff
{
    public const string CompletedStatus = "Moved to FlightLogX";

    private readonly Func<string, WorkbookMigrationStamp, WorkbookMigrationStampPackageResult> stampWorkbook;
    private readonly Func<string, WorkbookMigrationStamp?> readStamp;
    private readonly Func<
        string,
        string,
        string?,
        string?,
        IWorkbookPackageValidation,
        HandoffResult> replaceWorkbook;

    public PreviewWorkbookPostMigrationHandoff()
        : this(
            PortableLogbookWorkbookPackageStorage.EnsureWorkbookMigrationStamp,
            PortableLogbookWorkbookPackageStorage.ReadWorkbookMigrationStamp,
            static (source, staged, finalVersion, backupVersion, validation) =>
                WorkbookHandoff.ReplaceSourceWithUpdated(
                    source,
                    staged,
                    finalVersion,
                    backupVersion,
                    PhysicalWorkbookFileSystem.Instance,
                    validation))
    {
    }

    internal PreviewWorkbookPostMigrationHandoff(
        Func<string, WorkbookMigrationStamp, WorkbookMigrationStampPackageResult> stampWorkbook,
        Func<string, WorkbookMigrationStamp?> readStamp,
        Func<
            string,
            string,
            string?,
            string?,
            IWorkbookPackageValidation,
            HandoffResult> replaceWorkbook)
    {
        ArgumentNullException.ThrowIfNull(stampWorkbook);
        ArgumentNullException.ThrowIfNull(readStamp);
        ArgumentNullException.ThrowIfNull(replaceWorkbook);

        this.stampWorkbook = stampWorkbook;
        this.readStamp = readStamp;
        this.replaceWorkbook = replaceWorkbook;
    }

    public async Task<PreviewWorkbookPostMigrationHandoffResult> InstallAsync(
        StagedWorkbookMigration staging,
        PreviewWorkbookHostedMigrationResult hostedResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(hostedResult);
        cancellationToken.ThrowIfCancellationRequested();

        var stamp = CreateVerifiedStamp(hostedResult);
        await ValidateOriginalAndBackupAsync(staging, cancellationToken);
        WorkbookPackageValidator.ValidateStagedWorkbook(
            staging.StagedWorkbookPath,
            staging.MigrationReport.OutputVersion);

        stampWorkbook(staging.StagedWorkbookPath, stamp);
        EnsureStampMatches(staging.StagedWorkbookPath, stamp);
        WorkbookPackageValidator.ValidateStagedWorkbook(
            staging.StagedWorkbookPath,
            staging.MigrationReport.OutputVersion);

        await ValidateOriginalAndBackupAsync(staging, cancellationToken);
        var validation = new MigrationStampedWorkbookPackageValidation(
            staging.MigrationReport.OutputVersion,
            stamp,
            readStamp);
        var handoff = replaceWorkbook(
            staging.OriginalWorkbookPath,
            staging.StagedWorkbookPath,
            staging.MigrationReport.OutputVersion,
            staging.MigrationReport.SourceVersion,
            validation);

        await ValidateUntouchedBackupAsync(staging, cancellationToken);
        return new PreviewWorkbookPostMigrationHandoffResult(
            handoff.FinalWorkbookPath,
            staging.BackupWorkbookPath,
            handoff.BackupWorkbookPath,
            stamp);
    }

    private static WorkbookMigrationStamp CreateVerifiedStamp(
        PreviewWorkbookHostedMigrationResult hostedResult)
    {
        var migration = hostedResult.Migration;
        var receipt = hostedResult.VerifiedReceipt;
        if (migration.Status != HostedWorkbookMigrationStatus.Completed ||
            migration.CompletedAt is not { } completedAt ||
            migration.LogbookId != receipt.LogbookId ||
            migration.DeviceId != receipt.DeviceId ||
            migration.ExpectedOperationCount != receipt.EntryCount ||
            migration.VerifiedOperationCount != receipt.EntryCount ||
            !string.Equals(migration.SourceFingerprint, receipt.SourceFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
                migration.VerificationReceiptHash,
                receipt.VerificationReceiptSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The workbook cannot be installed because hosted migration completion has not been exactly verified.");
        }

        return new WorkbookMigrationStamp(
            CompletedStatus,
            completedAt,
            migration.MigrationId);
    }

    private static async Task ValidateOriginalAndBackupAsync(
        StagedWorkbookMigration staging,
        CancellationToken cancellationToken)
    {
        WorkbookPackageValidator.ValidateWorkbookPackage(
            staging.OriginalWorkbookPath,
            staging.MigrationReport.SourceVersion);
        await ValidateUntouchedBackupAsync(staging, cancellationToken);

        var sourceFingerprint = await Integrity.Sha256Async(
            staging.OriginalWorkbookPath,
            cancellationToken);
        if (!string.Equals(
                staging.SourceFingerprint,
                sourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The original workbook changed after hosted migration was verified. The stamped workbook was not installed; run the migration again from the unchanged backup.");
        }
    }

    private static async Task ValidateUntouchedBackupAsync(
        StagedWorkbookMigration staging,
        CancellationToken cancellationToken)
    {
        WorkbookPackageValidator.ValidateWorkbookPackage(
            staging.BackupWorkbookPath,
            staging.MigrationReport.SourceVersion);
        var backupFingerprint = await Integrity.Sha256Async(
            staging.BackupWorkbookPath,
            cancellationToken);
        if (!string.Equals(
                staging.SourceFingerprint,
                backupFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The untouched timestamped workbook backup changed after staging. The stamped workbook was not installed.");
        }
    }

    private void EnsureStampMatches(string workbookPath, WorkbookMigrationStamp expectedStamp)
    {
        if (readStamp(workbookPath) != expectedStamp)
        {
            throw new InvalidDataException(
                "The FlightLogX workbook migration stamp does not match the completed hosted migration.");
        }
    }

    private sealed class MigrationStampedWorkbookPackageValidation(
        string outputVersion,
        WorkbookMigrationStamp expectedStamp,
        Func<string, WorkbookMigrationStamp?> readStamp) : IWorkbookPackageValidation
    {
        public string ValidateWorkbookPackage(string workbookPath, string? expectedVersion = null)
        {
            var version = WorkbookPackageValidator.ValidateWorkbookPackage(
                workbookPath,
                expectedVersion);
            if (string.Equals(
                    expectedVersion,
                    outputVersion,
                    StringComparison.Ordinal))
            {
                var actualStamp = readStamp(workbookPath);
                if (actualStamp != expectedStamp)
                {
                    throw new InvalidDataException(
                        "The installed workbook does not contain the verified FlightLogX migration stamp.");
                }
            }

            return version;
        }
    }
}
