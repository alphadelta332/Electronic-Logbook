using System.Text.Json;

namespace ElectronicLogbook.Updater;

public static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "portable", StringComparison.OrdinalIgnoreCase))
        {
            return await PortableLogbookCommandRunner.RunAsync(args[1..]);
        }

        string? downloadDirectory = null;
        UpdaterOptions? options = null;
        string? masterPath = null;
        string? finalWorkbookPath = null;
        MigrationReport? report = null;
        RecordingUpdaterProgressSink? progressSink = null;
        try
        {
            options = UpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(UpdaterOptions.HelpText);
                return 0;
            }

            Console.WriteLine("Electronic Logbook external updater prototype");
            Console.WriteLine(options.InPlaceSwap
                ? "In-place mode enabled: source filename will be replaced after validation."
                : "The source workbook will not be modified.");

            var recovery = WorkbookHandoff.RecoverIfNeeded(options.SourcePath!);
            if (recovery.Action != HandoffRecoveryAction.None)
            {
                Console.WriteLine($"[updater] {recovery.Message}");
                if (!string.IsNullOrWhiteSpace(recovery.SourceWorkbookPath))
                {
                    Console.WriteLine($"[updater] Recovered workbook: {recovery.SourceWorkbookPath}");
                }
                if (!string.IsNullOrWhiteSpace(recovery.BackupWorkbookPath))
                {
                    Console.WriteLine($"[updater] {recovery.BackupWorkbookLabel}: {recovery.BackupWorkbookPath}");
                }
            }

            masterPath = options.MasterPath;
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

            progressSink = new RecordingUpdaterProgressSink(new ConsoleUpdaterProgressSink());
            var migrator = new ExcelWorkbookMigrator(progressSink);
            report = migrator.Migrate(new MigrationRequest(
                options.SourcePath!,
                masterPath,
                options.OutputPath!,
                manifest));

            finalWorkbookPath = options.OutputPath!;
            string? backupWorkbookPath = null;
            if (options.InPlaceSwap)
            {
                WorkbookPackageValidator.ValidateStagedWorkbook(
                    options.OutputPath!,
                    report.OutputVersion);
                var handoff = WorkbookHandoff.ReplaceSourceWithUpdated(
                    options.SourcePath!,
                    options.OutputPath!,
                    report.OutputVersion,
                    report.SourceVersion);
                finalWorkbookPath = handoff.FinalWorkbookPath;
                backupWorkbookPath = handoff.BackupWorkbookPath;
                WorkbookHandoff.CompletePostHandoffValidation(
                    finalWorkbookPath,
                    backupWorkbookPath,
                    report.OutputVersion,
                    report.SourceVersion);
            }

            var reportPath = options.ReportPath ??
                Path.ChangeExtension(finalWorkbookPath, ".update-report.json");
            await WriteDiagnosticBundleAsync(
                reportPath,
                report,
                progressSink.Events,
                error: null,
                options.SourcePath,
                masterPath,
                finalWorkbookPath);

            Console.WriteLine($"Updated workbook: {finalWorkbookPath}");
            if (!string.IsNullOrWhiteSpace(backupWorkbookPath))
            {
                Console.WriteLine($"Backup workbook: {backupWorkbookPath}");
            }
            Console.WriteLine($"Validation report: {reportPath}");
            return 0;
        }
        catch (UpdaterUsageException ex)
        {
            Console.Error.WriteLine(DiagnosticBundleFactory.RedactSensitiveText(ex.Message));
            Console.Error.WriteLine();
            Console.Error.WriteLine(UpdaterOptions.HelpText);
            return 2;
        }
        catch (Exception ex)
        {
            if (options is not null)
            {
                var reportPath = options.ReportPath ??
                    Path.ChangeExtension(finalWorkbookPath ?? options.OutputPath!, ".update-report.json");
                await TryWriteDiagnosticBundleAsync(
                    reportPath,
                    report,
                    progressSink?.Events ?? [],
                    ex,
                    options.SourcePath,
                    masterPath,
                    finalWorkbookPath ?? options.OutputPath);
            }

            Console.Error.WriteLine(
                $"Update failed: {DiagnosticBundleFactory.RedactSensitiveText(ex.Message)}");
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

    private static async Task WriteDiagnosticBundleAsync(
        string path,
        MigrationReport? report,
        IReadOnlyList<UpdaterProgressEvent> progressEvents,
        Exception? error,
        string? sourceWorkbookPath,
        string? masterWorkbookPath,
        string? outputWorkbookPath)
    {
        var applicationVersion = report?.OutputVersion ?? "unknown";
        var diagnosticBundle = DiagnosticBundleFactory.Create(
            applicationVersion,
            report,
            progressEvents,
            error,
            sourceWorkbookPath,
            masterWorkbookPath,
            outputWorkbookPath);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(diagnosticBundle, JsonDefaults.Indented));
    }

    private static async Task TryWriteDiagnosticBundleAsync(
        string path,
        MigrationReport? report,
        IReadOnlyList<UpdaterProgressEvent> progressEvents,
        Exception error,
        string? sourceWorkbookPath,
        string? masterWorkbookPath,
        string? outputWorkbookPath)
    {
        try
        {
            await WriteDiagnosticBundleAsync(
                path,
                report,
                progressEvents,
                error,
                sourceWorkbookPath,
                masterWorkbookPath,
                outputWorkbookPath);
            Console.Error.WriteLine($"Diagnostic report: {path}");
        }
        catch (Exception reportError)
        {
            Console.Error.WriteLine($"Could not write diagnostic report: {reportError.Message}");
        }
    }
}
