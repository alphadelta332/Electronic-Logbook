using System.Text.RegularExpressions;

namespace ElectronicLogbook.Updater;

public static class WorkbookOutputNamer
{
    private static readonly Regex UpdaterSuffixPattern = new(
        "^_Updated(?:_Staged)?(?:_\\d{8}-\\d{6})?$|^_Updated_Working$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string BuildDefaultOutputPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var name = StripUpdaterSuffixes(Path.GetFileNameWithoutExtension(sourcePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(directory, $"{name}_Updated_{DateTime.Now:yyyyMMdd-HHmmss}.xlsm");
    }

    public static string StripUpdaterSuffixes(string workbookName)
    {
        if (string.IsNullOrWhiteSpace(workbookName))
        {
            return "Electronic Logbook";
        }

        var name = workbookName.Trim();
        while (true)
        {
            var suffixStart = name.LastIndexOf("_Updated", StringComparison.OrdinalIgnoreCase);
            if (suffixStart < 0)
            {
                break;
            }

            var suffix = name[suffixStart..];
            if (!UpdaterSuffixPattern.IsMatch(suffix))
            {
                break;
            }

            name = name[..suffixStart].TrimEnd(' ', '.', '_', '-');
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Electronic Logbook";
            }
        }

        return name;
    }
}
