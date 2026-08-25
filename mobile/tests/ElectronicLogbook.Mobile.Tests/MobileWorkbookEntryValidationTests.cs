using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using System.Text.RegularExpressions;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileWorkbookEntryValidationTests
{
    [Fact]
    public void UserFacingEntryMessagesHaveOneSource()
    {
        var root = FindRepoRoot();
        var messageSource = File.ReadAllText(Path.Combine(
            root, "updater", "src", "ElectronicLogbook.Portable", "PortableLogbookEntryMessages.cs"));
        var ruleSources = new[]
        {
            Path.Combine(root, "updater", "src", "ElectronicLogbook.Portable", "PortableLogbookEntryRules.cs"),
            Path.Combine(root, "mobile", "src", "ElectronicLogbook.Mobile", "MobileLogbookEntryWarnings.cs"),
            Path.Combine(root, "mobile", "src", "ElectronicLogbook.Mobile", "MobileWorkbookEntryValidation.cs")
        }.Select(File.ReadAllText).ToArray();

        Assert.Contains("Edit entry validation and warning wording here", messageSource, StringComparison.Ordinal);
        Assert.All(ruleSources, source =>
            Assert.Contains("PortableLogbookEntryMessages.", source, StringComparison.Ordinal));
        Assert.All(ruleSources, source =>
            Assert.DoesNotContain("This registration has previously been logged", source, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationSourceContainsEveryAddToLogbookErrorAndWarningCode()
    {
        var root = FindRepoRoot();
        var vba = File.ReadAllText(Path.Combine(root, "modLogbook.bas"));
        var mobile = File.ReadAllText(Path.Combine(
            root,
            "mobile",
            "src",
            "ElectronicLogbook.Mobile",
            "MobileWorkbookEntryValidation.cs"));

        var vbaCodes = ValidationCodes(vba);
        var mobileCodes = ValidationCodes(mobile);

        Assert.Equal(vbaCodes, mobileCodes);
        Assert.Equal(30, mobileCodes.Length);
    }

    [Fact]
    public void ValidateMatchesAddToLogbookBlockingRulesAndTechnicalCodes()
    {
        var entry = ValidEntry() with
        {
            Year = 2027,
            Type = null,
            Reg = null,
            Pic = null,
            From = null,
            To = null,
            SeCommandDay = null,
            IfrIf = 1,
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [new CustomFieldId("cf_workbook_1")] = "not a number"
            }
        };

        var errors = MobileWorkbookEntryValidation.Validate(entry, new DateOnly(2026, 8, 25));

        Assert.Equal(
            ["NEWENTRY-E001", "NEWENTRY-E002", "NEWENTRY-E003", "NEWENTRY-E004", "NEWENTRY-E005", "NEWENTRY-E005", "NEWENTRY-E006", "NEWENTRY-E007", "NEWENTRY-E008"],
            errors.Select(error => error.Code));
    }

    [Fact]
    public void ValidateAllowsSimulatorEntryWithoutRegistrationOrRoute()
    {
        var entry = ValidEntry() with
        {
            Reg = null,
            From = null,
            To = null,
            SeCommandDay = null,
            LandingsDay = null,
            IfrSim = 1
        };

        var errors = MobileWorkbookEntryValidation.Validate(entry, new DateOnly(2026, 8, 25));

        Assert.Empty(errors);
    }

    [Fact]
    public void WarnMatchesCurrencyLandingApproachCrewAndMixedEngineRules()
    {
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        AddCodes(warnings, ValidEntry() with
        {
            OperatorProficiencyCheck = true,
            IfrIf = 0.2m,
            InstrumentProficiencyCheck = false
        });
        AddCodes(warnings, ValidEntry() with
        {
            InstrumentProficiencyCheck = true,
            FlightReview = false,
            Circling = 0
        });
        AddCodes(warnings, ValidEntry() with { LandingsDay = 0 });
        AddCodes(warnings, ValidEntry() with { SeCommandDay = null, SeCommandNight = 1, LandingsDay = 1, LandingsNight = 0 });
        AddCodes(warnings, ValidEntry() with { SeCommandDay = null, IfrSim = 1, LandingsDay = 1 });
        AddCodes(warnings, ValidEntry() with { SeCommandDay = null, IfrSim = 1, LandingsNight = 1 });
        AddCodes(warnings, ValidEntry() with { OperatorProficiencyCheck = true });
        AddCodes(warnings, ValidEntry() with { Ils = 1 });
        AddCodes(warnings, ValidEntry() with { LandingsDay = 7 });
        AddCodes(warnings, ValidEntry() with { Ils = 4, IfrIf = 0.2m });
        AddCodes(warnings, ValidEntry() with { SeCommandDay = null, SeDualDay = 1, OtherPilotOrCrew = null });
        AddCodes(warnings, ValidEntry() with { MeCommandDay = 0.5m });

        Assert.Contains("NEWENTRY-W001", warnings);
        Assert.Contains("NEWENTRY-W002", warnings);
        Assert.Contains("NEWENTRY-W003", warnings);
        Assert.Contains("NEWENTRY-W007", warnings);
        Assert.Contains("NEWENTRY-W009", warnings);
        Assert.Contains("NEWENTRY-W010", warnings);
        Assert.Contains("NEWENTRY-W011", warnings);
        Assert.Contains("NEWENTRY-W012", warnings);
        Assert.Contains("NEWENTRY-W013", warnings);
        Assert.Contains("NEWENTRY-W014", warnings);
        Assert.Contains("NEWENTRY-W015", warnings);
        Assert.Contains("NEWENTRY-W016", warnings);
        Assert.Contains("NEWENTRY-W017", warnings);
        Assert.Contains("NEWENTRY-W018", warnings);
    }

    [Fact]
    public void LandingWarningsOnlyPointToTheResultingLandingField()
    {
        var warnings = MobileWorkbookEntryValidation.Warn(
            ValidEntry() with { LandingsDay = 0 },
            [],
            airportCatalog: LocalCatalog())
            .Where(warning => warning.Code is "NEWENTRY-W007" or "NEWENTRY-W009")
            .ToArray();

        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning =>
            Assert.Equal(nameof(PortableLogbookWorkbookEntry.LandingsDay), warning.AffectedField));
    }

    [Fact]
    public void WarnMatchesExistingLogbookHistoryRulesAndExcludesEditedEntry()
    {
        var latest = Materialized("ent_latest", ValidEntry() with
        {
            Year = 2026,
            Month = 8,
            Day = 24,
            Type = "PA28",
            SeCommandDay = null,
            MeCommandDay = 1
        });
        var duplicate = Materialized("ent_duplicate", ValidEntry() with
        {
            Year = 2026,
            Month = 8,
            Day = 23,
            Type = "PA28",
            Reg = "VH-DUP",
            Remarks = "Same"
        });
        var draft = ValidEntry() with
        {
            Year = 2026,
            Month = 8,
            Day = 23,
            Type = " pa28 ",
            Reg = " vh-dup ",
            Remarks = " same ",
            SeCommandDay = 1,
            MeCommandDay = null
        };

        var warnings = MobileWorkbookEntryValidation.Warn(draft, [latest, duplicate], airportCatalog: LocalCatalog());

        Assert.Contains(warnings, warning => warning.Code == "NEWENTRY-W008");
        Assert.Contains(warnings, warning => warning.Code == "NEWENTRY-W019");
        Assert.Contains(warnings, warning => warning.Code == "NEWENTRY-W022");

        var excluded = MobileWorkbookEntryValidation.Warn(draft, [duplicate], duplicate.EntryId, LocalCatalog());
        Assert.DoesNotContain(excluded, warning => warning.Code is "NEWENTRY-W008" or "NEWENTRY-W019" or "NEWENTRY-W022");
    }

    [Fact]
    public void WarnMatchesAircraftTypeAndRegistrationHistoryRules()
    {
        var singleEngineHistory = Materialized("ent_single", ValidEntry() with { Type = "C172" });
        var differentTypeSameRegistration = Materialized("ent_reg", ValidEntry() with { Type = "PA28", Reg = "VH-ABC" });
        var multiEngineDraft = ValidEntry() with
        {
            Type = "C172",
            SeCommandDay = null,
            MeCommandDay = 1
        };

        var multiWarnings = MobileWorkbookEntryValidation.Warn(multiEngineDraft, [singleEngineHistory], airportCatalog: LocalCatalog());
        var registrationWarnings = MobileWorkbookEntryValidation.Warn(ValidEntry(), [differentTypeSameRegistration], airportCatalog: LocalCatalog());

        Assert.Contains(multiWarnings, warning => warning.Code == "NEWENTRY-W020");
        Assert.Contains(registrationWarnings, warning => warning.Code == "NEWENTRY-W021");
    }

    [Fact]
    public void WarnMatchesAirportRecognitionDistanceAndImpliedSpeedRules()
    {
        var catalog = LocalCatalog();
        var visited = Materialized("ent_visited", ValidEntry() with { From = "CCCC", To = "CCCC" });
        var route = ValidEntry() with { From = "AAAA", To = "BBBB", SeCommandDay = 1 };

        var warnings = MobileWorkbookEntryValidation.Warn(route, [visited], airportCatalog: catalog);
        var unrecognised = MobileWorkbookEntryValidation.Warn(
            ValidEntry() with { From = "NOPE", To = "MISSING" },
            [],
            airportCatalog: catalog);

        var distant = Assert.Single(warnings, warning => warning.Code == "NEWENTRY-W005");
        Assert.Contains("route from AAAA", distant.Message, StringComparison.Ordinal);
        Assert.Contains(warnings, warning => warning.Code == "NEWENTRY-W006");
        var airportWarnings = unrecognised.Where(warning => warning.Code == "NEWENTRY-W004").ToArray();
        Assert.Equal(2, airportWarnings.Length);
        Assert.Contains(airportWarnings, warning => warning.AffectedField == nameof(PortableLogbookWorkbookEntry.From));
        Assert.Contains(airportWarnings, warning => warning.AffectedField == nameof(PortableLogbookWorkbookEntry.To));
    }

    [Fact]
    public void EmbeddedCatalogContainsMasterWorkbookAirportsAndAliases()
    {
        Assert.True(MobileAirportCatalog.Default.TryFind("YSSY", out var sydney));
        Assert.Equal("YSSY", sydney.Icao);
        Assert.True(MobileAirportCatalog.Default.TryFind("SY", out var alias));
        Assert.Equal("YSSY", alias.Icao);
    }

    private static void AddCodes(HashSet<string> target, PortableLogbookWorkbookEntry entry)
    {
        foreach (var warning in MobileWorkbookEntryValidation.Warn(entry, [], airportCatalog: LocalCatalog()))
        {
            target.Add(warning.Code);
        }
    }

    private static PortableLogbookWorkbookEntry ValidEntry() => PortableLogbookWorkbookEntry.Empty with
    {
        Year = 2026,
        Month = 8,
        Day = 25,
        Type = "C172",
        Reg = "VH-ABC",
        Pic = "Self",
        From = "AAAA",
        To = "AAAA",
        Remarks = "Training",
        FlightReview = false,
        InstrumentProficiencyCheck = false,
        OperatorProficiencyCheck = false,
        SeCommandDay = 1,
        LandingsDay = 1
    };

    private static MobileAirportCatalog LocalCatalog() => MobileAirportCatalog.Create(
    [
        new MobileAirport("AAAA", "Alpha", "AAA", "AA", 0, 0),
        new MobileAirport("BBBB", "Bravo", "BBB", "BB", 0, 60),
        new MobileAirport("CCCC", "Charlie", "CCC", "CC", 0, 1)
    ]);

    private static PortableLogbookMaterializedEntryV2 Materialized(
        string entryId,
        PortableLogbookWorkbookEntry entry)
    {
        var revisionId = new RevisionId($"rev_{entryId}");
        return new PortableLogbookMaterializedEntryV2(
            new EntryId(entryId),
            revisionId,
            IsDeleted: false,
            entry,
            [revisionId]);
    }

    private static string[] ValidationCodes(string source) =>
        Regex.Matches(source, "NEWENTRY-[EW][0-9]{3}", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "modLogbook.bas")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing modLogbook.bas.");
    }
}
