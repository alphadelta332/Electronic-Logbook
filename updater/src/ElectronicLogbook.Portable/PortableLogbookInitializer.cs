namespace ElectronicLogbook.Portable;

public static class PortableLogbookInitializer
{
    public static PortableLogbookDocument CreateInitialDocument(
        IEnumerable<PortableLogbookEntry> entries,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        LogbookId logbookId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookIdFactory? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);

        idFactory ??= PortableLogbookIdFactory.Default;
        var operations = entries.Select(entry => new CreateEntryOperation(
            logbookId,
            idFactory.NewEntryId(),
            idFactory.NewRevisionId(),
            deviceId,
            createdAt,
            entry));

        return PortableLogbookDocument.CreateAustraliaFirst(logbookId, customFieldDefinitions, operations);
    }
}

public sealed class PortableLogbookIdFactory
{
    public static PortableLogbookIdFactory Default { get; } = new();

    private readonly Func<EntryId> newEntryId;
    private readonly Func<RevisionId> newRevisionId;

    public PortableLogbookIdFactory()
        : this(EntryId.New, RevisionId.New)
    {
    }

    public PortableLogbookIdFactory(Func<EntryId> newEntryId, Func<RevisionId> newRevisionId)
    {
        this.newEntryId = newEntryId;
        this.newRevisionId = newRevisionId;
    }

    public EntryId NewEntryId() => newEntryId();

    public RevisionId NewRevisionId() => newRevisionId();
}
