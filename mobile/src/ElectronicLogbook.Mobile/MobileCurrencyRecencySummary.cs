using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

/// <summary>
/// Makes the workbook-faithful Currency + Recency rows available to the mobile UI.
/// </summary>
public sealed class MobileCurrencyRecencySummary
{
    private MobileCurrencyRecencySummary(
        DateOnly today,
        IReadOnlyList<PortableLogbookCurrencyRow> singleEngineRows,
        IReadOnlyList<PortableLogbookCurrencyRow> multiEngineRows)
    {
        Today = today;
        SingleEngineRows = singleEngineRows;
        MultiEngineRows = multiEngineRows;
    }

    public DateOnly Today { get; }

    public IReadOnlyList<PortableLogbookCurrencyRow> SingleEngineRows { get; }

    public IReadOnlyList<PortableLogbookCurrencyRow> MultiEngineRows { get; }

    public IReadOnlyList<PortableLogbookCurrencyRow> CurrentlyExpiredSingleEngineRows =>
        SingleEngineRows.Where(row => string.Equals(row.Status, "Not Current", StringComparison.Ordinal)).ToArray();

    public PortableLogbookCurrencyRow? NextExpiringSingleEngineRow =>
        SingleEngineRows
            .Where(row =>
                string.Equals(row.Status, "Current", StringComparison.Ordinal) &&
                row.CurrentOrRecentUntil is { } expiry &&
                expiry > Today)
            .OrderBy(row => row.CurrentOrRecentUntil)
            .FirstOrDefault();

    public static MobileCurrencyRecencySummary Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var singleEngineRows = new PortableLogbookCurrencyRow[]
        {
            PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateSingleEngineInstrumentProficiencyCheck(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateDayPassengerCarrying(entryArray, today),
            PortableLogbookCurrencyRows.CreateNightPassengerCarrying(entryArray, today),
            PortableLogbookCurrencyRows.CreateIfrApps(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateNvfr(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateSinglePilotIfr(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateIlsApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateVorApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateRnpApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateNdbApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateDgaCdiApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateDgaAziApproach(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateCirclingApproach(entryArray, today)
        };
        var multiEngineRows = new PortableLogbookCurrencyRow[]
        {
            PortableLogbookCurrencyRows.CreateMultiEngineFlightReview(entryArray, overrideDates, today),
            PortableLogbookCurrencyRows.CreateMultiEngineInstrumentProficiencyCheck(entryArray, overrideDates, today)
        };

        return new MobileCurrencyRecencySummary(today, singleEngineRows, multiEngineRows);
    }
}
