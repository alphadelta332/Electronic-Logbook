namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookHandoffTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"WorkbookHandoffTests-{Guid.NewGuid():N}");

    public WorkbookHandoffTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ReplaceSourceWithUpdatedUsesAtomicReplaceAndKeepsBackup()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");

        var result = WorkbookHandoff.ReplaceSourceWithUpdated(source, staged);

        Assert.Equal(Path.GetFullPath(source), result.FinalWorkbookPath);
        Assert.Equal("new workbook", File.ReadAllText(source));
        Assert.Equal("old workbook", File.ReadAllText(result.BackupWorkbookPath));
        Assert.False(File.Exists(staged));
        Assert.Equal(_directory, Path.GetDirectoryName(result.BackupWorkbookPath));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedStagesExternalOutputBesideSourceBeforeReplacing()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var externalDirectory = Path.Combine(_directory, "external");
        Directory.CreateDirectory(externalDirectory);
        var staged = Path.Combine(externalDirectory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");

        var result = WorkbookHandoff.ReplaceSourceWithUpdated(source, staged);

        Assert.Equal("new workbook", File.ReadAllText(source));
        Assert.Equal("old workbook", File.ReadAllText(result.BackupWorkbookPath));
        Assert.False(File.Exists(staged));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".*_Staged_*.xlsm"));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedRejectsSourceAsStagedPath()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        File.WriteAllText(source, "old workbook");

        Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(source, source));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedValidatesFinalAndBackupVersions()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook.xlsm");
        var staged = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook_updated.xlsm");

        var result = WorkbookHandoff.ReplaceSourceWithUpdated(
            source,
            staged,
            TestRepo.Version,
            "2.0.0");

        Assert.Equal(TestRepo.Version, WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.Equal("2.0.0", WorkbookPackageValidator.ValidateWorkbookPackage(result.BackupWorkbookPath));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedRejectsUnexpectedSourceVersionBeforeHandoff()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var staged = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook_updated.xlsm");

        Assert.Throws<InvalidDataException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(
                source,
                staged,
                TestRepo.Version,
                "2.0.0"));

        Assert.True(File.Exists(staged));
        Assert.False(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm").Any());
    }

    [Fact]
    public void ReplaceSourceWithUpdatedKeepsRecoverableStateWhenFinalReplaceFails()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");
        var fileSystem = new ThrowingReplaceFileSystem();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(
                source,
                staged,
                expectedFinalVersion: null,
                expectedBackupVersion: null,
                fileSystem));

        Assert.Contains("Failed to finalise workbook handoff", exception.Message, StringComparison.Ordinal);
        Assert.Equal("old workbook", File.ReadAllText(source));
        Assert.Equal("new workbook", File.ReadAllText(staged));
        Assert.False(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm").Any());
        Assert.True(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededRestoresBackupWhenSourceIsMissing()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "external", "logbook_updated.xlsm");
        var replacement = Path.Combine(_directory, ".logbook_Staged_test.xlsm");
        var backup = Path.Combine(_directory, "logbook_Old_20260716-120000.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "new workbook");
        File.WriteAllText(replacement, "new workbook");
        File.WriteAllText(backup, "old workbook");
        WriteJournal(source, staged, replacement, backup, localStageCreated: true);

        WorkbookHandoff.RecoverIfNeeded(source);

        Assert.Equal("old workbook", File.ReadAllText(source));
        Assert.False(File.Exists(backup));
        Assert.False(File.Exists(replacement));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededCleansCompletedExternalHandoffJournal()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "external", "logbook_updated.xlsm");
        var replacement = Path.Combine(_directory, ".logbook_Staged_test.xlsm");
        var backup = Path.Combine(_directory, "logbook_Old_20260716-120000.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(source, "new workbook");
        File.WriteAllText(staged, "new workbook");
        File.WriteAllText(backup, "old workbook");
        WriteJournal(source, staged, replacement, backup, localStageCreated: true);

        WorkbookHandoff.RecoverIfNeeded(source);

        Assert.Equal("new workbook", File.ReadAllText(source));
        Assert.Equal("old workbook", File.ReadAllText(backup));
        Assert.False(File.Exists(staged));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededThrowsAndKeepsJournalWhenSourceAndBackupAreMissing()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "external", "logbook_updated.xlsm");
        var replacement = Path.Combine(_directory, ".logbook_Staged_test.xlsm");
        var backup = Path.Combine(_directory, "logbook_Old_20260716-120000.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "new workbook");
        File.WriteAllText(replacement, "new workbook");
        WriteJournal(source, staged, replacement, backup, localStageCreated: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.RecoverIfNeeded(source));

        Assert.Contains("Cannot recover interrupted handoff", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(backup));
        Assert.True(File.Exists(replacement));
        Assert.True(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void CompletePostHandoffValidationRetainsCurrentBackupAndDeletesOlderUpdaterBackups()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var retainedBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260716-120000.xlsm");
        var olderBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260715-120000.xlsm");
        var unrelatedBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "other_Old_20260715-120000.xlsm");

        WorkbookHandoff.CompletePostHandoffValidation(source, retainedBackup, TestRepo.Version, "2.0.0");

        Assert.True(File.Exists(retainedBackup));
        Assert.False(File.Exists(olderBackup));
        Assert.True(File.Exists(unrelatedBackup));
    }

    [Fact]
    public void CompletePostHandoffValidationDoesNotDeleteOlderBackupsWhenRetainedBackupIsInvalid()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var retainedBackup = Path.Combine(_directory, "logbook_Old_20260716-120000.xlsm");
        File.WriteAllText(retainedBackup, "not a workbook package");
        var olderBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260715-120000.xlsm");

        Assert.Throws<InvalidDataException>(() =>
            WorkbookHandoff.CompletePostHandoffValidation(source, retainedBackup, TestRepo.Version, "2.0.0"));

        Assert.True(File.Exists(retainedBackup));
        Assert.True(File.Exists(olderBackup));
    }

    [Fact]
    public void CompletePostHandoffValidationDoesNotDeleteOlderBackupsWhenRetainedBackupHasUpdatedVersion()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var retainedBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook_Old_20260716-120000.xlsm");
        var olderBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260715-120000.xlsm");

        Assert.Throws<InvalidDataException>(() =>
            WorkbookHandoff.CompletePostHandoffValidation(source, retainedBackup, TestRepo.Version, "2.0.0"));

        Assert.True(File.Exists(retainedBackup));
        Assert.True(File.Exists(olderBackup));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static string BuildJournalPath(string sourceWorkbookPath)
    {
        return Path.Combine(
            Path.GetDirectoryName(sourceWorkbookPath)!,
            $".{Path.GetFileNameWithoutExtension(sourceWorkbookPath)}_handoff.json");
    }

    private static void WriteJournal(
        string source,
        string staged,
        string replacement,
        string backup,
        bool localStageCreated)
    {
        var journal = new
        {
            sourceWorkbookPath = Path.GetFullPath(source),
            stagedUpdatedWorkbookPath = Path.GetFullPath(staged),
            replacementWorkbookPath = Path.GetFullPath(replacement),
            backupWorkbookPath = Path.GetFullPath(backup),
            localStageCreated,
            createdAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(BuildJournalPath(source), System.Text.Json.JsonSerializer.Serialize(journal));
    }

    private sealed class ThrowingReplaceFileSystem : IWorkbookFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public void DeleteFile(string path)
        {
            File.Delete(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            return Directory.EnumerateFiles(path, searchPattern);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            File.Move(sourcePath, destinationPath, overwrite);
        }

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void ReplaceFile(
            string sourceFileName,
            string destinationFileName,
            string destinationBackupFileName,
            bool ignoreMetadataErrors)
        {
            throw new IOException("Simulated replace failure.");
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }
    }
}
