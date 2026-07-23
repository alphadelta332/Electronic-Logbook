using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookSetupTests
{
    [Fact]
    public void CreateInitialSetupPlanBuildsValidDocumentAndEncryptedPackage()
    {
        var logbookId = new LogbookId("log_setup");
        var deviceId = new DeviceId("dev_excel");
        var key = PortableLogbookKey.Generate();

        var plan = PortableLogbookSetup.CreateInitialSetupPlan(
            [Entry("VH-ABC")],
            [],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            logbookId,
            deviceId,
            key,
            new PortableLogbookIdFactory(() => new EntryId("ent_1"), () => new RevisionId("rev_1")));
        var read = PortableLogbookPackage.Read(plan.InitialPackageBytes, key, logbookId);

        Assert.Equal(logbookId, plan.LogbookId);
        Assert.Equal(deviceId, plan.DeviceId);
        Assert.Equal(logbookId, read.Document.LogbookId);
        Assert.Equal(new EntryId("ent_1"), Assert.Single(read.Document.Operations).EntryId);
        var row = Assert.Single(plan.WorkbookRows);
        Assert.Equal(new EntryId("ent_1"), row.EntryId);
        Assert.Equal(new RevisionId("rev_1"), row.CurrentRevisionId);
        Assert.Equal("VH-ABC", row.Entry.Registration);
        Assert.True(PortableLogbookValidator.Validate(plan.InitialDocument).IsValid);
    }

    [Fact]
    public void CreateInitialSetupPlanGeneratesMissingIdentifiersAndKey()
    {
        var plan = PortableLogbookSetup.CreateInitialSetupPlan(
            [],
            [],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        Assert.StartsWith("log_", plan.LogbookId.Value, StringComparison.Ordinal);
        Assert.StartsWith("dev_", plan.DeviceId.Value, StringComparison.Ordinal);
        Assert.Equal(PortableLogbookPackage.KeySizeBytes, plan.Key.ToBytes().Length);
    }

    [Fact]
    public void CreateInitialSetupPlanReturnsWorkbookRowsWithAssignedStableIdsInInputOrder()
    {
        var plan = PortableLogbookSetup.CreateInitialSetupPlan(
            [Entry("VH-ABC"), Entry("VH-DEF")],
            [],
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            new LogbookId("log_setup"),
            new DeviceId("dev_excel"),
            PortableLogbookKey.Generate(),
            new PortableLogbookIdFactory(
                QueueIds([new EntryId("ent_1"), new EntryId("ent_2")]),
                QueueIds([new RevisionId("rev_1"), new RevisionId("rev_2")])));

        Assert.Equal(
            [(new EntryId("ent_1"), new RevisionId("rev_1"), "VH-ABC"), (new EntryId("ent_2"), new RevisionId("rev_2"), "VH-DEF")],
            plan.WorkbookRows.Select(row => (row.EntryId, row.CurrentRevisionId, row.Entry.Registration)));
        Assert.Equal(
            plan.InitialDocument.Operations.Select(operation => (operation.EntryId, operation.RevisionId)),
            plan.WorkbookRows.Select(row => (row.EntryId!.Value, row.CurrentRevisionId!.Value)));
    }

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.2m
        };

    private static Func<T> QueueIds<T>(IEnumerable<T> ids)
    {
        var queue = new Queue<T>(ids);
        return () => queue.Dequeue();
    }
}
