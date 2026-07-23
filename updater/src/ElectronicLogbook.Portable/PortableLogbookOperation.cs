namespace ElectronicLogbook.Portable;

public abstract record PortableLogbookOperation(
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    IReadOnlySet<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt)
{
    public abstract PortableOperationKind Kind { get; }
}

public enum PortableOperationKind
{
    Create,
    Correction,
    Deletion,
    ConflictResolution
}

public sealed record CreateEntryOperation(
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    PortableLogbookEntry Entry)
    : PortableLogbookOperation(LogbookId, EntryId, RevisionId, new HashSet<RevisionId>(), DeviceId, CreatedAt)
{
    public override PortableOperationKind Kind => PortableOperationKind.Create;
}

public sealed record CorrectEntryOperation(
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    IReadOnlySet<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    PortableLogbookEntry Entry)
    : PortableLogbookOperation(LogbookId, EntryId, RevisionId, ParentRevisionIds, DeviceId, CreatedAt)
{
    public override PortableOperationKind Kind => PortableOperationKind.Correction;
}

public sealed record DeleteEntryOperation(
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    IReadOnlySet<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    string? Reason = null)
    : PortableLogbookOperation(LogbookId, EntryId, RevisionId, ParentRevisionIds, DeviceId, CreatedAt)
{
    public override PortableOperationKind Kind => PortableOperationKind.Deletion;
}

public sealed record ResolveConflictOperation(
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    IReadOnlySet<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    PortableLogbookEntry Entry,
    string? ResolutionNote = null)
    : PortableLogbookOperation(LogbookId, EntryId, RevisionId, ParentRevisionIds, DeviceId, CreatedAt)
{
    public override PortableOperationKind Kind => PortableOperationKind.ConflictResolution;
}
