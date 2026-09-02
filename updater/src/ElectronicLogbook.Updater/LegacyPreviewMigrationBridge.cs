namespace ElectronicLogbook.Updater;

public static class LegacyPreviewMigrationBridge
{
    public static bool MatchesWorkbookPackages(string sourcePath, string masterPath)
    {
        try
        {
            return Matches(
                WorkbookPackageValidator.ReadWorkbookDefinedNameValue(
                    sourcePath,
                    "LogbookVersion"),
                WorkbookPackageValidator.ReadWorkbookDefinedNameValue(
                    sourcePath,
                    "GitHubBranch"),
                WorkbookPackageValidator.ReadWorkbookDefinedNameValue(
                    masterPath,
                    "LogbookVersion"));
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    public static bool Matches(
        string? sourceVersion,
        string? sourceBranch,
        string? masterVersion)
    {
        return string.Equals(sourceVersion?.Trim(), "2.0.3", StringComparison.Ordinal) &&
            string.Equals(sourceBranch?.Trim(), "pilot", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(masterVersion?.Trim(), "3.0.0", StringComparison.Ordinal);
    }
}
