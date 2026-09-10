namespace ElectronicLogbook.Updater;

public static class LegacyPreviewSourceResolver
{
    private static readonly string[] OneDriveEnvironmentVariables =
    [
        "OneDriveConsumer",
        "OneDriveCommercial",
        "OneDrive"
    ];

    public static string Resolve(
        string requestedSourcePath,
        string masterPath,
        IEnumerable<string>? oneDriveRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPath);

        var requestedFullPath = Path.GetFullPath(requestedSourcePath);
        if (File.Exists(requestedFullPath))
        {
            return requestedFullPath;
        }

        var masterFullPath = Path.GetFullPath(masterPath);
        if (!File.Exists(masterFullPath))
        {
            return requestedFullPath;
        }

        var fileName = Path.GetFileName(requestedFullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return requestedFullPath;
        }

        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in NormalizeRoots(oneDriveRoots ?? GetKnownOneDriveRoots()))
        {
            foreach (var candidate in EnumerateMatchingFileNames(root, fileName))
            {
                var candidateFullPath = Path.GetFullPath(candidate);
                if (string.Equals(
                        candidateFullPath,
                        masterFullPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !LegacyPreviewMigrationBridge.MatchesWorkbookPackages(
                        candidateFullPath,
                        masterFullPath))
                {
                    continue;
                }

                matches.Add(candidateFullPath);
                if (matches.Count > 1)
                {
                    return requestedFullPath;
                }
            }
        }

        return matches.SingleOrDefault() ?? requestedFullPath;
    }

    private static IEnumerable<string> GetKnownOneDriveRoots()
    {
        foreach (var variable in OneDriveEnvironmentVariables)
        {
            var processValue = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(processValue))
            {
                yield return processValue;
            }

            var userValue = Environment.GetEnvironmentVariable(
                variable,
                EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue))
            {
                yield return userValue;
            }
        }
    }

    private static IEnumerable<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(root);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (Directory.Exists(fullPath) && normalized.Add(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateMatchingFileNames(
        string root,
        string fileName)
    {
        var options = new EnumerationOptions
        {
            // OneDrive marks hydrated folders and files as reparse points. Skipping
            // reparse points would therefore skip the exact legacy workbook this
            // recovery exists to find.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            return Directory
                .EnumerateFiles(root, fileName, options)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
