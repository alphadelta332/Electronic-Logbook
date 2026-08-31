using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public enum PilotWorkbookMigrationStage
{
    Preparing,
    PreparingWorkbook,
    SigningIn,
    MovingToFlightLogX,
    InstallingWorkbook,
    Completed
}

public enum PilotWorkbookMigrationFailureKind
{
    WrongAccount,
    UnsupportedWorkbook,
    CorruptWorkbook,
    Validation,
    NetworkInterruption,
    HostedReadbackMismatch,
    HostedSafetyState,
    Backup,
    WorkbookInstall,
    SignIn,
    Unexpected
}

public sealed record PilotWorkbookMigrationFailurePresentation(
    PilotWorkbookMigrationFailureKind Kind,
    string Title,
    string Summary,
    string Detail,
    string RecoveryAction)
{
    public string CustomerMessage =>
        $"{Summary}{Environment.NewLine}{Environment.NewLine}" +
        $"{Detail}{Environment.NewLine}{Environment.NewLine}" +
        $"What to do: {RecoveryAction}";
}

public static class PilotWorkbookMigrationFailurePresenter
{
    public static PilotWorkbookMigrationFailurePresentation Create(
        PilotWorkbookMigrationStage stage,
        Exception error,
        bool backupAvailable,
        bool hostedMigrationCompleted,
        string? updaterPhaseId = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (IsWrongAccount(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.WrongAccount,
                "Migration Stopped - Check Google Account",
                "FlightLogX could not use the Google account you selected.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "The original spreadsheet was not replaced."),
                "Start the migration again and choose the Google account that received the FlightLogX Preview invitation. If that account is already selected, contact FlightLogX support.");
        }

        if (IsNetworkInterruption(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.NetworkInterruption,
                "Migration Paused - Connection Interrupted",
                "The connection stopped before FlightLogX could confirm this step.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "The updater did not report success or replace an unverified spreadsheet."),
                "Check your internet connection, close Excel, and run the migration again. FlightLogX will resume the same migration instead of creating a duplicate.");
        }

        if (error is WorkbookMigrationBackupException)
        {
            return new(
                PilotWorkbookMigrationFailureKind.Backup,
                "Migration Stopped - Backup Could Not Be Verified",
                "FlightLogX could not create and verify the untouched spreadsheet backup.",
                "No Google sign-in or upload was started. The original spreadsheet was not replaced, and an incomplete backup was removed where possible.",
                "Close Excel, wait for OneDrive or other file syncing to finish, check that the folder is writable and has free space, then run the migration again.");
        }

        if (stage == PilotWorkbookMigrationStage.InstallingWorkbook)
        {
            return new(
                PilotWorkbookMigrationFailureKind.WorkbookInstall,
                "Migration Incomplete - Spreadsheet Not Installed",
                hostedMigrationCompleted
                    ? "Your logbook was verified in FlightLogX, but the updated spreadsheet was not installed."
                    : "The updated spreadsheet could not be safely installed.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "The updater did not report success. The original spreadsheet and every recoverable copy were kept."),
                "Close Excel, wait for file syncing to finish, and run the migration again. A verified hosted migration will be checked and reused without uploading a duplicate copy.");
        }

        if (stage == PilotWorkbookMigrationStage.MovingToFlightLogX &&
            IsHostedReadbackMismatch(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.HostedReadbackMismatch,
                "Migration Stopped - Hosted Copy Did Not Match",
                "FlightLogX read back data that did not exactly match the spreadsheet.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "The mismatch was not accepted as success, and the original spreadsheet was not replaced."),
                "Keep the original spreadsheet and retained backup unchanged, then run the migration again. If the mismatch repeats, contact FlightLogX support and include the diagnostic report.");
        }

        if (stage == PilotWorkbookMigrationStage.MovingToFlightLogX &&
            ContainsInvalidData(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.HostedSafetyState,
                "Migration Stopped - Secure State Could Not Be Verified",
                "FlightLogX could not safely verify the migration account or recovery state.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "The updater stopped instead of uploading or installing an unverified result."),
                "Keep the original spreadsheet and retained backup unchanged, then run the migration again. If the same check fails, contact FlightLogX support with the diagnostic report.");
        }

        if (stage == PilotWorkbookMigrationStage.SigningIn)
        {
            return new(
                PilotWorkbookMigrationFailureKind.SignIn,
                "Migration Stopped - Google Sign-In Incomplete",
                "Google sign-in did not finish, so the migration did not continue.",
                "Nothing was uploaded, and the original spreadsheet was not replaced.",
                "Run the migration again and complete Google sign-in in the browser. If sign-in keeps failing, contact FlightLogX support.");
        }

        if (stage == PilotWorkbookMigrationStage.PreparingWorkbook &&
            IsUnsupportedWorkbook(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.UnsupportedWorkbook,
                "Migration Stopped - Spreadsheet Version Not Supported",
                "This spreadsheet version cannot be migrated automatically.",
                PlainDetail(error, "The spreadsheet was rejected before sign-in or upload. The original file was not changed."),
                "Keep the original spreadsheet unchanged and contact FlightLogX support for the correct upgrade path.");
        }

        if (stage == PilotWorkbookMigrationStage.PreparingWorkbook &&
            ContainsInvalidData(error) &&
            string.IsNullOrWhiteSpace(updaterPhaseId))
        {
            return new(
                PilotWorkbookMigrationFailureKind.CorruptWorkbook,
                "Migration Stopped - Spreadsheet Could Not Be Read",
                "FlightLogX could not safely read this spreadsheet.",
                "The file may be damaged or may be missing required Electronic Logbook parts. It was rejected before sign-in or upload, and the original file was not changed.",
                "Open the original in Excel, save it as an .xlsm file, close Excel, and run the migration again. If it still fails, contact FlightLogX support with the diagnostic report.");
        }

        if (stage == PilotWorkbookMigrationStage.PreparingWorkbook ||
            ContainsInvalidData(error))
        {
            return new(
                PilotWorkbookMigrationFailureKind.Validation,
                "Migration Stopped - Spreadsheet Check Failed",
                "The spreadsheet did not pass the migration safety checks.",
                IncompleteState(
                    backupAvailable,
                    hostedMigrationCompleted,
                    "A copied or calculated value could not be verified exactly, so no success was reported."),
                "Keep the original spreadsheet unchanged and run the migration again. If the same check fails, contact FlightLogX support with the diagnostic report.");
        }

        return new(
            PilotWorkbookMigrationFailureKind.Unexpected,
            "Migration Stopped Safely",
            "FlightLogX could not finish the migration.",
            IncompleteState(
                backupAvailable,
                hostedMigrationCompleted,
                "The updater did not report success or replace an unverified spreadsheet."),
            "Close Excel and run the migration again. If the same problem returns, contact FlightLogX support with the diagnostic report.");
    }

    private static bool IsWrongAccount(Exception error)
    {
        if (Find<HostedSignInException>(error) is { } signIn &&
            signIn.Reason == HostedSignInFailureReason.PublicRegistrationBlocked)
        {
            return true;
        }

        return Contains(
            error,
            "does not belong to the signed-in account",
            "invited account",
            "invited FlightLogX account",
            "invited Google account");
    }

    private static bool IsNetworkInterruption(Exception error) =>
        Find<HttpRequestException>(error) is not null ||
        Find<TimeoutException>(error) is not null ||
        Find<OperationCanceledException>(error) is not null;

    private static bool IsUnsupportedWorkbook(Exception error) =>
        Contains(
            error,
            "automatic updates are supported from",
            "workbook version is not supported",
            "unsupported workbook");

    private static bool ContainsInvalidData(Exception error) =>
        Find<InvalidDataException>(error) is not null;

    private static bool IsHostedReadbackMismatch(Exception error) =>
        ContainsInvalidData(error) && Contains(
            error,
            "readback",
            "verified workbook receipt");

    private static string PlainDetail(Exception error, string fallback) =>
        string.IsNullOrWhiteSpace(error.Message) ? fallback : error.Message;

    private static string IncompleteState(
        bool backupAvailable,
        bool hostedMigrationCompleted,
        string baseDetail)
    {
        var hosted = hostedMigrationCompleted
            ? "The encrypted FlightLogX copy was verified, but the spreadsheet handoff is still incomplete."
            : "FlightLogX was not confirmed complete.";
        var backup = backupAvailable
            ? "The untouched timestamped backup was retained."
            : "No verified timestamped backup is available from this attempt.";
        return $"{baseDetail} {hosted} {backup}";
    }

    private static bool Contains(Exception error, params string[] values)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (values.Any(value => current.Message.Contains(value, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static TException? Find<TException>(Exception error)
        where TException : Exception
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }
}
