using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed record PortableLogbookCommandOptions(
    PortableLogbookCommand Command,
    string? WorkbookPath,
    string? RecoveryOutputPath,
    string? RecoveryCodeFilePath,
    string? PackageOutputPath,
    string? PackageInputPath,
    string? WindowsCredentialTargetName,
    bool SaveWindowsCredential,
    bool Json,
    bool ShowHelp)
{
    public const string HelpText =
        """
        Usage:
          ElectronicLogbook.Updater portable status --workbook <logbook.xlsm> [--json]
          ElectronicLogbook.Updater portable enable --workbook <logbook.xlsm> --recovery-output <file.txt> [--save-windows-credential] [--json]
          ElectronicLogbook.Updater portable export --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --output <file.elogbook> [--json]
          ElectronicLogbook.Updater portable import-preview --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --package <file.elogbook> [--json]
          ElectronicLogbook.Updater portable import-apply --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --package <file.elogbook> [--json]

        Commands:
          enable                  Enable portable-logbook workbook storage with a new package key.
          export                  Export workbook portable storage to an encrypted package file.
          import-apply            Apply a conflict-free package to encrypted workbook storage.
          import-preview          Preview an incoming package without changing workbook storage.
          status                  Inspect portable-logbook workbook storage without decrypting it.

        Options:
          --workbook <logbook.xlsm>  Workbook package to inspect or enable.
          --recovery-output <file>   File that receives the recovery code for a new portable logbook.
          --recovery-code-file <file> File containing the recovery code for package export/import.
          --windows-credential-target <target> Windows Credential Manager target containing the package key.
          --output <file.elogbook>   Portable package file to create.
          --package <file.elogbook>  Portable package file to preview.
          --save-windows-credential  Store the generated package key in Windows Credential Manager during enable.
          --json                     Write machine-readable redacted status JSON.
          --help                     Show this help.

        Safety:
          Portable status inspection is read-only and does not require the package key.
          Portable enable writes encrypted workbook storage but stores the recovery code only in the requested file.
        """;

    public static PortableLogbookCommandOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            throw new UpdaterUsageException("Portable command is required.");
        }

        var command = args[0] switch
        {
            "enable" => PortableLogbookCommand.Enable,
            "export" => PortableLogbookCommand.Export,
            "import-apply" => PortableLogbookCommand.ImportApply,
            "import-preview" => PortableLogbookCommand.ImportPreview,
            "status" => PortableLogbookCommand.Status,
            "--help" or "-h" => PortableLogbookCommand.None,
            _ => throw new UpdaterUsageException($"Unknown portable command: {args[0]}")
        };
        string? workbook = null;
        string? recoveryOutput = null;
        string? recoveryCodeFile = null;
        string? packageOutput = null;
        string? packageInput = null;
        string? windowsCredentialTargetName = null;
        var saveWindowsCredential = false;
        var json = false;
        var showHelp = command == PortableLogbookCommand.None;

        for (var index = 1; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--workbook":
                    workbook = ReadValue(args, ref index, arg);
                    break;
                case "--recovery-output":
                    recoveryOutput = ReadValue(args, ref index, arg);
                    break;
                case "--recovery-code-file":
                    recoveryCodeFile = ReadValue(args, ref index, arg);
                    break;
                case "--windows-credential-target":
                    windowsCredentialTargetName = ReadValue(args, ref index, arg);
                    break;
                case "--output":
                    packageOutput = ReadValue(args, ref index, arg);
                    break;
                case "--package":
                    packageInput = ReadValue(args, ref index, arg);
                    break;
                case "--json":
                    json = true;
                    break;
                case "--save-windows-credential":
                    saveWindowsCredential = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new UpdaterUsageException($"Unknown portable argument: {arg}");
            }
        }

        if (showHelp)
        {
            return new(command, workbook, recoveryOutput, recoveryCodeFile, packageOutput, packageInput, windowsCredentialTargetName, saveWindowsCredential, json, true);
        }

        if ((command == PortableLogbookCommand.Enable ||
                command == PortableLogbookCommand.Export ||
                command == PortableLogbookCommand.ImportApply ||
                command == PortableLogbookCommand.ImportPreview ||
                command == PortableLogbookCommand.Status)
            && string.IsNullOrWhiteSpace(workbook))
        {
            throw new UpdaterUsageException($"--workbook is required for portable {CommandName(command)}.");
        }

        if (command == PortableLogbookCommand.Enable && string.IsNullOrWhiteSpace(recoveryOutput))
        {
            throw new UpdaterUsageException("--recovery-output is required for portable enable.");
        }

        var commandRequiresKeySource = command is
            PortableLogbookCommand.Export or
            PortableLogbookCommand.ImportApply or
            PortableLogbookCommand.ImportPreview;
        if (commandRequiresKeySource &&
            string.IsNullOrWhiteSpace(recoveryCodeFile) &&
            string.IsNullOrWhiteSpace(windowsCredentialTargetName))
        {
            throw new UpdaterUsageException($"--recovery-code-file or --windows-credential-target is required for portable {CommandName(command)}.");
        }

        if (!string.IsNullOrWhiteSpace(recoveryCodeFile) && !string.IsNullOrWhiteSpace(windowsCredentialTargetName))
        {
            throw new UpdaterUsageException("Use only one portable key source: --recovery-code-file or --windows-credential-target.");
        }

        if (command == PortableLogbookCommand.Export && string.IsNullOrWhiteSpace(packageOutput))
        {
            throw new UpdaterUsageException("--output is required for portable export.");
        }

        if ((command == PortableLogbookCommand.ImportApply || command == PortableLogbookCommand.ImportPreview)
            && string.IsNullOrWhiteSpace(packageInput))
        {
            throw new UpdaterUsageException($"--package is required for portable {CommandName(command)}.");
        }

        workbook = string.IsNullOrWhiteSpace(workbook) ? null : Path.GetFullPath(workbook);
        if (workbook is not null)
        {
            if (!File.Exists(workbook))
            {
                throw new UpdaterUsageException($"Workbook not found: {workbook}");
            }

            if (!string.Equals(Path.GetExtension(workbook), ".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdaterUsageException("Portable commands require a .xlsm workbook.");
            }
        }

        recoveryOutput = string.IsNullOrWhiteSpace(recoveryOutput) ? null : Path.GetFullPath(recoveryOutput);
        recoveryCodeFile = string.IsNullOrWhiteSpace(recoveryCodeFile) ? null : Path.GetFullPath(recoveryCodeFile);
        packageOutput = string.IsNullOrWhiteSpace(packageOutput) ? null : Path.GetFullPath(packageOutput);
        packageInput = string.IsNullOrWhiteSpace(packageInput) ? null : Path.GetFullPath(packageInput);
        windowsCredentialTargetName = string.IsNullOrWhiteSpace(windowsCredentialTargetName)
            ? null
            : windowsCredentialTargetName.Trim();
        if (recoveryOutput is not null)
        {
            var directory = Path.GetDirectoryName(recoveryOutput);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new UpdaterUsageException($"Recovery output directory not found: {directory}");
            }

            if (File.Exists(recoveryOutput))
            {
                throw new UpdaterUsageException($"Recovery output file already exists: {recoveryOutput}");
            }
        }

        if (recoveryCodeFile is not null && !File.Exists(recoveryCodeFile))
        {
            throw new UpdaterUsageException($"Recovery code file not found: {recoveryCodeFile}");
        }

        if (packageOutput is not null)
        {
            if (!string.Equals(Path.GetExtension(packageOutput), PortableLogbookPackageFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdaterUsageException($"Portable package output must use the '{PortableLogbookPackageFile.Extension}' extension.");
            }

            if (File.Exists(packageOutput))
            {
                throw new UpdaterUsageException($"Portable package output file already exists: {packageOutput}");
            }
        }

        if (packageInput is not null)
        {
            if (!string.Equals(Path.GetExtension(packageInput), PortableLogbookPackageFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdaterUsageException($"Portable package input must use the '{PortableLogbookPackageFile.Extension}' extension.");
            }

            if (!File.Exists(packageInput))
            {
                throw new UpdaterUsageException($"Portable package input file not found: {packageInput}");
            }
        }

        if (saveWindowsCredential && command != PortableLogbookCommand.Enable)
        {
            throw new UpdaterUsageException("--save-windows-credential is only supported for portable enable.");
        }

        return new(command, workbook, recoveryOutput, recoveryCodeFile, packageOutput, packageInput, windowsCredentialTargetName, saveWindowsCredential, json, false);
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

    private static string CommandName(PortableLogbookCommand command) =>
        command switch
        {
            PortableLogbookCommand.Enable => "enable",
            PortableLogbookCommand.Export => "export",
            PortableLogbookCommand.ImportApply => "import-apply",
            PortableLogbookCommand.ImportPreview => "import-preview",
            PortableLogbookCommand.Status => "status",
            _ => "command"
        };
}

public enum PortableLogbookCommand
{
    None,
    Enable,
    Export,
    ImportApply,
    ImportPreview,
    Status
}
