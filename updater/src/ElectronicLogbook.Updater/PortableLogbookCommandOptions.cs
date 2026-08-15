using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed record PortableLogbookCommandOptions(
    PortableLogbookCommand Command,
    string? WorkbookPath,
    string? RecoveryOutputPath,
    string? RecoveryCodeFilePath,
    string? PackageOutputPath,
    string? PackageInputPath,
    string? PrintedCopyOutputPath,
    string? HolderName,
    DateOnly? HolderDateOfBirth,
    DateOnly? CertifiedOn,
    int? RecordsPerPage,
    string? EntryId,
    string? RevisionId,
    string? Note,
    string? HostedAccountId,
    string? HostedAccessToken,
    string? HostedRefreshToken,
    DateTimeOffset? HostedAccessTokenExpiresAt,
    string? WindowsCredentialTargetName,
    int? WaitForWorkbookUnlockSeconds,
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
          ElectronicLogbook.Updater portable printed-copy --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --output <file.html> --holder-name <name> --holder-date-of-birth <yyyy-mm-dd> [--certified-on <yyyy-mm-dd>] [--records-per-page <count>] [--json]
          ElectronicLogbook.Updater portable revision-history --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --entry-id <entry-id> [--json]
          ElectronicLogbook.Updater portable resolve-conflict --workbook <logbook.xlsm> (--recovery-code-file <file.txt> | --windows-credential-target <target>) --entry-id <entry-id> --revision-id <revision-id> [--note <text>] [--json]

        Commands:
          enable                  Enable portable-logbook workbook storage with a new package key.
          export                  Export workbook portable storage to an encrypted package file.
          import-apply            Apply a conflict-free package to encrypted workbook storage.
          import-preview          Preview an incoming package without changing workbook storage.
          printed-copy            Render a certified printed-copy HTML file from portable storage.
          resolve-conflict        Resolve one entry conflict by keeping a selected head revision.
          revision-history        View immutable revision history for one portable entry.
          status                  Inspect portable-logbook workbook storage without decrypting it.

        Options:
          --workbook <logbook.xlsm>  Workbook package to inspect or enable.
          --recovery-output <file>   File that receives the recovery code for a new portable logbook.
          --recovery-code-file <file> File containing the recovery code for package export/import.
          --windows-credential-target <target> Windows Credential Manager target containing the package key.
          --output <file.elogbook>   Portable package file to create.
          --package <file.elogbook>  Portable package file to preview.
          --holder-name <name>       Holder name rendered into printed-copy output only.
          --holder-date-of-birth <yyyy-mm-dd> Holder date of birth rendered into printed-copy output only.
          --certified-on <yyyy-mm-dd> Certification date for printed-copy output. Defaults to today.
          --records-per-page <count> Current records per printed page. Defaults to 25.
          --entry-id <entry-id>      Portable entry identifier for revision-history.
          --revision-id <revision-id> Selected conflict head revision to keep.
          --note <text>              Optional conflict-resolution note.
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
            "printed-copy" => PortableLogbookCommand.PrintedCopy,
            "revision-history" => PortableLogbookCommand.RevisionHistory,
            "resolve-conflict" => PortableLogbookCommand.ResolveConflict,
            "status" => PortableLogbookCommand.Status,
            "hosted-pair" => PortableLogbookCommand.HostedPair,
            "hosted-sync" => PortableLogbookCommand.HostedSync,
            "hosted-status" => PortableLogbookCommand.HostedStatus,
            "--help" or "-h" => PortableLogbookCommand.None,
            _ => throw new UpdaterUsageException($"Unknown portable command: {args[0]}")
        };
        string? workbook = null;
        string? recoveryOutput = null;
        string? recoveryCodeFile = null;
        string? packageOutput = null;
        string? packageInput = null;
        string? printedCopyOutput = null;
        string? holderName = null;
        DateOnly? holderDateOfBirth = null;
        DateOnly? certifiedOn = null;
        int? recordsPerPage = null;
        string? entryId = null;
        string? revisionId = null;
        string? note = null;
        string? hostedAccountId = null;
        string? hostedAccessToken = null;
        string? hostedRefreshToken = null;
        DateTimeOffset? hostedAccessTokenExpiresAt = null;
        string? windowsCredentialTargetName = null;
        int? waitForWorkbookUnlockSeconds = null;
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
                    if (command == PortableLogbookCommand.PrintedCopy)
                    {
                        printedCopyOutput = ReadValue(args, ref index, arg);
                    }
                    else
                    {
                        packageOutput = ReadValue(args, ref index, arg);
                    }
                    break;
                case "--package":
                    packageInput = ReadValue(args, ref index, arg);
                    break;
                case "--holder-name":
                    holderName = ReadValue(args, ref index, arg);
                    break;
                case "--holder-date-of-birth":
                    holderDateOfBirth = ReadDateValue(args, ref index, arg);
                    break;
                case "--certified-on":
                    certifiedOn = ReadDateValue(args, ref index, arg);
                    break;
                case "--records-per-page":
                    recordsPerPage = ReadPositiveIntValue(args, ref index, arg);
                    break;
                case "--entry-id":
                    entryId = ReadValue(args, ref index, arg);
                    break;
                case "--revision-id":
                    revisionId = ReadValue(args, ref index, arg);
                    break;
                case "--note":
                    note = ReadValue(args, ref index, arg);
                    break;
                case "--hosted-account-id":
                    hostedAccountId = ReadValue(args, ref index, arg);
                    break;
                case "--hosted-access-token":
                    hostedAccessToken = ReadValue(args, ref index, arg);
                    break;
                case "--hosted-refresh-token":
                    hostedRefreshToken = ReadValue(args, ref index, arg);
                    break;
                case "--hosted-access-token-expires-at":
                    hostedAccessTokenExpiresAt = ReadDateTimeOffsetValue(args, ref index, arg);
                    break;
                case "--wait-for-workbook-unlock-seconds":
                    waitForWorkbookUnlockSeconds = ReadPositiveIntValue(args, ref index, arg);
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
            return new(command, workbook, recoveryOutput, recoveryCodeFile, packageOutput, packageInput, printedCopyOutput, holderName, holderDateOfBirth, certifiedOn, recordsPerPage, entryId, revisionId, note, hostedAccountId, hostedAccessToken, hostedRefreshToken, hostedAccessTokenExpiresAt, windowsCredentialTargetName, waitForWorkbookUnlockSeconds, saveWindowsCredential, json, true);
        }

        if ((command == PortableLogbookCommand.Enable ||
                command == PortableLogbookCommand.Export ||
                command == PortableLogbookCommand.ImportApply ||
                command == PortableLogbookCommand.ImportPreview ||
                command == PortableLogbookCommand.PrintedCopy ||
                command == PortableLogbookCommand.RevisionHistory ||
                command == PortableLogbookCommand.ResolveConflict ||
                command == PortableLogbookCommand.Status ||
                command == PortableLogbookCommand.HostedPair ||
                command == PortableLogbookCommand.HostedSync ||
                command == PortableLogbookCommand.HostedStatus)
            && string.IsNullOrWhiteSpace(workbook))
        {
            throw new UpdaterUsageException($"--workbook is required for portable {CommandName(command)}.");
        }

        if (command == PortableLogbookCommand.HostedPair)
        {
            if (string.IsNullOrWhiteSpace(hostedAccountId))
            {
                throw new UpdaterUsageException("--hosted-account-id is required for portable hosted-pair.");
            }

            if (string.IsNullOrWhiteSpace(hostedAccessToken))
            {
                throw new UpdaterUsageException("--hosted-access-token is required for portable hosted-pair.");
            }

            if (string.IsNullOrWhiteSpace(hostedRefreshToken))
            {
                throw new UpdaterUsageException("--hosted-refresh-token is required for portable hosted-pair.");
            }

            if (hostedAccessTokenExpiresAt is null)
            {
                throw new UpdaterUsageException("--hosted-access-token-expires-at is required for portable hosted-pair.");
            }
        }

        if (command == PortableLogbookCommand.Enable && string.IsNullOrWhiteSpace(recoveryOutput))
        {
            throw new UpdaterUsageException("--recovery-output is required for portable enable.");
        }

        var commandRequiresKeySource = command is
            PortableLogbookCommand.Export or
            PortableLogbookCommand.ImportApply or
            PortableLogbookCommand.ImportPreview or
            PortableLogbookCommand.PrintedCopy or
            PortableLogbookCommand.RevisionHistory or
            PortableLogbookCommand.ResolveConflict;
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

        if (command == PortableLogbookCommand.PrintedCopy)
        {
            if (string.IsNullOrWhiteSpace(printedCopyOutput))
            {
                throw new UpdaterUsageException("--output is required for portable printed-copy.");
            }

            if (string.IsNullOrWhiteSpace(holderName))
            {
                throw new UpdaterUsageException("--holder-name is required for portable printed-copy.");
            }

            if (holderDateOfBirth is null)
            {
                throw new UpdaterUsageException("--holder-date-of-birth is required for portable printed-copy.");
            }
        }

        if ((command == PortableLogbookCommand.ImportApply || command == PortableLogbookCommand.ImportPreview)
            && string.IsNullOrWhiteSpace(packageInput))
        {
            throw new UpdaterUsageException($"--package is required for portable {CommandName(command)}.");
        }

        if (command == PortableLogbookCommand.RevisionHistory && string.IsNullOrWhiteSpace(entryId))
        {
            throw new UpdaterUsageException("--entry-id is required for portable revision-history.");
        }

        if (command == PortableLogbookCommand.ResolveConflict)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                throw new UpdaterUsageException("--entry-id is required for portable resolve-conflict.");
            }

            if (string.IsNullOrWhiteSpace(revisionId))
            {
                throw new UpdaterUsageException("--revision-id is required for portable resolve-conflict.");
            }
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
        printedCopyOutput = string.IsNullOrWhiteSpace(printedCopyOutput) ? null : Path.GetFullPath(printedCopyOutput);
        holderName = string.IsNullOrWhiteSpace(holderName) ? null : holderName.Trim();
        entryId = string.IsNullOrWhiteSpace(entryId) ? null : entryId.Trim();
        revisionId = string.IsNullOrWhiteSpace(revisionId) ? null : revisionId.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        hostedAccountId = string.IsNullOrWhiteSpace(hostedAccountId) ? null : hostedAccountId.Trim();
        hostedAccessToken = string.IsNullOrWhiteSpace(hostedAccessToken) ? null : hostedAccessToken.Trim();
        hostedRefreshToken = string.IsNullOrWhiteSpace(hostedRefreshToken) ? null : hostedRefreshToken.Trim();
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

        if (printedCopyOutput is not null)
        {
            var extension = Path.GetExtension(printedCopyOutput);
            if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdaterUsageException("Portable printed-copy output must use the '.html' or '.htm' extension.");
            }

            var directory = Path.GetDirectoryName(printedCopyOutput);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new UpdaterUsageException($"Printed-copy output directory not found: {directory}");
            }

            if (File.Exists(printedCopyOutput))
            {
                throw new UpdaterUsageException($"Printed-copy output file already exists: {printedCopyOutput}");
            }
        }

        if (saveWindowsCredential && command != PortableLogbookCommand.Enable)
        {
            throw new UpdaterUsageException("--save-windows-credential is only supported for portable enable.");
        }

        if (waitForWorkbookUnlockSeconds is not null && command != PortableLogbookCommand.HostedSync)
        {
            throw new UpdaterUsageException(
                "--wait-for-workbook-unlock-seconds is only supported for portable hosted-sync.");
        }

        if (waitForWorkbookUnlockSeconds > 300)
        {
            throw new UpdaterUsageException(
                "--wait-for-workbook-unlock-seconds cannot exceed 300 seconds.");
        }

        return new(command, workbook, recoveryOutput, recoveryCodeFile, packageOutput, packageInput, printedCopyOutput, holderName, holderDateOfBirth, certifiedOn, recordsPerPage, entryId, revisionId, note, hostedAccountId, hostedAccessToken, hostedRefreshToken, hostedAccessTokenExpiresAt, windowsCredentialTargetName, waitForWorkbookUnlockSeconds, saveWindowsCredential, json, false);
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

    private static DateOnly ReadDateValue(IReadOnlyList<string> args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed))
        {
            throw new UpdaterUsageException($"{option} must use yyyy-mm-dd format.");
        }

        return parsed;
    }

    private static int ReadPositiveIntValue(IReadOnlyList<string> args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 1)
        {
            throw new UpdaterUsageException($"{option} must be a positive integer.");
        }

        return parsed;
    }

    private static DateTimeOffset ReadDateTimeOffsetValue(IReadOnlyList<string> args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed))
        {
            throw new UpdaterUsageException($"{option} must use an ISO-8601 date/time.");
        }

        return parsed;
    }

    private static string CommandName(PortableLogbookCommand command) =>
        command switch
        {
            PortableLogbookCommand.Enable => "enable",
            PortableLogbookCommand.Export => "export",
            PortableLogbookCommand.ImportApply => "import-apply",
            PortableLogbookCommand.ImportPreview => "import-preview",
            PortableLogbookCommand.PrintedCopy => "printed-copy",
            PortableLogbookCommand.RevisionHistory => "revision-history",
            PortableLogbookCommand.ResolveConflict => "resolve-conflict",
            PortableLogbookCommand.Status => "status",
            PortableLogbookCommand.HostedPair => "hosted-pair",
            PortableLogbookCommand.HostedSync => "hosted-sync",
            PortableLogbookCommand.HostedStatus => "hosted-status",
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
    PrintedCopy,
    RevisionHistory,
    ResolveConflict,
    Status,
    HostedPair,
    HostedSync,
    HostedStatus
}
