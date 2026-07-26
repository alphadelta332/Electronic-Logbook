namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookDashboardCurrencyTests
{
    [Theory]
    [InlineData(91, 31, true)]
    [InlineData(90, 31, false)]
    [InlineData(91, 30, false)]
    public void IsVfrGreen_RequiresStrictFlightReviewAndDayPassengerThresholds(
        int flightReviewDaysRemaining,
        int dayPassengerDaysRemaining,
        bool expected)
    {
        var actual = PortableLogbookDashboardCurrency.IsVfrGreen(
            Row(flightReviewDaysRemaining),
            Row(dayPassengerDaysRemaining));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsVfrRed_RecognizesExpiredAndNeverCurrentFlightReviewsButNotTheirExpiryBoundary()
    {
        var neverCurrent = PortableLogbookDashboardCurrency.IsVfrRed(Row(0, "Not Current"));
        var expired = PortableLogbookDashboardCurrency.IsVfrRed(
            Row(0, "Not Current", new DateOnly(2026, 7, 25)));
        var currentOnExpiryDate = PortableLogbookDashboardCurrency.IsVfrRed(
            Row(0, "Current", new DateOnly(2026, 7, 26)));

        Assert.True(neverCurrent);
        Assert.True(expired);
        Assert.False(currentOnExpiryDate);
    }

    [Theory]
    [InlineData(90, "Current", 31, true)]
    [InlineData(91, "Current", 30, true)]
    [InlineData(91, "Current", 31, false)]
    [InlineData(0, "Not Current", 31, false)]
    public void IsVfrOrange_IsTheComplementOfTheGreenAndRedRules(
        int flightReviewDaysRemaining,
        string flightReviewStatus,
        int dayPassengerDaysRemaining,
        bool expected)
    {
        var actual = PortableLogbookDashboardCurrency.IsVfrOrange(
            Row(flightReviewDaysRemaining, flightReviewStatus),
            Row(dayPassengerDaysRemaining));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(91, 91, 31, 31, 91, true)]
    [InlineData(90, 91, 31, 31, 91, false)]
    [InlineData(91, 90, 31, 31, 91, false)]
    [InlineData(91, 91, 30, 31, 91, false)]
    [InlineData(91, 91, 31, 30, 91, false)]
    [InlineData(91, 91, 31, 31, 90, false)]
    public void IsIfrGreen_RequiresEveryStrictDashboardThreshold(
        int flightReviewDaysRemaining,
        int instrumentProficiencyCheckDaysRemaining,
        int dayPassengerDaysRemaining,
        int ifrAppsDaysRemaining,
        int singlePilotIfrDaysRemaining,
        bool expected)
    {
        var actual = PortableLogbookDashboardCurrency.IsIfrGreen(
            Row(flightReviewDaysRemaining),
            Row(instrumentProficiencyCheckDaysRemaining),
            Row(dayPassengerDaysRemaining),
            Row(ifrAppsDaysRemaining),
            Row(singlePilotIfrDaysRemaining));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Current", "Current", "Current", "Current", false)]
    [InlineData("Not Current", "Current", "Current", "Current", true)]
    [InlineData("Current", "Not Current", "Current", "Current", true)]
    [InlineData("Current", "Current", "Not Current", "Current", true)]
    [InlineData("Current", "Current", "Current", "Not Current", true)]
    public void IsIfrRed_RecognizesEveryRequiredExpiredOrNeverCurrentCurrency(
        string flightReviewStatus,
        string instrumentProficiencyCheckStatus,
        string ifrAppsStatus,
        string singlePilotIfrStatus,
        bool expected)
    {
        var actual = PortableLogbookDashboardCurrency.IsIfrRed(
            Row(91, flightReviewStatus),
            Row(91, instrumentProficiencyCheckStatus),
            Row(31, ifrAppsStatus),
            Row(91, singlePilotIfrStatus));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(90, 91, 31, 31, 91, "Current", true)]
    [InlineData(91, 90, 31, 31, 91, "Current", true)]
    [InlineData(91, 91, 30, 31, 91, "Current", true)]
    [InlineData(91, 91, 31, 30, 91, "Current", true)]
    [InlineData(91, 91, 31, 31, 90, "Current", true)]
    [InlineData(91, 91, 31, 31, 91, "Current", false)]
    [InlineData(91, 91, 31, 31, 91, "Not Current", false)]
    public void IsIfrOrange_IsTheComplementOfTheGreenAndRedRules(
        int flightReviewDaysRemaining,
        int instrumentProficiencyCheckDaysRemaining,
        int dayPassengerDaysRemaining,
        int ifrAppsDaysRemaining,
        int singlePilotIfrDaysRemaining,
        string flightReviewStatus,
        bool expected)
    {
        var actual = PortableLogbookDashboardCurrency.IsIfrOrange(
            Row(flightReviewDaysRemaining, flightReviewStatus),
            Row(instrumentProficiencyCheckDaysRemaining),
            Row(dayPassengerDaysRemaining),
            Row(ifrAppsDaysRemaining),
            Row(singlePilotIfrDaysRemaining));

        Assert.Equal(expected, actual);
    }

    private static PortableLogbookCurrencyRow Row(
        int daysRemaining,
        string status = "Current",
        DateOnly? calculatedExpiry = null) => new(
        "Operation",
        "61.000",
        "Test",
        1,
        status,
        daysRemaining,
        null,
        calculatedExpiry);
}
