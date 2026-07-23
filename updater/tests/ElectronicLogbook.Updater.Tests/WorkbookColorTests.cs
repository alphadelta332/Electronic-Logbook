namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookColorTests
{
    [Theory]
    [InlineData(0x000000, 0.2d, 0x333333)]
    [InlineData(0xFFFFFF, 0.8d, 0xCCCCCC)]
    [InlineData(0x0000FF, 0.5d, 0x0000FF)]
    [InlineData(0x00FF00, 0.5d, 0x00FF00)]
    [InlineData(0xFF0000, 0.5d, 0xFF0000)]
    public void WithLightnessPreservesHueAndSetsTargetLightness(
        int sourceColor,
        double lightness,
        int expected)
    {
        Assert.Equal(expected, WorkbookColor.WithLightness(sourceColor, lightness));
    }

    [Theory]
    [InlineData(0x000000, 0xFFFFFF)]
    [InlineData(0xFFFFFF, 0x000000)]
    [InlineData(0x808080, 0xFFFFFF)]
    [InlineData(0xC0C0C0, 0x000000)]
    public void ContrastingTextColorChoosesReadableBlackOrWhite(
        int backgroundColor,
        int expected)
    {
        Assert.Equal(expected, WorkbookColor.ContrastingTextColor(backgroundColor));
    }
}
