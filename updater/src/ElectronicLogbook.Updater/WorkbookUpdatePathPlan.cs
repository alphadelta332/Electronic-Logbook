namespace ElectronicLogbook.Updater;

public sealed record WorkbookUpdatePathPlan(
    string OutputPath,
    string? MigrationOutputPath,
    bool UseInPlaceSwap,
    string HandoffNote);

public static class WorkbookUpdatePathPlanner
{
    public static WorkbookUpdatePathPlan Resolve(
        string sourcePath,
        string requestedOutputPath,
        bool useInPlaceSwap,
        Func<string, bool>? isCloudSynced = null,
        Func<string, string>? buildLocalMigrationOutputPath = null)
    {
        isCloudSynced ??= CloudStoragePath.IsLikelyCloudSynced;
        buildLocalMigrationOutputPath ??= BuildDefaultLocalMigrationOutputPath;

        if (useInPlaceSwap && isCloudSynced(sourcePath))
        {
            return new WorkbookUpdatePathPlan(
                requestedOutputPath,
                buildLocalMigrationOutputPath(sourcePath),
                UseInPlaceSwap: true,
                "OneDrive/cloud storage detected; migrating locally before safely replacing the original workbook filename.");
        }

        return new WorkbookUpdatePathPlan(
            requestedOutputPath,
            MigrationOutputPath: null,
            useInPlaceSwap,
            HandoffNote: string.Empty);
    }

    private static string BuildDefaultLocalMigrationOutputPath(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ElectronicLogbookUpdater",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"{name}_Updated_Working{extension}");
    }
}
