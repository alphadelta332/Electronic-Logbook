using System.Text.Json;

namespace ElectronicLogbook.Updater;

public sealed record HandoffResult(
    string FinalWorkbookPath,
    string BackupWorkbookPath);

public static class WorkbookHandoff
{
    public static HandoffResult ReplaceSourceWithUpdated(
        string sourceWorkbookPath,
        string stagedUpdatedWorkbookPath,
        string? expectedFinalVersion = null,
        string? expectedBackupVersion = null)
    {
        return ReplaceSourceWithUpdated(
            sourceWorkbookPath,
            stagedUpdatedWorkbookPath,
            expectedFinalVersion,
            expectedBackupVersion,
            PhysicalWorkbookFileSystem.Instance);
    }

    internal static HandoffResult ReplaceSourceWithUpdated(
        string sourceWorkbookPath,
        string stagedUpdatedWorkbookPath,
        string? expectedFinalVersion,
        string? expectedBackupVersion,
        IWorkbookFileSystem fileSystem)
    {
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        stagedUpdatedWorkbookPath = Path.GetFullPath(stagedUpdatedWorkbookPath);

        RecoverIfNeeded(sourceWorkbookPath, fileSystem);

        if (!fileSystem.FileExists(sourceWorkbookPath))
        {
            throw new FileNotFoundException("Source workbook not found.", sourceWorkbookPath);
        }
        if (!fileSystem.FileExists(stagedUpdatedWorkbookPath))
        {
            throw new FileNotFoundException("Staged updated workbook not found.", stagedUpdatedWorkbookPath);
        }
        if (string.Equals(sourceWorkbookPath, stagedUpdatedWorkbookPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Staged update path must differ from source workbook path.");
        }
        if (!string.IsNullOrWhiteSpace(expectedBackupVersion))
        {
            WorkbookPackageValidator.ValidateWorkbookPackage(sourceWorkbookPath, expectedBackupVersion);
        }
        if (!string.IsNullOrWhiteSpace(expectedFinalVersion))
        {
            WorkbookPackageValidator.ValidateWorkbookPackage(stagedUpdatedWorkbookPath, expectedFinalVersion);
        }

        var backupPath = BuildBackupPath(sourceWorkbookPath, fileSystem);
        var replacementPath = StageReplacementBesideSource(sourceWorkbookPath, stagedUpdatedWorkbookPath, fileSystem);
        var localStageCreated = !string.Equals(
            replacementPath,
            stagedUpdatedWorkbookPath,
            StringComparison.OrdinalIgnoreCase);
        var journalPath = BuildJournalPath(sourceWorkbookPath);
        var journal = new HandoffJournal(
            sourceWorkbookPath,
            stagedUpdatedWorkbookPath,
            replacementPath,
            backupPath,
            localStageCreated,
            DateTimeOffset.UtcNow);
        WriteJournal(journalPath, journal, fileSystem);

        try
        {
            fileSystem.ReplaceFile(
                replacementPath,
                sourceWorkbookPath,
                backupPath,
                ignoreMetadataErrors: true);
            if (localStageCreated)
            {
                fileSystem.DeleteFile(stagedUpdatedWorkbookPath);
            }
            if (!string.IsNullOrWhiteSpace(expectedFinalVersion))
            {
                WorkbookPackageValidator.ValidateWorkbookPackage(sourceWorkbookPath, expectedFinalVersion);
            }
            if (!string.IsNullOrWhiteSpace(expectedBackupVersion))
            {
                WorkbookPackageValidator.ValidateWorkbookPackage(backupPath, expectedBackupVersion);
            }
            fileSystem.DeleteFile(journalPath);

            return new HandoffResult(sourceWorkbookPath, backupPath);
        }
        catch (Exception ex)
        {
            if (localStageCreated && fileSystem.FileExists(replacementPath))
            {
                TryDelete(replacementPath, fileSystem);
            }

            throw new InvalidOperationException(
                $"Failed to finalise workbook handoff: {ex.Message}",
                ex);
        }
    }

    public static void RecoverIfNeeded(string sourceWorkbookPath)
    {
        RecoverIfNeeded(sourceWorkbookPath, PhysicalWorkbookFileSystem.Instance);
    }

    internal static void RecoverIfNeeded(
        string sourceWorkbookPath,
        IWorkbookFileSystem fileSystem)
    {
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        var journalPath = BuildJournalPath(sourceWorkbookPath);
        if (!fileSystem.FileExists(journalPath))
        {
            return;
        }

        var journal = ReadJournal(journalPath, fileSystem);
        if (!string.Equals(journal.SourceWorkbookPath, sourceWorkbookPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Handoff journal {journalPath} does not match source workbook {sourceWorkbookPath}.");
        }

        var sourceExists = fileSystem.FileExists(journal.SourceWorkbookPath);
        var backupExists = fileSystem.FileExists(journal.BackupWorkbookPath);
        var replacementExists = fileSystem.FileExists(journal.ReplacementWorkbookPath);

        if (sourceExists)
        {
            if (journal.LocalStageCreated && replacementExists)
            {
                TryDelete(journal.ReplacementWorkbookPath, fileSystem);
            }
            if (backupExists && journal.LocalStageCreated)
            {
                TryDelete(journal.StagedUpdatedWorkbookPath, fileSystem);
            }
            fileSystem.DeleteFile(journalPath);
            return;
        }

        if (backupExists)
        {
            fileSystem.MoveFile(journal.BackupWorkbookPath, journal.SourceWorkbookPath);
            if (journal.LocalStageCreated && replacementExists)
            {
                TryDelete(journal.ReplacementWorkbookPath, fileSystem);
            }
            fileSystem.DeleteFile(journalPath);
            return;
        }

        throw new InvalidOperationException(
            $"Cannot recover interrupted handoff for {sourceWorkbookPath}: source and backup are both missing.");
    }

    public static void CompletePostHandoffValidation(
        string sourceWorkbookPath,
        string retainedBackupWorkbookPath,
        string expectedSourceVersion,
        string? expectedBackupVersion = null)
    {
        CompletePostHandoffValidation(
            sourceWorkbookPath,
            retainedBackupWorkbookPath,
            expectedSourceVersion,
            expectedBackupVersion,
            PhysicalWorkbookFileSystem.Instance);
    }

    internal static void CompletePostHandoffValidation(
        string sourceWorkbookPath,
        string retainedBackupWorkbookPath,
        string expectedSourceVersion,
        string? expectedBackupVersion,
        IWorkbookFileSystem fileSystem)
    {
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        retainedBackupWorkbookPath = Path.GetFullPath(retainedBackupWorkbookPath);

        WorkbookPackageValidator.ValidateWorkbookPackage(sourceWorkbookPath, expectedSourceVersion);
        WorkbookPackageValidator.ValidateWorkbookPackage(retainedBackupWorkbookPath, expectedBackupVersion);
        PruneOlderBackups(sourceWorkbookPath, retainedBackupWorkbookPath, fileSystem);
    }

    private static string BuildBackupPath(
        string sourceWorkbookPath,
        IWorkbookFileSystem fileSystem)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var baseName = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var extension = Path.GetExtension(sourceWorkbookPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var candidate = Path.Combine(directory, $"{baseName}_Old_{timestamp}{extension}");
        var counter = 1;
        while (fileSystem.FileExists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_Old_{timestamp}_{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static void PruneOlderBackups(
        string sourceWorkbookPath,
        string retainedBackupWorkbookPath,
        IWorkbookFileSystem fileSystem)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var baseName = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var extension = Path.GetExtension(sourceWorkbookPath);
        var retainedFullPath = Path.GetFullPath(retainedBackupWorkbookPath);

        foreach (var backup in fileSystem.EnumerateFiles(directory, $"{baseName}_Old_*{extension}"))
        {
            var backupFullPath = Path.GetFullPath(backup);
            if (string.Equals(backupFullPath, retainedFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fileSystem.DeleteFile(backupFullPath);
        }
    }

    private static string StageReplacementBesideSource(
        string sourceWorkbookPath,
        string stagedUpdatedWorkbookPath,
        IWorkbookFileSystem fileSystem)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var stagedDirectory = Path.GetDirectoryName(stagedUpdatedWorkbookPath) ??
            throw new InvalidOperationException("Staged workbook directory is unavailable.");

        if (string.Equals(sourceDirectory, stagedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return stagedUpdatedWorkbookPath;
        }

        var extension = Path.GetExtension(sourceWorkbookPath);
        var localStagedPath = Path.Combine(
            sourceDirectory,
            $".{Path.GetFileNameWithoutExtension(sourceWorkbookPath)}_Staged_{Guid.NewGuid():N}{extension}");
        fileSystem.CopyFile(stagedUpdatedWorkbookPath, localStagedPath, overwrite: false);
        return localStagedPath;
    }

    private static void TryDelete(string path, IWorkbookFileSystem fileSystem)
    {
        try
        {
            fileSystem.DeleteFile(path);
        }
        catch
        {
            // Preserve the original handoff failure.
        }
    }

    private static string BuildJournalPath(string sourceWorkbookPath)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        return Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(sourceWorkbookPath)}_handoff.json");
    }

    private static void WriteJournal(string journalPath, HandoffJournal journal)
    {
        WriteJournal(journalPath, journal, PhysicalWorkbookFileSystem.Instance);
    }

    private static void WriteJournal(
        string journalPath,
        HandoffJournal journal,
        IWorkbookFileSystem fileSystem)
    {
        var tempPath = $"{journalPath}.{Guid.NewGuid():N}.tmp";
        fileSystem.WriteAllText(tempPath, JsonSerializer.Serialize(journal, JsonDefaults.Indented));
        fileSystem.MoveFile(tempPath, journalPath, overwrite: true);
    }

    private static HandoffJournal ReadJournal(
        string journalPath,
        IWorkbookFileSystem fileSystem)
    {
        return JsonSerializer.Deserialize<HandoffJournal>(
            fileSystem.ReadAllText(journalPath),
            JsonDefaults.Web) ?? throw new InvalidDataException(
                $"Handoff journal could not be parsed: {journalPath}");
    }

    private sealed record HandoffJournal(
        string SourceWorkbookPath,
        string StagedUpdatedWorkbookPath,
        string ReplacementWorkbookPath,
        string BackupWorkbookPath,
        bool LocalStageCreated,
        DateTimeOffset CreatedAtUtc);
}

internal interface IWorkbookFileSystem
{
    bool FileExists(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void DeleteFile(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);
    string ReadAllText(string path);
    void ReplaceFile(
        string sourceFileName,
        string destinationFileName,
        string destinationBackupFileName,
        bool ignoreMetadataErrors);
    void WriteAllText(string path, string contents);
}

internal sealed class PhysicalWorkbookFileSystem : IWorkbookFileSystem
{
    public static PhysicalWorkbookFileSystem Instance { get; } = new();

    private PhysicalWorkbookFileSystem()
    {
    }

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
