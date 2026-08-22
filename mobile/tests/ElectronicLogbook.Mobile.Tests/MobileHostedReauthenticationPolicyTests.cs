using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileHostedReauthenticationPolicyTests
{
    [Fact]
    public void ShouldOffer_WithoutHostedState_ReturnsTrue()
    {
        Assert.True(MobileHostedReauthenticationPolicy.ShouldOffer(null, null));
    }

    [Theory]
    [InlineData(MobileConnectionStage.CREDENTIAL_LOAD)]
    [InlineData(MobileConnectionStage.ACCESS_TOKEN_VALIDATE)]
    [InlineData(MobileConnectionStage.AUTH_USER_READ)]
    [InlineData(MobileConnectionStage.DEVICE_READ)]
    public void ShouldOffer_NeedsAttentionAndAuthenticationPreflightFailure_ReturnsTrue(
        MobileConnectionStage failedStage)
    {
        var hosted = HostedState(PortableHostedSyncStatus.NeedsAttention);
        var diagnostics = FailedDiagnostics(failedStage);

        Assert.True(MobileHostedReauthenticationPolicy.ShouldOffer(hosted, diagnostics));
    }

    [Theory]
    [InlineData(MobileConnectionStage.ACCOUNT_READ)]
    [InlineData(MobileConnectionStage.ANDROID_KEYSTORE_PROBE)]
    [InlineData(MobileConnectionStage.INDEXEDDB_PROBE)]
    public void ShouldOffer_NeedsAttentionButNonAuthenticationFailure_ReturnsFalse(
        MobileConnectionStage failedStage)
    {
        var hosted = HostedState(PortableHostedSyncStatus.NeedsAttention);
        var diagnostics = FailedDiagnostics(failedStage);

        Assert.False(MobileHostedReauthenticationPolicy.ShouldOffer(hosted, diagnostics));
    }

    [Fact]
    public void ShouldOffer_NeedsAttentionBeforePreflight_ReturnsFalse()
    {
        var hosted = HostedState(PortableHostedSyncStatus.NeedsAttention);

        Assert.False(MobileHostedReauthenticationPolicy.ShouldOffer(hosted, null));
    }

    [Fact]
    public void ShouldOffer_SyncedWithStaleFailure_ReturnsFalse()
    {
        var hosted = HostedState(PortableHostedSyncStatus.Synced);
        var diagnostics = FailedDiagnostics(MobileConnectionStage.DEVICE_READ);

        Assert.False(MobileHostedReauthenticationPolicy.ShouldOffer(hosted, diagnostics));
    }

    private static BrowserHostedSyncState HostedState(PortableHostedSyncStatus status) =>
        new(
            new HostedAccountId("acct_private"),
            new LogbookId("log_private"),
            new DeviceId("dev_android"),
            LastAcknowledgedHostedRevision: 7,
            status);

    private static MobileConnectionDiagnosticReport FailedDiagnostics(MobileConnectionStage stage) =>
        new(
            "attempt-redacted",
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
            stage,
            Passed: false,
            ErrorCode: "TEST_FAILURE",
            MobileCredentialState.Registered,
            AccountMatched: true,
            DeviceMatched: stage == MobileConnectionStage.DEVICE_READ ? false : null,
            AndroidKeystorePassed: null,
            IndexedDbPassed: null,
            LocalPlanPassed: null,
            PackageKeyImported: null,
            LocalStateSaved: null,
            LocalStateReadBack: null,
            Stages: []);
}
