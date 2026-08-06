using System.Text.Json;

namespace ElectronicLogbook.Portable;

public sealed record PortableLogbookDocument(
    int SchemaVersion,
    LogbookId LogbookId,
    string JurisdictionProfile,
    int JurisdictionProfileVersion,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    IReadOnlyList<PortableLogbookOperation> Operations)
{
    public const int CurrentSchemaVersion = 1;
    public const string AustraliaJurisdictionProfile = "AU";
    public const int AustraliaJurisdictionProfileVersion = 1;

    public static PortableLogbookDocument CreateAustraliaFirst(
        LogbookId logbookId,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        IEnumerable<PortableLogbookOperation> operations) =>
        new(
            CurrentSchemaVersion,
            logbookId,
            AustraliaJurisdictionProfile,
            AustraliaJurisdictionProfileVersion,
            customFieldDefinitions.OrderBy(field => field.Order).ToArray(),
            operations.OrderBy(operation => operation.CreatedAt).ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal).ToArray());
}

public static class PortableLogbookJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new LogbookIdJsonConverter(),
            new EntryIdJsonConverter(),
            new RevisionIdJsonConverter(),
            new DeviceIdJsonConverter(),
            new CustomFieldIdJsonConverter(),
            new PortableLogbookOperationJsonConverter()
        }
    };

    public static string Serialize(PortableLogbookDocument document) =>
        JsonSerializer.Serialize(document, SerializerOptions);

    public static PortableLogbookDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize<PortableLogbookDocument>(json, SerializerOptions);

    public static string SerializeV2(PortableLogbookDocumentV2 document) =>
        JsonSerializer.Serialize(document, SerializerOptions);

    public static PortableLogbookDocumentV2? DeserializeV2(string json) =>
        JsonSerializer.Deserialize<PortableLogbookDocumentV2>(json, SerializerOptions);

    public static string SerializeOperationV2(PortableLogbookOperationV2 operation) =>
        JsonSerializer.Serialize(operation, SerializerOptions);

    public static PortableLogbookOperationV2? DeserializeOperationV2(string json) =>
        JsonSerializer.Deserialize<PortableLogbookOperationV2>(json, SerializerOptions);
}
