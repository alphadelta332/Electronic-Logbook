using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookKeyCustodyTests
{
    [Fact]
    public void CreatePlanContainsRecoveryCodeAndUnrecoverableKeyWarning()
    {
        var key = PortableLogbookKey.Generate();

        var plan = PortableLogbookKeyCustody.CreatePlan(key);

        Assert.Equal(key, PortableLogbookKey.FromRecoveryCode(plan.RecoveryCode));
        Assert.False(plan.RecoveryCodeConfirmed);
        Assert.Contains("cannot be recovered", plan.UnrecoverableKeyWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separately", plan.RecoveryCodeStorageWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmRecoveryCodeSavedMarksPlanConfirmedOnlyForMatchingCode()
    {
        var key = PortableLogbookKey.Generate();
        var plan = PortableLogbookKeyCustody.CreatePlan(key);

        var confirmed = PortableLogbookKeyCustody.ConfirmRecoveryCodeSaved(plan, " " + plan.RecoveryCode + " ");
        var rejected = PortableLogbookKeyCustody.ConfirmRecoveryCodeSaved(plan, PortableLogbookKey.Generate().ToRecoveryCode());

        Assert.True(confirmed.RecoveryCodeConfirmed);
        Assert.False(rejected.RecoveryCodeConfirmed);
    }
}
