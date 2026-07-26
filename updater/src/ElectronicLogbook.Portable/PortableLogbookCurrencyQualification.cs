namespace ElectronicLogbook.Portable;

/// <summary>
/// Classifies workbook-faithful flight entries for the Currency + Recency tables.
/// </summary>
public static class PortableLogbookCurrencyQualification
{
    /// <summary>
    /// Applies the workbook's <c>LogbookSEA</c> and <c>LogbookMEA</c> definitions.
    /// SEA accepts either single- or multi-engine hour buckets; MEA accepts only the
    /// multi-engine buckets. Other logged time does not qualify either classification.
    /// </summary>
    public static PortableLogbookEngineQualification Classify(
        PortableLogbookWorkbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var singleEngineHours =
            Hours(entry.SeIcusDay) +
            Hours(entry.SeIcusNight) +
            Hours(entry.SeDualDay) +
            Hours(entry.SeDualNight) +
            Hours(entry.SeCommandDay) +
            Hours(entry.SeCommandNight);
        var multiEngineHours =
            Hours(entry.MeIcusDay) +
            Hours(entry.MeIcusNight) +
            Hours(entry.MeDualDay) +
            Hours(entry.MeDualNight) +
            Hours(entry.MeCommandDay) +
            Hours(entry.MeCommandNight);

        return new PortableLogbookEngineQualification(
            IsSingleEngineQualified: singleEngineHours + multiEngineHours > 0,
            IsMultiEngineQualified: multiEngineHours > 0);
    }

    private static decimal Hours(decimal? value) => value.GetValueOrDefault();
}

public sealed record PortableLogbookEngineQualification(
    bool IsSingleEngineQualified,
    bool IsMultiEngineQualified);
