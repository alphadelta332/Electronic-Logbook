namespace ElectronicLogbook.Updater.Tests;

internal static class TestRepo
{
    public static string Version => File.ReadAllText(FindFile("version.txt")).Trim();

    public static string FindFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file: {Path.Combine(relativeParts)}");
    }
}
