using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PilotWorkbookMigrationFailurePresenterTests
{
    [Fact]
    public void Create_WrongInvitedAccountExplainsExactRetryWithoutReportingSuccess()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.SigningIn,
            new HostedSignInException(
                HostedSignInFailureReason.InvitationRequired,
                "Google sign-in could not open the invited FlightLogX account."),
            backupAvailable: true,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.WrongAccount, result.Kind);
        Assert.Contains("Google account", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pilot invitation", result.RecoveryAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not confirmed complete", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_CancelledBrowserSignInIsNotMisreportedAsWrongAccount()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.SigningIn,
            new HostedSignInException(
                HostedSignInFailureReason.InvitationRequired,
                "Google sign-in was cancelled or could not be completed."),
            backupAvailable: true,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.SignIn, result.Kind);
        Assert.Contains("sign-in did not finish", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UnsupportedWorkbookKeepsTheUsefulVersionDetail()
    {
        const string message =
            "This workbook is version 1.9.0. Automatic updates are supported from 2.0.2 or newer.";

        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.PreparingWorkbook,
            new InvalidDataException(message),
            backupAvailable: false,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.UnsupportedWorkbook, result.Kind);
        Assert.Equal(message, result.Detail);
        Assert.Contains("correct upgrade path", result.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UnreadableSourceIsSeparatedFromLaterValidationFailure()
    {
        var corrupt = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.PreparingWorkbook,
            new InvalidDataException("Central Directory corrupt."),
            backupAvailable: false,
            hostedMigrationCompleted: false);
        var validation = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.PreparingWorkbook,
            new InvalidDataException("Copied totals do not match."),
            backupAvailable: false,
            hostedMigrationCompleted: false,
            UpdaterPhaseIds.ValidatePreservedData);

        Assert.Equal(PilotWorkbookMigrationFailureKind.CorruptWorkbook, corrupt.Kind);
        Assert.Equal(PilotWorkbookMigrationFailureKind.Validation, validation.Kind);
        Assert.Contains("diagnostic report", validation.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NetworkTimeoutIsRetryableAndDoesNotClaimHostedCompletion()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.MovingToFlightLogX,
            new TaskCanceledException("HTTP request timed out."),
            backupAvailable: true,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.NetworkInterruption, result.Kind);
        Assert.Contains("resume the same migration", result.RecoveryAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not confirmed complete", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Migration Complete", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_HostedMismatchStopsWithoutReplacingTheWorkbook()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.MovingToFlightLogX,
            new InvalidDataException("Hosted flight-operation readback does not exactly match the converted workbook operation set."),
            backupAvailable: true,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.HostedReadbackMismatch, result.Kind);
        Assert.Contains("did not exactly match", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not accepted as success", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_InvalidRecoveryStateIsNotMisreportedAsReadbackMismatch()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.MovingToFlightLogX,
            new InvalidDataException("Workbook account recovery configuration is invalid."),
            backupAvailable: true,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.HostedSafetyState, result.Kind);
        Assert.Contains("account or recovery state", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_BackupFailureExplainsThatSignInAndUploadNeverStarted()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.PreparingWorkbook,
            new WorkbookMigrationBackupException(
                "backup failed",
                "C:\\Logbook_Backup.xlsm",
                new IOException("disk full")),
            backupAvailable: false,
            hostedMigrationCompleted: false);

        Assert.Equal(PilotWorkbookMigrationFailureKind.Backup, result.Kind);
        Assert.Contains("No Google sign-in or upload", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("free space", result.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_InstallFailureExplainsCompletedHostedStateAndSafeRetry()
    {
        var result = PilotWorkbookMigrationFailurePresenter.Create(
            PilotWorkbookMigrationStage.InstallingWorkbook,
            new IOException("workbook locked"),
            backupAvailable: true,
            hostedMigrationCompleted: true);

        Assert.Equal(PilotWorkbookMigrationFailureKind.WorkbookInstall, result.Kind);
        Assert.Contains("verified in FlightLogX", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("untouched timestamped backup was retained", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without uploading a duplicate", result.RecoveryAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What to do:", result.CustomerMessage, StringComparison.Ordinal);
    }
}
