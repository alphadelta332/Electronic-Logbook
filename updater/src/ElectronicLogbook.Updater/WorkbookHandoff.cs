namespace ElectronicLogbook.Updater;

public sealed record HandoffResult(
    string FinalWorkbookPath,
    string BackupWorkbookPath);

public static class WorkbookHandoff
{
    public static HandoffResult ReplaceSourceWithUpdated(
        string sourceWorkbookPath,
        string stagedUpdatedWorkbookPath)
    {
        sourceWorkbookPath = Path.GetFullPath(sourceWorkbookPath);
        stagedUpdatedWorkbookPath = Path.GetFullPath(stagedUpdatedWorkbookPath);

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

        var backupPath = BuildBackupPath(sourceWorkbookPath);
        var sourceMovedToBackup = false;

        try
        {
            File.Move(sourceWorkbookPath, backupPath);
            sourceMovedToBackup = true;

            try
            {
                File.Move(stagedUpdatedWorkbookPath, sourceWorkbookPath);
            }
            catch (IOException)
            {
                // Cross-volume moves can fail; fallback to copy+delete.
                File.Copy(stagedUpdatedWorkbookPath, sourceWorkbookPath, overwrite: false);
                File.Delete(stagedUpdatedWorkbookPath);
            }

            return new HandoffResult(sourceWorkbookPath, backupPath);
        }
        catch (Exception ex)
        {
            if (sourceMovedToBackup && !File.Exists(sourceWorkbookPath) && File.Exists(backupPath))
            {
                try
                {
                    File.Move(backupPath, sourceWorkbookPath);
                }
                catch
                {
                    // Best-effort rollback only.
                }
            }

            throw new InvalidOperationException(
                $"Failed to finalise workbook handoff: {ex.Message}",
                ex);
        }
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
}