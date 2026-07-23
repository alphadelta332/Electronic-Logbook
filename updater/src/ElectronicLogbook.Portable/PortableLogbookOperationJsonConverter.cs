using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElectronicLogbook.Portable;

internal sealed class PortableLogbookOperationJsonConverter : JsonConverter<PortableLogbookOperation>
{
    public override PortableLogbookOperation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = RequiredString(root, "kind");
        var logbookId = Required<LogbookId>(root, "logbookId", options);
        var entryId = Required<EntryId>(root, "entryId", options);
        var revisionId = Required<RevisionId>(root, "revisionId", options);
        var deviceId = Required<DeviceId>(root, "deviceId", options);
        var createdAt = Required<DateTimeOffset>(root, "createdAt", options);
        var parentRevisionIds = root.TryGetProperty("parentRevisionIds", out var parents)
            ? Required<HashSet<RevisionId>>(parents, options)
            : [];

        return kind switch
        {
            "create" => new CreateEntryOperation(
                logbookId,
                entryId,
                revisionId,
                deviceId,
                createdAt,
                Required<PortableLogbookEntry>(root, "entry", options)),
            "correction" => new CorrectEntryOperation(
                logbookId,
                entryId,
                revisionId,
                parentRevisionIds,
                deviceId,
                createdAt,
                Required<PortableLogbookEntry>(root, "entry", options)),
            "deletion" => new DeleteEntryOperation(
                logbookId,
                entryId,
                revisionId,
                parentRevisionIds,
                deviceId,
                createdAt,
                root.TryGetProperty("reason", out var reason) ? reason.GetString() : null),
            "conflictResolution" => new ResolveConflictOperation(
                logbookId,
                entryId,
                revisionId,
                parentRevisionIds,
                deviceId,
                createdAt,
                Required<PortableLogbookEntry>(root, "entry", options),
                root.TryGetProperty("resolutionNote", out var note) ? note.GetString() : null),
            _ => throw new JsonException($"Unsupported portable operation kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, PortableLogbookOperation value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", ToDiscriminator(value.Kind));
        WriteCommon(writer, value, options);

        switch (value)
        {
            case CreateEntryOperation create:
                WriteProperty(writer, "entry", create.Entry, options);
                break;
            case CorrectEntryOperation correction:
                WriteProperty(writer, "entry", correction.Entry, options);
                break;
            case DeleteEntryOperation deletion:
                if (deletion.Reason is not null)
                {
                    writer.WriteString("reason", deletion.Reason);
                }

                break;
            case ResolveConflictOperation resolution:
                WriteProperty(writer, "entry", resolution.Entry, options);
                if (resolution.ResolutionNote is not null)
                {
                    writer.WriteString("resolutionNote", resolution.ResolutionNote);
                }

                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteCommon(Utf8JsonWriter writer, PortableLogbookOperation value, JsonSerializerOptions options)
    {
        WriteProperty(writer, "logbookId", value.LogbookId, options);
        WriteProperty(writer, "entryId", value.EntryId, options);
        WriteProperty(writer, "revisionId", value.RevisionId, options);
        WriteProperty(writer, "parentRevisionIds", value.ParentRevisionIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(), options);
        WriteProperty(writer, "deviceId", value.DeviceId, options);
        writer.WriteString("createdAt", value.CreatedAt);
    }

    private static string ToDiscriminator(PortableOperationKind kind) =>
        kind switch
        {
            PortableOperationKind.Create => "create",
            PortableOperationKind.Correction => "correction",
            PortableOperationKind.Deletion => "deletion",
            PortableOperationKind.ConflictResolution => "conflictResolution",
            _ => throw new JsonException($"Unsupported portable operation kind '{kind}'.")
        };

    private static string RequiredString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? throw new JsonException($"Property '{propertyName}' cannot be null.")
            : throw new JsonException($"Missing property '{propertyName}'.");

    private static T Required<T>(JsonElement root, string propertyName, JsonSerializerOptions options) =>
        root.TryGetProperty(propertyName, out var property)
            ? Required<T>(property, options)
            : throw new JsonException($"Missing property '{propertyName}'.");

    private static T Required<T>(JsonElement property, JsonSerializerOptions options) =>
        property.Deserialize<T>(options) ?? throw new JsonException($"Value cannot be converted to {typeof(T).Name}.");

    private static void WriteProperty<T>(Utf8JsonWriter writer, string propertyName, T value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, value, options);
    }
}
