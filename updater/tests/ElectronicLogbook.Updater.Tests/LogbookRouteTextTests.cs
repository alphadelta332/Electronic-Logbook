namespace ElectronicLogbook.Updater.Tests;

public sealed class LogbookRouteTextTests
{
    [Theory]
    [InlineData("YSSY", "", "YMML", "", "YSSY YMML")]
    [InlineData(" YSSY ", " WOL H65 ", " YMML ", "ignored", "YSSY WOL H65 YMML")]
    [InlineData("", "", "", "remarks route text", "remarks route text")]
    [InlineData(" ", " ", " ", "  remarks route text  ", "  remarks route text  ")]
    public void BuildAirportStatsSourceUsesRouteFieldsBeforeRemarks(
        string from,
        string via,
        string to,
        string remarks,
        string expected)
    {
        Assert.Equal(expected, LogbookRouteText.BuildAirportStatsSource(from, via, to, remarks));
    }
}
