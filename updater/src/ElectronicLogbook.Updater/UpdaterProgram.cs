using System.Text.Json;

namespace ElectronicLogbook.Updater;

public static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? downloadDirectory = null;
        try
        {
            var options = UpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(UpdaterOptions.HelpText);
                return 0;
            }

            Console.WriteLine("Electronic Logbook external updater prototype");
            Console.WriteLine("The source workbook will not be modified.");

            var masterPath = options.MasterPath;
            ReleaseManifest? manifest = null;
            if (masterPath is null)
            {
                var client = new ReleaseClient();
                var release = await client.GetLatestReleaseAsync(options.Repository, CancellationToken.None);
                manifest = release.Manifest;
                downloadDirectory = release.DownloadDirectory;
                masterPath = release.MasterWorkbookPath;
                Console.WriteLine($"Verified release {manifest.Version} ({manifest.Tag}).");
            }

            var progressSink = new ConsoleUpdaterProgressSink();
            var migrator = new ExcelWorkbookMigrator(progressSink);
            var report = migrator.Migrate(new MigrationRequest(
                options.SourcePath!,
                masterPath,
                options.OutputPath!,
                manifest));

            var reportPath = options.ReportPath ??
                Path.ChangeExtension(options.OutputPath!, ".update-report.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(report, JsonDefaults.Indented));

            Console.WriteLine($"Updated copy created: {options.OutputPath}");
            Console.WriteLine($"Validation report: {reportPath}");
            return 0;
        }
        catch (UpdaterUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(UpdaterOptions.HelpText);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Update failed: {ex.Message}");
            return 1;
        }
        finally
        {
            if (downloadDirectory is not null)
            {
                try { Directory.Delete(downloadDirectory, recursive: true); } catch { }
            }
        }
    }
}
