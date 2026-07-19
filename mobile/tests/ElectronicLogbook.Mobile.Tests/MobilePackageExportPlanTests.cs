using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobilePackageExportPlanTests
{
    [Fact]
    public void CreateUsesSharedPackageNamerAndElogbookContentType()
    {
        var document = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_mobile"), [], []);
        var exportedAt = DateTimeOffset.Parse("2026-07-19T03:04:05Z");

        var plan = MobilePackageExportPlan.Create(document, hasPackageKey: true, exportedAt);

        Assert.Equal("log_mobile_20260719_030405.elogbook", plan.FileName);
        Assert.Equal(BrowserFileStore.ElogbookContentType, plan.ContentType);
        Assert.Equal(exportedAt, plan.ExportedAt);
    }

    [Fact]
    public void CreateRequiresBrowserHeldPackageKey()
    {
        var document = PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_mobile"), [], []);

        var error = Assert.Throws<MobilePackageExportPlanException>(() =>
            MobilePackageExportPlan.Create(document, hasPackageKey: false, DateTimeOffset.Parse("2026-07-19T03:04:05Z")));

        Assert.Contains("package key", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
