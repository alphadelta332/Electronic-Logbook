using ElectronicLogbook.Mobile;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileRelativeDateTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void FormatReturnsNoIndicatorWhenDateIsMissing()
    {
        Assert.Null(MobileRelativeDate.Format(null, Today));
    }

    [Fact]
    public void FormatUsesNaturalLabelsForTodayAndYesterday()
    {
        Assert.Equal("Today", MobileRelativeDate.Format(Today, Today));
        Assert.Equal("Yesterday", MobileRelativeDate.Format(Today.AddDays(-1), Today));
    }

    [Fact]
    public void FormatUsesDayCountsBeforeACompleteCalendarMonth()
    {
        Assert.Equal("2 days ago", MobileRelativeDate.Format(Today.AddDays(-2), Today));
        Assert.Equal("29 days ago", MobileRelativeDate.Format(new DateOnly(2026, 8, 12), Today));
    }

    [Fact]
    public void FormatUsesCompleteCalendarMonthsBeforeACompleteYear()
    {
        Assert.Equal("1 month ago", MobileRelativeDate.Format(new DateOnly(2026, 8, 10), Today));
        Assert.Equal("7 months ago", MobileRelativeDate.Format(new DateOnly(2026, 2, 10), Today));
    }

    [Fact]
    public void FormatUsesCompleteCalendarYears()
    {
        Assert.Equal("1 year ago", MobileRelativeDate.Format(new DateOnly(2025, 9, 10), Today));
        Assert.Equal("3 years ago", MobileRelativeDate.Format(new DateOnly(2023, 9, 10), Today));
    }

    [Fact]
    public void FormatHandlesEndOfMonthAndLeapDayBoundaries()
    {
        Assert.Equal(
            "1 month ago",
            MobileRelativeDate.Format(new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30)));
        Assert.Equal(
            "1 year ago",
            MobileRelativeDate.Format(new DateOnly(2024, 2, 29), new DateOnly(2025, 2, 28)));
    }
}
