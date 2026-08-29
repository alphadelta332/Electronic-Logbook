using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
using ElectronicLogbook.Updater;
using ElectronicLogbook.Portable;
using Microsoft.Win32;

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
    private bool _isCheckingAvailability = true;
    private bool _availabilityReady;
    private bool _preflightPassed;
    private WorkbookPreMigrationSummary? _preMigrationSummary;
    private string? _preMigrationSummarySourceFingerprint;
    private string? _latestTag;
    private string? _lastOutputPath;
    private string? _lastBackupPath;
    private string? _lastBackupExpectedVersion;
    private string? _lastReportPath;
    private string? _lastOutputExpectedVersion;
    private string? _downloadDirectoryToCleanup;
    private CancellationTokenSource? _updateCts;

    public MainWindow()
    {
        InitializeComponent();

        _context = ResolveRunContext();
        _lastOutputPath = _context.OutputPath;

        UpdateWizardView();
    }

    public bool IsHostedConnectionMode => _context.ConnectHosted;

    public void BeginAvailabilityCheck()
    {
        _ = InitialiseAvailabilitySafelyAsync();
    }

    public void BeginHostedConnectionMode()
    {
        _ = InitialiseHostedConnectionModeSafelyAsync();
    }

    private async Task InitialiseHostedConnectionModeSafelyAsync()
    {
        Show();
        Activate();
        FooterStatusText.Text = "Waiting for the workbook to save and close...";
        var sourceCheck = await WaitForSourceWorkbookAsync(_context.SourcePath);
        if (!sourceCheck.IsOk)
        {
            FooterStatusText.Text = sourceCheck.Message;
            MessageBox.Show(
                this,
                sourceCheck.Message,
                "Connect to Electronic Logbook",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await RunHostedConnectionAsync();
    }

    private async Task InitialiseAvailabilitySafelyAsync()
    {
        try
        {
            await InitialiseAvailabilityAsync();
        }
        catch (Exception ex)
        {
            _availabilityReady = false;
            _isCheckingAvailability = false;
            FooterStatusText.Text = $"Availability check failed: {ex.Message}";
            SetReleaseSummaryMarkdown(ex.Message);
            UpdateWizardView();
            Show();
            Activate();
        }
    }

    private void UpdateWizardView()
    {
        if (_context.ConnectHosted)
        {
            StepHeaderText.Text = "Connect to Electronic Logbook";
            WelcomePanel.Visibility = Visibility.Visible;
            AvailablePanel.Visibility = Visibility.Collapsed;
            PreflightPanel.Visibility = Visibility.Collapsed;
            ReadyPanel.Visibility = Visibility.Collapsed;
            UpdatingPanel.Visibility = Visibility.Collapsed;
            CompletePanel.Visibility = Visibility.Collapsed;
            WelcomeTitleText.Text = "Connect this workbook";
            WelcomeDescriptionText.Text = "Sign in to your invited account. The updater will recover the existing hosted logbook key into Windows Credential Manager, register this workbook as its own device, and sync without showing keys or service tokens.";
            InstalledVersionText.Visibility = Visibility.Collapsed;
            LatestVersionText.Visibility = Visibility.Collapsed;
            LastCheckedText.Visibility = Visibility.Collapsed;
            AdvancedRecoveryHeadingText.Visibility = Visibility.Collapsed;
            AdvancedPortableActionsPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
            HostedConnectButton.IsEnabled = !_isUpdating && File.Exists(_context.SourcePath);
            return;
        }

        StepHeaderText.Text = $"Step {_stepIndex + 1} of {TotalSteps}: {_stepTitles[_stepIndex]}";

        WelcomePanel.Visibility = _stepIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        AvailablePanel.Visibility = _stepIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PreflightPanel.Visibility = _stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = _stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        UpdatingPanel.Visibility = _stepIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        CompletePanel.Visibility = _stepIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = _stepIndex > 0 && !_isUpdating && _stepIndex < 5;
        NextButton.IsEnabled = !_isUpdating && CanAdvanceFromCurrentStep();
        NextButton.Content = _stepIndex == 0 && !_availabilityReady
            ? "Retry"
            : (_stepIndex == 3
                ? (_context.Channel == UpdateChannel.Pilot ? "Continue" : "Start")
                : (_stepIndex == 5 ? "Finish" : "Next"));

        UpdatePreMigrationSummaryView();

        CancelButton.Content = _isUpdating ? "Cancel Update" : "Cancel";

        var portableActionsEnabled = !_isUpdating && File.Exists(_context.SourcePath);
        HostedConnectButton.IsEnabled = portableActionsEnabled;
        PortableEnableButton.IsEnabled = portableActionsEnabled;
        PortableExportButton.IsEnabled = portableActionsEnabled;
        PortableImportButton.IsEnabled = portableActionsEnabled;
        PortablePrintedCopyButton.IsEnabled = portableActionsEnabled;
        PortableRevisionHistoryButton.IsEnabled = portableActionsEnabled;
        PortableResolveConflictButton.IsEnabled = portableActionsEnabled;
        PortableRefreshStatusButton.IsEnabled = portableActionsEnabled;
    }

    private bool CanAdvanceFromCurrentStep()
    {
        return _stepIndex switch
        {
            0 => !_isCheckingAvailability,
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

        // Excel starts this wizard before it saves and closes the source workbook.
        // Do not race that hand-off by trying to automate the workbook immediately:
        // an AutoSave/OneDrive workbook can still be locked or mid-save, which made
        // the version look missing even though the workbook was valid.
        FooterStatusText.Text = "Reading logbook details...";
        UpdateWizardView();
        // Read the defined name directly from the XLSM package. Opening the
        // workbook in another Excel instance can trigger a hidden "file in
        // use" prompt, which is precisely what the wizard must avoid here.
        var recovery = RecoverPendingHandoffForWizard();
        if (recovery.Action != HandoffRecoveryAction.None)
        {
            FooterStatusText.Text = recovery.Message;
            UpdateWizardView();
        }

        var installedVersion = await TryReadWorkbookVersionFromPackageWithRetryAsync(_context.SourcePath);
        InstalledVersionText.Text = string.IsNullOrWhiteSpace(installedVersion)
            ? "Installed version: unknown"
            : $"Installed version: {installedVersion}";

        PortableLogbookStatusText.Text = await TryReadPortableLogbookStatusTextWithRetryAsync(_context.SourcePath);

        var compatibilityPolicy = CompatibilityPolicy.LoadDefault();
        var identifiedInstalledVersion = !string.IsNullOrWhiteSpace(installedVersion);
        string? availabilityFailureReason = null;
        if (identifiedInstalledVersion)
        {
            try
            {
                if (!compatibilityPolicy.IsVersionSupported(installedVersion!))
                {
                    identifiedInstalledVersion = false;
                    availabilityFailureReason =
                        "Unable to update - workbook does not meet minimum version requirements.";
                    InstalledVersionText.Text =
                        $"Installed version: {installedVersion} (automatic updates require " +
                        $"{compatibilityPolicy.MinimumSupportedVersion} or newer)";
                }
            }
            catch (InvalidDataException)
            {
                identifiedInstalledVersion = false;
                availabilityFailureReason =
                    "Unable to update - workbook version format is not supported.";
                InstalledVersionText.Text =
                    $"Installed version: {installedVersion} (version format is not supported)";
            }
        }
        var identifiedUpdateChannel = false;

        if (_context.UsesProvidedMaster)
        {
            var masterVersion = await Task.Run(() => TryReadWorkbookVersion(_context.MasterPath!));
            identifiedUpdateChannel = !string.IsNullOrWhiteSpace(masterVersion);
            var channelName = _context.Channel switch
            {
                UpdateChannel.Development => "Development",
                UpdateChannel.Hotfix => "Hotfix",
                UpdateChannel.Pilot => "Pilot",
                UpdateChannel.LocalMaster => "Local Master",
                _ => "Local Master"
            };
            LatestVersionText.Text = string.IsNullOrWhiteSpace(masterVersion)
                ? $"Update channel: {channelName} (version unavailable)"
                : $"Update channel: {channelName} ({masterVersion})";
            LastCheckedText.Text = $"Configured: {DateTime.Now:G}";
            AvailableVersionText.Text = _context.Channel switch
            {
                UpdateChannel.Development => string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using development build"
                    : $"Development version: {masterVersion}",
                UpdateChannel.Hotfix => string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using hotfix build"
                    : $"Hotfix version: {masterVersion}",
                UpdateChannel.Pilot => string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using private pilot build"
                    : $"Pilot version: {masterVersion}",
                _ => string.IsNullOrWhiteSpace(masterVersion)
                    ? "Using local master build"
                    : $"Local master version: {masterVersion}"
            };
            var branchName = _context.Channel switch
            {
                UpdateChannel.Hotfix => "hotfix",
                UpdateChannel.Pilot => "pilot",
                _ => "dev"
            };
            SetReleaseSummaryMarkdown(await GetBranchReadmeSummaryAsync(
                _context.Repository,
                branchName,
                installedVersion,
                masterVersion));
        }
        else
        {
            identifiedUpdateChannel = await CheckForReleaseAvailabilityAsync();
        }

        PreflightCheckResult? sourceCheck = null;
        if (identifiedInstalledVersion && identifiedUpdateChannel)
        {
            FooterStatusText.Text = "Saving and closing your logbook...";
            UpdateWizardView();
            sourceCheck = await WaitForSourceWorkbookAsync(_context.SourcePath);
            if (!sourceCheck.IsOk)
            {
                availabilityFailureReason =
                    "Logbook identified, but it is still open or syncing. Save and close it, then try again.";
            }
        }

        _availabilityReady = identifiedInstalledVersion && identifiedUpdateChannel && sourceCheck?.IsOk == true;
        _isCheckingAvailability = false;
        FooterStatusText.Text = _availabilityReady
            ? "Ready"
            : availabilityFailureReason ?? "Could not identify installed version or update channel.";
        UpdateWizardView();
        Show();
        Activate();
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
            SetReleaseSummaryMarkdown(string.IsNullOrWhiteSpace(summary)
                ? "No release notes summary was returned by GitHub."
                : summary);
            return true;
        }
        catch (Exception ex)
        {
            LatestVersionText.Text = "Update channel: Stable check failed";
            LastCheckedText.Text = $"Last checked: {DateTime.Now:G}";
            AvailableVersionText.Text = "Could not fetch release details.";
            SetReleaseSummaryMarkdown(ex.Message);
            return false;
        }
    }

    private void SetReleaseSummaryMarkdown(string markdown)
    {
        ReleaseSummaryText.Document = BuildReleaseSummaryDocument(markdown);
    }

    private static FlowDocument BuildReleaseSummaryDocument(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = SystemFonts.MessageFontFamily,
            FontSize = SystemFonts.MessageFontSize,
            Foreground = Brushes.Black
        };

        var normalised = string.IsNullOrWhiteSpace(markdown)
            ? "Release notes unavailable."
            : markdown.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        foreach (var rawLine in normalised.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryCreateMarkdownHeading(line, out var heading))
            {
                document.Blocks.Add(heading);
                continue;
            }

            if (TryCreateMarkdownListItem(line, out var listItem))
            {
                document.Blocks.Add(listItem);
                continue;
            }

            var paragraph = CreateParagraph(line, new Thickness(0, 0, 0, 5));
            document.Blocks.Add(paragraph);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(CreateParagraph("Release notes unavailable.", new Thickness(0)));
        }

        return document;
    }

    private static bool TryCreateMarkdownHeading(string line, out Paragraph paragraph)
    {
        var match = Regex.Match(line, "^(?<marks>#{1,6})\\s+(?<text>.+)$");
        if (!match.Success)
        {
            paragraph = null!;
            return false;
        }

        paragraph = CreateParagraph(match.Groups["text"].Value, new Thickness(0, 0, 0, 5));
        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.FontSize = match.Groups["marks"].Value.Length <= 3
            ? SystemFonts.MessageFontSize + 1
            : SystemFonts.MessageFontSize;
        return true;
    }

    private static bool TryCreateMarkdownListItem(string line, out Paragraph paragraph)
    {
        var match = Regex.Match(line, "^[-*+]\\s+(?<text>.+)$");
        if (!match.Success)
        {
            paragraph = null!;
            return false;
        }

        paragraph = new Paragraph
        {
            Margin = new Thickness(12, 0, 0, 3),
            TextIndent = -12
        };
        paragraph.Inlines.Add(new Run("- "));
        AddMarkdownInlines(paragraph.Inlines, match.Groups["text"].Value);
        return true;
    }

    private static Paragraph CreateParagraph(string text, Thickness margin)
    {
        var paragraph = new Paragraph
        {
            Margin = margin
        };
        AddMarkdownInlines(paragraph.Inlines, text);
        return paragraph;
    }

    private static void AddMarkdownInlines(InlineCollection inlines, string text)
    {
        var remaining = text;
        while (remaining.Length > 0)
        {
            var start = remaining.IndexOf("**", StringComparison.Ordinal);
            if (start < 0)
            {
                inlines.Add(new Run(remaining));
                return;
            }

            if (start > 0)
            {
                inlines.Add(new Run(remaining[..start]));
            }

            var boldStart = start + 2;
            var end = remaining.IndexOf("**", boldStart, StringComparison.Ordinal);
            if (end < 0)
            {
                inlines.Add(new Run(remaining[start..]));
                return;
            }

            var boldText = remaining[boldStart..end];
            inlines.Add(new Bold(new Run(boldText)));
            remaining = remaining[(end + 2)..];
        }
    }

    private static async Task<string> GetBranchReadmeSummaryAsync(
        string repository,
        string branchName,
        string? installedVersion,
        string? targetVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/'))
            {
                return $"Could not load {branchName}-branch README notes: repository format is invalid.";
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ElectronicLogbook-UpdaterWizard/0.1");
            var url = $"https://raw.githubusercontent.com/{repository}/{branchName}/README.md";
            var markdown = await client.GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return $"{branchName}-branch README is empty.";
            }

            return ExtractChangelogDelta(markdown, installedVersion, targetVersion);
        }
        catch (Exception ex)
        {
            return $"Could not load {branchName}-branch README notes: {ex.Message}";
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
            return "No changelog section found in branch README.";
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

        return string.Join("\n\n", included);
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

        return (tag, summary);
    }

    private async void PortableRefreshStatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshPortableLogbookStatusAsync();
    }

    private async void HostedConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunHostedConnectionAsync();
    }

    private async Task RunHostedConnectionAsync()
    {
        if (_isUpdating)
        {
            return;
        }

        var email = PromptForText(
            "Connect to Electronic Logbook",
            "Enter the email address used for your invited Electronic Logbook account:",
            allowEmpty: false);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        if (!SupabaseHostedSyncConfiguration.TryLoad(out var configuration, out var unavailableReason))
        {
            ShowPortableActionError(
                "Account connection is not configured",
                new InvalidOperationException(unavailableReason));
            return;
        }

        _isUpdating = true;
        UpdateWizardView();
        try
        {
            using var client = new SupabaseWorkbookConnectionClient(
                configuration ?? throw new InvalidOperationException("Hosted configuration was not resolved."));
            FooterStatusText.Text = "Sending the sign-in email...";
            var signIn = await client.StartEmailSignInAsync(email);
            var verificationInput = PromptForText(
                "Verify invited account",
                $"A sign-in code was sent to {signIn.DeliveryHint}. Enter the six-digit code shown in the email. It expires after 10 minutes. Check your junk or spam folder if it does not arrive:",
                allowEmpty: false);
            if (string.IsNullOrWhiteSpace(verificationInput))
            {
                FooterStatusText.Text = "Account connection cancelled.";
                return;
            }

            FooterStatusText.Text = "Verifying the invited account...";
            var session = await client.CompleteEmailSignInAsync(verificationInput);
            var logbooks = await client.DiscoverActiveLogbooksAsync();
            if (logbooks.Count == 0)
            {
                throw new InvalidOperationException("This invited account has no active Electronic Logbook to connect.");
            }

            var logbook = PromptForHostedLogbook(logbooks);
            if (logbook is null)
            {
                FooterStatusText.Text = "Account connection cancelled.";
                return;
            }

            var currentStatus = PortableLogbookCommandRunner.ReadHostedStatus(_context.SourcePath);
            var warning = currentStatus.IsPaired
                ? "This workbook is already connected. Continuing will create a new workbook device and retain a timestamped backup of the current file."
                : "The workbook will be backed up, registered as a separate device, and its visible flight rows will merge with the selected hosted logbook.";
            if (MessageBox.Show(
                    this,
                    $"Connect this workbook to '{logbook.DisplayName}'?{Environment.NewLine}{Environment.NewLine}{warning}",
                    "Confirm workbook connection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) != MessageBoxResult.Yes)
            {
                FooterStatusText.Text = "Account connection cancelled.";
                return;
            }

            var deviceId = DeviceId.New();
            using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();
            FooterStatusText.Text = "Recovering the hosted logbook key securely...";
            var logbookKey = await client.RestoreWorkbookKeyAsync(
                logbook.LogbookId,
                deviceId,
                recoveryKeyPair);

            FooterStatusText.Text = "Saving the connected workbook device...";
            _ = await Task.Run(() => PortableLogbookCommandRunner.ConnectHostedWorkbook(
                _context.SourcePath,
                session.AccountId,
                logbook.LogbookId,
                deviceId,
                session.Credential,
                logbookKey,
                recoveryKeyPair,
                DateTimeOffset.UtcNow));

            FooterStatusText.Text = "Recovering existing hosted history...";
            PortableHostedWorkbookSyncResult? recoverySync = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                recoverySync = await Task.Run(() => PortableLogbookCommandRunner.SyncHostedWorkbook(
                    _context.SourcePath,
                    DateTimeOffset.UtcNow,
                    uploadLocalOperations: false));
                if (recoverySync.Status != "Waiting")
                {
                    break;
                }
            }

            if (recoverySync is null || recoverySync.Status != "Synced")
            {
                var status = recoverySync?.Status ?? "Needs attention";
                var reason = string.IsNullOrWhiteSpace(recoverySync?.AttentionRequiredReason)
                    ? null
                    : $" {recoverySync.AttentionRequiredReason}";
                throw new InvalidOperationException(
                    $"The workbook was saved securely, but its hosted-history recovery reports '{status}'.{reason} " +
                    "The workbook device was not activated; reconnect when hosted sync is available.");
            }

            FooterStatusText.Text = "Activating the recovered workbook device...";
            await client.ActivateWorkbookDeviceAsync(logbook.LogbookId, deviceId);

            FooterStatusText.Text = "Uploading workbook rows...";
            PortableHostedWorkbookSyncResult? sync = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                sync = await Task.Run(() => PortableLogbookCommandRunner.SyncHostedWorkbook(
                    _context.SourcePath,
                    DateTimeOffset.UtcNow));
                if (sync.Status != "Waiting")
                {
                    break;
                }
            }

            if (sync is null || sync.Status != "Synced")
            {
                var status = sync?.Status ?? "Needs attention";
                var reason = string.IsNullOrWhiteSpace(sync?.AttentionRequiredReason)
                    ? null
                    : $" {sync.AttentionRequiredReason}";
                throw new InvalidOperationException(
                    $"The workbook device was activated, but its initial upload reports '{status}'.{reason} " +
                    "The workbook remains connected and will retry during the next hosted sync.");
            }

            PortableLogbookStatusText.Text = $"Workbook sync: {sync.Status}";
            FooterStatusText.Text = "Account connected and workbook synced.";

            MessageBox.Show(
                this,
                "This workbook is connected to your Electronic Logbook account and the first sync completed.",
                "Electronic Logbook connected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (_context.ConnectHosted)
            {
                Process.Start(new ProcessStartInfo(_context.SourcePath) { UseShellExecute = true });
                Close();
            }
        }
        catch (Exception ex)
        {
            ShowPortableActionError("Account connection failed", ex);
        }
        finally
        {
            _isUpdating = false;
            UpdateWizardView();
        }
    }

    private SupabaseWorkbookLogbook? PromptForHostedLogbook(IReadOnlyList<SupabaseWorkbookLogbook> logbooks)
    {
        if (logbooks.Count == 1)
        {
            return logbooks[0];
        }

        var choices = logbooks
            .Select((logbook, index) => new HostedLogbookChoice(index, $"{logbook.DisplayName} ({logbook.Role})"))
            .ToArray();
        var comboBox = new ComboBox
        {
            MinWidth = 360,
            Margin = new Thickness(0, 8, 0, 0),
            ItemsSource = choices,
            DisplayMemberPath = nameof(HostedLogbookChoice.Label),
            SelectedIndex = 0
        };
        var okButton = new Button { Content = "Continue", Width = 90, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var window = new Window
        {
            Title = "Choose Electronic Logbook",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight
        };
        okButton.Click += (_, _) => window.DialogResult = true;
        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Choose the logbook this workbook should use:" },
                comboBox,
                new StackPanel
                {
                    Margin = new Thickness(0, 12, 0, 0),
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { okButton, cancelButton }
                }
            }
        };
        if (window.ShowDialog() != true || comboBox.SelectedItem is not HostedLogbookChoice selected)
        {
            return null;
        }

        return logbooks[selected.Index];
    }

    private async void PortableEnableButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseSavePath(
            "Save portable recovery code",
            "Text file (*.txt)|*.txt",
            $"Electronic Logbook Portable Recovery {DateTime.Now:yyyy-MM-dd}.txt");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        await RunPortableActionAsync(
            "Enable portable logbook",
            () => PortableLogbookCommandRunner.Enable(_context.SourcePath, recoveryPath, DateTimeOffset.UtcNow),
            FormatEnableResult);
    }

    private async void PortableExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseOpenPath("Choose portable recovery code", "Text file (*.txt)|*.txt|All files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        var packagePath = ChooseSavePath(
            "Save portable package",
            "Portable logbook package (*.elogbook)|*.elogbook",
            $"Electronic Logbook {DateTime.Now:yyyy-MM-dd}.elogbook");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return;
        }

        await RunPortableActionAsync(
            "Export portable package",
            () => PortableLogbookCommandRunner.Export(_context.SourcePath, recoveryPath, packagePath, DateTimeOffset.UtcNow),
            FormatExportResult);
    }

    private async void PortableImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseOpenPath("Choose portable recovery code", "Text file (*.txt)|*.txt|All files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        var packagePath = ChooseOpenPath("Choose portable package", "Portable logbook package (*.elogbook)|*.elogbook");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return;
        }

        try
        {
            FooterStatusText.Text = "Previewing portable import...";
            var preview = await Task.Run(() => PortableLogbookCommandRunner.PreviewImport(_context.SourcePath, recoveryPath, packagePath));
            var previewText = FormatImportPreviewResult(preview);
            if (!CanApplyPortableImportPreview(preview))
            {
                FooterStatusText.Text = "Portable import requires resolution.";
                MessageBox.Show(
                    this,
                    previewText + Environment.NewLine + Environment.NewLine + "Resolve the reported conflicts before applying this package.",
                    "Portable import preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var response = MessageBox.Show(
                this,
                previewText + Environment.NewLine + Environment.NewLine + "Apply this package to portable workbook storage?",
                "Portable import preview",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (response != MessageBoxResult.Yes)
            {
                FooterStatusText.Text = "Portable import cancelled.";
                return;
            }

            await RunPortableActionAsync(
                "Import portable package",
                () => PortableLogbookCommandRunner.ApplyImport(_context.SourcePath, recoveryPath, packagePath, DateTimeOffset.UtcNow),
                FormatImportApplyResult);
        }
        catch (Exception ex)
        {
            ShowPortableActionError("Portable import failed", ex);
        }
    }

    private static bool CanApplyPortableImportPreview(PortableLogbookImportPreviewResult preview) =>
        preview.Status is "readyToApply" or "duplicateOnly";

    private async void PortablePrintedCopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseOpenPath("Choose portable recovery code", "Text file (*.txt)|*.txt|All files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        var outputPath = ChooseSavePath(
            "Save printed copy",
            "HTML file (*.html)|*.html",
            $"Electronic Logbook Printed Copy {DateTime.Now:yyyy-MM-dd}.html");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var holderName = PromptForText("Portable printed copy", "Holder full name (rendered only into this printed copy):", allowEmpty: false);
        if (string.IsNullOrWhiteSpace(holderName))
        {
            return;
        }

        var holderDateText = PromptForText("Portable printed copy", "Holder date of birth (yyyy-mm-dd, rendered only into this printed copy):", allowEmpty: false);
        if (!TryParseIsoDate(holderDateText, out var holderDateOfBirth))
        {
            MessageBox.Show(this, "Enter the holder date of birth as yyyy-mm-dd.", "Portable printed copy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunPortableActionAsync(
            "Create printed copy",
            () => PortableLogbookCommandRunner.CreatePrintedCopy(
                _context.SourcePath,
                recoveryPath,
                outputPath,
                holderName,
                holderDateOfBirth,
                DateOnly.FromDateTime(DateTime.Today),
                recordsPerPage: 25),
            FormatPrintedCopyResult);
    }

    private async void PortableRevisionHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseOpenPath("Choose portable recovery code", "Text file (*.txt)|*.txt|All files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        var entryId = PromptForText("Portable revision history", "Portable entry ID:", allowEmpty: false);
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return;
        }

        await RunPortableActionAsync(
            "View revision history",
            () => PortableLogbookCommandRunner.ReadRevisionHistory(_context.SourcePath, recoveryPath, new EntryId(entryId)),
            FormatRevisionHistoryResult);
    }

    private async void PortableResolveConflictButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recoveryPath = ChooseOpenPath("Choose portable recovery code", "Text file (*.txt)|*.txt|All files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(recoveryPath))
        {
            return;
        }

        var entryId = PromptForText("Resolve portable conflict", "Portable entry ID:", allowEmpty: false);
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return;
        }

        var revisionId = PromptForText("Resolve portable conflict", "Conflict head revision ID to keep:", allowEmpty: false);
        if (string.IsNullOrWhiteSpace(revisionId))
        {
            return;
        }

        var note = PromptForText("Resolve portable conflict", "Optional resolution note:", allowEmpty: true);
        await RunPortableActionAsync(
            "Resolve portable conflict",
            () => PortableLogbookCommandRunner.ResolveConflict(
                _context.SourcePath,
                recoveryPath,
                new EntryId(entryId),
                new RevisionId(revisionId),
                string.IsNullOrWhiteSpace(note) ? null : note,
                DateTimeOffset.UtcNow),
            FormatResolveConflictResult);
    }

    private async Task RunPortableActionAsync<TResult>(
        string title,
        Func<TResult> action,
        Func<TResult, string> formatResult)
    {
        try
        {
            FooterStatusText.Text = title + "...";
            var result = await Task.Run(action);
            FooterStatusText.Text = title + " complete.";
            await RefreshPortableLogbookStatusAsync(showUnavailableInFooter: false);
            MessageBox.Show(this, formatResult(result), title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowPortableActionError(title + " failed", ex);
        }
    }

    private async Task RefreshPortableLogbookStatusAsync(bool showUnavailableInFooter = true)
    {
        PortableLogbookStatusText.Text = await TryReadPortableLogbookStatusTextWithRetryAsync(_context.SourcePath);
        if (showUnavailableInFooter)
        {
            FooterStatusText.Text = PortableLogbookStatusText.Text;
        }
    }

    private void ShowPortableActionError(string title, Exception ex)
    {
        FooterStatusText.Text = title + ".";
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string? ChooseOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? ChooseSavePath(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PromptForText(string title, string prompt, bool allowEmpty)
    {
        var textBox = new TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var okButton = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var window = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Width = 480,
            SizeToContent = SizeToContent.Height
        };
        okButton.Click += (_, _) =>
        {
            if (!allowEmpty && string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            window.DialogResult = true;
        };
        var buttons = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { okButton, cancelButton }
        };
        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap },
                textBox,
                buttons
            }
        };
        return window.ShowDialog() == true ? textBox.Text.Trim() : null;
    }

    private static bool TryParseIsoDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static string FormatEnableResult(PortableLogbookEnableResult result) =>
        string.Join(
            Environment.NewLine,
            "Portable logbook enabled.",
            $"Logbook ID: {result.LogbookId.Value}",
            $"Device ID: {result.DeviceId.Value}",
            $"Recovery file: {result.RecoveryOutputPath}",
            $"Backup: {result.BackupPath}");

    private static string FormatExportResult(PortableLogbookExportResult result) =>
        string.Join(
            Environment.NewLine,
            "Portable package exported.",
            $"Package: {result.PackageOutputPath}",
            $"Operations: {result.OperationCount}",
            $"Custom fields: {result.CustomFieldDefinitionCount}");

    private static string FormatImportPreviewResult(PortableLogbookImportPreviewResult result) =>
        string.Join(
            Environment.NewLine,
            "Portable import preview",
            $"Status: {result.Status}",
            $"New operations: {result.NewOperationCount}",
            $"Duplicate operations: {result.DuplicateOperationCount}",
            $"Creates: {result.CreateCount}",
            $"Corrections: {result.CorrectionCount}",
            $"Deletions: {result.DeletionCount}",
            $"Entry conflicts: {result.ConflictCount}",
            $"Custom-field conflicts: {result.CustomFieldConflictCount}");

    private static string FormatImportApplyResult(PortableLogbookImportApplyResult result) =>
        string.Join(
            Environment.NewLine,
            "Portable package import finished.",
            $"Status: {result.Status}",
            $"Storage updated: {result.StorageUpdated}",
            $"Receipt recorded: {result.ReceiptRecorded}",
            $"Backup: {result.BackupPath ?? "(none)"}",
            $"Workbook rows requiring sync: {result.WorkbookRowCount}");

    private static string FormatPrintedCopyResult(PortableLogbookPrintedCopyResult result) =>
        string.Join(
            Environment.NewLine,
            "Printed copy created.",
            $"Output: {result.OutputPath}",
            $"Pages: {result.PageCount}",
            $"Current records: {result.CurrentRecordCount}",
            $"Revision history records: {result.RevisionCount}");

    private static string FormatRevisionHistoryResult(PortableLogbookRevisionHistoryResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Portable revision history");
        builder.AppendLine($"Entry ID: {result.EntryId.Value}");
        builder.AppendLine($"Current revision ID: {result.CurrentRevisionId?.Value ?? "(none)"}");
        builder.AppendLine($"Deleted: {result.IsDeleted}");
        builder.AppendLine($"Conflict: {result.HasConflict}");
        builder.AppendLine($"Revisions: {result.RevisionCount}");
        foreach (var revision in result.Revisions)
        {
            builder.AppendLine($"{revision.Kind}: {revision.RevisionId.Value} ({revision.CreatedAt:O})");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatResolveConflictResult(PortableLogbookResolveConflictResult result) =>
        string.Join(
            Environment.NewLine,
            "Portable conflict resolved.",
            $"Entry ID: {result.EntryId.Value}",
            $"Kept revision ID: {result.KeptRevisionId.Value}",
            $"Resolution revision ID: {result.ResolutionRevisionId.Value}",
            $"Remaining conflicts: {result.RemainingConflictCount}",
            $"Backup: {result.BackupPath}");

    private async void RunPreflightButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunPreflightAsync();
    }

    private async Task RunPreflightAsync()
    {
        _preflightPassed = false;
        _preMigrationSummary = null;
        _preMigrationSummarySourceFingerprint = null;

        var source = _context.SourcePath;
        var stagedOutput = _context.MigrationOutputPath ?? (_context.UseInPlaceSwap
            ? BuildStagedOutputPath(source)
            : _context.OutputPath);

        CheckSourcePathText.Text = "[ ] Waiting for source workbook to close...";
        FooterStatusText.Text = "Waiting for source workbook to close...";
        var recovery = RecoverPendingHandoffForWizard();
        if (recovery.Action != HandoffRecoveryAction.None)
        {
            FooterStatusText.Text = recovery.Message;
        }
        var sourceCheck = await WaitForSourceWorkbookAsync(source);
        var sourceOk = sourceCheck.IsOk;
        if (sourceOk && _context.Channel == UpdateChannel.Pilot)
        {
            try
            {
                var fingerprintBeforeInspection = await Integrity.Sha256Async(source, CancellationToken.None);
                _preMigrationSummary = await Task.Run(() =>
                    WorkbookPreMigrationInspector.Inspect(source));
                var fingerprintAfterInspection = await Integrity.Sha256Async(source, CancellationToken.None);
                if (!string.Equals(
                        fingerprintBeforeInspection,
                        fingerprintAfterInspection,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The workbook changed while its summary was being prepared. Close it and run the checks again.");
                }

                _preMigrationSummarySourceFingerprint = fingerprintAfterInspection;
                sourceCheck = new(true, "Source workbook is ready and its flight summary was inspected.");
            }
            catch (Exception ex)
            {
                sourceOk = false;
                _preMigrationSummary = null;
                _preMigrationSummarySourceFingerprint = null;
                sourceCheck = new(false, $"The workbook summary could not be read: {ex.Message}");
            }
        }
        CheckSourcePathText.Text = sourceOk
            ? (_context.Channel == UpdateChannel.Pilot
                ? "[OK] Source workbook is closed, valid, and its flight summary was read"
                : "[OK] Source workbook exists, is .xlsm, and is closed")
            : $"[FAIL] {sourceCheck.Message}";

        var finalOutput = _context.UseInPlaceSwap
            ? source
            : _context.OutputPath;
        var outputDir = string.IsNullOrWhiteSpace(stagedOutput)
            ? string.Empty
            : (Path.GetDirectoryName(stagedOutput) ?? string.Empty);
        var finalOutputDir = string.IsNullOrWhiteSpace(finalOutput)
            ? string.Empty
            : (Path.GetDirectoryName(finalOutput) ?? string.Empty);
        var outputDirExists = !string.IsNullOrWhiteSpace(outputDir) && Directory.Exists(outputDir);
        var finalOutputDirExists = !string.IsNullOrWhiteSpace(finalOutputDir) && Directory.Exists(finalOutputDir);
        var outputMissing = _context.UseInPlaceSwap
            ? !File.Exists(stagedOutput)
            : !File.Exists(stagedOutput) && !File.Exists(finalOutput);
        var outputExtOk =
            string.Equals(Path.GetExtension(stagedOutput), ".xlsm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetExtension(finalOutput), ".xlsm", StringComparison.OrdinalIgnoreCase);
        var writeAccess = outputDirExists && await CanWriteToDirectoryAsync(outputDir);
        var finalWriteAccess = finalOutputDirExists && await CanWriteToDirectoryAsync(finalOutputDir);

        var outputOk = outputDirExists &&
            finalOutputDirExists &&
            outputMissing &&
            outputExtOk &&
            writeAccess &&
            finalWriteAccess;
        CheckOutputPathText.Text = outputOk
            ? (_context.UseInPlaceSwap
                ? "[OK] Temporary output is writable and original workbook can be replaced"
                : "[OK] Output path is writable and output file does not exist")
            : (_context.UseInPlaceSwap
                ? "[FAIL] Temporary output invalid, unwritable, wrong extension, or already exists"
                : "[FAIL] Output path invalid, unwritable, wrong extension, or already exists");

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
            ? (_context.Channel == UpdateChannel.Pilot
                ? "All checks passed. Review the workbook summary before continuing."
                : "All checks passed. You can continue.")
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
            if (OpenUpdatedCheckBox.IsEnabled && OpenUpdatedCheckBox.IsChecked == true)
            {
                await OpenLastOutputWorkbookAsync();
            }

            Close();
            return;
        }

        if (_stepIndex == 0 && !_availabilityReady)
        {
            _ = InitialiseAvailabilityAsync();
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
        OpenUpdatedCheckBox.Content = "Open updated workbook when I click Finish";

        var progressSink = new RecordingUpdaterProgressSink(new WizardProgressSink(AppendProgressEvent));

        var source = _context.SourcePath;
        var stagedOutput = _context.MigrationOutputPath ?? (_context.UseInPlaceSwap
            ? BuildStagedOutputPath(source)
            : _context.OutputPath);
        var diagnosticReportPath = DetailedLoggingCheckBox.IsChecked == true
            ? (_context.UseInPlaceSwap
                ? Path.ChangeExtension(source, ".update-report.json")
                : Path.ChangeExtension(_context.OutputPath, ".update-report.json"))
            : null;
        string? resolvedMasterForDiagnostics = null;
        MigrationReport? report = null;
        string? pilotAccountEmail = null;
        PilotWorkbookHostedMigrationResult? pilotHostedMigration = null;
        PilotWorkbookPostMigrationHandoffResult? pilotHandoff = null;

        try
        {
            var recovery = RecoverPendingHandoffForWizard();
            if (recovery.Action != HandoffRecoveryAction.None)
            {
                FooterStatusText.Text = recovery.Message;
            }

            string resolvedMaster;
            ReleaseManifest? manifest = null;

            if (_context.UsesProvidedMaster)
            {
                resolvedMaster = _context.MasterPath!;
                resolvedMasterForDiagnostics = resolvedMaster;
                AppendLog($"Using {_context.ChannelDisplayName} master workbook: {resolvedMaster}");
            }
            else
            {
                AppendLog($"Resolving stable release from {_context.Repository}...");
                var releaseClient = new ReleaseClient();
                var release = await releaseClient.GetLatestReleaseAsync(_context.Repository, _updateCts.Token);
                _downloadDirectoryToCleanup = release.DownloadDirectory;
                resolvedMaster = release.MasterWorkbookPath;
                resolvedMasterForDiagnostics = resolvedMaster;
                manifest = release.Manifest;
                AppendLog($"Using release {manifest.Version} ({manifest.Tag})");
            }

            var sourceCheck = await WaitForSourceWorkbookAsync(source);
            if (!sourceCheck.IsOk)
            {
                throw new InvalidOperationException(sourceCheck.Message);
            }

            StagedWorkbookMigration? pilotStaging = null;
            var migrationRequest = new MigrationRequest(
                source,
                resolvedMaster,
                stagedOutput,
                manifest);
            if (_context.Channel == UpdateChannel.Pilot)
            {
                AppendLog("validating the source and creating its untouched timestamped backup...");
                var stager = new WorkbookMigrationStager(progressSink);
                pilotStaging = await stager.StageAsync(
                    migrationRequest,
                    _updateCts.Token,
                    _preMigrationSummarySourceFingerprint);
                report = pilotStaging.MigrationReport;
                _lastBackupPath = pilotStaging.BackupWorkbookPath;
                _lastBackupExpectedVersion = report.SourceVersion;
                AppendLog($"Untouched source backup: {_lastBackupPath}");
                AppendLog("The original workbook remains unchanged until hosted migration is verified.");

                if (!SupabaseHostedSyncConfiguration.TryLoad(out var configuration, out _))
                {
                    throw new InvalidOperationException(
                        "Google sign-in is not configured in this pilot updater. Ask FlightLogX support for the correct pilot installer.");
                }

                using var connectionClient = new SupabaseWorkbookConnectionClient(
                    configuration ?? throw new InvalidOperationException(
                        "Google sign-in is not configured in this pilot updater."));
                FooterStatusText.Text = "Opening Google sign-in in your browser...";
                AppendLog("Opening secure Google sign-in in the system browser...");
                var pilotSession = await connectionClient.SignInWithGoogleAsync(_updateCts.Token);
                pilotAccountEmail = pilotSession.AccountDisplay;
                AppendLog("Google sign-in completed.");

                FooterStatusText.Text = "Securely moving your logbook to FlightLogX...";
                AppendLog("Preparing account recovery before encrypted upload...");
                var hostedMigration = new PilotWorkbookHostedMigration(
                    connectionClient,
                    pilotSession,
                    configuration);
                pilotHostedMigration = await hostedMigration.RunAsync(
                    pilotStaging,
                    "FlightLogX Logbook",
                    _updateCts.Token);
                AppendLog(
                    $"Encrypted hosted readback verified for {pilotHostedMigration.VerifiedFlightCount:N0} flights.");
                AppendLog("Hosted migration completed and temporary recovery keys were removed.");

                FooterStatusText.Text = "Finalising your updated workbook...";
                AppendLog("Stamping the verified workbook and installing it at the original filename...");
                var postMigrationHandoff = new PilotWorkbookPostMigrationHandoff();
                pilotHandoff = await postMigrationHandoff.InstallAsync(
                    pilotStaging,
                    pilotHostedMigration,
                    _updateCts.Token);
                _lastBackupPath = pilotHandoff.UntouchedBackupWorkbookPath;
                _lastBackupExpectedVersion = report.SourceVersion;
                AppendLog(
                    $"Workbook stamped '{pilotHandoff.Stamp.Status}' at {pilotHandoff.Stamp.CompletedAt:O}.");
                AppendLog("The verified 3.0.0 workbook now uses the original workbook filename.");
                AppendLog($"Untouched timestamped backup retained: {_lastBackupPath}");
            }
            else
            {
                var migrator = new ExcelWorkbookMigrator(progressSink);
                report = await Task.Run(
                    () => migrator.Migrate(migrationRequest, _updateCts.Token),
                    _updateCts.Token);
                _lastBackupPath = null;
                _lastBackupExpectedVersion = null;
            }

            _lastOutputPath = pilotHandoff?.FinalWorkbookPath ?? stagedOutput;
            _lastReportPath = null;
            if (!string.IsNullOrWhiteSpace(diagnosticReportPath))
            {
                _lastReportPath = diagnosticReportPath;
                await TryWriteDiagnosticBundleAsync(
                    _lastReportPath,
                    report,
                    progressSink.Events,
                    error: null,
                    source,
                    resolvedMasterForDiagnostics,
                    _lastOutputPath,
                    _updateCts.Token);
                AppendLog($"Detailed diagnostic report: {_lastReportPath}");
            }
            AppendLog(
                "airport visit stats: " +
                $"{report.AirportVisitStats.WrittenVisitedAirportRows} written, " +
                $"{report.AirportVisitStats.SavedNonBlankVisitRows} saved, " +
                $"{report.AirportVisitStats.LogbookRowsWithRecognisedAirports} recognised logbook rows");

            if (!string.IsNullOrWhiteSpace(_context.HandoffNote))
            {
                AppendLog(_context.HandoffNote);
            }

            if (pilotStaging is not null)
            {
                AppendLog("Pilot hosted migration and workbook installation are complete.");
            }
            else if (_context.UseInPlaceSwap)
            {
                AppendLog("finalising workbook handoff...");
                var handoff = await Task.Run(
                    () => WorkbookHandoff.ReplaceSourceWithUpdated(
                        source,
                        stagedOutput,
                        report.OutputVersion,
                        report.SourceVersion),
                    _updateCts.Token);
                _lastOutputPath = handoff.FinalWorkbookPath;
                _lastBackupPath = handoff.BackupWorkbookPath;
                _lastBackupExpectedVersion = report.SourceVersion;
            }
            else if (!string.IsNullOrWhiteSpace(_context.MigrationOutputPath))
            {
                AppendLog("copying validated workbook into OneDrive folder...");
                WorkbookPackageValidator.ValidateStagedWorkbook(stagedOutput, report.OutputVersion);
                File.Copy(stagedOutput, _context.OutputPath, overwrite: false);
                WorkbookPackageValidator.ValidateWorkbookPackage(_context.OutputPath, report.OutputVersion);
                TryDelete(stagedOutput);
                _lastOutputPath = _context.OutputPath;
            }
            _lastOutputExpectedVersion = report.OutputVersion;

            AppendLog("Waiting for workbook file to settle...");
            var finalWorkbookReady = await WaitForFileToSettleAsync(_lastOutputPath, _updateCts.Token);
            if (finalWorkbookReady &&
                pilotStaging is null &&
                _context.UseInPlaceSwap &&
                !string.IsNullOrWhiteSpace(_lastBackupPath))
            {
                WorkbookHandoff.CompletePostHandoffValidation(
                    _lastOutputPath,
                    _lastBackupPath,
                    report.OutputVersion,
                    report.SourceVersion);
                AppendLog("Post-handoff validation complete; older update backups pruned.");
            }

            CompleteTitleText.Text = pilotStaging is not null
                ? (finalWorkbookReady ? "Migration Complete" : "Migration Complete With Warnings")
                : (finalWorkbookReady ? "Update Complete" : "Update Complete With Warnings");
            CompleteSummaryText.Text = pilotStaging is not null
                ? (finalWorkbookReady
                    ? $"Signed in as {pilotAccountEmail}. FlightLogX verified {pilotHostedMigration!.VerifiedFlightCount:N0} flights against the encrypted hosted copy. The updated workbook is stamped 'Moved to FlightLogX' and installed at the original filename; the untouched timestamped backup was retained."
                    : "The hosted migration and workbook installation completed, but the workbook file is still settling. Wait for OneDrive sync before opening it.")
                : (finalWorkbookReady
                    ? (_context.UseInPlaceSwap
                        ? "Update complete. The original filename now points to the updated workbook."
                        : "The updated workbook was created as a separate file and validated.")
                    : "Update complete, but the workbook file is still settling. Wait for OneDrive sync to finish before opening it.");
            CompleteOutputPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Current workbook",
                _lastOutputPath,
                _lastOutputExpectedVersion,
                "Current workbook: not available");
            CompleteBackupPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Retained backup",
                _lastBackupPath,
                _lastBackupExpectedVersion,
                "Retained backup: not available");
            RestoreBackupButton.IsEnabled = pilotStaging is null &&
                finalWorkbookReady &&
                _context.UseInPlaceSwap &&
                !string.IsNullOrWhiteSpace(_lastBackupPath);
            OpenUpdatedCheckBox.IsEnabled = finalWorkbookReady;
            OpenUpdatedCheckBox.IsChecked = finalWorkbookReady;
            OpenDiagnosticReportButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastReportPath) &&
                File.Exists(_lastReportPath);
            FooterStatusText.Text = pilotStaging is not null
                ? (finalWorkbookReady
                    ? "Migration and workbook installation completed."
                    : "Migration completed. Wait for sync before opening the workbook.")
                : (finalWorkbookReady
                    ? "Update completed."
                    : "Update completed. Wait for sync before opening.");

            _stepIndex = 5;
        }
        catch (OperationCanceledException ex)
        {
            if (!string.IsNullOrWhiteSpace(diagnosticReportPath))
            {
                _lastReportPath = diagnosticReportPath;
                await TryWriteDiagnosticBundleAsync(
                    _lastReportPath,
                    report,
                    progressSink.Events,
                    ex,
                    source,
                    resolvedMasterForDiagnostics,
                    _lastOutputPath ?? stagedOutput,
                    CancellationToken.None);
            }

            CompleteTitleText.Text = "Update Cancelled";
            CompleteSummaryText.Text = "Update was cancelled before completion.";
            CompleteOutputPathText.Text = "Current workbook: not created";
            CompleteBackupPathText.Text = string.Empty;
            RestoreBackupButton.IsEnabled = false;
            OpenDiagnosticReportButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastReportPath) &&
                File.Exists(_lastReportPath);
            OpenUpdatedCheckBox.IsEnabled = false;
            OpenUpdatedCheckBox.IsChecked = false;
            FooterStatusText.Text = "Update cancelled.";
            _stepIndex = 5;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(diagnosticReportPath))
            {
                _lastReportPath = diagnosticReportPath;
                await TryWriteDiagnosticBundleAsync(
                    _lastReportPath,
                    report,
                    progressSink.Events,
                    ex,
                    source,
                    resolvedMasterForDiagnostics,
                    _lastOutputPath ?? stagedOutput,
                    CancellationToken.None);
            }

            CompleteTitleText.Text = string.IsNullOrWhiteSpace(_lastBackupPath)
                ? "Update Failed"
                : "Update Failed - Backup Available";
            CompleteSummaryText.Text = ex.Message;
            CompleteOutputPathText.Text = "Current workbook: not available";
            CompleteBackupPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Retained backup",
                _lastBackupPath,
                _lastBackupExpectedVersion,
                "Retained backup: not available");
            RestoreBackupButton.IsEnabled = _context.Channel != UpdateChannel.Pilot &&
                _context.UseInPlaceSwap &&
                !string.IsNullOrWhiteSpace(_lastBackupPath);
            OpenDiagnosticReportButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastReportPath) &&
                File.Exists(_lastReportPath);
            OpenUpdatedCheckBox.IsEnabled = false;
            OpenUpdatedCheckBox.IsChecked = false;
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
            if (!string.IsNullOrWhiteSpace(progressEvent.RecoveryHint))
            {
                AppendLog($"Recovery: {progressEvent.RecoveryHint}");
            }

            if (progressEvent.Percent.HasValue)
            {
                UpdateProgressBar.Value = progressEvent.Percent.Value;
            }
            else if (UpdaterPhaseProgress.GetPercent(progressEvent.PhaseId) is { } phasePercent)
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

    private async Task TryWriteDiagnosticBundleAsync(
        string path,
        MigrationReport? report,
        IReadOnlyList<UpdaterProgressEvent> progressEvents,
        Exception? error,
        string? sourceWorkbookPath,
        string? masterWorkbookPath,
        string? outputWorkbookPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var applicationVersion = report?.OutputVersion ?? "unknown";
            var bundle = DiagnosticBundleFactory.Create(
                applicationVersion,
                report,
                progressEvents,
                error,
                sourceWorkbookPath,
                masterWorkbookPath,
                outputWorkbookPath);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(bundle, JsonDefaults.Indented),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendLog($"Could not write diagnostic report: {ex.Message}");
        }
    }

    private HandoffRecoveryResult RecoverPendingHandoffForWizard()
    {
        HandoffRecoveryResult recovery;
        try
        {
            recovery = WorkbookHandoff.RecoverIfNeeded(_context.SourcePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or JsonException)
        {
            recovery = new HandoffRecoveryResult(
                HandoffRecoveryAction.UnrecoverableFailure,
                $"Recovery required: an interrupted workbook handoff could not be recovered automatically. {ex.Message}",
                Path.GetFullPath(_context.SourcePath));
        }

        if (recovery.Action != HandoffRecoveryAction.None)
        {
            AppendLog(recovery.Message);
            if (!string.IsNullOrWhiteSpace(recovery.SourceWorkbookPath))
            {
                AppendLog($"Recovered workbook: {recovery.SourceWorkbookPath}");
            }
            if (!string.IsNullOrWhiteSpace(recovery.BackupWorkbookPath))
            {
                AppendLog($"{recovery.BackupWorkbookLabel}: {recovery.BackupWorkbookPath}");
            }
        }

        return recovery;
    }

    private async Task OpenLastOutputWorkbookAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastOutputPath) || !File.Exists(_lastOutputPath))
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(_lastOutputExpectedVersion))
        {
            var version = await TryReadWorkbookVersionFromPackageWithRetryAsync(_lastOutputPath);
            if (!string.Equals(version, _lastOutputExpectedVersion, StringComparison.Ordinal))
            {
                MessageBox.Show(
                    this,
                    "The updated workbook is not ready to open yet. Wait for OneDrive to finish syncing, then open the updated workbook from the path shown on this screen.",
                    "Workbook still syncing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastOutputPath,
            UseShellExecute = true
        });
    }

    private static async Task<string> BuildWorkbookDisplayTextAsync(
        string label,
        string? workbookPath,
        string? expectedVersion,
        string missingText)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
        {
            return missingText;
        }

        var details = new List<string>();
        var version = await TryReadWorkbookVersionFromPackageWithRetryAsync(workbookPath);
        if (!string.IsNullOrWhiteSpace(version))
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
            {
                details.Add($"version {version}");
            }
            else if (string.Equals(version, expectedVersion, StringComparison.Ordinal))
            {
                details.Add($"validated version {version}");
            }
            else
            {
                details.Add($"version {version}; expected {expectedVersion}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(expectedVersion))
        {
            details.Add($"version unreadable; expected {expectedVersion}");
        }

        details.Add($"saved {File.GetLastWriteTime(workbookPath):G}");

        return details.Count == 0
            ? $"{label}: {workbookPath}"
            : $"{label}: {workbookPath} ({string.Join(", ", details)})";
    }

    private async void RestoreBackupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastOutputPath) ||
            string.IsNullOrWhiteSpace(_lastBackupPath))
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Restore the retained backup to the original workbook filename? The current workbook at that filename will be kept for investigation.",
            "Restore backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = WorkbookHandoff.RestoreBackup(
                _lastOutputPath,
                _lastBackupPath,
                _lastBackupExpectedVersion);
            _lastOutputPath = result.RestoredWorkbookPath;
            _lastOutputExpectedVersion = result.RestoredWorkbookVersion;
            CompleteTitleText.Text = "Backup Restored";
            CompleteSummaryText.Text = "The retained backup was validated and restored to the original workbook filename.";
            CompleteOutputPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Restored workbook",
                result.RestoredWorkbookPath,
                result.RestoredWorkbookVersion,
                "Restored workbook: not available");
            CompleteBackupPathText.Text = result.FailedWorkbookPath is null
                ? $"Retained backup: {_lastBackupPath} (validated version {result.BackupWorkbookVersion})"
                : $"Previous failed workbook kept: {result.FailedWorkbookPath}";
            RestoreBackupButton.IsEnabled = false;
            OpenUpdatedCheckBox.IsEnabled = true;
            OpenUpdatedCheckBox.IsChecked = true;
            OpenUpdatedCheckBox.Content = "Open restored workbook when I click Finish";
            FooterStatusText.Text = "Backup restored.";
        }
        catch (Exception ex)
        {
            CompleteTitleText.Text = "Restore Failed";
            CompleteSummaryText.Text =
                "The backup could not be restored. Recoverable workbook copies were kept so the restore can be retried or investigated.";
            CompleteOutputPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Workbook at original path",
                _lastOutputPath,
                null,
                $"Workbook at original path: {_lastOutputPath} (not available)");
            CompleteBackupPathText.Text = await BuildWorkbookDisplayTextAsync(
                "Retained backup",
                _lastBackupPath,
                _lastBackupExpectedVersion,
                $"Retained backup: {_lastBackupPath} (not available)");
            RestoreBackupButton.IsEnabled = File.Exists(_lastBackupPath);
            OpenUpdatedCheckBox.IsEnabled = false;
            OpenUpdatedCheckBox.IsChecked = false;
            FooterStatusText.Text = "Restore failed. Recoverable workbook copies were retained.";
            MessageBox.Show(
                this,
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                "No recoverable workbook copy was deleted. Review the paths shown on the completion screen, then retry or investigate the retained files.",
                "Restore failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenDiagnosticReportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastReportPath) || !File.Exists(_lastReportPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastReportPath,
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

    private static async Task<string?> TryReadWorkbookVersionFromPackageWithRetryAsync(string workbookPath)
    {
        // OneDrive can briefly replace the package while it synchronises. Retrying
        // the package read is safe: unlike Excel automation, it cannot display a
        // hidden file-in-use prompt while the user's workbook remains open.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var version = await Task.Run(() => TryReadWorkbookVersionFromPackage(workbookPath));
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            if (attempt < 9)
            {
                await Task.Delay(1000);
            }
        }

        return null;
    }

    private static string? TryReadWorkbookVersionFromPackage(string workbookPath)
    {
        if (!File.Exists(workbookPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                workbookPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var spreadsheet = (XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var documentRelationships =
                (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var packageRelationships =
                (XNamespace)"http://schemas.openxmlformats.org/package/2006/relationships";
            var workbook = ReadXmlEntry(archive, "xl/workbook.xml");
            var workbookRelationships = ReadXmlEntry(archive, "xl/_rels/workbook.xml.rels");
            if (workbook is null || workbookRelationships is null)
            {
                return null;
            }

            var definedName = workbook
                .Descendants(spreadsheet + "definedName")
                .FirstOrDefault(name => string.Equals(
                    (string?)name.Attribute("name"),
                    "LogbookVersion",
                    StringComparison.OrdinalIgnoreCase));
            if (definedName is null ||
                !TryParseSingleCellReference(definedName.Value, out var sheetName, out var cellReference))
            {
                return null;
            }

            var sheet = workbook
                .Descendants(spreadsheet + "sheet")
                .FirstOrDefault(candidate => string.Equals(
                    (string?)candidate.Attribute("name"),
                    sheetName,
                    StringComparison.OrdinalIgnoreCase));
            var relationshipId = (string?)sheet?.Attribute(documentRelationships + "id");
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                return null;
            }

            var relationship = workbookRelationships
                .Descendants(packageRelationships + "Relationship")
                .FirstOrDefault(candidate => string.Equals(
                    (string?)candidate.Attribute("Id"),
                    relationshipId,
                    StringComparison.Ordinal));
            var target = (string?)relationship?.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            var worksheet = ReadXmlEntry(archive, ResolveWorkbookRelationshipTarget(target));
            var cell = worksheet?
                .Descendants(spreadsheet + "c")
                .FirstOrDefault(candidate => string.Equals(
                    (string?)candidate.Attribute("r"),
                    cellReference,
                    StringComparison.OrdinalIgnoreCase));
            if (cell is null)
            {
                return null;
            }

            var cellType = (string?)cell.Attribute("t");
            if (string.Equals(cellType, "s", StringComparison.Ordinal))
            {
                var sharedStringIndex = (int?)cell.Element(spreadsheet + "v");
                var sharedStrings = ReadXmlEntry(archive, "xl/sharedStrings.xml");
                return sharedStringIndex.HasValue
                    ? sharedStrings?
                        .Descendants(spreadsheet + "si")
                        .ElementAtOrDefault(sharedStringIndex.Value)?
                        .Descendants(spreadsheet + "t")
                        .Select(text => text.Value)
                        .Aggregate(string.Empty, static (value, text) => value + text)
                    : null;
            }

            if (string.Equals(cellType, "inlineStr", StringComparison.Ordinal))
            {
                return string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
            }

            return (string?)cell.Element(spreadsheet + "v");
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static async Task<string> TryReadPortableLogbookStatusTextWithRetryAsync(string workbookPath)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var status = await Task.Run(() => TryReadPortableLogbookStatusText(workbookPath));
            if (!string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            if (attempt < 9)
            {
                await Task.Delay(1000);
            }
        }

        return "Workbook sync: unavailable";
    }

    private static string? TryReadPortableLogbookStatusText(string workbookPath)
    {
        if (!File.Exists(workbookPath))
        {
            return null;
        }

        try
        {
            var status = PortableLogbookCommandRunner.ReadStatus(workbookPath);
            if (!status.IsEnabled || status.Summary is null)
            {
                return "Workbook sync: not enabled";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Workbook sync: enabled ({0} current, {1} conflict(s))",
                status.Summary.CurrentRecordCount,
                status.Summary.UnresolvedConflictCount);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return "Workbook sync: storage unreadable";
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Xml.XmlException)
        {
            return "Workbook sync: storage unreadable";
        }
    }

    private static XDocument? ReadXmlEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static bool TryParseSingleCellReference(
        string formula,
        out string sheetName,
        out string cellReference)
    {
        sheetName = string.Empty;
        cellReference = string.Empty;

        var separator = formula.LastIndexOf('!');
        if (separator <= 0 || separator == formula.Length - 1)
        {
            return false;
        }

        sheetName = formula[..separator].Trim().TrimStart('=');
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
        {
            sheetName = sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        cellReference = formula[(separator + 1)..].Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        return !string.IsNullOrWhiteSpace(sheetName) &&
            System.Text.RegularExpressions.Regex.IsMatch(cellReference, "^[A-Za-z]+[0-9]+$");
    }

    private static string ResolveWorkbookRelationshipTarget(string target)
    {
        return target.StartsWith("/", StringComparison.Ordinal)
            ? target[1..]
            : $"xl/{target}";
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
        var connectHosted = false;

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
                case "--connect-hosted":
                    connectHosted = true;
                    break;
            }
        }

        source ??= GetDefaultSourcePath();
        source = Path.GetFullPath(source);
        output ??= WorkbookOutputNamer.BuildDefaultOutputPath(source);
        output = Path.GetFullPath(output);
        master = string.IsNullOrWhiteSpace(master) ? null : Path.GetFullPath(master);
        channel ??= string.IsNullOrWhiteSpace(master)
            ? UpdateChannel.Stable
            : UpdateChannel.LocalMaster;
        var updatePathPlan = WorkbookUpdatePathPlanner.Resolve(
            source,
            output,
            useInPlaceSwap,
            CloudStoragePath.IsLikelyCloudSynced,
            BuildLocalMigrationOutputPath);

        return new RunContext(
            SourcePath: source,
            OutputPath: updatePathPlan.OutputPath,
            MigrationOutputPath: updatePathPlan.MigrationOutputPath,
            MasterPath: master,
            Repository: repository,
            Channel: channel.Value,
            UseInPlaceSwap: updatePathPlan.UseInPlaceSwap,
            HandoffNote: updatePathPlan.HandoffNote,
            ConnectHosted: connectHosted);
    }

    private static UpdateChannel ParseUpdateChannel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stable" => UpdateChannel.Stable,
            "development" => UpdateChannel.Development,
            "dev" => UpdateChannel.Development,
            "hotfix" => UpdateChannel.Hotfix,
            "pilot" => UpdateChannel.Pilot,
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

    private static string BuildLocalMigrationOutputPath(string sourcePath)
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

    private static async Task<bool> CanWriteToDirectoryAsync(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A temp cleanup failure must not hide a validated update.
        }
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

        for (var attempt = 0; attempt < 60; attempt++)
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
        Hotfix,
        Pilot,
        LocalMaster
    }

    private sealed record RunContext(
        string SourcePath,
        string OutputPath,
        string? MigrationOutputPath,
        string? MasterPath,
        string Repository,
        UpdateChannel Channel,
        bool UseInPlaceSwap,
        string HandoffNote,
        bool ConnectHosted)
    {
        public bool UsesProvidedMaster => !string.IsNullOrWhiteSpace(MasterPath);

        public string ChannelDisplayName => Channel switch
        {
            UpdateChannel.Stable => "Stable",
            UpdateChannel.Development => "Development",
            UpdateChannel.Hotfix => "Hotfix",
            UpdateChannel.Pilot => "Pilot",
            UpdateChannel.LocalMaster => "Local Master",
            _ => "Stable"
        };
    }

    private void UpdatePreMigrationSummaryView()
    {
        var showSummary = _context.Channel == UpdateChannel.Pilot &&
            _preMigrationSummary is not null;
        PreMigrationSummaryPanel.Visibility = showSummary
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReadyTitleText.Text = showSummary ? "Review Your Logbook" : "Ready to Update";
        ReadyDescriptionText.Text = showSummary
            ? "Confirm these saved workbook totals before signing in and moving your logbook to FlightLogX."
            : "Click Start to begin the update.";

        if (!showSummary)
        {
            return;
        }

        var summary = _preMigrationSummary!;
        PreMigrationFlightCountText.Text = $"Flights: {summary.FlightCount:N0}";
        PreMigrationLoggedHoursText.Text = $"Logged hours: {summary.LoggedHoursDisplay}";
        PreMigrationDateRangeText.Text = $"Date range: {summary.DateRangeDisplay}";
        PreMigrationWarningsText.Text = WorkbookPreMigrationInspector.FormatWarnings(summary);
    }

    private sealed record HostedLogbookChoice(int Index, string Label);
}
