using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookSyncContractsTests
{
    [Fact]
    public async Task InMemoryLedgerAppendsPagesAndAcknowledgesEncryptedOperations()
    {
        var ledger = new InMemoryHostedLogbookLedger();
        var logbookId = new LogbookId("log_sync");
        var deviceId = new DeviceId("dev_android");
        var upload = new HostedOperationUpload(
            new RevisionId("rev_001"),
            new EntryId("ent_001"),
            deviceId,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            PortableLogbookDocumentV2.CurrentSchemaVersion,
            PayloadCiphertext: "ciphertext",
            PayloadNonce: "nonce",
            PayloadTag: "tag",
            ParentRevisionIds: []);

        var appended = await ledger.AppendOperationsAsync(logbookId, deviceId, [upload]);
        var firstPage = await ledger.ReadMissingOperationsAsync(logbookId, afterHostedRevision: 0, pageSize: 1);
        await ledger.RecordAcknowledgementAsync(logbookId, deviceId, firstPage.ThroughHostedRevision);

        Assert.Equal(1, appended.ThroughHostedRevision);
        var envelope = Assert.Single(firstPage.Operations);
        Assert.Equal(1, envelope.HostedRevision);
        Assert.Equal("ciphertext", envelope.PayloadCiphertext);
        Assert.Equal(firstPage.ThroughHostedRevision, ledger.Acknowledgements[(logbookId, deviceId)]);
    }

    [Fact]
    public async Task PlatformTestDoublesCoverAuthStorageSchedulerNetworkClockAndWorkbookBridge()
    {
        var clock = new ManualSyncClock(DateTimeOffset.Parse("2026-08-06T01:00:00Z"));
        var network = new StaticNetworkStatus(new NetworkAvailability(IsOnline: true));
        var storage = new InMemorySyncSecureStorage();
        var authenticator = new InMemoryHostedLogbookAuthenticator(
            new HostedAccountId("acct_private"),
            new DeviceId("dev_android"),
            clock);
        var scheduler = new RecordingBackgroundSyncScheduler();
        var bridge = new RecordingWorkbookSyncBridge(new WorkbookSyncSnapshot(
            "placeholder.xlsm",
            new LogbookId("log_sync"),
            new DeviceId("dev_workbook"),
            LocalRevisionIds: [],
            LastAcknowledgedHostedRevision: 0,
            IsEditable: true));

        await storage.SaveAsync(new SyncSecretName("refresh-token"), new byte[] { 1, 2, 3 });
        var signInStart = await authenticator.StartEmailSignInAsync("pilot@example.com");
        var session = await authenticator.CompleteEmailSignInAsync("123456");
        await scheduler.ScheduleAsync(new BackgroundSyncRequest(
            new LogbookId("log_sync"),
            session.DeviceId,
            BackgroundSyncReason.LocalEdit,
            clock.UtcNow));
        var snapshot = await bridge.ReadSnapshotAsync(new WorkbookSyncRequest(
            "Electronic_Logbook_Master.xlsm",
            new LogbookId("log_sync"),
            new DeviceId("dev_workbook")));
        var applyResult = await bridge.ApplyAsync(new WorkbookSyncApplyRequest(snapshot, RemoteOperations: [], ThroughHostedRevision: 4));

        Assert.Equal("p***@example.com", signInStart.DeliveryHint);
        Assert.True((await network.GetAvailabilityAsync()).IsOnline);
        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.LoadAsync(new SyncSecretName("refresh-token")));
        Assert.Equal(clock.UtcNow.AddHours(1), session.AccessTokenExpiresAt);
        Assert.Single(scheduler.Scheduled);
        Assert.Equal("Electronic_Logbook_Master.xlsm", snapshot.WorkbookPath);
        Assert.True(applyResult.Applied);
        Assert.Equal(4, applyResult.LastAcknowledgedHostedRevision);
    }
}
