namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookMigrationStagerTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 28, 7, 15, 23, TimeSpan.Zero);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"WorkbookMigrationStagerTests-{Guid.NewGuid():N}");

    public WorkbookMigrationStagerTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void DefaultMigrator_OpensSourceWorkbookReadOnly()
    {
        var migrator = File.ReadAllText(TestRepo.FindFile(
            "updater/src/ElectronicLogbook.Updater/ExcelWorkbookMigrator.cs"));

        Assert.Contains(
            "sourceWorkbook = excel.Workbooks.Open(request.SourcePath, 0, true);",
            migrator,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageAsync_ValidSource_ValidatesStageBeforeCreatingExactBackup()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, "2.0.3", "Electronic_Logbook.xlsm");
        var master = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0", "Electronic_Logbook_Master.xlsm");
        var staged = Path.Combine(directory, "Electronic_Logbook_Staged.xlsm");
        var sourceBytes = await File.ReadAllBytesAsync(source);
        var migrationCalled = false;
        var stager = new WorkbookMigrationStager(
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                migrationCalled = true;
                Assert.Empty(Directory.EnumerateFiles(
                    directory,
                    "Electronic_Logbook_MigrationBackup_*.xlsm"));
                Assert.Equal(sourceBytes, File.ReadAllBytes(request.SourcePath));
                File.Copy(request.MasterPath, request.OutputPath);
                return CreateReport(request, "2.0.3", "3.0.0");
            },
            () => Timestamp);

        var result = await stager.StageAsync(
            new MigrationRequest(source, master, staged, Manifest: null),
            CancellationToken.None);

        Assert.True(migrationCalled);
        Assert.Equal(Path.GetFullPath(source), result.OriginalWorkbookPath);
        Assert.Equal(Path.GetFullPath(staged), result.StagedWorkbookPath);
        Assert.Equal(
            "Electronic_Logbook_MigrationBackup_20260828-071523Z.xlsm",
            Path.GetFileName(result.BackupWorkbookPath));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(source));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(result.BackupWorkbookPath));
        Assert.Equal(
            await Integrity.Sha256Async(source, CancellationToken.None),
            result.SourceFingerprint);
        Assert.Equal("3.0.0", WorkbookPackageValidator.ValidateWorkbookPackage(staged));
    }

    [Fact]
    public async Task StageAsync_InvalidSource_StopsBeforeBackupOrMigration()
    {
        var source = Path.Combine(directory, "Electronic_Logbook.xlsm");
        await File.WriteAllTextAsync(source, "not a workbook package");
        var master = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0", "Electronic_Logbook_Master.xlsm");
        var staged = Path.Combine(directory, "Electronic_Logbook_Staged.xlsm");
        var migrationCallCount = 0;
        var stager = new WorkbookMigrationStager(
            (_, _) =>
            {
                migrationCallCount++;
                throw new InvalidOperationException("Migration must not run.");
            },
            () => Timestamp);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() =>
            stager.StageAsync(
                new MigrationRequest(source, master, staged, Manifest: null),
                CancellationToken.None));

        Assert.Equal(0, migrationCallCount);
        Assert.Empty(Directory.EnumerateFiles(directory, "*_MigrationBackup_*.xlsm"));
        Assert.False(File.Exists(staged));
        Assert.Equal("not a workbook package", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task StageAsync_MigrationFails_CleansPartialStageWithoutCreatingBackup()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, "2.0.3", "Electronic_Logbook.xlsm");
        var master = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0", "Electronic_Logbook_Master.xlsm");
        var staged = Path.Combine(directory, "Electronic_Logbook_Staged.xlsm");
        var stager = new WorkbookMigrationStager(
            (request, _) =>
            {
                File.Copy(request.MasterPath, request.OutputPath);
                throw new InvalidOperationException("staging interrupted");
            },
            () => Timestamp);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stager.StageAsync(
                new MigrationRequest(source, master, staged, Manifest: null),
                CancellationToken.None));

        Assert.Equal("staging interrupted", error.Message);
        Assert.Empty(Directory.EnumerateFiles(
            directory,
            "Electronic_Logbook_MigrationBackup_*.xlsm"));
        Assert.False(File.Exists(staged));
    }

    [Fact]
    public async Task StageAsync_SourceChangesDuringMigration_RejectsStageBeforeCreatingBackup()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, "2.0.3", "Electronic_Logbook.xlsm");
        var master = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0", "Electronic_Logbook_Master.xlsm");
        var staged = Path.Combine(directory, "Electronic_Logbook_Staged.xlsm");
        var stager = new WorkbookMigrationStager(
            (request, _) =>
            {
                File.Copy(request.MasterPath, request.OutputPath);
                File.AppendAllText(request.SourcePath, "external change");
                return CreateReport(request, "2.0.3", "3.0.0");
            },
            () => Timestamp);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            stager.StageAsync(
                new MigrationRequest(source, master, staged, Manifest: null),
                CancellationToken.None));

        Assert.Contains("source workbook changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(
            directory,
            "Electronic_Logbook_MigrationBackup_*.xlsm"));
        Assert.False(File.Exists(staged));
    }

    [Fact]
    public async Task StageAsync_ExistingTimestampedBackup_UsesUniqueSuffix()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, "2.0.3", "Electronic_Logbook.xlsm");
        var master = TestRepo.CreateMinimalWorkbookPackage(directory, "3.0.0", "Electronic_Logbook_Master.xlsm");
        var staged = Path.Combine(directory, "Electronic_Logbook_Staged.xlsm");
        var existingBackup = Path.Combine(
            directory,
            "Electronic_Logbook_MigrationBackup_20260828-071523Z.xlsm");
        await File.WriteAllTextAsync(existingBackup, "existing backup");
        var stager = new WorkbookMigrationStager(
            (request, _) =>
            {
                File.Copy(request.MasterPath, request.OutputPath);
                return CreateReport(request, "2.0.3", "3.0.0");
            },
            () => Timestamp);

        var result = await stager.StageAsync(
            new MigrationRequest(source, master, staged, Manifest: null),
            CancellationToken.None);

        Assert.Equal(
            "Electronic_Logbook_MigrationBackup_20260828-071523Z_1.xlsm",
            Path.GetFileName(result.BackupWorkbookPath));
        Assert.Equal("existing backup", await File.ReadAllTextAsync(existingBackup));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MigrationReport CreateReport(
        MigrationRequest request,
        string sourceVersion,
        string outputVersion) =>
        new(
            request.SourcePath,
            request.MasterPath,
            request.OutputPath,
            sourceVersion,
            outputVersion,
            LogbookRows: 0,
            new AirportVisitStatsDiagnostics(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, int>()),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            "validated");
}
