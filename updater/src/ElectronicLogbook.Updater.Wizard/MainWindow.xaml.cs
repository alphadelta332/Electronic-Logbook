using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Wizard;

public partial class MainWindow : Window
{
    private const int TotalSteps = 6;

    private readonly string[] _stepTitles =
    [
        "Welcome",
        "Update Available",
        "Preflight Checks",
        "Ready to Update",
        "Updating",
        "Complete"
    ];

    private readonly RunContext _context;

    private int _stepIndex;
    private bool _isUpdating;
    private bool _preflightPassed;
    private string? _latestTag;
    private string? _lastOutputPath;
    private string? _lastReportPath;
    private string? _downloadDirectoryToCleanup;
    private CancellationTokenSource? _updateCts;

    public MainWindow()
    {
        InitializeComponent();

        _context = ResolveRunContext();
        _lastOutputPath = _context.OutputPath;

        InstalledVersionText.Text = "Installed version: detected from source workbook during update";

        UpdateWizardView();
        _ = InitializeAvailabilityAsync();
    }

    private void UpdateWizardView()
    {
        StepHeaderText.Text = $"Step {_stepIndex + 1} of {TotalSteps}: {_stepTitles[_stepIndex]}";

        WelcomePanel.Visibility = _stepIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        AvailablePanel.Visibility = _stepIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PreflightPanel.Visibility = _stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = _stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        UpdatingPanel.Visibility = _stepIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        CompletePanel.Visibility = _stepIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = _stepIndex > 0 && !_isUpdating && _stepIndex < 5;
        NextButton.IsEnabled = !_isUpdating && CanAdvanceFromCurrentStep();
        NextButton.Content = _stepIndex == 3 ? "Start" : (_stepIndex == 5 ? "Finish" : "Next");

        CancelButton.Content = _isUpdating ? "Cancel Update" : "Cancel";
    }

    private bool CanAdvanceFromCurrentStep()
    {
        return _stepIndex switch
        {
            0 => true,
            1 => true,
            2 => _preflightPassed,
            3 => true,
            4 => false,
            5 => true,
            _ => false
        };
    }

    private async Task InitializeAvailabilityAsync()
    {
        FooterStatusText.Text = "Checking update channel...";

        if (_context.IsLocalMasterMode)
        {
            LatestVersionText.Text = "Update channel: local master";
            LastCheckedText.Text = $"Configured: {DateTime.Now:G}";
            AvailableVersionText.Text = "Using local master build";
            ReleaseSummaryText.Text = "Release notes are not queried in local-master mode.";
        }
        else
        {
            await CheckForReleaseAvailabilityAsync();
        }

        FooterStatusText.Text = "Ready";
    }

    private async Task CheckForReleaseAvailabilityAsync()
    {
        try
        {
            var (tag, summary) = await GetLatestReleaseInfoAsync(_context.Repository);
            _latestTag = tag;
            LatestVersionText.Text = $"Update channel: latest release ({tag})";
            LastCheckedText.Text = $"Last checked: {DateTime.Now:G}";
            AvailableVersionText.Text = $"Latest available release: {tag}";
            ReleaseSummaryText.Text = string.IsNullOrWhiteSpace(summary)
                ? "No release notes summary was returned by GitHub."
                : summary;
        }
        catch (Exception ex)
        {
            LatestVersionText.Text = "Update channel: release check failed";
            LastCheckedText.Text = $"Last checked: {DateTime.Now:G}";
            AvailableVersionText.Text = "Could not fetch release details.";
            ReleaseSummaryText.Text = ex.Message;
        }
    }

    private static async Task<(string Tag, string Summary)> GetLatestReleaseInfoAsync(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/'))
        {
            throw new InvalidOperationException("Repository must be in owner/name format.");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ElectronicLogbook-UpdaterWizard/0.1");
        var url = $"https://api.github.com/repos/{repository}/releases/latest";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";
        var summary = root.TryGetProperty("body", out var body)
            ? (body.GetString() ?? string.Empty)
            : string.Empty;

        if (summary.Length > 300)
        {
            summary = summary[..300] + "...";
        }

        return (tag, summary);
    }

    private async void RunPreflightButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunPreflightAsync();
    }

    private async Task RunPreflightAsync()
    {
        _preflightPassed = false;

        var source = _context.SourcePath;
        var output = _context.OutputPath;

        var sourceOk = File.Exists(source) &&
            string.Equals(Path.GetExtension(source), ".xlsm", StringComparison.OrdinalIgnoreCase);
        CheckSourcePathText.Text = sourceOk
            ? "[OK] Source workbook exists and is .xlsm"
            : "[FAIL] Source workbook missing or not .xlsm";

        var outputDir = string.IsNullOrWhiteSpace(output)
            ? string.Empty
            : (Path.GetDirectoryName(output) ?? string.Empty);
        var outputDirExists = !string.IsNullOrWhiteSpace(outputDir) && Directory.Exists(outputDir);
        var outputMissing = !File.Exists(output);
        var outputExtOk = string.Equals(Path.GetExtension(output), ".xlsm", StringComparison.OrdinalIgnoreCase);
        var writeAccess = false;
        if (outputDirExists)
        {
            try
            {
                var probe = Path.Combine(outputDir, $".write-test-{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(probe, "probe");
                File.Delete(probe);
                writeAccess = true;
            }
            catch
            {
                writeAccess = false;
            }
        }

        var outputOk = outputDirExists && outputMissing && outputExtOk && writeAccess;
        CheckOutputPathText.Text = outputOk
            ? "[OK] Output path is writable and output file does not exist"
            : "[FAIL] Output path invalid, unwritable, wrong extension, or already exists";

        var channelOk = _context.IsLocalMasterMode
            ? File.Exists(_context.MasterPath!)
            : (!string.IsNullOrWhiteSpace(_context.Repository) && _context.Repository.Contains('/'));
        CheckMasterOrRepoText.Text = channelOk
            ? (_context.IsLocalMasterMode
                ? "[OK] Local master workbook is available"
                : "[OK] Release repository format is valid")
            : (_context.IsLocalMasterMode
                ? "[FAIL] Local master workbook is missing"
                : "[FAIL] Repository format is invalid");

        var diskOk = false;
        if (outputDirExists)
        {
            try
            {
                var root = Path.GetPathRoot(outputDir);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var drive = new DriveInfo(root);
                    diskOk = drive.AvailableFreeSpace > 200L * 1024 * 1024;
                }
            }
            catch
            {
                diskOk = false;
            }
        }

        CheckDiskText.Text = diskOk
            ? "[OK] Disk space is sufficient (>200 MB)"
            : "[FAIL] Disk space check failed or available space too low";

        _preflightPassed = sourceOk && outputOk && channelOk && diskOk;
        PreflightSummaryText.Text = _preflightPassed
            ? "All checks passed. You can continue."
            : "One or more checks failed. Resolve environment issues and run checks again.";

        UpdateWizardView();
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _stepIndex <= 0)
        {
            return;
        }

        _stepIndex--;
        UpdateWizardView();
    }

    private async void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_stepIndex == 5)
        {
            Close();
            return;
        }

        if (_stepIndex == 2 && !_preflightPassed)
        {
            MessageBox.Show(this, "Run preflight checks and resolve failures first.", "Preflight required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_stepIndex == 3)
        {
            _stepIndex = 4;
            UpdateWizardView();
            await StartUpdateAsync();
            return;
        }

        if (_stepIndex < 5)
        {
            _stepIndex++;
            UpdateWizardView();
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            _updateCts?.Cancel();
            FooterStatusText.Text = "Cancellation requested. Waiting for a safe checkpoint...";
            return;
        }

        Close();
    }

    private async Task StartUpdateAsync()
    {
        if (_isUpdating)
        {
            return;
        }

        _isUpdating = true;
        _updateCts = new CancellationTokenSource();
        FooterStatusText.Text = "Updating...";
        UpdateProgressBar.IsIndeterminate = true;
        UpdateProgressBar.Value = 0;
        UpdateLogTextBox.Clear();

        var progressSink = new WizardProgressSink(AppendProgressEvent);

        var source = _context.SourcePath;
        var output = _context.OutputPath;

        try
        {
            string resolvedMaster;
            ReleaseManifest? manifest = null;

            if (_context.IsLocalMasterMode)
            {
                resolvedMaster = _context.MasterPath!;
                AppendLog($"Using local master workbook: {resolvedMaster}");
            }
            else
            {
                AppendLog($"Resolving latest release from {_context.Repository}...");
                var releaseClient = new ReleaseClient();
                var release = await releaseClient.GetLatestReleaseAsync(_context.Repository, _updateCts.Token);
                _downloadDirectoryToCleanup = release.DownloadDirectory;
                resolvedMaster = release.MasterWorkbookPath;
                manifest = release.Manifest;
                AppendLog($"Using release {manifest.Version} ({manifest.Tag})");
            }

            var migrator = new ExcelWorkbookMigrator(progressSink);
            var report = await Task.Run(() => migrator.Migrate(new MigrationRequest(
                source,
                resolvedMaster,
                output,
                manifest)), _updateCts.Token);

            _lastOutputPath = output;
            _lastReportPath = Path.ChangeExtension(output, ".update-report.json");
            if (DetailedLoggingCheckBox.IsChecked != false)
            {
                await File.WriteAllTextAsync(
                    _lastReportPath,
                    JsonSerializer.Serialize(report, JsonDefaults.Indented),
                    _updateCts.Token);
            }

            CompleteTitleText.Text = "Update Complete";
            CompleteSummaryText.Text = "The updated workbook was created and validated.";
            CompleteOutputPathText.Text = $"Updated workbook: {_lastOutputPath}";
            OpenUpdatedButton.IsEnabled = true;
            FooterStatusText.Text = "Update completed.";

            _stepIndex = 5;
        }
        catch (OperationCanceledException)
        {
            CompleteTitleText.Text = "Update Cancelled";
            CompleteSummaryText.Text = "Update was cancelled before completion.";
            CompleteOutputPathText.Text = "Updated workbook: not created";
            OpenUpdatedButton.IsEnabled = false;
            FooterStatusText.Text = "Update cancelled.";
            _stepIndex = 5;
        }
        catch (Exception ex)
        {
            CompleteTitleText.Text = "Update Failed";
            CompleteSummaryText.Text = ex.Message;
            CompleteOutputPathText.Text = "Updated workbook: not available";
            OpenUpdatedButton.IsEnabled = false;
            FooterStatusText.Text = "Update failed.";
            AppendLog($"ERROR: {ex.Message}");
            _stepIndex = 5;
        }
        finally
        {
            CleanupDownloadDirectory();
            _updateCts?.Dispose();
            _updateCts = null;
            _isUpdating = false;
            UpdateProgressBar.IsIndeterminate = false;
            UpdateWizardView();
        }
    }

    private void CleanupDownloadDirectory()
    {
        if (string.IsNullOrWhiteSpace(_downloadDirectoryToCleanup))
        {
            return;
        }

        try
        {
            if (Directory.Exists(_downloadDirectoryToCleanup))
            {
                Directory.Delete(_downloadDirectoryToCleanup, recursive: true);
            }
        }
        catch
        {
            // Keep cleanup best-effort.
        }
        finally
        {
            _downloadDirectoryToCleanup = null;
        }
    }

    private void AppendProgressEvent(UpdaterProgressEvent progressEvent)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatingPhaseText.Text = progressEvent.Message;
            AppendLog($"[{progressEvent.PhaseId}] {progressEvent.Message}");

            if (progressEvent.Percent.HasValue)
            {
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = progressEvent.Percent.Value;
            }
            else
            {
                UpdateProgressBar.IsIndeterminate = true;
            }
        });
    }

    private void AppendLog(string message)
    {
        UpdateLogTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        UpdateLogTextBox.ScrollToEnd();
    }

    private void OpenUpdatedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastOutputPath) || !File.Exists(_lastOutputPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastOutputPath,
            UseShellExecute = true
        });
    }

    private static RunContext ResolveRunContext()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        string? source = null;
        string? output = null;
        string? master = null;
        var repository = UpdaterOptions.DefaultRepository;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--source":
                    source = ReadOptionValue(args, ref index, arg);
                    break;
                case "--output":
                    output = ReadOptionValue(args, ref index, arg);
                    break;
                case "--master":
                    master = ReadOptionValue(args, ref index, arg);
                    break;
                case "--repo":
                    repository = ReadOptionValue(args, ref index, arg);
                    break;
            }
        }

        source ??= GetDefaultSourcePath();
        output ??= BuildDefaultOutputPath(source);
        master = string.IsNullOrWhiteSpace(master) ? null : Path.GetFullPath(master);

        return new RunContext(
            SourcePath: Path.GetFullPath(source),
            OutputPath: Path.GetFullPath(output),
            MasterPath: master,
            Repository: repository,
            IsLocalMasterMode: !string.IsNullOrWhiteSpace(master));
    }

    private static string GetDefaultSourcePath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var preferred = Path.Combine(documents, "Electronic Logbook - Working Copy.xlsm");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var fallback = Path.Combine(documents, "Electronic Logbook.xlsm");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return preferred;
    }

    private static string BuildDefaultOutputPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(directory, $"{name}_Updated_{DateTime.Now:yyyyMMdd-HHmmss}.xlsm");
    }

    private static string ReadOptionValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count)
        {
            throw new InvalidOperationException($"Missing value for {option}.");
        }

        return args[index];
    }

    private sealed class WizardProgressSink(Action<UpdaterProgressEvent> onEvent) : IUpdaterProgressSink
    {
        public void Report(UpdaterProgressEvent progressEvent)
        {
            onEvent(progressEvent);
        }
    }

    private sealed record RunContext(
        string SourcePath,
        string OutputPath,
        string? MasterPath,
        string Repository,
        bool IsLocalMasterMode);
}
