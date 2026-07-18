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
}
