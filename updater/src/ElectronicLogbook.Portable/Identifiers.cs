namespace ElectronicLogbook.Portable;

public readonly record struct LogbookId(string Value)
{
    public static LogbookId New() => new($"log_{Guid.NewGuid():N}");

    public override string ToString() => Value;
}

public readonly record struct EntryId(string Value)
{
    public static EntryId New() => new($"ent_{Guid.NewGuid():N}");

    public static bool IsValid(EntryId entryId) =>
        !string.IsNullOrWhiteSpace(entryId.Value) &&
        entryId.Value.StartsWith("ent_", StringComparison.Ordinal) &&
        entryId.Value.Length > "ent_".Length &&
        entryId.Value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public override string ToString() => Value;
}

public readonly record struct RevisionId(string Value)
{
    public static RevisionId New() => new($"rev_{Guid.NewGuid():N}");

    public override string ToString() => Value;
}

public readonly record struct DeviceId(string Value)
{
    public static DeviceId New() => new($"dev_{Guid.NewGuid():N}");

    public override string ToString() => Value;
}

public readonly record struct CustomFieldId(string Value)
{
    public static CustomFieldId New() => new($"cf_{Guid.NewGuid():N}");

    public override string ToString() => Value;
}
