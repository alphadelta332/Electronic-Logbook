namespace ElectronicLogbook.Portable;

public static class PortableLogbookConflictResolution
{
    public static ResolveConflictOperation CreateResolution(
        PortableLogbookConflict conflict,
        LogbookId logbookId,
        DeviceId deviceId,
        RevisionId revisionId,
        DateTimeOffset createdAt,
        PortableLogbookEntry resolvedEntry,
        string? resolutionNote = null)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(resolvedEntry);
        if (conflict.HeadRevisionIds.Count < 2)
        {
            throw new ArgumentException("Conflict resolution requires at least two branch heads.", nameof(conflict));
        }

        return new ResolveConflictOperation(
            logbookId,
            conflict.EntryId,
            revisionId,
            conflict.HeadRevisionIds.ToHashSet(),
            deviceId,
            createdAt,
            resolvedEntry,
            resolutionNote);
    }
}
