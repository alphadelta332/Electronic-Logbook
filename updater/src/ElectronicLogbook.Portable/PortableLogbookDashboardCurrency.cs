namespace ElectronicLogbook.Portable;

/// <summary>
/// Evaluates the workbook-faithful dashboard currency thresholds from canonical rows.
/// </summary>
public static class PortableLogbookDashboardCurrency
{
    /// <summary>
    /// VFR is green only when the Single Engine Flight Review has more than 90 days
    /// remaining and day passenger carrying has more than 30 days remaining.
    /// </summary>
    public static bool IsVfrGreen(
        PortableLogbookCurrencyRow singleEngineFlightReview,
        PortableLogbookCurrencyRow dayPassengerCarrying)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);
        ArgumentNullException.ThrowIfNull(dayPassengerCarrying);

        return singleEngineFlightReview.DaysRemaining > 90 &&
               dayPassengerCarrying.DaysRemaining > 30;
    }

    /// <summary>
    /// VFR is red when the Single Engine Flight Review is expired or was never current.
    /// </summary>
    public static bool IsVfrRed(PortableLogbookCurrencyRow singleEngineFlightReview)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);

        return string.Equals(
            singleEngineFlightReview.Status,
            "Not Current",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// VFR is orange for every combination that is neither VFR green nor VFR red.
    /// </summary>
    public static bool IsVfrOrange(
        PortableLogbookCurrencyRow singleEngineFlightReview,
        PortableLogbookCurrencyRow dayPassengerCarrying)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);
        ArgumentNullException.ThrowIfNull(dayPassengerCarrying);

        return !IsVfrGreen(singleEngineFlightReview, dayPassengerCarrying) &&
               !IsVfrRed(singleEngineFlightReview);
    }

    /// <summary>
    /// IFR is green only when every required Single Engine currency has more than its
    /// workbook dashboard threshold remaining.
    /// </summary>
    public static bool IsIfrGreen(
        PortableLogbookCurrencyRow singleEngineFlightReview,
        PortableLogbookCurrencyRow singleEngineInstrumentProficiencyCheck,
        PortableLogbookCurrencyRow dayPassengerCarrying,
        PortableLogbookCurrencyRow ifrApps,
        PortableLogbookCurrencyRow singlePilotIfr)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);
        ArgumentNullException.ThrowIfNull(singleEngineInstrumentProficiencyCheck);
        ArgumentNullException.ThrowIfNull(dayPassengerCarrying);
        ArgumentNullException.ThrowIfNull(ifrApps);
        ArgumentNullException.ThrowIfNull(singlePilotIfr);

        return singleEngineFlightReview.DaysRemaining > 90 &&
               singleEngineInstrumentProficiencyCheck.DaysRemaining > 90 &&
               dayPassengerCarrying.DaysRemaining > 30 &&
               ifrApps.DaysRemaining > 30 &&
               singlePilotIfr.DaysRemaining > 90;
    }

    /// <summary>
    /// IFR is red when any required Single Engine currency is expired or was never
    /// current.
    /// </summary>
    public static bool IsIfrRed(
        PortableLogbookCurrencyRow singleEngineFlightReview,
        PortableLogbookCurrencyRow singleEngineInstrumentProficiencyCheck,
        PortableLogbookCurrencyRow ifrApps,
        PortableLogbookCurrencyRow singlePilotIfr)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);
        ArgumentNullException.ThrowIfNull(singleEngineInstrumentProficiencyCheck);
        ArgumentNullException.ThrowIfNull(ifrApps);
        ArgumentNullException.ThrowIfNull(singlePilotIfr);

        return IsVfrRed(singleEngineFlightReview) ||
               string.Equals(singleEngineInstrumentProficiencyCheck.Status, "Not Current", StringComparison.Ordinal) ||
               string.Equals(ifrApps.Status, "Not Current", StringComparison.Ordinal) ||
               string.Equals(singlePilotIfr.Status, "Not Current", StringComparison.Ordinal);
    }

    /// <summary>
    /// IFR is orange for every combination that is neither IFR green nor IFR red.
    /// </summary>
    public static bool IsIfrOrange(
        PortableLogbookCurrencyRow singleEngineFlightReview,
        PortableLogbookCurrencyRow singleEngineInstrumentProficiencyCheck,
        PortableLogbookCurrencyRow dayPassengerCarrying,
        PortableLogbookCurrencyRow ifrApps,
        PortableLogbookCurrencyRow singlePilotIfr)
    {
        ArgumentNullException.ThrowIfNull(singleEngineFlightReview);
        ArgumentNullException.ThrowIfNull(singleEngineInstrumentProficiencyCheck);
        ArgumentNullException.ThrowIfNull(dayPassengerCarrying);
        ArgumentNullException.ThrowIfNull(ifrApps);
        ArgumentNullException.ThrowIfNull(singlePilotIfr);

        return !IsIfrGreen(
                   singleEngineFlightReview,
                   singleEngineInstrumentProficiencyCheck,
                   dayPassengerCarrying,
                   ifrApps,
                   singlePilotIfr) &&
               !IsIfrRed(
                   singleEngineFlightReview,
                   singleEngineInstrumentProficiencyCheck,
                   ifrApps,
                   singlePilotIfr);
    }
}
