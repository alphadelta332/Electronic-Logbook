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
        IReadOnlyList<PortableLogbookCurrencyRow> multiEngineRows,
        MobileCurrencyApproachPeriodTotals approachPeriodTotals)
    {
        Today = today;
        SingleEngineRows = singleEngineRows;
        MultiEngineRows = multiEngineRows;
        ApproachPeriodTotals = approachPeriodTotals;
    }

    public DateOnly Today { get; }

    public IReadOnlyList<PortableLogbookCurrencyRow> SingleEngineRows { get; }

    public IReadOnlyList<PortableLogbookCurrencyRow> MultiEngineRows { get; }

    public MobileCurrencyApproachPeriodTotals ApproachPeriodTotals { get; }

    public int CurrentCount => SingleEngineRows.Count(IsCurrent);

    public int DueSoonCount => SingleEngineRows.Count(IsDueSoon);

    public int ExpiredCount => SingleEngineRows.Count(IsExpired);

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

    public MobileDashboardCurrencyPanel VfrDashboardPanel =>
        MobileDashboardCurrencyPanel.CreateVfr(SingleEngineRows);

    public MobileDashboardCurrencyPanel IfrDashboardPanel =>
        MobileDashboardCurrencyPanel.CreateIfr(SingleEngineRows);

    private static bool IsExpired(PortableLogbookCurrencyRow row) =>
        string.Equals(row.Status, "Not Current", StringComparison.Ordinal);

    private static bool IsDueSoon(PortableLogbookCurrencyRow row) =>
        !IsExpired(row) && row.DaysRemaining <= 30;

    private static bool IsCurrent(PortableLogbookCurrencyRow row) =>
        !IsExpired(row) && !IsDueSoon(row);

    public static MobileCurrencyRecencySummary Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var entryArray = entries.ToArray();
        var entryOnlyDates = PortableLogbookCurrencyOverrideDates.Empty;
        var singleEngineRows = new PortableLogbookCurrencyRow[]
        {
            PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateSingleEngineInstrumentProficiencyCheck(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateDayPassengerCarrying(entryArray, today),
            PortableLogbookCurrencyRows.CreateNightPassengerCarrying(entryArray, today),
            PortableLogbookCurrencyRows.CreateIfrApps(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateNvfr(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateSinglePilotIfr(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateIlsApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateVorApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateRnpApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateNdbApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateDgaCdiApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateDgaAziApproach(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateCirclingApproach(entryArray, today)
        };
        var multiEngineRows = new PortableLogbookCurrencyRow[]
        {
            PortableLogbookCurrencyRows.CreateMultiEngineFlightReview(entryArray, entryOnlyDates, today),
            PortableLogbookCurrencyRows.CreateMultiEngineInstrumentProficiencyCheck(entryArray, entryOnlyDates, today)
        };

        return new MobileCurrencyRecencySummary(
            today,
            singleEngineRows,
            multiEngineRows,
            MobileCurrencyApproachPeriodTotals.Create(entryArray, today));
    }
}
