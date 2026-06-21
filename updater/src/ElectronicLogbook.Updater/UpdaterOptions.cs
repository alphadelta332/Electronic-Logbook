namespace ElectronicLogbook.Updater;

public sealed record UpdaterOptions(
    string? SourcePath,
    string? OutputPath,
    string? MasterPath,
    string? ReportPath,
    string Repository,
    bool InPlaceSwap,
    bool ShowHelp)
{
    public const string DefaultRepository = "alphadelta332/Electronic-Logbook";

    public const string HelpText =
        """
        Usage:
                    ElectronicLogbook.Updater --source <logbook.xlsm> --output <updated.xlsm> [options]

        Options:
          --master <master.xlsm>   Use a local master workbook instead of GitHub latest release.
          --report <report.json>   Write the validation report to a specific path.
          --repo <owner/name>      GitHub repository. Defaults to alphadelta332/Electronic-Logbook.
                    --inplace                Replace source filename with updated file and keep *_Old backup.
                    --no-inplace             Disable in-place swap behavior.
          --help                   Show this help.

        Safety:
                    By default, the source workbook is replaced in-place after validation and a
                    timestamped *_Old backup is created in the same folder.
          The output path must not already exist.
        """;

    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        string? source = null;
        string? output = null;
        string? master = null;
        string? report = null;
        var repository = DefaultRepository;
        var inPlaceSwap = false;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--source":
                    source = ReadValue(args, ref index, arg);
                    break;
                case "--output":
                    output = ReadValue(args, ref index, arg);
                    break;
                case "--master":
                    master = ReadValue(args, ref index, arg);
                    break;
                case "--report":
                    report = ReadValue(args, ref index, arg);
                    break;
                case "--repo":
                    repository = ReadValue(args, ref index, arg);
                    break;
                case "--inplace":
                    inPlaceSwap = true;
                    break;
                case "--no-inplace":
                    inPlaceSwap = false;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new UpdaterUsageException($"Unknown argument: {arg}");
            }
        }

        if (showHelp)
        {
            return new(source, output, master, report, repository, inPlaceSwap, true);
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output))
        {
            throw new UpdaterUsageException("--source and --output are required.");
        }

        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        master = string.IsNullOrWhiteSpace(master) ? null : Path.GetFullPath(master);
        report = string.IsNullOrWhiteSpace(report) ? null : Path.GetFullPath(report);

        if (!File.Exists(source))
        {
            throw new UpdaterUsageException($"Source workbook not found: {source}");
        }
        if (master is not null && !File.Exists(master))
        {
            throw new UpdaterUsageException($"Master workbook not found: {master}");
        }
        if (!string.Equals(Path.GetExtension(source), ".xlsm", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(output), ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdaterUsageException("Source and output files must use the .xlsm extension.");
        }
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdaterUsageException("The output path must differ from the source path.");
        }
        if (File.Exists(output))
        {
            throw new UpdaterUsageException($"The output path already exists: {output}");
        }
        if (!repository.Contains('/', StringComparison.Ordinal))
        {
            throw new UpdaterUsageException("--repo must use owner/name format.");
        }

        return new(source, output, master, report, repository, inPlaceSwap, false);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new UpdaterUsageException($"{option} requires a value.");
        }
        return args[index];
    }
}

public sealed class UpdaterUsageException(string message) : Exception(message);
