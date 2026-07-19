using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookCustomFieldDefinitionsTests
{
    [Fact]
    public void MergeAddsIncomingDefinitionsInStableOrder()
    {
        var local = new CustomFieldDefinition(new CustomFieldId("cf_local"), "Local", 2);
        var incoming = new CustomFieldDefinition(new CustomFieldId("cf_incoming"), "Incoming", 1);

        var result = PortableLogbookCustomFieldDefinitions.Merge([local], [incoming]);

        Assert.False(result.HasConflicts);
        Assert.Equal(["cf_incoming", "cf_local"], result.Definitions.Select(definition => definition.Id.Value));
    }

    [Fact]
    public void MergeReportsConflictingDefinitionsWithSameId()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var incoming = new CustomFieldDefinition(fieldId, "Training category", 1);

        var result = PortableLogbookCustomFieldDefinitions.Merge([local], [incoming]);

        Assert.True(result.HasConflicts);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(fieldId, conflict.Id);
        Assert.Equal(local, conflict.LocalDefinition);
        Assert.Equal(incoming, conflict.IncomingDefinition);
    }

    [Fact]
    public void ResolveKeepsLocalDefinitionWhenSelected()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var incoming = new CustomFieldDefinition(fieldId, "Training category", 1);
        var merge = PortableLogbookCustomFieldDefinitions.Merge([local], [incoming]);

        var resolved = PortableLogbookCustomFieldDefinitions.Resolve(
            merge,
            [new PortableLogbookCustomFieldDefinitionResolution(fieldId, PortableLogbookCustomFieldDefinitionChoice.KeepLocal)]);

        Assert.Equal(local, Assert.Single(resolved));
    }

    [Fact]
    public void ResolveUsesIncomingDefinitionWhenSelected()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var incoming = new CustomFieldDefinition(fieldId, "Training category", 1);
        var merge = PortableLogbookCustomFieldDefinitions.Merge([local], [incoming]);

        var resolved = PortableLogbookCustomFieldDefinitions.Resolve(
            merge,
            [new PortableLogbookCustomFieldDefinitionResolution(fieldId, PortableLogbookCustomFieldDefinitionChoice.UseIncoming)]);

        Assert.Equal(incoming, Assert.Single(resolved));
    }

    [Fact]
    public void ResolveRequiresEveryConflictToHaveExplicitChoice()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var local = new CustomFieldDefinition(fieldId, "Training kind", 1);
        var incoming = new CustomFieldDefinition(fieldId, "Training category", 1);
        var merge = PortableLogbookCustomFieldDefinitions.Merge([local], [incoming]);

        var error = Assert.Throws<ArgumentException>(() => PortableLogbookCustomFieldDefinitions.Resolve(merge, []));

        Assert.Contains(fieldId.Value, error.Message, StringComparison.Ordinal);
    }
}
