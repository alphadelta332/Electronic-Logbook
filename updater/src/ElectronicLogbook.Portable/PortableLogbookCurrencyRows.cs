namespace ElectronicLogbook.Portable;

/// <summary>
/// Produces the workbook's user-facing Currency + Recency table rows from portable data.
/// </summary>
public static class PortableLogbookCurrencyRows
{
    /// <summary>
    /// Produces the Single Engine Flight Review row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateSingleEngineFlightReview(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            entries,
            overrideDates.FlightReview,
            PortableLogbookEngineCategory.SingleEngine);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;

        return new PortableLogbookCurrencyRow(
            "License",
            "61.800 (2)",
            "Flight Review",
            isCurrent ? 1 : 0,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Multi Engine Flight Review row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateMultiEngineFlightReview(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            entries,
            overrideDates.FlightReview,
            PortableLogbookEngineCategory.MultiEngine);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;

        return new PortableLogbookCurrencyRow(
            "License",
            "61.800 (2)",
            "Flight Review",
            isCurrent ? 1 : 0,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Single Engine IPC row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateSingleEngineInstrumentProficiencyCheck(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entries,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entries,
                overrideDates.OperatorProficiencyCheck);
        var expiry = PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
            instrumentProficiencyCheckExpiry,
            operatorProficiencyCheckExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;

        return new PortableLogbookCurrencyRow(
            "License",
            "61.880 (3)",
            "IPC",
            isCurrent ? 1 : 0,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Multi Engine IPC row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateMultiEngineInstrumentProficiencyCheck(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entries,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.MultiEngine);
        var operatorProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entries,
                overrideDates.OperatorProficiencyCheck);
        var expiry = PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
            instrumentProficiencyCheckExpiry,
            operatorProficiencyCheckExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;

        return new PortableLogbookCurrencyRow(
            "License",
            "61.880 (3)",
            "IPC",
            isCurrent ? 1 : 0,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Day passenger-carrying row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateDayPassengerCarrying(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var entryArray = entries.ToArray();
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(entryArray);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var relevantPeriodTotal = entryArray
            .Where(entry =>
                entry.Date is { } date &&
                date >= today.AddDays(-90) &&
                PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
            .Sum(entry => entry.LandingsDay.GetValueOrDefault());

        return new PortableLogbookCurrencyRow(
            "Passenger Carrying",
            "61.395 (1)",
            "Day",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Night passenger-carrying row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateNightPassengerCarrying(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var entryArray = entries.ToArray();
        var expiry = PortableLogbookCurrencyCalculator.CalculateNightPassengerCarryingRecencyExpiry(entryArray);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var relevantPeriodTotal = entryArray
            .Where(entry =>
                entry.Date is { } date &&
                date >= today.AddDays(-90) &&
                PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
            .Sum(entry => entry.LandingsNight.GetValueOrDefault());

        return new PortableLogbookCurrencyRow(
            "Passenger Carrying",
            "61.395 (2)",
            "Night",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the IFR Apps row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateIfrApps(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var expiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var recentApproaches = entryArray
            .Where(entry =>
                entry.Date is { } date &&
                date >= today.AddDays(-90) &&
                PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
            .Sum(TotalApps);

        return new PortableLogbookCurrencyRow(
            "Operation",
            "61.870 (2)",
            "IFR (Apps)",
            Math.Max(recentApproaches, isCurrent ? 3 : 0),
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the NVFR row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateNvfr(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckExpiry);
        var expiry = PortableLogbookCurrencyCalculator.CalculateNvfrRecencyExpiry(
            entryArray,
            instrumentOrOperatorProficiencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var recentNightLandings = entryArray
            .Where(entry =>
                entry.Date is { } date &&
                date >= today.AddMonths(-6) &&
                PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
            .Sum(entry => entry.LandingsNight.GetValueOrDefault());

        return new PortableLogbookCurrencyRow(
            "Operation",
            "61.965 (a) & 61.855 (b)",
            "NVFR",
            Math.Max(recentNightLandings, instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today ? 1 : 0),
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Single Pilot IFR row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateSinglePilotIfr(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckExpiry);
        var expiry = PortableLogbookCurrencyCalculator.CalculateSinglePilotIfrRecencyExpiry(entryArray);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var relevantPeriodTotal = instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddMonths(-6) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified &&
                    entry.IfrIf.GetValueOrDefault() >= 1)
                .Sum(TotalApps)
            : 0;

        return new PortableLogbookCurrencyRow(
            "Operation",
            "61.875",
            "Single Pilot IFR",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the ILS approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateIlsApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateIlsThreeDimensionalCdiApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.Ils.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (5 & 7)",
            "ILS",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the VOR approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateVorApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.Vor.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (4 & 7)",
            "VOR",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the RNP approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateRnpApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.Rnp.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (4 & 7)",
            "RNP",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the NDB approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateNdbApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.Ndb.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (4 & 6)",
            "NDB",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the DGA (CDI) approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateDgaCdiApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.DgaCdi.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (4 & 7)",
            "DGA (CDI)",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the DGA (Azi) approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateDgaAziApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        PortableLogbookCurrencyOverrideDates overrideDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var entryArray = entries.ToArray();
        var instrumentProficiencyCheckExpiry =
            PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
                entryArray,
                overrideDates.InstrumentProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var operatorProficiencyCheckProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck);
        var instrumentOrOperatorProficiencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
                instrumentProficiencyCheckExpiry,
                operatorProficiencyCheckProficiencyExpiry);
        var operatorProficiencyCheckRecencyExpiry =
            PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
                entryArray,
                overrideDates.OperatorProficiencyCheck,
                PortableLogbookEngineCategory.SingleEngine);
        var ifrAppsExpiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            entryArray,
            operatorProficiencyCheckRecencyExpiry);
        var expiry =
            PortableLogbookCurrencyCalculator.CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
                entryArray,
                operatorProficiencyCheckRecencyExpiry);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;
        var requirementsAreCurrent =
            instrumentOrOperatorProficiencyExpiry is { } validity && validity >= today &&
            ifrAppsExpiry is { } ifrApps && ifrApps >= today;
        var relevantPeriodTotal = requirementsAreCurrent
            ? entryArray
                .Where(entry =>
                    entry.Date is { } date &&
                    date >= today.AddDays(-90) &&
                    PortableLogbookCurrencyQualification.Classify(entry).IsSingleEngineQualified)
                .Sum(entry => entry.DgaAzi.GetValueOrDefault())
            : 0;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.870 (4 & 6)",
            "DGA (Azi)",
            relevantPeriodTotal,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    /// <summary>
    /// Produces the Circling approach row from the canonical Currency table.
    /// </summary>
    public static PortableLogbookCurrencyRow CreateCirclingApproach(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var expiry = PortableLogbookCurrencyCalculator.CalculateCirclingApproachRecencyExpiry(entries);
        var isCurrent = expiry is { } expiryDate && expiryDate >= today;

        return new PortableLogbookCurrencyRow(
            "Approaches",
            "61.860 (3)",
            "Circling",
            isCurrent ? 1 : 0,
            isCurrent ? "Current" : "Not Current",
            expiry is { } date ? Math.Max(date.DayNumber - today.DayNumber, 0) : 0,
            expiry,
            expiry);
    }

    private static int TotalApps(PortableLogbookWorkbookEntry entry) =>
        entry.Ils.GetValueOrDefault() +
        entry.Vor.GetValueOrDefault() +
        entry.Rnp.GetValueOrDefault() +
        entry.Ndb.GetValueOrDefault() +
        entry.DgaCdi.GetValueOrDefault() +
        entry.DgaAzi.GetValueOrDefault();
}
