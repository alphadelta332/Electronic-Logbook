using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageImportPlanTests
{
    [Fact]
    public void InspectReadsPublicManifestWithoutDecryptingPackage()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.FromBytes(Enumerable.Range(1, PortableLogbookPackage.KeySizeBytes).Select(value => (byte)value).ToArray());
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, packageBytes);

        var plan = MobilePackageImportPlan.Inspect(file);

        Assert.Equal("backup.elogbook", plan.FileName);
        Assert.Equal(document.LogbookId, plan.LogbookId);
        Assert.Equal(document.Operations.Count, plan.OperationCount);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, plan.SchemaVersion);
        Assert.True(plan.PackageCreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public void InspectRejectsWrongExtensionBeforeManifestRead()
    {
        var file = new BrowserFile("backup.zip", "application/zip", []);

        Assert.Throws<BrowserFileStoreException>(() => MobilePackageImportPlan.Inspect(file));
    }

    [Fact]
    public void InspectRejectsInvalidPackageHeader()
    {
        var file = new BrowserFile("backup.elogbook", BrowserFileStore.ElogbookContentType, [1, 2, 3]);

        var error = Assert.Throws<PortableLogbookPackageException>(() => MobilePackageImportPlan.Inspect(file));

        Assert.Equal(PortableLogbookPackageError.TruncatedPackage, error.Error);
    }

    [Fact]
    public void CheckCompatibilityRejectsDifferentLogbookManifest()
    {
        var plan = new MobilePackageImportPlanResult(
            "backup.elogbook",
            new LogbookId("log_other"),
            1,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            PortableLogbookDocument.CurrentSchemaVersion);

        var compatibility = MobilePackageImportPlan.CheckCompatibility(plan, new LogbookId("log_mobile"));

        Assert.Equal(MobilePackageImportCompatibility.WrongLogbook, compatibility);
    }

    [Fact]
    public void CheckCompatibilityAcceptsMatchingLogbookManifest()
    {
        var plan = new MobilePackageImportPlanResult(
            "backup.elogbook",
            new LogbookId("log_mobile"),
            1,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            PortableLogbookDocument.CurrentSchemaVersion);

        var compatibility = MobilePackageImportPlan.CheckCompatibility(plan, new LogbookId("log_mobile"));

        Assert.Equal(MobilePackageImportCompatibility.Compatible, compatibility);
    }

    [Fact]
    public void CheckCompatibilityRejectsUnsupportedSchemaManifest()
    {
        var plan = new MobilePackageImportPlanResult(
            "backup.elogbook",
            new LogbookId("log_mobile"),
            1,
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            PortableLogbookDocument.CurrentSchemaVersion + 1);

        var compatibility = MobilePackageImportPlan.CheckCompatibility(plan, new LogbookId("log_mobile"));

        Assert.Equal(MobilePackageImportCompatibility.UnsupportedSchema, compatibility);
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var logbookId = new LogbookId("log_mobile");
        var create = new CreateEntryOperation(
            logbookId,
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSCN",
                PilotInCommand = 1.2m
            });
        return PortableLogbookDocument.CreateAustraliaFirst(logbookId, [], [create]);
    }
}
