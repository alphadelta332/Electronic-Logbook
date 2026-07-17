namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookUpdatePathPlannerTests
{
    [Fact]
    public void ResolveKeepsInPlaceHandoffForCloudSyncedSource()
    {
        var source = Path.Combine(Path.GetTempPath(), "OneDrive", "logbook.xlsm");
        var output = Path.Combine(Path.GetTempPath(), "OneDrive", "logbook_Updated.xlsm");
        var localMigrationOutput = Path.Combine(Path.GetTempPath(), "local", "logbook_Updated_Working.xlsm");

        var plan = WorkbookUpdatePathPlanner.Resolve(
            source,
            output,
            useInPlaceSwap: true,
            isCloudSynced: _ => true,
            buildLocalMigrationOutputPath: _ => localMigrationOutput);

        Assert.True(plan.UseInPlaceSwap);
        Assert.Equal(Path.GetFullPath(output), Path.GetFullPath(plan.OutputPath));
        Assert.Equal(Path.GetFullPath(localMigrationOutput), Path.GetFullPath(plan.MigrationOutputPath!));
        Assert.Contains("replacing the original workbook filename", plan.HandoffNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDoesNotCreateLocalMigrationOutputForSeparateOutputMode()
    {
        var source = Path.Combine(Path.GetTempPath(), "OneDrive", "logbook.xlsm");
        var output = Path.Combine(Path.GetTempPath(), "OneDrive", "logbook_Updated.xlsm");

        var plan = WorkbookUpdatePathPlanner.Resolve(
            source,
            output,
            useInPlaceSwap: false,
            isCloudSynced: _ => true,
            buildLocalMigrationOutputPath: _ => throw new InvalidOperationException("Should not be called."));

        Assert.False(plan.UseInPlaceSwap);
        Assert.Null(plan.MigrationOutputPath);
        Assert.Equal(Path.GetFullPath(output), Path.GetFullPath(plan.OutputPath));
        Assert.Equal(string.Empty, plan.HandoffNote);
    }

    [Fact]
    public void ResolveUsesDirectStagingForNonCloudInPlaceSource()
    {
        var source = Path.Combine(Path.GetTempPath(), "Logbooks", "logbook.xlsm");
        var output = Path.Combine(Path.GetTempPath(), "Logbooks", "logbook_Updated.xlsm");

        var plan = WorkbookUpdatePathPlanner.Resolve(
            source,
            output,
            useInPlaceSwap: true,
            isCloudSynced: _ => false,
            buildLocalMigrationOutputPath: _ => throw new InvalidOperationException("Should not be called."));

        Assert.True(plan.UseInPlaceSwap);
        Assert.Null(plan.MigrationOutputPath);
        Assert.Equal(Path.GetFullPath(output), Path.GetFullPath(plan.OutputPath));
        Assert.Equal(string.Empty, plan.HandoffNote);
    }
}
