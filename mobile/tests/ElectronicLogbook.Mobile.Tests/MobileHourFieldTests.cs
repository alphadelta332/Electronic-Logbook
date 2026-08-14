using System.Globalization;
using ElectronicLogbook.Mobile;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileHourFieldTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("0.0")]
    public void Format_KeepsMissingAndZeroHoursBlank(string? value)
    {
        decimal? hours = value is null
            ? null
            : decimal.Parse(value, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, MobileHourField.Format(hours));
    }

    [Theory]
    [InlineData("0.7", "0.7")]
    [InlineData("1", "1.0")]
    [InlineData("12.34", "12.3")]
    public void Format_UsesOneDecimalForCommittedNonZeroHours(string value, string expected)
    {
        var hours = decimal.Parse(value, CultureInfo.InvariantCulture);

        Assert.Equal(expected, MobileHourField.Format(hours));
    }

    [Fact]
    public void Format_AlwaysUsesTheHtmlNumberDecimalSeparator()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.Equal("0.7", MobileHourField.Format(0.7m));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("0.7", "0.7")]
    [InlineData("1", "1")]
    [InlineData("", null)]
    [InlineData("not-a-number", null)]
    public void Parse_PreservesValidTypedHoursAndClearsInvalidInput(string value, string? expected)
    {
        var parsed = MobileHourField.Parse(value);
        decimal? expectedHours = expected is null
            ? null
            : decimal.Parse(expected, CultureInfo.InvariantCulture);

        Assert.Equal(expectedHours, parsed);
    }
}
