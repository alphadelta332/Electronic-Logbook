using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileRecentValuesTests
{
    [Fact]
    public void CreateOrdersByEntryDateAndDeduplicatesCaseInsensitively()
    {
        var entries = new[]
        {
            Current("ent_old", " vh-old ", new DateOnly(2026, 7, 18)),
            Current("ent_latest", "VH-RECENT", new DateOnly(2026, 7, 20)),
            Current("ent_duplicate", "vh-recent", new DateOnly(2026, 7, 19)),
            Current("ent_blank", "", new DateOnly(2026, 7, 21))
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
        Current(entryId, registration, new DateOnly(2026, 7, 19));

    private static PortableLogbookMaterializedEntry Current(string entryId, string registration, DateOnly date) =>
        new(
            new EntryId(entryId),
            new RevisionId($"rev_{entryId}"),
            IsDeleted: false,
            Entry(registration, date),
            [new RevisionId($"rev_{entryId}")]);

    private static PortableLogbookEntry Entry(string registration) =>
        Entry(registration, new DateOnly(2026, 7, 19));

    private static PortableLogbookEntry Entry(string registration, DateOnly date) =>
        PortableLogbookEntry.Empty with
        {
            Date = date,
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSCN",
            PilotInCommand = 1.0m
        };
}
