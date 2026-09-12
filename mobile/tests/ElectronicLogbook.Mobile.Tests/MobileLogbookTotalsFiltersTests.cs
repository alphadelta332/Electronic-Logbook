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
        Assert.Equal(["1.2", "2.0", "10.0"], Labels("seCommandDay", entries));
        Assert.Equal(["No", "Yes", "(Blanks)"], Labels("fr", entries));
        Assert.Equal(["Goggle", "(Blanks)"], Labels("custom:cf_workbook_1", entries));
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
