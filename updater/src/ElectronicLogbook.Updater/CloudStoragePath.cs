namespace ElectronicLogbook.Updater;

public static class CloudStoragePath
{
    public static bool IsLikelyCloudSynced(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (IsUnderKnownOneDriveRoot(fullPath))
        {
            return true;
        }

        return HasReparsePointInPath(fullPath);
    }

    private static bool IsUnderKnownOneDriveRoot(string fullPath)
    {
        foreach (var variable in new[] { "OneDriveConsumer", "OneDriveCommercial", "OneDrive" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var fullRoot = Path.GetFullPath(root);
            if (IsSameOrChildPath(fullPath, fullRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalisedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalisedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            (Directory.Exists(path) ? Path.DirectorySeparatorChar : string.Empty);

        return string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison) ||
            normalisedPath.StartsWith(normalisedRoot, comparison);
    }

    private static bool HasReparsePointInPath(string fullPath)
    {
        var current = File.Exists(fullPath)
            ? new FileInfo(fullPath).FullName
            : Path.GetDirectoryName(fullPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                FileAttributes attributes;
                if (File.Exists(current))
                {
                    attributes = File.GetAttributes(current);
                }
                else if (Directory.Exists(current))
                {
                    attributes = File.GetAttributes(current);
                }
                else
                {
                    current = Path.GetDirectoryName(current);
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }
}
