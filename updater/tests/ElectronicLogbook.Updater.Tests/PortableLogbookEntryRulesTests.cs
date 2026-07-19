using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookEntryRulesTests
{
    private static readonly DateOnly Today = new(2026, 7, 19);

    [Fact]
    public void ValidateAcceptsCompleteFlightEntry()
    {
        var result = PortableLogbookEntryRules.Validate(CompleteEntry(), Today);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateAcceptsSimulatorEntryWithoutRegistrationOrRoute()
    {
        var entry = PortableLogbookEntry.Empty with
        {
            Date = Today,
            AircraftType = "SIM",
            InstrumentSimulated = 1.1m
        };

        var result = PortableLogbookEntryRules.Validate(entry, Today);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsMissingRequiredFlightFields()
    {
        var entry = CompleteEntry() with
        {
            AircraftType = "",
            Registration = "",
            From = "",
            To = ""
        };

        var result = PortableLogbookEntryRules.Validate(entry, Today);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.MissingAircraftType);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.MissingRegistration);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.MissingDeparture);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.MissingDestination);
    }

    [Fact]
    public void ValidateRejectsFutureDateAndZeroLoggedTime()
    {
        var entry = CompleteEntry() with
        {
            Date = Today.AddDays(1),
            PilotInCommand = 0
        };

        var result = PortableLogbookEntryRules.Validate(entry, Today);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.InvalidDate);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.MissingLoggedTime);
    }

    [Fact]
    public void ValidateRejectsInstrumentActualGreaterThanFlightTime()
    {
        var entry = CompleteEntry() with { PilotInCommand = 1.0m, InstrumentActual = 1.1m };

        var result = PortableLogbookEntryRules.Validate(entry, Today);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.InstrumentActualExceedsFlightTime);
    }

    [Fact]
    public void ValidateRejectsNegativeDurationsAndCounts()
    {
        var entry = CompleteEntry() with
        {
            PilotInCommand = -0.1m,
            LandingsDay = -1
        };

        var result = PortableLogbookEntryRules.Validate(entry, Today);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.NegativeDuration);
        Assert.Contains(result.Errors, error => error.Code == PortableLogbookEntryRuleCode.NegativeCount);
    }

    [Fact]
    public void LoggedTimeIncludesSimulatorTime()
    {
        var entry = CompleteEntry() with
        {
            PilotInCommand = 1.2m,
            Dual = 0.3m,
            InstrumentSimulated = 0.5m
        };

        Assert.Equal(1.5m, PortableLogbookEntryRules.FlightTime(entry));
        Assert.Equal(2.0m, PortableLogbookEntryRules.LoggedTime(entry));
    }

    [Fact]
    public void WarnReportsFlightTimeWithoutLandings()
    {
        var warnings = PortableLogbookEntryRules.Warn(CompleteEntry());

        Assert.Contains(warnings, warning => warning.Code == PortableLogbookEntryRuleWarningCode.FlightTimeWithoutLanding);
    }

    [Fact]
    public void WarnReportsDayAndNightLandingMismatches()
    {
        var entry = CompleteEntry() with
        {
            Day = 0.7m,
            Night = 0,
            LandingsDay = 0,
            LandingsNight = 1
        };

        var warnings = PortableLogbookEntryRules.Warn(entry);

        Assert.Contains(warnings, warning => warning.Code == PortableLogbookEntryRuleWarningCode.DayTimeWithoutDayLanding);
        Assert.Contains(warnings, warning => warning.Code == PortableLogbookEntryRuleWarningCode.NightLandingWithoutNightTime);
    }

    [Fact]
    public void WarnReportsInstrumentApproachMismatches()
    {
        var approachWithoutInstrument = CompleteEntry() with
        {
            InstrumentActual = 0,
            InstrumentSimulated = 0,
            IfrApproaches = 1
        };
        var instrumentWithoutApproach = CompleteEntry() with
        {
            InstrumentActual = 0.2m,
            IfrApproaches = 0,
            Holding = 0,
            Rnav = 0,
            Circling = 0
        };

        Assert.Contains(
            PortableLogbookEntryRules.Warn(approachWithoutInstrument),
            warning => warning.Code == PortableLogbookEntryRuleWarningCode.ApproachWithoutInstrumentTime);
        Assert.Contains(
            PortableLogbookEntryRules.Warn(instrumentWithoutApproach),
            warning => warning.Code == PortableLogbookEntryRuleWarningCode.InstrumentTimeWithoutApproach);
    }

    private static PortableLogbookEntry CompleteEntry() =>
        PortableLogbookEntry.Empty with
        {
            Date = Today,
            AircraftType = "C172",
            Registration = "VH-ABC",
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.2m
        };
}
