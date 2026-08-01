namespace ElectronicLogbook.Mobile;

using ElectronicLogbook.Portable;

public sealed record MobileCurrencyApproachPeriodTotals(
    int ThreeDimensional,
    int TwoDimensional,
    int Cdi,
    int Azimuth)
{
    public static MobileCurrencyApproachPeriodTotals Empty { get; } = new(0, 0, 0, 0);

    public static MobileCurrencyApproachPeriodTotals Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var relevantEntries = entries.Where(entry =>
            entry.Date is { } date &&
            date >= today.AddDays(-90) &&
            PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified);

        var threeDimensional = 0;
        var twoDimensional = 0;
        var cdi = 0;
        var azimuth = 0;

        foreach (var entry in relevantEntries)
        {
            threeDimensional += entry.Ils.GetValueOrDefault();
            twoDimensional +=
                entry.Vor.GetValueOrDefault() +
                entry.Rnp.GetValueOrDefault() +
                entry.Ndb.GetValueOrDefault() +
                entry.DgaCdi.GetValueOrDefault() +
                entry.DgaAzi.GetValueOrDefault();
            cdi +=
                entry.Ils.GetValueOrDefault() +
                entry.Vor.GetValueOrDefault() +
                entry.Rnp.GetValueOrDefault() +
                entry.DgaCdi.GetValueOrDefault();
            azimuth += entry.Ndb.GetValueOrDefault() + entry.DgaAzi.GetValueOrDefault();
        }

        return new MobileCurrencyApproachPeriodTotals(
            threeDimensional,
            twoDimensional,
            cdi,
            azimuth);
    }
}
