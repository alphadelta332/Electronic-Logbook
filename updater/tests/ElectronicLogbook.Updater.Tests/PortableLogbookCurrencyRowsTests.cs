namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookCurrencyRowsTests
{
    [Fact]
    public void CreateSingleEngineFlightReview_ReturnsTheCanonicalCurrentRow()
    {
        var row = PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(
            [Entry(new DateOnly(2025, 5, 12))],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2026, 5, 20));

        Assert.Equal("License", row.Category);
        Assert.Equal("61.800 (2)", row.CasrReference);
        Assert.Equal("Flight Review", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(376, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CalculatedExpiry);
        Assert.Equal("31 May 2027", row.CurrentOrRecentUntilDisplay);
        Assert.Equal("31 May 2027", row.CalculatedExpiryDisplay);
    }

    [Fact]
    public void CreateSingleEngineFlightReview_WithNoQualifyingReview_ReturnsTheCanonicalNeverCurrentDisplay()
    {
        var row = PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2026, 5, 20));

        Assert.Equal(0, row.RelevantPeriodTotal);
        Assert.Equal("Not Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Null(row.CurrentOrRecentUntil);
        Assert.Null(row.CalculatedExpiry);
        Assert.Equal("Never Current", row.CurrentOrRecentUntilDisplay);
        Assert.Equal("-", row.CalculatedExpiryDisplay);
    }

    [Fact]
    public void CreateSingleEngineFlightReview_WithManualOverride_ReturnsTheCanonicalCurrentDisplay()
    {
        var row = PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2025, 4, 1), null, null),
            new DateOnly(2026, 4, 20));

        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(375, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 4, 30), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 4, 30), row.CalculatedExpiry);
        Assert.Equal("30 Apr 2027", row.CurrentOrRecentUntilDisplay);
        Assert.Equal("30 Apr 2027", row.CalculatedExpiryDisplay);
    }

    [Fact]
    public void CreateSingleEngineFlightReview_OnItsExpiryDate_RemainsCurrentWithZeroDays()
    {
        var row = PortableLogbookCurrencyRows.CreateSingleEngineFlightReview(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2025, 4, 1), null, null),
            new DateOnly(2027, 4, 30));

        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 4, 30), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 4, 30), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateMultiEngineFlightReview_ReturnsTheCanonicalMeaQualifiedRow()
    {
        var row = PortableLogbookCurrencyRows.CreateMultiEngineFlightReview(
            [Entry(new DateOnly(2025, 5, 12), seCommandDay: null, meCommandDay: 1m)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2026, 5, 20));

        Assert.Equal("License", row.Category);
        Assert.Equal("61.800 (2)", row.CasrReference);
        Assert.Equal("Flight Review", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(376, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateSingleEngineInstrumentProficiencyCheck_UsesTheLaterOpcProficiencyExpiry()
    {
        var row = PortableLogbookCurrencyRows.CreateSingleEngineInstrumentProficiencyCheck(
            [
                Entry(new DateOnly(2024, 5, 12), instrumentProficiencyCheck: true),
                Entry(new DateOnly(2026, 5, 12), operatorProficiencyCheck: true)
            ],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2026, 5, 20));

        Assert.Equal("License", row.Category);
        Assert.Equal("61.880 (3)", row.CasrReference);
        Assert.Equal("IPC", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(376, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateMultiEngineInstrumentProficiencyCheck_UsesTheLaterOpcProficiencyExpiry()
    {
        var row = PortableLogbookCurrencyRows.CreateMultiEngineInstrumentProficiencyCheck(
            [
                Entry(
                    new DateOnly(2024, 5, 12),
                    instrumentProficiencyCheck: true,
                    seCommandDay: null,
                    meCommandDay: 1m),
                Entry(new DateOnly(2026, 5, 12), operatorProficiencyCheck: true)
            ],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2026, 5, 20));

        Assert.Equal("License", row.Category);
        Assert.Equal("61.880 (3)", row.CasrReference);
        Assert.Equal("IPC", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(376, row.DaysRemaining);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2027, 5, 31), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateDayPassengerCarrying_ReturnsCanonicalInclusiveNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateDayPassengerCarrying(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 2, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 4, 1), landingsDay: 1)
            ],
            new DateOnly(2025, 4, 1));

        Assert.Equal("Passenger Carrying", row.Category);
        Assert.Equal("61.395 (1)", row.CasrReference);
        Assert.Equal("Day", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateNightPassengerCarrying_ReturnsCanonicalInclusiveNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateNightPassengerCarrying(
            [
                Entry(new DateOnly(2025, 1, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 2, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 4, 1), landingsNight: 1)
            ],
            new DateOnly(2025, 4, 1));

        Assert.Equal("Passenger Carrying", row.Category);
        Assert.Equal("61.395 (2)", row.CasrReference);
        Assert.Equal("Night", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateIfrApps_ReturnsCanonicalTotalAppsRow()
    {
        var row = PortableLogbookCurrencyRows.CreateIfrApps(
            [
                Entry(new DateOnly(2025, 1, 1), ils: 1),
                Entry(new DateOnly(2025, 2, 1), vor: 1),
                Entry(new DateOnly(2025, 4, 1), rnp: 1)
            ],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 4, 1));

        Assert.Equal("Operation", row.Category);
        Assert.Equal("61.870 (2)", row.CasrReference);
        Assert.Equal("IFR (Apps)", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateIfrApps_WithCurrentOpcRecencyReportsTheWorkbookMinimumOfThree()
    {
        var row = PortableLogbookCurrencyRows.CreateIfrApps(
            [Entry(new DateOnly(2025, 1, 1), operatorProficiencyCheck: true)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateNvfr_ReturnsCanonicalSixMonthNightLandingRow()
    {
        var row = PortableLogbookCurrencyRows.CreateNvfr(
            [Entry(new DateOnly(2025, 1, 31), landingsNight: 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 7, 31));

        Assert.Equal("Operation", row.Category);
        Assert.Equal("61.965 (a) & 61.855 (b)", row.CasrReference);
        Assert.Equal("NVFR", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(0, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 7, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 7, 31), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateNvfr_WithCurrentIpcReportsTheWorkbookValidityMinimumOfOne()
    {
        var row = PortableLogbookCurrencyRows.CreateNvfr(
            [Entry(new DateOnly(2025, 1, 1), instrumentProficiencyCheck: true)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(new DateOnly(2026, 1, 31), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateSinglePilotIfr_ReturnsCanonicalIfrIfTotalWhenIpcIsCurrent()
    {
        var row = PortableLogbookCurrencyRows.CreateSinglePilotIfr(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                ils: 1,
                ifrIf: 1m)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Operation", row.Category);
        Assert.Equal("61.875", row.CasrReference);
        Assert.Equal("Single Pilot IFR", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(150, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 7, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 7, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateSinglePilotIfr_UsesSimulatedTimeForExpiryButNotTheIfrIfPeriodTotal()
    {
        var row = PortableLogbookCurrencyRows.CreateSinglePilotIfr(
            [Entry(new DateOnly(2025, 1, 1), ils: 1, ifrSim: 1m)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal(0, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(new DateOnly(2025, 7, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateIlsApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateIlsApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                ils: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (5 & 7)", row.CasrReference);
        Assert.Equal("ILS", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateIlsApproach_LeavesThePeriodTotalAtZeroWhenWorkbookGatesAreNotCurrent()
    {
        var row = PortableLogbookCurrencyRows.CreateIlsApproach(
            [Entry(new DateOnly(2025, 1, 1), ils: 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal(0, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateVorApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateVorApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                vor: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (4 & 7)", row.CasrReference);
        Assert.Equal("VOR", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateRnpApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateRnpApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                rnp: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (4 & 7)", row.CasrReference);
        Assert.Equal("RNP", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateNdbApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateNdbApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                ndb: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (4 & 6)", row.CasrReference);
        Assert.Equal("NDB", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateDgaCdiApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateDgaCdiApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                dgaCdi: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (4 & 7)", row.CasrReference);
        Assert.Equal("DGA (CDI)", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateDgaAziApproach_ReturnsCanonicalGatedNinetyDayRow()
    {
        var row = PortableLogbookCurrencyRows.CreateDgaAziApproach(
            [Entry(
                new DateOnly(2025, 1, 1),
                instrumentProficiencyCheck: true,
                operatorProficiencyCheck: true,
                dgaAzi: 3)],
            PortableLogbookCurrencyOverrideDates.Empty,
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.870 (4 & 6)", row.CasrReference);
        Assert.Equal("DGA (Azi)", row.Requirement);
        Assert.Equal(3, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(59, row.DaysRemaining);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2025, 4, 1), row.CalculatedExpiry);
    }

    [Fact]
    public void CreateCirclingApproach_ReturnsCanonicalQualifyingIpcRow()
    {
        var row = PortableLogbookCurrencyRows.CreateCirclingApproach(
            [Entry(new DateOnly(2025, 1, 1), instrumentProficiencyCheck: true, circling: 1)],
            new DateOnly(2025, 2, 1));

        Assert.Equal("Approaches", row.Category);
        Assert.Equal("61.860 (3)", row.CasrReference);
        Assert.Equal("Circling", row.Requirement);
        Assert.Equal(1, row.RelevantPeriodTotal);
        Assert.Equal("Current", row.Status);
        Assert.Equal(364, row.DaysRemaining);
        Assert.Equal(new DateOnly(2026, 1, 31), row.CurrentOrRecentUntil);
        Assert.Equal(new DateOnly(2026, 1, 31), row.CalculatedExpiry);
    }

    private static PortableLogbookWorkbookEntry Entry(
        DateOnly date,
        bool instrumentProficiencyCheck = false,
        bool operatorProficiencyCheck = false,
        int? landingsDay = null,
        int? landingsNight = null,
        int? ils = null,
        int? vor = null,
        int? rnp = null,
        int? ndb = null,
        int? dgaCdi = null,
        int? dgaAzi = null,
        int? circling = null,
        decimal? ifrIf = null,
        decimal? ifrSim = null,
        decimal? seCommandDay = 1m,
        decimal? meCommandDay = null) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            FlightReview = true,
            InstrumentProficiencyCheck = instrumentProficiencyCheck,
            OperatorProficiencyCheck = operatorProficiencyCheck,
            LandingsDay = landingsDay,
            LandingsNight = landingsNight,
            Ils = ils,
            Vor = vor,
            Rnp = rnp,
            Ndb = ndb,
            DgaCdi = dgaCdi,
            DgaAzi = dgaAzi,
            Circling = circling,
            IfrIf = ifrIf,
            IfrSim = ifrSim,
            SeCommandDay = seCommandDay,
            MeCommandDay = meCommandDay
        };
}
