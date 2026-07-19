using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileRecentValuesTests
{
    [Fact]
    public void CreatePreservesCurrentEntryOrderAndDeduplicatesCaseInsensitively()
    {
        var entries = new[]
        {
            Current("ent_1", "VH-RECENT"),
            Current("ent_2", " vh-old "),
            Current("ent_3", "vh-recent"),
            Current("ent_4", "")
        };

        var values = MobileRecentValues.Create(entries, entry => entry.Registration);

        Assert.Equal(["VH-RECENT", "vh-old"], values);
    }

    [Fact]
    public void CreateHonoursLimitAfterDeduplication()
    {
        var entries = Enumerable
            .Range(1, 5)
            .Select(index => Current($"ent_{index}", $"VH-{index}"))
            .ToArray();

        var values = MobileRecentValues.Create(entries, entry => entry.Registration, limit: 3);

        Assert.Equal(["VH-1", "VH-2", "VH-3"], values);
    }

    [Fact]
    public void CreateIgnoresDeletedEntries()
    {
        var entries = new[]
        {
            Current("ent_deleted", "VH-DELETED") with { IsDeleted = true },
            Current("ent_current", "VH-CURRENT")
        };

        var values = MobileRecentValues.Create(entries, entry => entry.Registration);

        Assert.Equal(["VH-CURRENT"], values);
    }

    [Fact]
    public void CreateManyKeepsMultipleValuesPerRecentEntry()
    {
        var entries = new[]
        {
            Current("ent_1", "VH-ABC") with { Entry = Entry("VH-ABC") with { From = "YSBK", To = "YSCN" } },
            Current("ent_2", "VH-DEF") with { Entry = Entry("VH-DEF") with { From = "YSCN", To = "YMML" } }
        };

        var values = MobileRecentValues.CreateMany(entries, entry => [entry.From, entry.To]);

        Assert.Equal(["YSBK", "YSCN", "YMML"], values);
    }

    private static PortableLogbookMaterializedEntry Current(string entryId, string registration) =>
        new(
            new EntryId(entryId),
            new RevisionId($"rev_{entryId}"),
            IsDeleted: false,
            Entry(registration),
            [new RevisionId($"rev_{entryId}")]);

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 19),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.0m
        };
}
