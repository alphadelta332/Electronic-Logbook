namespace ElectronicLogbook.Updater;

public sealed record StagedWorkbookMigration(
    string OriginalWorkbookPath,
    string StagedWorkbookPath,
    string BackupWorkbookPath,
    string SourceFingerprint,
    MigrationReport MigrationReport);

public sealed class WorkbookMigrationStager
{
    private readonly Func<MigrationRequest, CancellationToken, MigrationReport> migrate;
    private readonly Func<DateTimeOffset> utcNow;

    public WorkbookMigrationStager(IUpdaterProgressSink? progressSink = null)
        : this(
            new ExcelWorkbookMigrator(progressSink).Migrate,
            static () => DateTimeOffset.UtcNow)
    {
    }

    internal WorkbookMigrationStager(
        Func<MigrationRequest, CancellationToken, MigrationReport> migrate,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(migrate);
        this.migrate = migrate;
        this.utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    public async Task<StagedWorkbookMigration> StageAsync(
        MigrationRequest request,
        CancellationToken cancellationToken = default,
        string? expectedSourceFingerprint = null)
    {
        request = MigrationRequestValidator.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceVersion = WorkbookPackageValidator.ValidateWorkbookPackage(request.SourcePath);
        CompatibilityPolicy.LoadDefault().ThrowIfUnsupported(sourceVersion);
        var sourceFingerprint = await Integrity.Sha256Async(request.SourcePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedSourceFingerprint) &&
            !string.Equals(sourceFingerprint, expectedSourceFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The source workbook changed after its pre-migration summary was shown. Run the checks again before continuing.");
        }

        var backupPath = BuildBackupPath(request.SourcePath, utcNow());
        var backupVerified = false;
        var stagingSucceeded = false;

        try
        {
            var report = await Task.Run(
                () => migrate(request, cancellationToken),
                cancellationToken);

            ValidateMigrationReport(request, report, sourceVersion);
            WorkbookPackageValidator.ValidateStagedWorkbook(
                request.OutputPath,
                report.OutputVersion);

            var sourceFingerprintAfterStaging = await Integrity.Sha256Async(
                request.SourcePath,
                cancellationToken);
            if (!string.Equals(sourceFingerprint, sourceFingerprintAfterStaging, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The source workbook changed while the upgraded workbook was being staged.");
            }

            File.Copy(request.SourcePath, backupPath, overwrite: false);
            WorkbookPackageValidator.ValidateWorkbookPackage(backupPath, sourceVersion);
            var backupFingerprint = await Integrity.Sha256Async(
                backupPath,
                cancellationToken);
            if (!string.Equals(sourceFingerprint, backupFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The timestamped workbook backup does not exactly match the inspected source workbook.");
            }

            var sourceFingerprintAfterBackup = await Integrity.Sha256Async(
                request.SourcePath,
                cancellationToken);
            if (!string.Equals(sourceFingerprint, sourceFingerprintAfterBackup, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The source workbook changed while its timestamped backup was being created.");
            }

            backupVerified = true;
            stagingSucceeded = true;
            return new StagedWorkbookMigration(
                request.SourcePath,
                request.OutputPath,
                backupPath,
                sourceFingerprint,
                report);
        }
        finally
        {
            if (!stagingSucceeded)
            {
                TryDelete(request.OutputPath);
            }
            if (!backupVerified)
            {
                TryDelete(backupPath);
            }
        }
    }

    private static void ValidateMigrationReport(
        MigrationRequest request,
        MigrationReport report,
        string sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!string.Equals(report.Status, "validated", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The upgraded workbook was not reported as validated.");
        }
        if (!string.Equals(
                Path.GetFullPath(report.SourcePath),
                request.SourcePath,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFullPath(report.OutputPath),
                request.OutputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The migration report does not belong to the staged workbook request.");
        }
        if (!string.Equals(report.SourceVersion, sourceVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The migration report source version does not match the inspected workbook.");
        }
    }

    private static string BuildBackupPath(
        string sourceWorkbookPath,
        DateTimeOffset timestamp)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var baseName = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var extension = Path.GetExtension(sourceWorkbookPath);
        var marker = timestamp.UtcDateTime.ToString(
            "yyyyMMdd-HHmmss'Z'",
            System.Globalization.CultureInfo.InvariantCulture);

        var candidate = Path.Combine(
            directory,
            $"{baseName}_MigrationBackup_{marker}{extension}");
        var counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(
                directory,
                $"{baseName}_MigrationBackup_{marker}_{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Preserve the staging failure. Any retained artifact remains available for diagnosis.
        }
    }
}
