using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly IReadOnlyDictionary<string, int> _phaseProgress = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [UpdaterPhaseIds.StartExcel] = 5,
        [UpdaterPhaseIds.OpenSourceWorkbook] = 10,
        [UpdaterPhaseIds.OpenMasterCopy] = 15,
        [UpdaterPhaseIds.PrepareMasterCopy] = 20,
        [UpdaterPhaseIds.ReadSourceValidationData] = 25,
        [UpdaterPhaseIds.CopyLogbookData] = 40,
        [UpdaterPhaseIds.CopyKeywordsData] = 50,
        [UpdaterPhaseIds.CopyRoutesData] = 58,
        [UpdaterPhaseIds.CopyAirportBaseFlags] = 64,
        [UpdaterPhaseIds.CopyNamedPreferences] = 70,
        [UpdaterPhaseIds.RestoreLogbookPresentation] = 76,
        [UpdaterPhaseIds.CalculateOutputWorkbook] = 82,
        [UpdaterPhaseIds.RefreshPivotTables] = 88,
        [UpdaterPhaseIds.UpdateHoursOverTimeChart] = 92,
        [UpdaterPhaseIds.ValidatePreservedData] = 96,
        [UpdaterPhaseIds.SaveOutputWorkbook] = 99,
        [UpdaterPhaseIds.Completed] = 100
    };

    private int _stepIndex;
    private bool _isUpdating;
    private bool _isCheckingAvailability = true;
    private bool _availabilityReady;
    private bool _preflightPassed;
    private string? _latestTag;
    private string? _lastOutputPath;
    private string? _lastBackupPath;
    private string? _lastReportPath;
    private string? _downloadDirectoryToCleanup;
    private CancellationTokenSource? _updateCts;

    public MainWindow()
    {
        InitializeComponent();

        _context = ResolveRunContext();
        _lastOutputPath = _context.OutputPath;

        UpdateWizardView();
        _ = InitialiseAvailabilityAsync();
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
            0 => _availabilityReady && !_isCheckingAvailability,
            1 => _availabilityReady && !_isCheckingAvailability,
            2 => _preflightPassed,
            3 => true,
            4 => false,
            5 => true,
            _ => false
        };
    }

    private async Task InitialiseAvailabilityAsync()
    {
        _isCheckingAvailability = true;
        _availabilityReady = false;
        FooterStatusText.Text = "Checking update channel...";
        UpdateWizardView();

        var installedVersion = await Task.Run(() => TryReadWorkbookVersion(_context.SourcePath));
        InstalledVersionText.Text = string.IsNullOrWhiteSpace(installedVersion)
            ? "Installed version: unknown"
            : $"Installed version: {installedVersion}";

        var identifiedInstalledVersion = !string.IsNullOrWhiteSpace(installedVersion);
        var identifiedUpdateChannel = false;

        if (_context.UsesProvidedMaster)
        {
            var masterVersion = await Task.Run(() => TryReadWorkbookVersion(_context.MasterPath!));
            identifiedUpdateChannel = !string.IsNullOrWhiteSpace(masterVersion);
            var channelName = _context.Channel switch
            {
                UpdateChannel.Development => "Development",
                UpdateChannel.LocalMaster => "Local Master",
                _ => "Local Master"
            };
            LatestVersionText.Text = string.IsNullOrWhiteSpace(masterVersion)
                ? $"Update channel: {channelName} (version unavailable)"
                : $"Update channel: {channelName} ({masterVersion})";
            LastCheckedText.Text = $"Configured: {DateTime.Now:G}";
            AvailableVersionText.Text = _context.Channel == UpdateChannel.Development
                ? (string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using development build"
                    : $"Development version: {masterVersion}")
                : (string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using local master build"
                    : $"Local master version: {masterVersion}");
            ReleaseSummaryText.Text = await GetDevBranchReadmeSummaryAsync(
                _context.Repository,
                installedVersion,
                masterVersion);
        }
        else
        {
            identifiedUpdateChannel = await CheckForReleaseAvailabilityAsync();
        }

        _availabilityReady = identifiedInstalledVersion && identifiedUpdateChannel;
        _isCheckingAvailability = false;
        FooterStatusText.Text = _availabilityReady
            ? "Ready"
            : "Could not identify installed version or update channel.";
        UpdateWizardView();
    }

    private async Task<bool> CheckForReleaseAvailabilityAsync()
    {
        try
        {
            var (tag, summary) = await GetLatestReleaseInfoAsync(_context.Repository);
            _latestTag = tag;
            LatestVersionText.Text = $"Update channel: Stable ({tag})";
            LastCheckedText.Text = $"Last checked: {DateTime.Now:G}";
            AvailableVersionText.Text = $"Stable version: {tag}";
            ReleaseSummaryText.Text = string.IsNullOrWhiteSpace(summary)
                ? "No release notes summary was returned by GitHub."
                : summary;
            return true;
        }
        catch (Exception ex)
        {
            LatestVersionText.Text = "Update channel: Stable check failed";
            LastCheckedText.Text = $"Last checked: {DateTime.Now:G}";
            AvailableVersionText.Text = "Could not fetch release details.";
            ReleaseSummaryText.Text = ex.Message;
            return false;
        }
    }

    private static async Task<string> GetDevBranchReadmeSummaryAsync(
        string repository,
        string? installedVersion,
        string? targetVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/'))
            {
                return "Could not load dev-branch README notes: repository format is invalid.";
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ElectronicLogbook-UpdaterWizard/0.1");
            var url = $"https://raw.githubusercontent.com/{repository}/dev/README.md";
            var markdown = await client.GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return "Dev-branch README is empty.";
            }

            return ExtractChangelogDelta(markdown, installedVersion, targetVersion);
        }
        catch (Exception ex)
        {
            return $"Could not load dev-branch README notes: {ex.Message}";
        }
    }

    private static string ExtractChangelogDelta(
        string readmeMarkdown,
        string? installedVersion,
        string? targetVersion)
    {
        var Normalised = readmeMarkdown.Replace("\r\n", "\n");
        var changelogIndex = Normalised.IndexOf("## Changelog", StringComparison.OrdinalIgnoreCase);
        if (changelogIndex < 0)
        {
            return "No changelog section found in dev-branch README.";
        }

        var tail = Normalised[changelogIndex..];
        var nextSectionIndex = tail.IndexOf("\n## ", StringComparison.Ordinal);
        var changelogSection = nextSectionIndex > 0 ? tail[..nextSectionIndex] : tail;

        var entryMatches = Regex.Matches(
            changelogSection,
            "(?ms)^### \\[(?<version>\\d+\\.\\d+\\.\\d+)\\].*?(?=^### \\[|\\z)");

        if (entryMatches.Count == 0)
        {
            return "No versioned changelog entries found in dev-branch README.";
        }

        var installed = TryParseSemVer(installedVersion);
        var target = TryParseSemVer(targetVersion);

        var included = new List<string>();
        foreach (Match match in entryMatches)
        {
            if (!match.Success)
            {
                continue;
            }

            var versionText = match.Groups["version"].Value;
            var entryVersion = TryParseSemVer(versionText);
            if (entryVersion is null)
            {
                continue;
            }

            var entryVersionValue = entryVersion.Value;

            var afterInstalled = installed is null || CompareSemVer(entryVersionValue, installed) > 0;
            var upToTarget = target is null || CompareSemVer(entryVersionValue, target) <= 0;
            if (afterInstalled && upToTarget)
            {
                included.Add(match.Value.Trim());
            }
        }

        if (included.Count == 0)
        {
            return targetVersion is null
                ? "No newer changelog entries were found from your installed version."
                : $"No changelog entries found between {installedVersion ?? "current"} and {targetVersion}.";
        }

        var merged = string.Join("\n\n", included);
        const int maxChars = 1200;
        if (merged.Length > maxChars)
        {
            merged = merged[..maxChars] + "...";
        }

        return merged;
    }

    private static (int Major, int Minor, int Patch)? TryParseSemVer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value.Trim(), "^(?:v)?(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)$");
        if (!match.Success)
        {
            return null;
        }

        return (
            int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture));
    }

    private static int CompareSemVer((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch)? right)
    {
        if (right is null)
        {
            return 1;
        }

        var rightValue = right.Value;
        var major = left.Major.CompareTo(rightValue.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(rightValue.Minor);
        if (minor != 0)
        {
            return minor;
        }

        return left.Patch.CompareTo(rightValue.Patch);
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
        var stagedOutput = _context.UseInPlaceSwap
            ? BuildStagedOutputPath(source)
            : _context.OutputPath;

        CheckSourcePathText.Text = "[ ] Waiting for source workbook to close...";
        FooterStatusText.Text = "Waiting for source workbook to close...";
        var sourceCheck = await WaitForSourceWorkbookAsync(source);
        var sourceOk = sourceCheck.IsOk;
        CheckSourcePathText.Text = sourceOk
            ? "[OK] Source workbook exists, is .xlsm, and is closed"
            : $"[FAIL] {sourceCheck.Message}";

        var outputDir = string.IsNullOrWhiteSpace(stagedOutput)
            ? string.Empty
            : (Path.GetDirectoryName(stagedOutput) ?? string.Empty);
        var outputDirExists = !string.IsNullOrWhiteSpace(outputDir) && Directory.Exists(outputDir);
        var outputMissing = !File.Exists(stagedOutput);
        var outputExtOk = string.Equals(Path.GetExtension(stagedOutput), ".xlsm", StringComparison.OrdinalIgnoreCase);
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

        var channelOk = _context.UsesProvidedMaster
            ? File.Exists(_context.MasterPath!)
            : (!string.IsNullOrWhiteSpace(_context.Repository) && _context.Repository.Contains('/'));
        CheckMasterOrRepoText.Text = channelOk
            ? (_context.UsesProvidedMaster
                ? $"[OK] {_context.ChannelDisplayName} master workbook is available"
                : "[OK] Stable repository format is valid")
            : (_context.UsesProvidedMaster
                ? $"[FAIL] {_context.ChannelDisplayName} master workbook is missing"
                : "[FAIL] Stable repository format is invalid");

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

    private async Task RunPreflightAndAdvanceAsync()
    {
        await RunPreflightAsync();
        if (_preflightPassed)
        {
            _stepIndex = 3;
            UpdateWizardView();
            return;
        }

        MessageBox.Show(
            this,
            "Preflight checks failed. Fix the issue and try again.",
            "Preflight failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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

        if (_stepIndex == 3)
        {
            _stepIndex = 4;
            UpdateWizardView();
            await StartUpdateAsync();
            return;
        }

        if (_stepIndex == 1)
        {
            _stepIndex = 2;
            UpdateWizardView();
            await RunPreflightAndAdvanceAsync();
            return;
        }

        if (_stepIndex == 2)
        {
            await RunPreflightAndAdvanceAsync();
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
        UpdateProgressBar.IsIndeterminate = false;
        UpdateProgressBar.Value = 0;
        UpdateLogTextBox.Clear();

        var progressSink = new WizardProgressSink(AppendProgressEvent);

        var source = _context.SourcePath;
        var stagedOutput = _context.UseInPlaceSwap
            ? BuildStagedOutputPath(source)
            : _context.OutputPath;

        try
        {
            string resolvedMaster;
            ReleaseManifest? manifest = null;

            if (_context.UsesProvidedMaster)
            {
                resolvedMaster = _context.MasterPath!;
                AppendLog($"Using {_context.ChannelDisplayName} master workbook: {resolvedMaster}");
            }
            else
            {
                AppendLog($"Resolving stable release from {_context.Repository}...");
                var releaseClient = new ReleaseClient();
                var release = await releaseClient.GetLatestReleaseAsync(_context.Repository, _updateCts.Token);
                _downloadDirectoryToCleanup = release.DownloadDirectory;
                resolvedMaster = release.MasterWorkbookPath;
                manifest = release.Manifest;
                AppendLog($"Using release {manifest.Version} ({manifest.Tag})");
            }

            var sourceCheck = await WaitForSourceWorkbookAsync(source);
            if (!sourceCheck.IsOk)
            {
                throw new InvalidOperationException(sourceCheck.Message);
            }

            var migrator = new ExcelWorkbookMigrator(progressSink);
            var report = await Task.Run(() => migrator.Migrate(new MigrationRequest(
                source,
                resolvedMaster,
                stagedOutput,
                manifest)), _updateCts.Token);

            _lastOutputPath = stagedOutput;
            _lastBackupPath = null;
            _lastReportPath = _context.UseInPlaceSwap
                ? Path.ChangeExtension(source, ".update-report.json")
                : Path.ChangeExtension(stagedOutput, ".update-report.json");
            if (DetailedLoggingCheckBox.IsChecked != false)
            {
                await File.WriteAllTextAsync(
                    _lastReportPath,
                    JsonSerializer.Serialize(report, JsonDefaults.Indented),
                    _updateCts.Token);
            }

            if (_context.UseInPlaceSwap)
            {
                AppendLog("finalising workbook handoff...");
                var handoff = await Task.Run(
                    () => WorkbookHandoff.ReplaceSourceWithUpdated(source, stagedOutput),
                    _updateCts.Token);
                _lastOutputPath = handoff.FinalWorkbookPath;
                _lastBackupPath = handoff.BackupWorkbookPath;
            }

            AppendLog("Waiting for workbook file to settle...");
            var finalWorkbookReady = await WaitForFileToSettleAsync(_lastOutputPath, _updateCts.Token);

            CompleteTitleText.Text = "Update Complete";
            CompleteSummaryText.Text = finalWorkbookReady
                ? (_context.UseInPlaceSwap
                    ? "Update complete. The original filename now points to the updated workbook."
                    : "The updated workbook was created and validated.")
                : "Update complete, but the workbook file is still settling. Wait for OneDrive sync to finish before opening it.";
            CompleteOutputPathText.Text = $"Updated workbook: {_lastOutputPath}";
            CompleteBackupPathText.Text = string.IsNullOrWhiteSpace(_lastBackupPath)
                ? string.Empty
                : $"Backup workbook: {_lastBackupPath}";
            OpenUpdatedButton.IsEnabled = finalWorkbookReady;
            FooterStatusText.Text = finalWorkbookReady
                ? "Update completed."
                : "Update completed. Wait for sync before opening.";

            _stepIndex = 5;
        }
        catch (OperationCanceledException)
        {
            CompleteTitleText.Text = "Update Cancelled";
            CompleteSummaryText.Text = "Update was cancelled before completion.";
            CompleteOutputPathText.Text = "Updated workbook: not created";
            CompleteBackupPathText.Text = string.Empty;
            OpenUpdatedButton.IsEnabled = false;
            FooterStatusText.Text = "Update cancelled.";
            _stepIndex = 5;
        }
        catch (Exception ex)
        {
            CompleteTitleText.Text = "Update Failed";
            CompleteSummaryText.Text = ex.Message;
            CompleteOutputPathText.Text = "Updated workbook: not available";
            CompleteBackupPathText.Text = string.Empty;
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
            AppendLog(progressEvent.Message);

            if (progressEvent.Percent.HasValue)
            {
                UpdateProgressBar.Value = progressEvent.Percent.Value;
            }
            else if (_phaseProgress.TryGetValue(progressEvent.PhaseId, out var phasePercent))
            {
                UpdateProgressBar.Value = phasePercent;
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

    private static string? TryReadWorkbookVersion(string workbookPath)
    {
        if (!File.Exists(workbookPath))
        {
            return null;
        }

        dynamic? excel = null;
        dynamic? workbook = null;

        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType is null)
            {
                return null;
            }

            excel = Activator.CreateInstance(excelType);
            if (excel is null)
            {
                return null;
            }

            dynamic excelApp = excel;
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            excelApp.EnableEvents = false;

            workbook = excelApp.Workbooks.Open(workbookPath, 0, true);
            dynamic versionName = workbook.Names.Item("LogbookVersion");
            dynamic versionRange = versionName.RefersToRange;
            var value = Convert.ToString(versionRange.Value2, CultureInfo.InvariantCulture);

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
        catch
        {
            return null;
        }
        finally
        {
            try { workbook?.Close(false); } catch { }
            try { excel?.Quit(); } catch { }

            if (workbook is not null)
            {
                try { Marshal.FinalReleaseComObject(workbook); } catch { }
            }
            if (excel is not null)
            {
                try { Marshal.FinalReleaseComObject(excel); } catch { }
            }
        }
    }

    private static RunContext ResolveRunContext()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        string? source = null;
        string? output = null;
        string? master = null;
        var repository = UpdaterOptions.DefaultRepository;
        UpdateChannel? channel = null;
        var useInPlaceSwap = true;

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
                case "--channel":
                    channel = ParseUpdateChannel(ReadOptionValue(args, ref index, arg));
                    break;
                case "--inplace":
                    useInPlaceSwap = true;
                    break;
                case "--no-inplace":
                    useInPlaceSwap = false;
                    break;
            }
        }

        source ??= GetDefaultSourcePath();
        output ??= BuildDefaultOutputPath(source);
        master = string.IsNullOrWhiteSpace(master) ? null : Path.GetFullPath(master);
        channel ??= string.IsNullOrWhiteSpace(master)
            ? UpdateChannel.Stable
            : UpdateChannel.LocalMaster;

        return new RunContext(
            SourcePath: Path.GetFullPath(source),
            OutputPath: Path.GetFullPath(output),
            MasterPath: master,
            Repository: repository,
            Channel: channel.Value,
            UseInPlaceSwap: useInPlaceSwap);
    }

    private static UpdateChannel ParseUpdateChannel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stable" => UpdateChannel.Stable,
            "development" => UpdateChannel.Development,
            "dev" => UpdateChannel.Development,
            "local-master" => UpdateChannel.LocalMaster,
            "localmaster" => UpdateChannel.LocalMaster,
            "local" => UpdateChannel.LocalMaster,
            _ => throw new InvalidOperationException($"Unknown update channel: {value}.")
        };
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

    private static string BuildStagedOutputPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(directory, $"{name}_Updated_Staged_{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
    }

    private static bool IsWorkbookLocked(string workbookPath)
    {
        try
        {
            using var stream = File.Open(workbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<PreflightCheckResult> WaitForSourceWorkbookAsync(string source)
    {
        if (!File.Exists(source))
        {
            return new(false, $"Source workbook not found: {source}");
        }

        if (!string.Equals(Path.GetExtension(source), ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, $"Source workbook must be an .xlsm file: {source}");
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!IsWorkbookLocked(source))
            {
                return new(true, "Source workbook is ready.");
            }

            await Task.Delay(1000);
        }

        return new(false, $"Source workbook is still open or locked: {source}");
    }

    private static async Task<bool> WaitForFileToSettleAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        long? lastLength = null;
        DateTime? lastWriteTime = null;
        var stableSamples = 0;

        for (var attempt = 0; attempt < 15; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(path);
                using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                if (lastLength == info.Length && lastWriteTime == info.LastWriteTimeUtc)
                {
                    stableSamples++;
                    if (stableSamples >= 3)
                    {
                        return true;
                    }
                }
                else
                {
                    stableSamples = 0;
                    lastLength = info.Length;
                    lastWriteTime = info.LastWriteTimeUtc;
                }
            }
            catch
            {
                stableSamples = 0;
            }

            await Task.Delay(1000, cancellationToken);
        }

        return false;
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

    private sealed record PreflightCheckResult(bool IsOk, string Message);

    private enum UpdateChannel
    {
        Stable,
        Development,
        LocalMaster
    }

    private sealed record RunContext(
        string SourcePath,
        string OutputPath,
        string? MasterPath,
        string Repository,
        UpdateChannel Channel,
        bool UseInPlaceSwap)
    {
        public bool UsesProvidedMaster => !string.IsNullOrWhiteSpace(MasterPath);

        public string ChannelDisplayName => Channel switch
        {
            UpdateChannel.Stable => "Stable",
            UpdateChannel.Development => "Development",
            UpdateChannel.LocalMaster => "Local Master",
            _ => "Stable"
        };
    }
}
