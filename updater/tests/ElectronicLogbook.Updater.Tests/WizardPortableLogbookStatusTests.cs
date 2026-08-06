namespace ElectronicLogbook.Updater.Tests;

public sealed class WizardPortableLogbookStatusTests
{
    [Fact]
    public void WelcomeScreenIncludesPortableLogbookStatusAndActions()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml")));
        var codeBehind = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs")));

        Assert.Contains("x:Name=\"PortableLogbookStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableEnableButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableExportButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableImportButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortablePrintedCopyButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableRevisionHistoryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableResolveConflictButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PortableRefreshStatusButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Account, sync, and Advanced recovery", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Enable Sync\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Recovery Export\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Recovery Import\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Resolve Sync Conflict\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Refresh Sync Status\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TryReadPortableLogbookStatusTextWithRetryAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ReadStatus", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.Enable", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.Export", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.PreviewImport", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ApplyImport", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CanApplyPortableImportPreview(preview)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Resolve the reported conflicts before applying this package.", codeBehind, StringComparison.Ordinal);
        Assert.Contains("preview.Status is \"readyToApply\" or \"duplicateOnly\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HandoffRecoveryAction.UnrecoverableFailure", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Recovery required: an interrupted workbook handoff could not be recovered automatically.", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.CreatePrintedCopy", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Holder full name (rendered only into this printed copy):", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Holder date of birth (yyyy-mm-dd, rendered only into this printed copy):", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ReadRevisionHistory", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ResolveConflict", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Workbook sync: not enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Workbook sync: enabled", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticReportFlowIsLocalRedactedAndReviewableBeforeSharing()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml")));
        var codeBehind = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs")));

        Assert.Contains("Write local redacted diagnostic report beside output workbook", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Redacted Report", xaml, StringComparison.Ordinal);
        Assert.Contains("Diagnostic reports stay on this device.", xaml, StringComparison.Ordinal);
        Assert.Contains("Open and review the exact redacted report before sharing it.", xaml, StringComparison.Ordinal);

        var openHandler = ExtractMethodBody(codeBehind, "OpenDiagnosticReportButton_OnClick");
        Assert.Contains("Process.Start(new ProcessStartInfo", openHandler, StringComparison.Ordinal);
        Assert.Contains("FileName = _lastReportPath", openHandler, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", openHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", openHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload", openHandler, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Send", openHandler, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Clipboard", openHandler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseSummaryRendersMarkdownInsteadOfRawMarkers()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml")));
        var codeBehind = File.ReadAllText(FindRepoFile(Path.Combine(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs")));

        Assert.Contains("<RichTextBox x:Name=\"ReleaseSummaryText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock x:Name=\"ReleaseSummaryText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseSummaryText.Text", codeBehind, StringComparison.Ordinal);

        Assert.Contains("BuildReleaseSummaryDocument", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryCreateMarkdownHeading", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryCreateMarkdownListItem", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AddMarkdownInlines", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new Bold(new Run(boldText))", codeBehind, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method '{methodName}'.");
        }

        var openBraceIndex = source.IndexOf('{', methodIndex);
        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method body for '{methodName}'.");
        }

        var depth = 0;
        for (var index = openBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find end of method body for '{methodName}'.");
    }
}
