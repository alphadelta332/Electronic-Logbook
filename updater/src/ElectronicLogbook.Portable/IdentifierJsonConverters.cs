using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElectronicLogbook.Portable;

internal abstract class StringIdentifierJsonConverter<TIdentifier> : JsonConverter<TIdentifier>
{
    public override TIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Create(reader.GetString() ?? throw new JsonException($"{typeof(TIdentifier).Name} cannot be null."));

    public override void Write(Utf8JsonWriter writer, TIdentifier value, JsonSerializerOptions options) =>
        writer.WriteStringValue(GetValue(value));

    public override TIdentifier ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Create(reader.GetString() ?? throw new JsonException($"{typeof(TIdentifier).Name} property name cannot be null."));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TIdentifier value, JsonSerializerOptions options) =>
        writer.WritePropertyName(GetValue(value));

    protected abstract TIdentifier Create(string value);

    protected abstract string GetValue(TIdentifier value);
}

internal sealed class LogbookIdJsonConverter : StringIdentifierJsonConverter<LogbookId>
{
    protected override LogbookId Create(string value) => new(value);

    protected override string GetValue(LogbookId value) => value.Value;
}

internal sealed class EntryIdJsonConverter : StringIdentifierJsonConverter<EntryId>
{
    protected override EntryId Create(string value) => new(value);

    protected override string GetValue(EntryId value) => value.Value;
}

internal sealed class RevisionIdJsonConverter : StringIdentifierJsonConverter<RevisionId>
{
    protected override RevisionId Create(string value) => new(value);

    protected override string GetValue(RevisionId value) => value.Value;
}

internal sealed class DeviceIdJsonConverter : StringIdentifierJsonConverter<DeviceId>
{
    protected override DeviceId Create(string value) => new(value);

    protected override string GetValue(DeviceId value) => value.Value;
}

internal sealed class CustomFieldIdJsonConverter : StringIdentifierJsonConverter<CustomFieldId>
{
    protected override CustomFieldId Create(string value) => new(value);

    protected override string GetValue(CustomFieldId value) => value.Value;
}
