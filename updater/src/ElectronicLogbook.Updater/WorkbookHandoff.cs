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
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        stagedUpdatedWorkbookPath = Path.GetFullPath(stagedUpdatedWorkbookPath);

        RecoverIfNeeded(sourceWorkbookPath);

        if (!File.Exists(sourceWorkbookPath))
        {
            throw new FileNotFoundException("Source workbook not found.", sourceWorkbookPath);
        }
        if (!File.Exists(stagedUpdatedWorkbookPath))
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

        var backupPath = BuildBackupPath(sourceWorkbookPath);
        var replacementPath = StageReplacementBesideSource(sourceWorkbookPath, stagedUpdatedWorkbookPath);
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
        WriteJournal(journalPath, journal);

        try
        {
            File.Replace(
                replacementPath,
                sourceWorkbookPath,
                backupPath,
                ignoreMetadataErrors: true);
            if (localStageCreated)
            {
                File.Delete(stagedUpdatedWorkbookPath);
            }
            if (!string.IsNullOrWhiteSpace(expectedFinalVersion))
            {
                WorkbookPackageValidator.ValidateWorkbookPackage(sourceWorkbookPath, expectedFinalVersion);
            }
            if (!string.IsNullOrWhiteSpace(expectedBackupVersion))
            {
                WorkbookPackageValidator.ValidateWorkbookPackage(backupPath, expectedBackupVersion);
            }
            File.Delete(journalPath);

            return new HandoffResult(sourceWorkbookPath, backupPath);
        }
        catch (Exception ex)
        {
            if (localStageCreated && File.Exists(replacementPath))
            {
                TryDelete(replacementPath);
            }

            throw new InvalidOperationException(
                $"Failed to finalise workbook handoff: {ex.Message}",
                ex);
        }
    }

    public static void RecoverIfNeeded(string sourceWorkbookPath)
    {
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        var journalPath = BuildJournalPath(sourceWorkbookPath);
        if (!File.Exists(journalPath))
        {
            return;
        }

        var journal = ReadJournal(journalPath);
        if (!string.Equals(journal.SourceWorkbookPath, sourceWorkbookPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Handoff journal {journalPath} does not match source workbook {sourceWorkbookPath}.");
        }

        var sourceExists = File.Exists(journal.SourceWorkbookPath);
        var backupExists = File.Exists(journal.BackupWorkbookPath);
        var replacementExists = File.Exists(journal.ReplacementWorkbookPath);

        if (sourceExists)
        {
            if (journal.LocalStageCreated && replacementExists)
            {
                TryDelete(journal.ReplacementWorkbookPath);
            }
            if (backupExists && journal.LocalStageCreated)
            {
                TryDelete(journal.StagedUpdatedWorkbookPath);
            }
            File.Delete(journalPath);
            return;
        }

        if (backupExists)
        {
            File.Move(journal.BackupWorkbookPath, journal.SourceWorkbookPath);
            if (journal.LocalStageCreated && replacementExists)
            {
                TryDelete(journal.ReplacementWorkbookPath);
            }
            File.Delete(journalPath);
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
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        retainedBackupWorkbookPath = Path.GetFullPath(retainedBackupWorkbookPath);

        WorkbookPackageValidator.ValidateWorkbookPackage(sourceWorkbookPath, expectedSourceVersion);
        WorkbookPackageValidator.ValidateWorkbookPackage(retainedBackupWorkbookPath, expectedBackupVersion);
        PruneOlderBackups(sourceWorkbookPath, retainedBackupWorkbookPath);
    }

    private static string BuildBackupPath(string sourceWorkbookPath)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var baseName = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var extension = Path.GetExtension(sourceWorkbookPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var candidate = Path.Combine(directory, $"{baseName}_Old_{timestamp}{extension}");
        var counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_Old_{timestamp}_{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static void PruneOlderBackups(
        string sourceWorkbookPath,
        string retainedBackupWorkbookPath)
    {
        var directory = Path.GetDirectoryName(sourceWorkbookPath) ??
            throw new InvalidOperationException("Source workbook directory is unavailable.");
        var baseName = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var extension = Path.GetExtension(sourceWorkbookPath);
        var retainedFullPath = Path.GetFullPath(retainedBackupWorkbookPath);

        foreach (var backup in Directory.EnumerateFiles(directory, $"{baseName}_Old_*{extension}"))
        {
            var backupFullPath = Path.GetFullPath(backup);
            if (string.Equals(backupFullPath, retainedFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(backupFullPath);
        }
    }

    private static string StageReplacementBesideSource(
        string sourceWorkbookPath,
        string stagedUpdatedWorkbookPath)
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
        File.Copy(stagedUpdatedWorkbookPath, localStagedPath, overwrite: false);
        return localStagedPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
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
        var tempPath = $"{journalPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(journal, JsonDefaults.Indented));
        File.Move(tempPath, journalPath, overwrite: true);
    }

    private static HandoffJournal ReadJournal(string journalPath)
    {
        return JsonSerializer.Deserialize<HandoffJournal>(
            File.ReadAllText(journalPath),
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
