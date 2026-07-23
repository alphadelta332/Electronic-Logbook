namespace ElectronicLogbook.Portable;

public static class PortableLogbookKeyCustody
{
    public const string UnrecoverableKeyWarning =
        "If this key and recovery code are lost, encrypted portable logbook packages cannot be recovered.";

    public const string RecoveryCodeStorageWarning =
        "Store the recovery code separately from the workbook and exported logbook packages.";

    public static PortableLogbookKeyCustodyPlan CreatePlan(PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new PortableLogbookKeyCustodyPlan(
            key.ToRecoveryCode(),
            UnrecoverableKeyWarning,
            RecoveryCodeStorageWarning,
            RecoveryCodeConfirmed: false);
    }

    public static PortableLogbookKeyCustodyPlan ConfirmRecoveryCodeSaved(
        PortableLogbookKeyCustodyPlan plan,
        string enteredRecoveryCode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(enteredRecoveryCode);

        var confirmed = string.Equals(
            NormalizeRecoveryCode(plan.RecoveryCode),
            NormalizeRecoveryCode(enteredRecoveryCode),
            StringComparison.Ordinal);
        return plan with { RecoveryCodeConfirmed = confirmed };
    }

    private static string NormalizeRecoveryCode(string recoveryCode) =>
        recoveryCode.Trim().Replace(" ", "", StringComparison.Ordinal);
}

public sealed record PortableLogbookKeyCustodyPlan(
    string RecoveryCode,
    string UnrecoverableKeyWarning,
    string RecoveryCodeStorageWarning,
    bool RecoveryCodeConfirmed);
