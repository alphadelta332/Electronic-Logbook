namespace ElectronicLogbook.Updater.Tests;

public sealed class WorkbookValueTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(1.25d, "1.25")]
    [InlineData("  text  ", "  text  ")]
    public void StableValueNormalisesWorkbookValues(object? value, string expected)
    {
        Assert.Equal(expected, WorkbookValue.StableValue(value));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(0, false)]
    [InlineData("0", false)]
    public void IsBlankValueOnlyTreatsNullAndWhitespaceStringsAsBlank(object? value, bool expected)
    {
        Assert.Equal(expected, WorkbookValue.IsBlankValue(value));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1.25d, 1.25d)]
    [InlineData(2, 2d)]
    [InlineData("3.5", 3.5d)]
    [InlineData("bad", 0)]
    public void ToDoubleConvertsCommonWorkbookValues(object? value, double expected)
    {
        Assert.Equal(expected, WorkbookValue.ToDouble(value));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("Y", true)]
    [InlineData("x", true)]
    [InlineData("no", false)]
    public void ToBooleanConvertsWorkbookFlags(object? value, bool expected)
    {
        Assert.Equal(expected, WorkbookValue.ToBoolean(value));
    }

    [Theory]
    [InlineData(2026, "Jan", 17)]
    [InlineData(2026, "January", 17)]
    [InlineData(2026, 1, 17)]
    [InlineData(2026, "1", 17)]
    public void ToLogbookDateAcceptsWorkbookDateParts(object year, object month, object day)
    {
        var expected = new DateTime(2026, 1, 17).ToOADate();

        Assert.Equal(expected, WorkbookValue.ToLogbookDate(year, month, day));
    }

    [Theory]
    [InlineData(0, "Jan", 17)]
    [InlineData(2026, "", 17)]
    [InlineData(2026, "bad", 17)]
    [InlineData(2026, "Jan", 0)]
    [InlineData(2026, "Feb", 31)]
    public void ToLogbookDateRejectsInvalidDateParts(object year, object month, object day)
    {
        Assert.Null(WorkbookValue.ToLogbookDate(year, month, day));
    }
}
