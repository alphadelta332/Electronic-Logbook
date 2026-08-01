namespace ElectronicLogbook.Mobile;

using ElectronicLogbook.Portable;

public static class MobileCurrencyPeriodDetail
{
    public static IReadOnlyList<string> Items(
        PortableLogbookCurrencyRow row,
        MobileCurrencyApproachPeriodTotals approachTotals)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(approachTotals);

        if (!string.Equals(row.Category, "Approaches", StringComparison.Ordinal))
        {
            return [$"{row.RelevantPeriodTotal} in period"];
        }

        var items = row.Requirement switch
        {
            "ILS" => PositiveItems(
                (approachTotals.ThreeDimensional, "3D"),
                (approachTotals.Cdi, "CDI")),
            "VOR" or "RNP" or "DGA (CDI)" => PositiveItems(
                (approachTotals.TwoDimensional, "2D"),
                (approachTotals.Cdi, "CDI")),
            "NDB" or "DGA (Azi)" => PositiveItems(
                (approachTotals.TwoDimensional, "2D"),
                (approachTotals.Azimuth, "azimuth")),
            "Circling" when row.RelevantPeriodTotal > 0 => [$"{row.RelevantPeriodTotal} × circling"],
            _ => []
        };

        return items.Count > 0 ? items : ["No qualifying approaches in period"];
    }

    private static IReadOnlyList<string> PositiveItems(params (int Count, string Label)[] values) =>
        values
            .Where(value => value.Count > 0)
            .Select(value => $"{value.Count} × {value.Label}")
            .ToArray();
}
