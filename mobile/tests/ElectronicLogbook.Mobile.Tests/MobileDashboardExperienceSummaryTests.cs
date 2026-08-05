using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileDashboardExperienceSummaryTests
{
    [Fact]
    public void CreateKeepsAuthorityAndClassifiedEngineDenominatorsIndependent()
    {
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            SeCommandDay = 100m,
            SeCommandNight = 10m,
            SeIcusDay = 5m,
            SeIcusNight = 1m,
            SeDualDay = 3m,
            SeDualNight = 4m,
            MeCommandDay = 20m,
            MeCommandNight = 2m,
            MeIcusDay = 6m,
            MeIcusNight = 2m,
            MeDualDay = 7m,
            MeDualNight = 8m,
            CopilotDay = 9m,
            CopilotNight = 10m,
            IfrIf = 40m,
            IfrSim = 50m
        };

        var summary = MobileDashboardExperienceSummary.Create([entry]);

        Assert.Equal(132m, summary.CommandHours);
        Assert.Equal(14m, summary.IcusHours);
        Assert.Equal(22m, summary.DualHours);
        Assert.Equal(19m, summary.CopilotHours);
        Assert.Equal(187m, summary.AuthorityHours);
        Assert.Equal(123m, summary.SingleEngineHours);
        Assert.Equal(45m, summary.MultiEngineHours);
        Assert.Equal(168m, summary.ClassifiedEngineHours);
    }

    [Fact]
    public void CreateRejectsMissingEntries()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MobileDashboardExperienceSummary.Create(null!));
    }
}
