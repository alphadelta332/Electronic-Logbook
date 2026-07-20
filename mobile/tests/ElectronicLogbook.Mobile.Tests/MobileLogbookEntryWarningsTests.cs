using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookEntryWarningsTests
{
    [Fact]
    public void CreateIncludesSharedPortableEntryRuleWarnings()
    {
        var draft = Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check") with
        {
            Day = 1.0m,
            PilotInCommand = 1.0m,
            LandingsDay = 0,
            LandingsNight = 0
        };

        var warnings = MobileLogbookEntryWarnings.Create(draft, []);

        Assert.Contains(warnings, warning => warning.Contains("flight time but no landing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("day time but no day landing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateIncludesSharedInstrumentAndApproachWarnings()
    {
        var draft = Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check") with
        {
            InstrumentActual = 0.4m,
            IfrApproaches = 0,
            Holding = 0,
            Rnav = 0,
            Circling = 0
        };

        var warnings = MobileLogbookEntryWarnings.Create(draft, []);

        Assert.Contains(warnings, warning => warning.Contains("instrument time", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("approach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateIncludesSharedHighCountWarnings()
    {
        var draft = Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check") with
        {
            PilotInCommand = 1.0m,
            InstrumentActual = 0.5m,
            LandingsDay = 7,
            IfrApproaches = 4
        };

        var warnings = MobileLogbookEntryWarnings.Create(draft, []);

        Assert.Contains(warnings, warning => warning.Contains("landings", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("approaches", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateIncludesSharedDayNightTimeConsistencyWarnings()
    {
        var missingDayNight = Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check") with
        {
            PilotInCommand = 1.0m,
            Day = 0,
            Night = 0
        };
        var excessiveDayNight = Entry(new DateOnly(2026, 7, 20), "C172", "VH-DEF", "Check") with
        {
            PilotInCommand = 1.0m,
            Day = 0.8m,
            Night = 0.5m
        };

        var missingWarnings = MobileLogbookEntryWarnings.Create(missingDayNight, []);
        var excessiveWarnings = MobileLogbookEntryWarnings.Create(excessiveDayNight, []);

        Assert.Contains(missingWarnings, warning => warning.Contains("no day or night time", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(excessiveWarnings, warning => warning.Contains("exceed the total flight time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateWarnsWhenDraftIsEarlierThanLatestExistingEntry()
    {
        var existing = Materialized("ent_latest", Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check"));
        var draft = Entry(new DateOnly(2026, 7, 18), "C172", "VH-DEF", "Earlier");

        var warnings = MobileLogbookEntryWarnings.Create(draft, [existing]);

        Assert.Contains(warnings, warning => warning.Contains("before the latest existing entry", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateWarnsWhenDraftLooksLikeDuplicateExistingEntry()
    {
        var existing = Materialized("ent_existing", Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check"));
        var draft = Entry(new DateOnly(2026, 7, 19), " c172 ", "vh-abc", " check ");

        var warnings = MobileLogbookEntryWarnings.Create(draft, [existing]);

        Assert.Contains(warnings, warning => warning.Contains("same date, type, registration, and remarks", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateExcludesEntryBeingEditedFromDocumentWarnings()
    {
        var entryId = new EntryId("ent_existing");
        var existing = Materialized(entryId.Value, Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check"));
        var draft = Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check");

        var warnings = MobileLogbookEntryWarnings.Create(draft, [existing], entryId);

        Assert.DoesNotContain(warnings, warning => warning.Contains("same date, type, registration, and remarks", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, warning => warning.Contains("before the latest existing entry", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateWarnsWhenRegistrationWasPreviouslyLoggedWithDifferentAircraftType()
    {
        var existing = Materialized("ent_existing", Entry(new DateOnly(2026, 7, 19), "PA28", "VH-ABC", "Check"));
        var draft = Entry(new DateOnly(2026, 7, 20), " c172 ", " vh-abc ", "New type");

        var warnings = MobileLogbookEntryWarnings.Create(draft, [existing]);

        Assert.Contains(warnings, warning => warning.Contains("previously been logged with a different aircraft type", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDoesNotWarnForRegistrationTypeHistoryWhenTypeMatchesOrDraftIsIncomplete()
    {
        var existing = Materialized("ent_existing", Entry(new DateOnly(2026, 7, 19), "C172", "VH-ABC", "Check"));
        var matchingDraft = Entry(new DateOnly(2026, 7, 20), " c172 ", " vh-abc ", "Same type");
        var missingTypeDraft = Entry(new DateOnly(2026, 7, 20), " ", " vh-abc ", "Missing type");

        var matchingWarnings = MobileLogbookEntryWarnings.Create(matchingDraft, [existing]);
        var incompleteWarnings = MobileLogbookEntryWarnings.Create(missingTypeDraft, [existing]);

        Assert.DoesNotContain(matchingWarnings, warning => warning.Contains("previously been logged with a different aircraft type", StringComparison.Ordinal));
        Assert.DoesNotContain(incompleteWarnings, warning => warning.Contains("previously been logged with a different aircraft type", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateExcludesEntryBeingEditedFromRegistrationTypeHistoryWarning()
    {
        var entryId = new EntryId("ent_existing");
        var existing = Materialized(entryId.Value, Entry(new DateOnly(2026, 7, 19), "PA28", "VH-ABC", "Check"));
        var draft = Entry(new DateOnly(2026, 7, 20), "C172", "VH-ABC", "Correcting type");

        var warnings = MobileLogbookEntryWarnings.Create(draft, [existing], entryId);

        Assert.DoesNotContain(warnings, warning => warning.Contains("previously been logged with a different aircraft type", StringComparison.Ordinal));
    }

    private static PortableLogbookMaterializedEntry Materialized(string entryId, PortableLogbookEntry entry)
    {
        var operation = new CreateEntryOperation(
            new LogbookId("log_mobile"),
            new EntryId(entryId),
            new RevisionId($"rev_{entryId}"),
            new DeviceId("dev_mobile"),
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            entry);
        return new PortableLogbookMaterializedEntry(
            operation.EntryId,
            operation.RevisionId,
            IsDeleted: false,
            operation.Entry,
            [operation.RevisionId]);
    }

    private static PortableLogbookEntry Entry(
        DateOnly date,
        string aircraftType,
        string registration,
        string details) =>
        PortableLogbookEntry.Empty with
        {
            Date = date,
            AircraftType = aircraftType,
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.0m,
            Day = 1.0m,
            Details = details
        };
}
