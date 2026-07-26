namespace ElectronicLogbook.Portable;

/// <summary>
/// Reproduces workbook Currency + Recency calculations from canonical portable entries.
/// </summary>
public static class PortableLogbookCurrencyCalculator
{
    /// <summary>
    /// Applies the workbook's <c>FRExpirySEA</c> and <c>FRExpiryMEA</c> formulas.
    /// </summary>
    public static DateOnly? CalculateFlightReviewExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate,
        PortableLogbookEngineCategory engineCategory) =>
        CalculateRenewalExpiry(
            entries,
            manualOverrideDate,
            engineCategory,
            entry => entry.FlightReview is true,
            renewalMonths: 24);

    /// <summary>
    /// Applies the IPC portion of the workbook's <c>IPCOPCExpirySEA</c> and
    /// <c>IPCOPCExpiryMEA</c> formulas. Operator proficiency effects are calculated
    /// separately.
    /// </summary>
    public static DateOnly? CalculateInstrumentProficiencyCheckExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate,
        PortableLogbookEngineCategory engineCategory) =>
        CalculateRenewalExpiry(
            entries,
            manualOverrideDate,
            engineCategory,
            entry => entry.InstrumentProficiencyCheck is true,
            renewalMonths: 12);

    /// <summary>
    /// Applies <c>OPCRecencyExpirySEA</c> and <c>OPCRecencyExpiryMEA</c>.
    /// </summary>
    public static DateOnly? CalculateOperatorProficiencyCheckRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate,
        PortableLogbookEngineCategory engineCategory)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(engineCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(engineCategory), engineCategory, null);
        }

        var latest = CalculateLatestDate(
            entries,
            manualOverrideDate,
            entry => entry.OperatorProficiencyCheck is true && QualifiesFor(entry, engineCategory));

        return latest?.AddMonths(3);
    }

    /// <summary>
    /// Applies the workbook's <c>OPCLast</c> and <c>OPCProficiencyExpiry</c> formulas.
    /// Unlike approach recency, proficiency is not limited to an engine category.
    /// </summary>
    public static DateOnly? CalculateOperatorProficiencyCheckProficiencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var latest = CalculateLatestDate(
            entries,
            manualOverrideDate,
            entry => entry.OperatorProficiencyCheck is true);

        return latest is { } date ? EndOfMonth(date.AddMonths(12)) : null;
    }

    /// <summary>
    /// Applies the workbook's overall IPC/OPC validity maximum.
    /// </summary>
    public static DateOnly? CalculateOverallInstrumentProficiencyExpiry(
        DateOnly? instrumentProficiencyCheckExpiry,
        DateOnly? operatorProficiencyCheckExpiry) =>
        LaterOf(instrumentProficiencyCheckExpiry, operatorProficiencyCheckExpiry);

    /// <summary>
    /// Applies the workbook's <c>DayLandingExpirySEA</c> formula: three or more
    /// SEA-qualified day landings in an inclusive 90-day window.
    /// </summary>
    public static DateOnly? CalculateDayPassengerCarryingRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries) =>
        CalculateLandingRecencyExpiry(entries, entry => entry.LandingsDay);

    /// <summary>
    /// Applies the workbook's <c>NightLandingExpirySEA</c> formula: three or more
    /// SEA-qualified night landings in an inclusive 90-day window.
    /// </summary>
    public static DateOnly? CalculateNightPassengerCarryingRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries) =>
        CalculateLandingRecencyExpiry(entries, entry => entry.LandingsNight);

    /// <summary>
    /// Applies <c>IFRAppsExpirySEA</c>: three or more workbook <c>TotalApps</c> in an
    /// inclusive 90-day SEA-qualified window, extended by OPC recency when later.
    /// </summary>
    public static DateOnly? CalculateIfrAppsRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? operatorProficiencyCheckRecencyExpiry)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var datedEntries = entries
            .Where(entry => entry.Date is not null)
            .Select(entry => (Entry: entry, Date: entry.Date!.Value))
            .ToArray();
        DateOnly? activityExpiry = null;

        foreach (var candidate in datedEntries)
        {
            var windowEnd = candidate.Date.AddDays(90);
            var approaches = datedEntries
                .Where(item =>
                    item.Date >= candidate.Date &&
                    item.Date <= windowEnd &&
                    QualifiesFor(item.Entry, PortableLogbookEngineCategory.SingleEngine))
                .Sum(item => TotalApps(item.Entry));
            if (approaches >= 3)
            {
                activityExpiry = LaterOf(activityExpiry, windowEnd);
            }
        }

        return LaterOf(activityExpiry, operatorProficiencyCheckRecencyExpiry);
    }

    /// <summary>
    /// Applies <c>NVFRExpirySEA</c>: the latest SEA-qualified night landing plus six
    /// months, or the later IPC expiry.
    /// </summary>
    public static DateOnly? CalculateNvfrRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? instrumentProficiencyCheckExpiry)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var latestNightLanding = CalculateLatestDate(
            entries,
            null,
            entry =>
                entry.LandingsNight.GetValueOrDefault() >= 1 &&
                QualifiesFor(entry, PortableLogbookEngineCategory.SingleEngine));

        return LaterOf(latestNightLanding?.AddMonths(6), instrumentProficiencyCheckExpiry);
    }

    /// <summary>
    /// Applies <c>SPIFRExpirySEA</c>: latest SEA-qualified entry with at least one
    /// workbook TotalApps and at least one in-flight or simulated instrument hour.
    /// </summary>
    public static DateOnly? CalculateSinglePilotIfrRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var latestQualifyingEntry = CalculateLatestDate(
            entries,
            null,
            entry =>
                QualifiesFor(entry, PortableLogbookEngineCategory.SingleEngine) &&
                TotalApps(entry) >= 1 &&
                (entry.IfrIf.GetValueOrDefault() >= 1 || entry.IfrSim.GetValueOrDefault() >= 1));

        return latestQualifyingEntry?.AddMonths(6);
    }

    /// <summary>
    /// Applies <c>App3DCDIExpirySEA</c>: within an inclusive 90-day window, a
    /// SEA-qualified ILS and a CDI approach (ILS, VOR, RNP, or DGA CDI) are
    /// required. A current OPC recency may extend the resulting expiry.
    /// </summary>
    public static DateOnly? CalculateIlsThreeDimensionalCdiApproachRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? operatorProficiencyCheckRecencyExpiry)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var datedEntries = entries
            .Where(entry => entry.Date is not null)
            .Select(entry => (Entry: entry, Date: entry.Date!.Value))
            .ToArray();
        DateOnly? activityExpiry = null;

        foreach (var candidate in datedEntries)
        {
            var windowEnd = candidate.Date.AddDays(90);
            var qualifyingEntries = datedEntries.Where(item =>
                item.Date >= candidate.Date &&
                item.Date <= windowEnd &&
                QualifiesFor(item.Entry, PortableLogbookEngineCategory.SingleEngine));
            var hasIls = qualifyingEntries.Sum(item => item.Entry.Ils.GetValueOrDefault()) >= 1;
            var hasCdiApproach = qualifyingEntries.Sum(item =>
                item.Entry.Ils.GetValueOrDefault() +
                item.Entry.Vor.GetValueOrDefault() +
                item.Entry.Rnp.GetValueOrDefault() +
                item.Entry.DgaCdi.GetValueOrDefault()) >= 1;

            if (hasIls && hasCdiApproach)
            {
                activityExpiry = LaterOf(activityExpiry, windowEnd);
            }
        }

        return LaterOf(activityExpiry, operatorProficiencyCheckRecencyExpiry);
    }

    /// <summary>
    /// Applies <c>App2DCDIExpirySEA</c>: within an inclusive 90-day window, a
    /// SEA-qualified 2D approach (VOR, RNP, NDB, DGA CDI, or DGA Azi) and a CDI
    /// approach (ILS, VOR, RNP, or DGA CDI) are required. A current OPC recency
    /// may extend the resulting expiry.
    /// </summary>
    public static DateOnly? CalculateTwoDimensionalCdiApproachRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? operatorProficiencyCheckRecencyExpiry)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var datedEntries = entries
            .Where(entry => entry.Date is not null)
            .Select(entry => (Entry: entry, Date: entry.Date!.Value))
            .ToArray();
        DateOnly? activityExpiry = null;

        foreach (var candidate in datedEntries)
        {
            var windowEnd = candidate.Date.AddDays(90);
            var qualifyingEntries = datedEntries.Where(item =>
                item.Date >= candidate.Date &&
                item.Date <= windowEnd &&
                QualifiesFor(item.Entry, PortableLogbookEngineCategory.SingleEngine));
            var hasTwoDimensionalApproach = qualifyingEntries.Sum(item =>
                item.Entry.Vor.GetValueOrDefault() +
                item.Entry.Rnp.GetValueOrDefault() +
                item.Entry.Ndb.GetValueOrDefault() +
                item.Entry.DgaCdi.GetValueOrDefault() +
                item.Entry.DgaAzi.GetValueOrDefault()) >= 1;
            var hasCdiApproach = qualifyingEntries.Sum(item =>
                item.Entry.Ils.GetValueOrDefault() +
                item.Entry.Vor.GetValueOrDefault() +
                item.Entry.Rnp.GetValueOrDefault() +
                item.Entry.DgaCdi.GetValueOrDefault()) >= 1;

            if (hasTwoDimensionalApproach && hasCdiApproach)
            {
                activityExpiry = LaterOf(activityExpiry, windowEnd);
            }
        }

        return LaterOf(activityExpiry, operatorProficiencyCheckRecencyExpiry);
    }

    /// <summary>
    /// Applies <c>App2DAziExpirySEA</c>: within an inclusive 90-day window, a
    /// SEA-qualified 2D approach (VOR, RNP, NDB, DGA CDI, or DGA Azi) and an
    /// azimuth approach (NDB or DGA Azi) are required. A current OPC recency may
    /// extend the resulting expiry.
    /// </summary>
    public static DateOnly? CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? operatorProficiencyCheckRecencyExpiry)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var datedEntries = entries
            .Where(entry => entry.Date is not null)
            .Select(entry => (Entry: entry, Date: entry.Date!.Value))
            .ToArray();
        DateOnly? activityExpiry = null;

        foreach (var candidate in datedEntries)
        {
            var windowEnd = candidate.Date.AddDays(90);
            var qualifyingEntries = datedEntries.Where(item =>
                item.Date >= candidate.Date &&
                item.Date <= windowEnd &&
                QualifiesFor(item.Entry, PortableLogbookEngineCategory.SingleEngine));
            var hasTwoDimensionalApproach = qualifyingEntries.Sum(item =>
                item.Entry.Vor.GetValueOrDefault() +
                item.Entry.Rnp.GetValueOrDefault() +
                item.Entry.Ndb.GetValueOrDefault() +
                item.Entry.DgaCdi.GetValueOrDefault() +
                item.Entry.DgaAzi.GetValueOrDefault()) >= 1;
            var hasAzimuthApproach = qualifyingEntries.Sum(item =>
                item.Entry.Ndb.GetValueOrDefault() +
                item.Entry.DgaAzi.GetValueOrDefault()) >= 1;

            if (hasTwoDimensionalApproach && hasAzimuthApproach)
            {
                activityExpiry = LaterOf(activityExpiry, windowEnd);
            }
        }

        return LaterOf(activityExpiry, operatorProficiencyCheckRecencyExpiry);
    }

    /// <summary>
    /// Applies <c>CirclingExpirySEA</c>: an IPC with at least one circling approach
    /// on a SEA-qualified entry renews for one year to month-end. A renewal during
    /// the preceding three months preserves the previous expiry anniversary.
    /// </summary>
    public static DateOnly? CalculateCirclingApproachRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        return CalculateRenewalExpiry(
            entries,
            null,
            PortableLogbookEngineCategory.SingleEngine,
            entry =>
                entry.InstrumentProficiencyCheck.GetValueOrDefault() &&
                entry.Circling.GetValueOrDefault() > 0,
            renewalMonths: 12);
    }

    private static DateOnly? CalculateRenewalExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate,
        PortableLogbookEngineCategory engineCategory,
        Func<PortableLogbookWorkbookEntry, bool> isQualifyingEvent,
        int renewalMonths)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(isQualifyingEvent);
        if (!Enum.IsDefined(engineCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(engineCategory), engineCategory, null);
        }

        var qualifyingDates = entries
            .Where(entry => isQualifyingEvent(entry) && QualifiesFor(entry, engineCategory))
            .Select(entry => entry.Date)
            .Where(date => date is not null)
            .Select(date => date!.Value)
            .OrderByDescending(date => date)
            .ToArray();
        DateOnly? loggedLatest = qualifyingDates.Length > 0 ? qualifyingDates[0] : null;
        DateOnly? loggedPrevious = qualifyingDates.Length > 1 ? qualifyingDates[1] : null;
        var latest = LaterOf(loggedLatest, manualOverrideDate);
        if (latest is null)
        {
            return null;
        }

        DateOnly? previous = manualOverrideDate switch
        {
            null => loggedPrevious,
            _ when loggedLatest is null || manualOverrideDate >= loggedLatest => loggedLatest,
            _ => LaterOf(manualOverrideDate, loggedPrevious)
        };
        var normalExpiry = EndOfMonth(latest.Value.AddMonths(renewalMonths));
        DateOnly? previousExpiry = previous is { } previousDate
            ? EndOfMonth(previousDate.AddMonths(renewalMonths))
            : null;

        return previousExpiry is { } expiry &&
            latest <= expiry &&
            expiry <= latest.Value.AddMonths(3)
                ? EndOfMonth(expiry.AddMonths(renewalMonths))
                : normalExpiry;
    }

    private static bool QualifiesFor(
        PortableLogbookWorkbookEntry entry,
        PortableLogbookEngineCategory engineCategory)
    {
        var qualification = PortableLogbookCurrencyQualification.Classify(entry);
        return engineCategory switch
        {
            PortableLogbookEngineCategory.SingleEngine => qualification.IsSingleEngineQualified,
            PortableLogbookEngineCategory.MultiEngine => qualification.IsMultiEngineQualified,
            _ => throw new ArgumentOutOfRangeException(nameof(engineCategory), engineCategory, null)
        };
    }

    private static DateOnly? CalculateLatestDate(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        DateOnly? manualOverrideDate,
        Func<PortableLogbookWorkbookEntry, bool> isQualifyingEvent)
    {
        ArgumentNullException.ThrowIfNull(isQualifyingEvent);

        var qualifyingDates = entries
            .Where(isQualifyingEvent)
            .Select(entry => entry.Date)
            .Where(date => date is not null)
            .Select(date => date!.Value)
            .ToArray();
        DateOnly? loggedLatest = qualifyingDates.Length > 0 ? qualifyingDates.Max() : null;

        return LaterOf(loggedLatest, manualOverrideDate);
    }

    private static DateOnly? CalculateLandingRecencyExpiry(
        IEnumerable<PortableLogbookWorkbookEntry> entries,
        Func<PortableLogbookWorkbookEntry, int?> landingCount)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(landingCount);

        var datedEntries = entries
            .Where(entry => entry.Date is not null)
            .Select(entry => (Entry: entry, Date: entry.Date!.Value))
            .ToArray();
        DateOnly? latestExpiry = null;

        foreach (var candidate in datedEntries)
        {
            var windowEnd = candidate.Date.AddDays(90);
            var landings = datedEntries
                .Where(item =>
                    item.Date >= candidate.Date &&
                    item.Date <= windowEnd &&
                    QualifiesFor(item.Entry, PortableLogbookEngineCategory.SingleEngine))
                .Sum(item => landingCount(item.Entry).GetValueOrDefault());
            if (landings >= 3)
            {
                latestExpiry = LaterOf(latestExpiry, windowEnd);
            }
        }

        return latestExpiry;
    }

    private static int TotalApps(PortableLogbookWorkbookEntry entry) =>
        entry.Ils.GetValueOrDefault() +
        entry.Vor.GetValueOrDefault() +
        entry.Rnp.GetValueOrDefault() +
        entry.Ndb.GetValueOrDefault() +
        entry.DgaCdi.GetValueOrDefault() +
        entry.DgaAzi.GetValueOrDefault();

    private static DateOnly? LaterOf(DateOnly? left, DateOnly? right) =>
        left is null ? right :
        right is null ? left :
        left >= right ? left : right;

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}

public enum PortableLogbookEngineCategory
{
    SingleEngine,
    MultiEngine
}
