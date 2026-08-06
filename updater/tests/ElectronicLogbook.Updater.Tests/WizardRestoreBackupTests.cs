namespace ElectronicLogbook.Updater.Tests;

public sealed class WizardRestoreBackupTests
{
    [Fact]
    public void CompletionScreenWiresOneClickRestoreAndClearOutcomes()
    {
        var xaml = File.ReadAllText(TestRepo.FindFile(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(TestRepo.FindFile(
            "updater",
            "src",
            "ElectronicLogbook.Updater.Wizard",
            "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"RestoreBackupButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Restore Backup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"RestoreBackupButton_OnClick\"", xaml, StringComparison.Ordinal);

        var handler = ExtractMethodBody(codeBehind, "RestoreBackupButton_OnClick");
        Assert.Contains(
            "The current workbook at that filename will be kept for investigation.",
            handler,
            StringComparison.Ordinal);
        Assert.Contains("WorkbookHandoff.RestoreBackup(", handler, StringComparison.Ordinal);
        Assert.Contains("CompleteTitleText.Text = \"Backup Restored\"", handler, StringComparison.Ordinal);
        Assert.Contains("Previous failed workbook kept:", handler, StringComparison.Ordinal);
        Assert.Contains("FooterStatusText.Text = \"Backup restored.\"", handler, StringComparison.Ordinal);

        Assert.Contains("CompleteTitleText.Text = \"Restore Failed\"", handler, StringComparison.Ordinal);
        Assert.Contains("Workbook at original path", handler, StringComparison.Ordinal);
        Assert.Contains("Retained backup", handler, StringComparison.Ordinal);
        Assert.Contains("RestoreBackupButton.IsEnabled = File.Exists(_lastBackupPath)", handler, StringComparison.Ordinal);
        Assert.Contains("Recoverable workbook copies were retained.", handler, StringComparison.Ordinal);
        Assert.Contains("No recoverable workbook copy was deleted.", handler, StringComparison.Ordinal);
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
