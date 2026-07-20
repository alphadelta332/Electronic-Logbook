using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileRecentValues
{
    public static IReadOnlyList<string> Create(
        IEnumerable<PortableLogbookMaterializedEntry> currentEntries,
        Func<PortableLogbookEntry, string?> selector,
        int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return CreateMany(currentEntries, entry => [selector(entry)], limit);
    }

    public static IReadOnlyList<string> CreateMany(
        IEnumerable<PortableLogbookMaterializedEntry> currentEntries,
        Func<PortableLogbookEntry, IEnumerable<string?>> selector,
        int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(currentEntries);
        ArgumentNullException.ThrowIfNull(selector);

        return currentEntries
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderByDescending(entry => entry.Entry!.Date ?? DateOnly.MinValue)
            .SelectMany(entry => selector(entry.Entry!))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }
}
