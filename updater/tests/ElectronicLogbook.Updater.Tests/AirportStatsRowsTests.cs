namespace ElectronicLogbook.Updater.Tests;

public sealed class AirportStatsRowsTests
{
    [Fact]
    public void IsSimOnlyReturnsTrueWhenOnlyIfrSimHasHours()
    {
        object?[] ifrSim = [0, "1.5"];
        object?[] dayHours = [1.0, 0];
        object?[] nightHours = [0, ""];

        Assert.True(AirportStatsRows.IsSimOnly(1, ifrSim, [dayHours, nightHours]));
    }

    [Fact]
    public void IsSimOnlyReturnsFalseWhenOtherHoursArePresent()
    {
        object?[] ifrSim = [2.0];
        object?[] dayHours = [0.1];

        Assert.False(AirportStatsRows.IsSimOnly(0, ifrSim, [dayHours]));
    }

    [Fact]
    public void IsSimOnlyReturnsFalseWhenSimHoursAreBlank()
    {
        object?[] ifrSim = [""];
        object?[] dayHours = [0];

        Assert.False(AirportStatsRows.IsSimOnly(0, ifrSim, [dayHours]));
    }
}
