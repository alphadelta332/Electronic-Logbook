using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileHostedReauthenticationPolicy
{
    public static bool ShouldOffer(
        BrowserHostedSyncState? hostedSync,
        MobileConnectionDiagnosticReport? diagnostics)
    {
        if (hostedSync is null)
        {
            return true;
        }

        if (hostedSync.LastStatus != PortableHostedSyncStatus.NeedsAttention
            || diagnostics is null
            || diagnostics.Passed)
        {
            return false;
        }

        return diagnostics.CurrentStage is
            MobileConnectionStage.CREDENTIAL_LOAD or
            MobileConnectionStage.ACCESS_TOKEN_VALIDATE or
            MobileConnectionStage.AUTH_USER_READ or
            MobileConnectionStage.DEVICE_READ;
    }
}
