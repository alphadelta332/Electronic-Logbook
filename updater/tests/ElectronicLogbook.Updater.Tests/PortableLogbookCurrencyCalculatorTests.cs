namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookCurrencyCalculatorTests
{
    [Fact]
    public void CalculateFlightReviewExpiry_WithNoQualifyingReview_ReturnsNeverCurrent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [Entry(new DateOnly(2025, 6, 10), flightReview: false)],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_WithQualifyingReview_ReturnsTwoYearMonthEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [Entry(new DateOnly(2024, 5, 12))],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2026, 5, 31), expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_WithEarlyRenewal_PreservesPreviousExpiryAnniversary()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [
                Entry(new DateOnly(2024, 6, 10)),
                Entry(new DateOnly(2026, 4, 30))
            ],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2028, 6, 30), expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_WithReviewBeforeEarlyRenewalWindow_UsesNormalExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [
                Entry(new DateOnly(2024, 6, 10)),
                Entry(new DateOnly(2026, 3, 29))
            ],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2028, 3, 31), expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_WithManualOverride_UsesOverrideAsLatestReview()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new DateOnly(2025, 4, 1),
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2027, 4, 30), expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_ForMultiEngine_IgnoresSingleEngineOnlyReview()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [Entry(new DateOnly(2025, 4, 1))],
            null,
            PortableLogbookEngineCategory.MultiEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateFlightReviewExpiry_ForMultiEngine_UsesMultiEngineReview()
    {
        var entry = Entry(new DateOnly(2025, 4, 1)) with
        {
            SeCommandDay = null,
            MeCommandDay = 1m
        };

        var expiry = PortableLogbookCurrencyCalculator.CalculateFlightReviewExpiry(
            [entry],
            null,
            PortableLogbookEngineCategory.MultiEngine);

        Assert.Equal(new DateOnly(2027, 4, 30), expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_WithNoQualifyingCheck_ReturnsNeverCurrent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            [Entry(new DateOnly(2025, 6, 10))],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_WithQualifyingCheck_ReturnsOneYearMonthEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            [Entry(new DateOnly(2025, 5, 12), instrumentProficiencyCheck: true)],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2026, 5, 31), expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_WithRenewalInWindow_PreservesPreviousExpiryAnniversary()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            [
                Entry(new DateOnly(2025, 6, 10), instrumentProficiencyCheck: true),
                Entry(new DateOnly(2026, 4, 30), instrumentProficiencyCheck: true)
            ],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2027, 6, 30), expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_WithCheckBeforeRenewalWindow_UsesNormalExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            [
                Entry(new DateOnly(2025, 6, 10), instrumentProficiencyCheck: true),
                Entry(new DateOnly(2026, 3, 29), instrumentProficiencyCheck: true)
            ],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2027, 3, 31), expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_WithManualOverride_UsesOverrideAsLatestCheck()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new DateOnly(2025, 4, 1),
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2026, 4, 30), expiry);
    }

    [Fact]
    public void CalculateInstrumentProficiencyCheckExpiry_ForMultiEngine_IgnoresSingleEngineOnlyCheck()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateInstrumentProficiencyCheckExpiry(
            [Entry(new DateOnly(2025, 4, 1), instrumentProficiencyCheck: true)],
            null,
            PortableLogbookEngineCategory.MultiEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateOperatorProficiencyCheckRecencyExpiry_WithNoQualifyingCheck_ReturnsNeverRecent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
            [Entry(new DateOnly(2025, 5, 12))],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateOperatorProficiencyCheckRecencyExpiry_WithQualifyingCheck_ReturnsThreeMonthsLater()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
            [Entry(new DateOnly(2025, 5, 31), operatorProficiencyCheck: true)],
            null,
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2025, 8, 31), expiry);
    }

    [Fact]
    public void CalculateOperatorProficiencyCheckRecencyExpiry_WithManualOverride_UsesOverrideDate()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new DateOnly(2025, 1, 15),
            PortableLogbookEngineCategory.SingleEngine);

        Assert.Equal(new DateOnly(2025, 4, 15), expiry);
    }

    [Fact]
    public void CalculateOperatorProficiencyCheckRecencyExpiry_ForMultiEngine_IgnoresSingleEngineOnlyCheck()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckRecencyExpiry(
            [Entry(new DateOnly(2025, 5, 12), operatorProficiencyCheck: true)],
            null,
            PortableLogbookEngineCategory.MultiEngine);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateOperatorProficiencyCheckProficiencyExpiry_UsesLatestOpcRegardlessOfEngineQualification()
    {
        var entry = Entry(new DateOnly(2025, 5, 12), operatorProficiencyCheck: true) with
        {
            SeCommandDay = null,
            CopilotDay = 1m
        };

        var expiry = PortableLogbookCurrencyCalculator.CalculateOperatorProficiencyCheckProficiencyExpiry(
            [entry],
            null);

        Assert.Equal(new DateOnly(2026, 5, 31), expiry);
    }

    [Fact]
    public void CalculateOverallInstrumentProficiencyExpiry_UsesLaterIpcOrOpcExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateOverallInstrumentProficiencyExpiry(
            new DateOnly(2026, 5, 31),
            new DateOnly(2026, 8, 31));

        Assert.Equal(new DateOnly(2026, 8, 31), expiry);
    }

    [Fact]
    public void CalculateDayPassengerCarryingRecencyExpiry_WithFewerThanThreeLandings_ReturnsNeverRecent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 2, 1), landingsDay: 1)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateDayPassengerCarryingRecencyExpiry_WithThreeLandingsInInclusiveWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 2, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 4, 1), landingsDay: 1)
            ]);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateDayPassengerCarryingRecencyExpiry_WithThirdLandingAfterWindow_ReturnsNeverRecent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 2, 1), landingsDay: 1),
                Entry(new DateOnly(2025, 4, 2), landingsDay: 1)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateDayPassengerCarryingRecencyExpiry_UsesLatestQualifyingWindowStart()
    {
        var nonQualifyingStart = Entry(new DateOnly(2025, 1, 1), flightReview: false) with
        {
            SeCommandDay = null,
            CopilotDay = 1m
        };
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(
            [
                nonQualifyingStart,
                Entry(new DateOnly(2025, 1, 2), landingsDay: 1),
                Entry(new DateOnly(2025, 1, 3), landingsDay: 1),
                Entry(new DateOnly(2025, 1, 4), landingsDay: 1)
            ]);

        Assert.Equal(new DateOnly(2025, 4, 2), expiry);
    }

    [Fact]
    public void CalculateDayPassengerCarryingRecencyExpiry_IgnoresNightLandings()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateDayPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 2, landingsNight: 1),
                Entry(new DateOnly(2025, 1, 2), landingsNight: 2)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateNightPassengerCarryingRecencyExpiry_WithThreeLandingsInInclusiveWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNightPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 2, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 4, 1), landingsNight: 1)
            ]);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateNightPassengerCarryingRecencyExpiry_WithThirdLandingAfterWindow_ReturnsNeverRecent()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNightPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 2, 1), landingsNight: 1),
                Entry(new DateOnly(2025, 4, 2), landingsNight: 1)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateNightPassengerCarryingRecencyExpiry_IgnoresDayLandings()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNightPassengerCarryingRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), landingsDay: 3),
                Entry(new DateOnly(2025, 1, 2), landingsDay: 3)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateIfrAppsRecencyExpiry_WithThreeWorkbookTotalAppsInWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ils: 1),
                Entry(new DateOnly(2025, 2, 1), vor: 1),
                Entry(new DateOnly(2025, 4, 1), rnp: 1)
            ],
            null);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateIfrAppsRecencyExpiry_ExcludesCirclingFromWorkbookTotalApps()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ils: 1),
                Entry(new DateOnly(2025, 1, 2), vor: 1, circling: 3)
            ],
            null);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateIfrAppsRecencyExpiry_UsesOpcRecencyWithoutApproaches()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            Array.Empty<PortableLogbookWorkbookEntry>(),
            new DateOnly(2025, 5, 12));

        Assert.Equal(new DateOnly(2025, 5, 12), expiry);
    }

    [Fact]
    public void CalculateIfrAppsRecencyExpiry_UsesLaterOpcRecencyExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIfrAppsRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ils: 1),
                Entry(new DateOnly(2025, 2, 1), vor: 1),
                Entry(new DateOnly(2025, 4, 1), rnp: 1)
            ],
            new DateOnly(2025, 4, 15));

        Assert.Equal(new DateOnly(2025, 4, 15), expiry);
    }

    [Fact]
    public void CalculateNvfrRecencyExpiry_WithQualifyingNightLanding_ReturnsSixMonthsLater()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNvfrRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 31), landingsNight: 1)],
            null);

        Assert.Equal(new DateOnly(2025, 7, 31), expiry);
    }

    [Fact]
    public void CalculateNvfrRecencyExpiry_IgnoresDayOnlyLanding()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNvfrRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 31), landingsDay: 1)],
            null);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateNvfrRecencyExpiry_UsesLaterIpcExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateNvfrRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 31), landingsNight: 1)],
            new DateOnly(2025, 8, 1));

        Assert.Equal(new DateOnly(2025, 8, 1), expiry);
    }

    [Fact]
    public void CalculateSinglePilotIfrRecencyExpiry_WithInstrumentFlightAndApp_ReturnsSixMonthsLater()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateSinglePilotIfrRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 31), ils: 1, ifrIf: 1m)]);

        Assert.Equal(new DateOnly(2025, 7, 31), expiry);
    }

    [Fact]
    public void CalculateSinglePilotIfrRecencyExpiry_WithSimulatedInstrumentHourAndApp_Qualifies()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateSinglePilotIfrRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 31), rnp: 1, ifrSim: 1m)]);

        Assert.Equal(new DateOnly(2025, 7, 31), expiry);
    }

    [Fact]
    public void CalculateSinglePilotIfrRecencyExpiry_RequiresBothInstrumentTimeAndWorkbookTotalApps()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateSinglePilotIfrRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 31), ils: 1),
                Entry(new DateOnly(2025, 2, 1), ifrIf: 1m, circling: 1)
            ]);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateIlsThreeDimensionalCdiApproachRecencyExpiry_WithIlsAndCdiApproachInInclusiveWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIlsThreeDimensionalCdiApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ils: 1),
                Entry(new DateOnly(2025, 4, 1), vor: 1)
            ],
            null);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateIlsThreeDimensionalCdiApproachRecencyExpiry_RequiresAnIls()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIlsThreeDimensionalCdiApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), vor: 1),
                Entry(new DateOnly(2025, 2, 1), rnp: 1)
            ],
            null);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateIlsThreeDimensionalCdiApproachRecencyExpiry_UsesLaterOpcRecencyExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateIlsThreeDimensionalCdiApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 1), ils: 1)],
            new DateOnly(2025, 5, 1));

        Assert.Equal(new DateOnly(2025, 5, 1), expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalCdiApproachRecencyExpiry_WithTwoDimensionalAndCdiApproachesInInclusiveWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ndb: 1),
                Entry(new DateOnly(2025, 4, 1), ils: 1)
            ],
            null);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalCdiApproachRecencyExpiry_RequiresACdiApproach()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 1), ndb: 1)],
            null);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalCdiApproachRecencyExpiry_UsesLaterOpcRecencyExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalCdiApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 1), vor: 1)],
            new DateOnly(2025, 5, 1));

        Assert.Equal(new DateOnly(2025, 5, 1), expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalAzimuthApproachRecencyExpiry_WithTwoDimensionalAndAzimuthApproachesInInclusiveWindow_ReturnsWindowEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 1, 1), ndb: 1),
                Entry(new DateOnly(2025, 4, 1), rnp: 1)
            ],
            null);

        Assert.Equal(new DateOnly(2025, 4, 1), expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalAzimuthApproachRecencyExpiry_RequiresAnAzimuthApproach()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 1), vor: 1)],
            null);

        Assert.Null(expiry);
    }

    [Fact]
    public void CalculateTwoDimensionalAzimuthApproachRecencyExpiry_UsesLaterOpcRecencyExpiry()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateTwoDimensionalAzimuthApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 1, 1), rnp: 1)],
            new DateOnly(2025, 5, 1));

        Assert.Equal(new DateOnly(2025, 5, 1), expiry);
    }

    [Fact]
    public void CalculateCirclingApproachRecencyExpiry_WithQualifyingIpcAndCircling_ReturnsOneYearMonthEnd()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateCirclingApproachRecencyExpiry(
            [Entry(new DateOnly(2025, 5, 12), instrumentProficiencyCheck: true, circling: 1)]);

        Assert.Equal(new DateOnly(2026, 5, 31), expiry);
    }

    [Fact]
    public void CalculateCirclingApproachRecencyExpiry_WithEarlyQualifyingRenewal_PreservesPreviousExpiryAnniversary()
    {
        var expiry = PortableLogbookCurrencyCalculator.CalculateCirclingApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 6, 10), instrumentProficiencyCheck: true, circling: 1),
                Entry(new DateOnly(2026, 4, 30), instrumentProficiencyCheck: true, circling: 1)
            ]);

        Assert.Equal(new DateOnly(2027, 6, 30), expiry);
    }

    [Fact]
    public void CalculateCirclingApproachRecencyExpiry_RequiresSeaQualifiedIpcWithCircling()
    {
        var copilotOnlyIpc = Entry(new DateOnly(2025, 5, 12), instrumentProficiencyCheck: true, circling: 1) with
        {
            SeCommandDay = null,
            CopilotDay = 1m
        };
        var expiry = PortableLogbookCurrencyCalculator.CalculateCirclingApproachRecencyExpiry(
            [
                Entry(new DateOnly(2025, 5, 1), circling: 1),
                copilotOnlyIpc
            ]);

        Assert.Null(expiry);
    }

    private static PortableLogbookWorkbookEntry Entry(
        DateOnly date,
        bool flightReview = true,
        bool instrumentProficiencyCheck = false,
        bool operatorProficiencyCheck = false,
        int? landingsDay = null,
        int? landingsNight = null,
        int? ils = null,
        int? vor = null,
        int? rnp = null,
        int? ndb = null,
        int? circling = null,
        decimal? ifrIf = null,
        decimal? ifrSim = null) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date.Year,
            Month = date.Month,
            Day = date.Day,
            FlightReview = flightReview,
            InstrumentProficiencyCheck = instrumentProficiencyCheck,
            OperatorProficiencyCheck = operatorProficiencyCheck,
            LandingsDay = landingsDay,
            LandingsNight = landingsNight,
            Ils = ils,
            Vor = vor,
            Rnp = rnp,
            Ndb = ndb,
            Circling = circling,
            IfrIf = ifrIf,
            IfrSim = ifrSim,
            SeCommandDay = 1m
        };
}
