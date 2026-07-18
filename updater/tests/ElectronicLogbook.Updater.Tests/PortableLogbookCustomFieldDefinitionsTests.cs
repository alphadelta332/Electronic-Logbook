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
}
