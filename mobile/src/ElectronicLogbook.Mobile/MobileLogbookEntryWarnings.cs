using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileLogbookEntryWarnings
{
    public static IReadOnlyList<string> Create(
        PortableLogbookEntry draft,
        IEnumerable<PortableLogbookMaterializedEntry> currentEntries,
        EntryId? editingEntryId = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(currentEntries);

        var comparableEntries = currentEntries
            .Where(entry => !entry.IsDeleted && entry.Entry is not null && entry.EntryId != editingEntryId)
            .ToArray();
        var warnings = PortableLogbookEntryRules
            .Warn(draft)
            .Select(warning => warning.Message)
            .ToList();

        var latestDate = comparableEntries
            .Select(entry => entry.Entry!.Date)
            .Where(date => date is not null)
            .Max();
        if (draft.Date is not null && latestDate is not null && draft.Date < latestDate)
        {
            warnings.Add($"This entry is dated before the latest existing entry ({latestDate:dd MMM yyyy}).");
        }

        if (comparableEntries.Any(entry => IsDuplicate(draft, entry.Entry!)))
        {
            warnings.Add("An entry with the same date, type, registration, and remarks already exists.");
        }

        if (comparableEntries.Any(entry => HasRegistrationWithDifferentType(draft, entry.Entry!)))
        {
            warnings.Add("This registration has previously been logged with a different aircraft type.");
        }

        return warnings;
    }

    private static bool IsDuplicate(PortableLogbookEntry draft, PortableLogbookEntry existing) =>
        draft.Date == existing.Date &&
        Same(draft.AircraftType, existing.AircraftType) &&
        Same(draft.Registration, existing.Registration) &&
        Same(draft.Details, existing.Details);

    private static bool HasRegistrationWithDifferentType(PortableLogbookEntry draft, PortableLogbookEntry existing) =>
        !string.IsNullOrWhiteSpace(draft.AircraftType) &&
        !string.IsNullOrWhiteSpace(draft.Registration) &&
        !string.IsNullOrWhiteSpace(existing.AircraftType) &&
        Same(draft.Registration, existing.Registration) &&
        !Same(draft.AircraftType, existing.AircraftType);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
