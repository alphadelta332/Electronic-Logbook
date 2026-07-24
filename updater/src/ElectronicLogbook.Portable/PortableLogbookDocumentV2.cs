namespace ElectronicLogbook.Portable;

public sealed record PortableLogbookDocumentV2(
    int SchemaVersion,
    LogbookId LogbookId,
    string JurisdictionProfile,
    int JurisdictionProfileVersion,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    PortableLogbookCurrencyOverrideDates CurrencyOverrideDates,
    IReadOnlyList<PortableLogbookOperationV2> Operations)
{
    public const int CurrentSchemaVersion = PortableLogbookWorkbookFieldCatalog.SchemaVersion;
    public const string AustraliaJurisdictionProfile = PortableLogbookDocument.AustraliaJurisdictionProfile;
    public const int AustraliaJurisdictionProfileVersion = PortableLogbookDocument.AustraliaJurisdictionProfileVersion;

    public static PortableLogbookDocumentV2 CreateAustraliaFirst(
        LogbookId logbookId,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        PortableLogbookCurrencyOverrideDates currencyOverrideDates,
        IEnumerable<PortableLogbookOperationV2> operations)
    {
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);
        ArgumentNullException.ThrowIfNull(currencyOverrideDates);
        ArgumentNullException.ThrowIfNull(operations);

        return new PortableLogbookDocumentV2(
            CurrentSchemaVersion,
            logbookId,
            AustraliaJurisdictionProfile,
            AustraliaJurisdictionProfileVersion,
            customFieldDefinitions.OrderBy(field => field.Order).ToArray(),
            currencyOverrideDates,
            operations.OrderBy(operation => operation.CreatedAt).ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal).ToArray());
    }
}

public sealed record PortableLogbookCurrencyOverrideDates(
    DateOnly? FlightReview,
    DateOnly? InstrumentProficiencyCheck,
    DateOnly? OperatorProficiencyCheck)
{
    public static PortableLogbookCurrencyOverrideDates Empty { get; } = new(null, null, null);
}

public sealed record PortableLogbookOperationV2(
    PortableOperationKind Kind,
    LogbookId LogbookId,
    EntryId EntryId,
    RevisionId RevisionId,
    IReadOnlyList<RevisionId> ParentRevisionIds,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    PortableLogbookWorkbookEntry? Entry,
    string? Reason = null,
    string? ResolutionNote = null)
{
    public static PortableLogbookOperationV2 Create(
        LogbookId logbookId,
        EntryId entryId,
        RevisionId revisionId,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookWorkbookEntry entry) =>
        new(
            PortableOperationKind.Create,
            logbookId,
            entryId,
            revisionId,
            Array.Empty<RevisionId>(),
            deviceId,
            createdAt,
            entry);

    public static PortableLogbookOperationV2 Correct(
        LogbookId logbookId,
        EntryId entryId,
        RevisionId revisionId,
        IEnumerable<RevisionId> parentRevisionIds,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookWorkbookEntry entry) =>
        new(
            PortableOperationKind.Correction,
            logbookId,
            entryId,
            revisionId,
            parentRevisionIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
            deviceId,
            createdAt,
            entry);

    public static PortableLogbookOperationV2 Delete(
        LogbookId logbookId,
        EntryId entryId,
        RevisionId revisionId,
        IEnumerable<RevisionId> parentRevisionIds,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        string? reason = null) =>
        new(
            PortableOperationKind.Deletion,
            logbookId,
            entryId,
            revisionId,
            parentRevisionIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
            deviceId,
            createdAt,
            null,
            reason);

    public static PortableLogbookOperationV2 ResolveConflict(
        LogbookId logbookId,
        EntryId entryId,
        RevisionId revisionId,
        IEnumerable<RevisionId> parentRevisionIds,
        DeviceId deviceId,
        DateTimeOffset createdAt,
        PortableLogbookWorkbookEntry entry,
        string? resolutionNote = null) =>
        new(
            PortableOperationKind.ConflictResolution,
            logbookId,
            entryId,
            revisionId,
            parentRevisionIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
            deviceId,
            createdAt,
            entry,
            ResolutionNote: resolutionNote);
}
