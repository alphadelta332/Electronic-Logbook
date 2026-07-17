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
    public void ReplaceSourceWithUpdatedReplacesFromSourceDirectoryWhenOutputIsExternal()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var externalDirectory = Path.Combine(_directory, "external");
        Directory.CreateDirectory(externalDirectory);
        var staged = Path.Combine(externalDirectory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");
        var fileSystem = new RecordingFileSystem();

        WorkbookHandoff.ReplaceSourceWithUpdated(
            source,
            staged,
            expectedFinalVersion: null,
            expectedBackupVersion: null,
            fileSystem);

        var copiedPath = Assert.Single(fileSystem.CopiedDestinations);
        Assert.Equal(_directory, Path.GetDirectoryName(copiedPath));
        Assert.StartsWith(".logbook_Staged_", Path.GetFileName(copiedPath), StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(copiedPath), fileSystem.ReplaceSourcePath);
        Assert.Equal(Path.GetFullPath(source), fileSystem.ReplaceDestinationPath);
        Assert.Equal(_directory, Path.GetDirectoryName(fileSystem.ReplaceBackupPath));
        Assert.False(File.Exists(staged));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".*_Staged_*.xlsm"));
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
    public void ReplaceSourceWithUpdatedLeavesSourceUntouchedWhenExternalStageCopyFails()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var externalDirectory = Path.Combine(_directory, "external");
        Directory.CreateDirectory(externalDirectory);
        var staged = Path.Combine(externalDirectory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");

        Assert.Throws<IOException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(
                source,
                staged,
                expectedFinalVersion: null,
                expectedBackupVersion: null,
                new ThrowingCopyFileSystem()));

        Assert.Equal("old workbook", File.ReadAllText(source));
        Assert.Equal("new workbook", File.ReadAllText(staged));
        Assert.Empty(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm"));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedLeavesSourceUntouchedWhenJournalWriteFails()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");

        Assert.Throws<IOException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(
                source,
                staged,
                expectedFinalVersion: null,
                expectedBackupVersion: null,
                new ThrowingJournalMoveFileSystem(source)));

        Assert.Equal("old workbook", File.ReadAllText(source));
        Assert.Equal("new workbook", File.ReadAllText(staged));
        Assert.Empty(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm"));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedKeepsRecoverableStateWhenSourceIsLocked()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var staged = Path.Combine(_directory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");

        using var sourceLock = new FileStream(
            source,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(source, staged));

        Assert.Contains("Failed to finalise workbook handoff", exception.Message, StringComparison.Ordinal);
        sourceLock.Position = 0;
        using var reader = new StreamReader(sourceLock, leaveOpen: true);
        Assert.Equal("old workbook", reader.ReadToEnd());
        Assert.True(File.Exists(staged));
        Assert.False(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm").Any());
        Assert.True(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedRestoresBackupWhenPostReplaceValidationFails()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook.xlsm");
        var staged = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook_updated.xlsm");
        var packageValidation = new PostReplaceFailingValidation(source);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.ReplaceSourceWithUpdated(
                source,
                staged,
                expectedFinalVersion: TestRepo.Version,
                expectedBackupVersion: "2.0.0",
                PhysicalWorkbookFileSystem.Instance,
                packageValidation));

        Assert.Contains("Failed to finalise workbook handoff", exception.Message, StringComparison.Ordinal);
        Assert.Equal("2.0.0", WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.False(File.Exists(BuildJournalPath(source)));
        Assert.Empty(Directory.EnumerateFiles(_directory, "logbook_Old_*.xlsm"));
    }

    [Fact]
    public void ReplaceSourceWithUpdatedDoesNotRollbackWhenExternalStagedCleanupFails()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var externalDirectory = Path.Combine(_directory, "external");
        Directory.CreateDirectory(externalDirectory);
        var staged = Path.Combine(externalDirectory, "logbook_updated.xlsm");
        File.WriteAllText(source, "old workbook");
        File.WriteAllText(staged, "new workbook");
        var fileSystem = new ThrowingDeleteFileSystem(staged);

        var result = WorkbookHandoff.ReplaceSourceWithUpdated(
            source,
            staged,
            expectedFinalVersion: null,
            expectedBackupVersion: null,
            fileSystem);

        Assert.Equal("new workbook", File.ReadAllText(source));
        Assert.Equal("old workbook", File.ReadAllText(result.BackupWorkbookPath));
        Assert.True(File.Exists(staged));
        Assert.False(File.Exists(BuildJournalPath(source)));
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

        var result = WorkbookHandoff.RecoverIfNeeded(source);

        Assert.Equal(HandoffRecoveryAction.BackupRestored, result.Action);
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

        var result = WorkbookHandoff.RecoverIfNeeded(source);

        Assert.Equal(HandoffRecoveryAction.CompletedJournalCleaned, result.Action);
        Assert.Equal("new workbook", File.ReadAllText(source));
        Assert.Equal("old workbook", File.ReadAllText(backup));
        Assert.False(File.Exists(staged));
        Assert.False(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededReturnsNoneWhenNoJournalExists()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        File.WriteAllText(source, "workbook");

        var result = WorkbookHandoff.RecoverIfNeeded(source);

        Assert.Equal(HandoffRecoveryAction.None, result.Action);
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
    public void RecoverIfNeededKeepsJournalWhenBackupRestoreMoveFails()
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

        Assert.Throws<IOException>(() =>
            WorkbookHandoff.RecoverIfNeeded(source, new ThrowingMoveFileSystem(backup, source)));

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(replacement));
        Assert.True(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededRejectsJournalForDifferentSourceWorkbook()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var otherSource = Path.Combine(_directory, "other-logbook.xlsm");
        var staged = Path.Combine(_directory, "external", "logbook_updated.xlsm");
        var replacement = Path.Combine(_directory, ".logbook_Staged_test.xlsm");
        var backup = Path.Combine(_directory, "logbook_Old_20260716-120000.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        WriteJournal(otherSource, staged, replacement, backup, localStageCreated: true);
        File.Move(BuildJournalPath(otherSource), BuildJournalPath(source));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkbookHandoff.RecoverIfNeeded(source));

        Assert.Contains("does not match source workbook", exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(BuildJournalPath(source)));
    }

    [Fact]
    public void RecoverIfNeededRejectsMalformedJournal()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        File.WriteAllText(BuildJournalPath(source), "not json");

        Assert.Throws<System.Text.Json.JsonException>(() =>
            WorkbookHandoff.RecoverIfNeeded(source));

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

    [Fact]
    public void CompletePostHandoffValidationDoesNotDeleteOlderBackupsWhenSourceIsInvalid()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        File.WriteAllText(source, "not a workbook package");
        var retainedBackup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
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

    [Fact]
    public void RestoreBackupValidatesBackupAndKeepsFailedWorkbookForInvestigation()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var backup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260717-120000.xlsm");

        var result = WorkbookHandoff.RestoreBackup(source, backup, "2.0.0");

        Assert.Equal("2.0.0", WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.Equal(Path.GetFullPath(source), result.RestoredWorkbookPath);
        Assert.NotNull(result.FailedWorkbookPath);
        Assert.True(File.Exists(result.FailedWorkbookPath));
        Assert.Equal(TestRepo.Version, WorkbookPackageValidator.ValidateWorkbookPackage(result.FailedWorkbookPath));
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void RestoreBackupRestoresWhenSourceIsMissing()
    {
        var source = Path.Combine(_directory, "logbook.xlsm");
        var backup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260717-120000.xlsm");

        var result = WorkbookHandoff.RestoreBackup(source, backup, "2.0.0");

        Assert.Equal("2.0.0", WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.Equal(Path.GetFullPath(source), result.RestoredWorkbookPath);
        Assert.Null(result.FailedWorkbookPath);
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void RestoreBackupRejectsInvalidBackupWithoutMovingSource()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var backup = Path.Combine(_directory, "logbook_Old_20260717-120000.xlsm");
        File.WriteAllText(backup, "not a workbook package");

        Assert.Throws<InvalidDataException>(() =>
            WorkbookHandoff.RestoreBackup(source, backup, "2.0.0"));

        Assert.Equal(TestRepo.Version, WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.False(Directory.EnumerateFiles(_directory, "logbook_FailedRestore_*.xlsm").Any());
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void RestoreBackupRollsBackFailedRestoreValidation()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            TestRepo.Version,
            "logbook.xlsm");
        var backup = TestRepo.CreateMinimalWorkbookPackage(
            _directory,
            "2.0.0",
            "logbook_Old_20260717-120000.xlsm");
        var validation = new SecondSourceValidationFailingValidation(source);

        Assert.Throws<InvalidDataException>(() =>
            WorkbookHandoff.RestoreBackup(
                source,
                backup,
                "2.0.0",
                PhysicalWorkbookFileSystem.Instance,
                validation));

        Assert.Equal(TestRepo.Version, WorkbookPackageValidator.ValidateWorkbookPackage(source));
        Assert.True(File.Exists(backup));
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

    private class LocalFileSystem : IWorkbookFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public virtual void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public virtual void DeleteFile(string path)
        {
            File.Delete(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            return Directory.EnumerateFiles(path, searchPattern);
        }

        public virtual void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            File.Move(sourcePath, destinationPath, overwrite);
        }

        public string ReadAllText(string path) => File.ReadAllText(path);

        public virtual void ReplaceFile(
            string sourceFileName,
            string destinationFileName,
            string destinationBackupFileName,
            bool ignoreMetadataErrors)
        {
            File.Replace(
                sourceFileName,
                destinationFileName,
                destinationBackupFileName,
                ignoreMetadataErrors);
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }
    }

    private sealed class ThrowingReplaceFileSystem : LocalFileSystem
    {
        public override void ReplaceFile(
            string sourceFileName,
            string destinationFileName,
            string destinationBackupFileName,
            bool ignoreMetadataErrors)
        {
            throw new IOException("Simulated replace failure.");
        }
    }

    private sealed class ThrowingCopyFileSystem : LocalFileSystem
    {
        public override void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            throw new IOException("Simulated stage copy failure.");
        }
    }

    private sealed class ThrowingMoveFileSystem(string sourceToFail, string destinationToFail) : LocalFileSystem
    {
        private readonly string _sourceToFail = Path.GetFullPath(sourceToFail);
        private readonly string _destinationToFail = Path.GetFullPath(destinationToFail);

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            if (string.Equals(Path.GetFullPath(sourcePath), _sourceToFail, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(destinationPath), _destinationToFail, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated recovery move failure.");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private sealed class ThrowingJournalMoveFileSystem(string sourcePath) : LocalFileSystem
    {
        private readonly string _journalPath = BuildJournalPath(sourcePath);

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            if (string.Equals(Path.GetFullPath(destinationPath), Path.GetFullPath(_journalPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated journal write failure.");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private sealed class RecordingFileSystem : LocalFileSystem
    {
        public List<string> CopiedDestinations { get; } = [];
        public string? ReplaceSourcePath { get; private set; }
        public string? ReplaceDestinationPath { get; private set; }
        public string? ReplaceBackupPath { get; private set; }

        public override void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            CopiedDestinations.Add(Path.GetFullPath(destinationPath));
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public override void ReplaceFile(
            string sourceFileName,
            string destinationFileName,
            string destinationBackupFileName,
            bool ignoreMetadataErrors)
        {
            ReplaceSourcePath = Path.GetFullPath(sourceFileName);
            ReplaceDestinationPath = Path.GetFullPath(destinationFileName);
            ReplaceBackupPath = Path.GetFullPath(destinationBackupFileName);
            File.Replace(
                sourceFileName,
                destinationFileName,
                destinationBackupFileName,
                ignoreMetadataErrors);
        }
    }

    private sealed class ThrowingDeleteFileSystem(string pathToFail) : LocalFileSystem
    {
        private readonly string _pathToFail = Path.GetFullPath(pathToFail);

        public override void DeleteFile(string path)
        {
            if (string.Equals(Path.GetFullPath(path), _pathToFail, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated cleanup failure.");
            }

            File.Delete(path);
        }
    }

    private sealed class PostReplaceFailingValidation(string sourcePath) : IWorkbookPackageValidation
    {
        private readonly string _sourcePath = Path.GetFullPath(sourcePath);

        public string ValidateWorkbookPackage(string workbookPath, string? expectedVersion = null)
        {
            workbookPath = Path.GetFullPath(workbookPath);
            if (string.Equals(workbookPath, _sourcePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(expectedVersion, TestRepo.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Simulated post-replace validation failure.");
            }

            return WorkbookPackageValidator.ValidateWorkbookPackage(workbookPath, expectedVersion);
        }
    }

    private sealed class SecondSourceValidationFailingValidation(string sourcePath) : IWorkbookPackageValidation
    {
        private readonly string _sourcePath = Path.GetFullPath(sourcePath);
        private int _sourceValidationCount;

        public string ValidateWorkbookPackage(string workbookPath, string? expectedVersion = null)
        {
            workbookPath = Path.GetFullPath(workbookPath);
            if (string.Equals(workbookPath, _sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _sourceValidationCount++;
                if (_sourceValidationCount == 1)
                {
                    throw new InvalidDataException("Simulated restored workbook validation failure.");
                }
            }

            return WorkbookPackageValidator.ValidateWorkbookPackage(workbookPath, expectedVersion);
        }
    }
}
