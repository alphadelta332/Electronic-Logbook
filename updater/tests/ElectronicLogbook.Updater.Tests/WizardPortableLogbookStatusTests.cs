namespace ElectronicLogbook.Updater.Tests;

public sealed class WizardPortableLogbookStatusTests
{
    [Fact]
    public void WelcomeScreenIncludesReadOnlyPortableLogbookStatus()
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
        Assert.Contains("TryReadPortableLogbookStatusTextWithRetryAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ReadStatus", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Portable logbook: not enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Portable logbook: enabled", codeBehind, StringComparison.Ordinal);
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
}
