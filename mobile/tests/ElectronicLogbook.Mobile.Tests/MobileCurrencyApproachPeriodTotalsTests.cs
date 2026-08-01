namespace ElectronicLogbook.Mobile.Tests;

using ElectronicLogbook.Portable;

public sealed class MobileCurrencyApproachPeriodTotalsTests
{
    [Fact]
    public void Summary_CurrentRnpRowShowsQualifyingTwoDimensionalAndCdiActivityFromOtherTypes()
    {
        var today = new DateOnly(2026, 8, 1);
        var entry = Entry(
            today.AddDays(-20),
            ils: 1,
            ndb: 1,
            circling: 1,
            instrumentProficiencyCheck: true);

        var summary = MobileCurrencyRecencySummary.Create(
            [entry],
            PortableLogbookCurrencyOverrideDates.Empty,
            today);
        var rnpRow = summary.SingleEngineRows.Single(row => row.Requirement == "RNP");

        Assert.Equal("Current", rnpRow.Status);
        Assert.Equal(0, rnpRow.RelevantPeriodTotal);
        Assert.Equal(
            ["1 × 2D", "1 × CDI"],
            MobileCurrencyPeriodDetail.Items(rnpRow, summary.ApproachPeriodTotals));
    }

    [Fact]
    public void Create_GroupsRecentSingleEngineApproachesLikeTheCurrencyCalculator()
    {
        var today = new DateOnly(2026, 8, 1);
        var totals = MobileCurrencyApproachPeriodTotals.Create(
            [
                Entry(today.AddDays(-20), ils: 1),
                Entry(today.AddDays(-10), ndb: 1),
                Entry(today.AddDays(-5), rnp: 2),
                Entry(today.AddDays(-100), ils: 9),
                Entry(today.AddDays(-2), ils: 9, multiEngine: true)
            ],
            today);

        Assert.Equal(10, totals.ThreeDimensional);
        Assert.Equal(3, totals.TwoDimensional);
        Assert.Equal(12, totals.Cdi);
        Assert.Equal(1, totals.Azimuth);
    }

    private static PortableLogbookWorkbookEntry Entry(
        DateOnly date,
        int? ils = null,
        int? rnp = null,
        int? ndb = null,
        int? circling = null,
        bool instrumentProficiencyCheck = false,
        bool multiEngine = false) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            Ils = ils,
            Rnp = rnp,
            Ndb = ndb,
            Circling = circling,
            InstrumentProficiencyCheck = instrumentProficiencyCheck,
            SeCommandDay = multiEngine ? null : 1,
            MeCommandDay = multiEngine ? 1 : null
        };
}
