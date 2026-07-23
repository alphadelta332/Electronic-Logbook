using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookCustomFieldSetTests
{
    [Fact]
    public void CreateWorkbookCustomFieldsAssignsStableIdsIndependentOfLabels()
    {
        var first = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["OPC", "Role", "Notes", "Client"]);
        var renamed = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Other Pilot", "Task", "Remark", "Customer"]);

        Assert.Equal(["cf_workbook_1", "cf_workbook_2", "cf_workbook_3", "cf_workbook_4"], first.Select(field => field.Id.Value));
        Assert.Equal(first.Select(field => field.Id), renamed.Select(field => field.Id));
        Assert.Equal(["OPC", "Role", "Notes", "Client"], first.Select(field => field.Label));
        Assert.Equal([1, 2, 3, 4], first.Select(field => field.Order));
    }

    [Fact]
    public void CreateWorkbookCustomFieldsRejectsTooFewFields()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["Only one"]));

        Assert.Equal("labels", exception.ParamName);
    }

    [Fact]
    public void CreateWorkbookCustomFieldsRejectsTooManyFields()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["One", "Two", "Three", "Four", "Five"]));

        Assert.Equal("labels", exception.ParamName);
    }

    [Fact]
    public void CreateWorkbookCustomFieldsRejectsBlankLabels()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(["One", "Two", " ", "Four"]));

        Assert.Equal("labels", exception.ParamName);
    }
}
