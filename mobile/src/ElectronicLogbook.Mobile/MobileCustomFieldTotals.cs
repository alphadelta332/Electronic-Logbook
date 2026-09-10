using System.Globalization;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileCustomFieldTotal(
    CustomFieldDefinition Definition,
    decimal Total)
{
    public string FormattedTotal => Definition.Order == PortableLogbookCustomFieldSet.WorkbookCustomFieldCount
        ? Total.ToString("0", CultureInfo.InvariantCulture)
        : Total.ToString("0.0", CultureInfo.InvariantCulture);
}

public static class MobileCustomFieldTotals
{
    public static IReadOnlyList<MobileCustomFieldTotal> Calculate(
        IEnumerable<CustomFieldDefinition> definitions,
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(entries);

        var entryArray = entries.ToArray();
        return definitions
            .OrderBy(definition => definition.Order)
            .Select(definition => new MobileCustomFieldTotal(
                definition,
                entryArray.Sum(entry => Parse(entry.CustomFields.GetValueOrDefault(definition.Id)))))
            .ToArray();
    }

    private static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
}
