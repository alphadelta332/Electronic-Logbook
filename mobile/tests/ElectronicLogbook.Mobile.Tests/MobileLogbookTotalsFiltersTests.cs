using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookTotalsFiltersTests
{
    private static readonly IReadOnlyList<CustomFieldDefinition> CustomFields =
        PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Night vision", "Custom 2", "Custom 3", "Custom 4"]);

    private static readonly CustomFieldDefinition NightVision = CustomFields[0];

    private static readonly IReadOnlyList<MobileLogbookTotalsFilterField> Fields =
        MobileLogbookTotalsFilters.CreateFields(CustomFields);

    [Fact]
    public void CreateFields_ExposesEveryExcelFilterableColumnAndUsesTheCustomFieldLabel()
    {
        Assert.Equal(42, Fields.Count);
        Assert.Equal("Date", Fields[0].Label);
        Assert.Contains(Fields, field => field is { Id: "type", Label: "Aircraft type" });
        Assert.Contains(Fields, field => field is { Id: "custom:cf_workbook_1", Label: "Night vision" });
        Assert.Contains(Fields, field => field.Id == "seCommandDay");
        Assert.Contains(Fields, field => field.Id == "circling");
    }

    [Fact]
    public void Matches_CombinesValuesWithinAColumnWithOrAndDifferentColumnsWithAnd()
    {
        var entries = new[]
        {
            Entry("C208", "VH-AAA", seCommandDay: 1.2m),
            Entry("C404", "VH-BBB", seCommandDay: 2.3m),
            Entry("C208", "VH-CCC", seCommandDay: 4.5m),
            Entry("B350", "VH-DDD", seCommandDay: 8.0m)
        };
        var type = Field("type");
        var registration = Field("reg");
        var typeValues = MobileLogbookTotalsFilters.Values(type, entries);
        var registrationValues = MobileLogbookTotalsFilters.Values(registration, entries);
        var selections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [type.Id] = typeValues
                .Where(value => value.Label is "C208" or "C404")
                .Select(value => value.Key)
                .ToHashSet(StringComparer.Ordinal),
            [registration.Id] = registrationValues
                .Where(value => value.Label is "VH-AAA" or "VH-BBB")
                .Select(value => value.Key)
                .ToHashSet(StringComparer.Ordinal)
        };

        var matching = entries.Where(entry => MobileLogbookTotalsFilters.Matches(entry, selections, Fields)).ToArray();

        Assert.Equal(2, matching.Length);
        Assert.Equal(3.5m, matching.Sum(entry => entry.SeCommandDay));
    }

    [Fact]
    public void Values_CollapsesTextCaseAndWhitespaceAndIncludesBlankWithEntryCounts()
    {
        var entries = new[]
        {
            Entry(" C208 ", "VH-AAA"),
            Entry("c208", "VH-BBB"),
            Entry(null, "VH-CCC")
        };

        var values = MobileLogbookTotalsFilters.Values(Field("type"), entries);

        Assert.Collection(
            values,
            value =>
            {
                Assert.Equal("C208", value.Label);
                Assert.Equal(2, value.EntryCount);
            },
            value =>
            {
                Assert.Equal("(Blanks)", value.Label);
                Assert.Equal(1, value.EntryCount);
            });
    }

    [Fact]
    public void Values_FormatsAndSortsDatesNumbersFlagsAndCustomFields()
    {
        var entries = new[]
        {
            Entry("C208", "VH-AAA", 1.2m, new DateOnly(2026, 9, 12), true, "Goggle"),
            Entry("C404", "VH-BBB", 10m, new DateOnly(2025, 1, 2), false, null),
            Entry("C404", "VH-CCC", 2m)
        };

        Assert.Equal(["02 Jan 2025", "12 Sep 2026", "(Blanks)"], Labels("date", entries));
        Assert.Equal(["Non-zero"], Labels("seCommandDay", entries));
        Assert.Equal(["No", "Yes", "(Blanks)"], Labels("fr", entries));
        Assert.Equal(["Goggle", "(Blanks)"], Labels("custom:cf_workbook_1", entries));
    }

    [Fact]
    public void NumericFields_GroupExactValuesAndMatchOnlyEntriesWithANonZeroValue()
    {
        var entries = new[]
        {
            Entry("C208", "VH-BLANK"),
            Entry("C208", "VH-ZERO", seCommandDay: 0m) with { LandingsDay = 0, Ils = 0 },
            Entry("C208", "VH-ONE", seCommandDay: 1.2m) with { LandingsDay = 1, Ils = 1 },
            Entry("C208", "VH-TWO", seCommandDay: 2.3m) with { LandingsDay = 2, Ils = 2 }
        };

        foreach (var fieldId in new[] { "seCommandDay", "landingsDay", "ils" })
        {
            var field = Field(fieldId);
            var values = MobileLogbookTotalsFilters.Values(field, entries);
            var nonZero = Assert.Single(values, value => value.Label == "Non-zero");
            var zeroOrBlank = Assert.Single(values, value => value.Label == "Zero or blank");
            var selections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                [field.Id] = new([nonZero.Key], StringComparer.Ordinal)
            };

            Assert.Equal(2, nonZero.EntryCount);
            Assert.Equal(2, zeroOrBlank.EntryCount);
            Assert.Equal(
                ["VH-ONE", "VH-TWO"],
                entries
                    .Where(entry => MobileLogbookTotalsFilters.Matches(entry, selections, Fields))
                    .Select(entry => entry.Reg!)
                    .ToArray());
        }
    }

    [Fact]
    public void Matches_AnEmptyActiveSelectionMatchesNoEntries()
    {
        var selections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["type"] = new(StringComparer.Ordinal)
        };

        Assert.False(MobileLogbookTotalsFilters.Matches(Entry("C208", "VH-AAA"), selections, Fields));
    }

    [Theory]
    [InlineData("eq", 2, true)]
    [InlineData("eq", 1.9, false)]
    [InlineData("ne", 2, false)]
    [InlineData("ne", 2.1, true)]
    [InlineData("gt", 2, false)]
    [InlineData("gt", 2.1, true)]
    [InlineData("ge", 2, true)]
    [InlineData("ge", 1.9, false)]
    [InlineData("lt", 2, false)]
    [InlineData("lt", 1.9, true)]
    [InlineData("le", 2, true)]
    [InlineData("le", 2.1, false)]
    [InlineData("between", 2, true)]
    [InlineData("between", 3, true)]
    [InlineData("between", 1.9, false)]
    [InlineData("between", 3.1, false)]
    public void NumericComparison_UsesExactBoundaries(string comparison, double value, bool expected)
    {
        Assert.Equal(expected, new MobileLogbookNumericFilter(comparison, 2m, 3m).Matches((decimal)value));
    }

    [Fact]
    public void NumericComparison_RejectsMissingValuesAndReversedRanges()
    {
        foreach (var filter in new[] {
            new MobileLogbookNumericFilter("gt", null),
            new MobileLogbookNumericFilter("between", 2m),
            new MobileLogbookNumericFilter("between", 3m, 2m) })
        {
            Assert.NotNull(filter.Error);
            Assert.False(filter.Matches(2m));
        }
    }

    [Fact]
    public void Matches_CombinesNumericHoursAndCountsWithTextAndDates()
    {
        var entries = new[] {
            Entry("C208", "MATCH", 2.25m, new(2026, 1, 1)) with { LandingsDay = 2 },
            Entry("C208", "LOW-HOURS", 2m, new(2026, 1, 1)) with { LandingsDay = 2 },
            Entry("C208", "HIGH-COUNT", 2.25m, new(2026, 1, 1)) with { LandingsDay = 3 },
            Entry("C404", "WRONG-TYPE", 2.25m, new(2026, 1, 1)) with { LandingsDay = 2 },
            Entry("C208", "WRONG-DATE", 2.25m, new(2025, 1, 1)) with { LandingsDay = 2 } };
        var selections = new Dictionary<string, HashSet<string>> { ["type"] = ["text:C208"] };
        var numeric = new Dictionary<string, MobileLogbookNumericFilter> {
            ["seCommandDay"] = new("gt", 2m), ["landingsDay"] = new("le", 2m) };

        Assert.Equal(["MATCH"], entries.Where(entry => MobileLogbookTotalsFilters.Matches(
            entry, selections, Fields, new(2026, 1, 1), new(2026, 1, 1), numeric)).Select(entry => entry.Reg));
    }

    [Theory]
    [InlineData("seCommandDay")]
    [InlineData("landingsDay")]
    [InlineData("ils")]
    public void Matches_NumericComparisonsCountBlanksAsZero(string fieldId)
    {
        var entries = new[] {
            Entry("C208", "BLANK"),
            Entry("C208", "ZERO", 0m) with { LandingsDay = 0, Ils = 0 },
            Entry("C208", "TWO", 2m) with { LandingsDay = 2, Ils = 2 } };
        var numeric = new Dictionary<string, MobileLogbookNumericFilter> { [fieldId] = new("eq", 0m) };
        Assert.Equal(["BLANK", "ZERO"], entries.Where(entry => MobileLogbookTotalsFilters.Matches(
            entry, new Dictionary<string, HashSet<string>>(), Fields, numericFilters: numeric)).Select(entry => entry.Reg));
        numeric[fieldId] = new("gt", 0m);
        Assert.Equal(["TWO"], entries.Where(entry => MobileLogbookTotalsFilters.Matches(
            entry, new Dictionary<string, HashSet<string>>(), Fields, numericFilters: numeric)).Select(entry => entry.Reg));
    }

    [Theory]
    [InlineData("gt", 1.5, null, "DECIMAL,WHOLE,GROUPED")]
    [InlineData("lt", 2, null, "BLANK,SPACE,ZERO,DECIMAL")]
    [InlineData("eq", 0, null, "BLANK,SPACE,ZERO")]
    [InlineData("ne", 0, null, "DECIMAL,WHOLE,GROUPED")]
    [InlineData("between", 1.75, 2d, "DECIMAL,WHOLE")]
    public void Matches_CustomNumbersUseStoredPrecisionAndExcludeNonNumericText(
        string comparison, double value, double? upperValue, string expected)
    {
        var field = Field($"custom:{NightVision.Id.Value}");
        Assert.True(field.SupportsNumericComparison);
        Assert.False(Field("type").SupportsNumericComparison);
        var entries = new[] {
            Entry("C208", "BLANK"),
            Entry("C208", "SPACE", nightVision: " "),
            Entry("C208", "ZERO", nightVision: "0"),
            Entry("C208", "DECIMAL", nightVision: "1.75"),
            Entry("C208", "WHOLE", nightVision: "2"),
            Entry("C208", "GROUPED", nightVision: "1,000.5"),
            Entry("C208", "TEXT", nightVision: "Goggle") };
        var numeric = new Dictionary<string, MobileLogbookNumericFilter> {
            [field.Id] = new(comparison, (decimal)value, (decimal?)upperValue) };

        Assert.Equal(expected.Split(','), entries.Where(entry => MobileLogbookTotalsFilters.Matches(
            entry, new Dictionary<string, HashSet<string>>(), Fields, numericFilters: numeric)).Select(entry => entry.Reg));
        Assert.Equal("1.75", entries[3].CustomFields[NightVision.Id]);
    }

    [Fact]
    public void Matches_DateRangeIsInclusiveAndRejectsEntriesWithoutAValidDate()
    {
        var entries = new[]
        {
            Entry("C208", "VH-BEFORE", date: new DateOnly(2024, 12, 31)),
            Entry("C208", "VH-START", date: new DateOnly(2025, 1, 1)),
            Entry("C208", "VH-MIDDLE", date: new DateOnly(2025, 6, 15)),
            Entry("C208", "VH-END", date: new DateOnly(2025, 12, 31)),
            Entry("C208", "VH-AFTER", date: new DateOnly(2026, 1, 1)),
            Entry("C208", "VH-NODATE")
        };

        var matching = entries
            .Where(entry => MobileLogbookTotalsFilters.Matches(
                entry,
                new Dictionary<string, HashSet<string>>(),
                Fields,
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31)))
            .Select(entry => entry.Reg!)
            .ToArray();

        Assert.Equal(["VH-START", "VH-MIDDLE", "VH-END"], matching);
    }

    [Fact]
    public void Matches_DateRangeRejectsAnEndBeforeTheStart()
    {
        void Action() => MobileLogbookTotalsFilters.Matches(
            Entry("C208", "VH-AAA"),
            new Dictionary<string, HashSet<string>>(),
            Fields,
            new DateOnly(2025, 12, 31),
            new DateOnly(2025, 1, 1));

        var error = Assert.Throws<ArgumentException>(Action);
        Assert.Contains("start date cannot be later", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Labels(string fieldId, IEnumerable<PortableLogbookWorkbookEntry> entries) =>
        MobileLogbookTotalsFilters.Values(Field(fieldId), entries).Select(value => value.Label).ToArray();

    private static MobileLogbookTotalsFilterField Field(string id) =>
        Fields.Single(field => field.Id == id);

    private static PortableLogbookWorkbookEntry Entry(
        string? type,
        string reg,
        decimal? seCommandDay = null,
        DateOnly? date = null,
        bool? flightReview = null,
        string? nightVision = null) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = date?.Year,
            Month = date?.Month,
            Day = date?.Day,
            Type = type,
            Reg = reg,
            FlightReview = flightReview,
            SeCommandDay = seCommandDay,
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [NightVision.Id] = nightVision
            }
        };
}
