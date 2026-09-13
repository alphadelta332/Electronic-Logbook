using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileCustomFieldTotalsTests
{
    private static readonly CustomFieldDefinition DecimalField =
        new(new CustomFieldId("cf_workbook_1"), "Night vision", 1);

    private static readonly CustomFieldDefinition CountField =
        new(new CustomFieldId("cf_workbook_4"), "External loads", 4);

    [Fact]
    public void Calculate_SumsNumericValuesInDefinitionOrderAndIgnoresTextOrMissingValues()
    {
        var entries = new[]
        {
            Entry((DecimalField.Id, "1.25"), (CountField.Id, "2")),
            Entry((DecimalField.Id, "2.25"), (CountField.Id, "not a number")),
            Entry((DecimalField.Id, null))
        };

        var totals = MobileCustomFieldTotals.Calculate([CountField, DecimalField], entries);

        Assert.Collection(
            totals,
            total =>
            {
                Assert.Equal(DecimalField, total.Definition);
                Assert.Equal(3.50m, total.Total);
            },
            total =>
            {
                Assert.Equal(CountField, total.Definition);
                Assert.Equal(2m, total.Total);
            });
    }

    [Fact]
    public void FormattedTotal_MatchesTheWorkbookCustomColumnNumberFormats()
    {
        Assert.Equal("3.5", new MobileCustomFieldTotal(DecimalField, 3.50m).FormattedTotal);
        Assert.Equal("2", new MobileCustomFieldTotal(CountField, 2m).FormattedTotal);
        Assert.Equal("0.0", new MobileCustomFieldTotal(DecimalField, 0m).FormattedTotal);
        Assert.Equal("0", new MobileCustomFieldTotal(CountField, 0m).FormattedTotal);
    }

    [Fact]
    public void FormattedTotal_UsesExplicitNumberFormatInsteadOfTheLegacyPositionDefault()
    {
        var wholeNumberFirstField = DecimalField with
        {
            NumberFormat = CustomFieldNumberFormat.WholeNumbers
        };
        var decimalFourthField = CountField with
        {
            NumberFormat = CustomFieldNumberFormat.Decimals
        };

        Assert.Equal("4", new MobileCustomFieldTotal(wholeNumberFirstField, 3.5m).FormattedTotal);
        Assert.Equal("2.0", new MobileCustomFieldTotal(decimalFourthField, 2m).FormattedTotal);
        Assert.Equal("4", MobileCustomFieldTotals.FormatValue(wholeNumberFirstField, "3.5"));
        Assert.Equal("raw note", MobileCustomFieldTotals.FormatValue(wholeNumberFirstField, " raw note "));
    }

    private static PortableLogbookWorkbookEntry Entry(
        params (CustomFieldId Id, string? Value)[] customFields) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            CustomFields = customFields.ToDictionary(field => field.Id, field => field.Value)
        };
}
