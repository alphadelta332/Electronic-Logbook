using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileCustomFieldSettingsTests
{
    private static readonly CustomFieldDefinition Used =
        new(new CustomFieldId("cf_workbook_1"), "Operation", 1);

    private static readonly CustomFieldDefinition Empty =
        new(new CustomFieldId("cf_workbook_2"), "Training", 2);

    [Fact]
    public void SummarizeIncludesOnlyActiveFieldsAndUsesWorkbookNumericTotals()
    {
        var removed = new CustomFieldDefinition(
            new CustomFieldId("cf_removed"),
            "Removed",
            3,
            IsInactive: true);
        var entries = new[]
        {
            Entry((Used.Id, "1.25"), (Empty.Id, "not numeric"), (removed.Id, "9")),
            Entry((Used.Id, "0.75"))
        };

        var totals = MobileCustomFieldSettings.Summarize([removed, Empty, Used], entries);

        Assert.Equal(["Operation", "Training"], totals.Select(total => total.Definition.Label));
        Assert.Equal([2m, 0m], totals.Select(total => total.Total));
    }

    [Fact]
    public void RenameAndRemoveRejectAFieldWhoseTotalIsNotZero()
    {
        var entries = new[] { Entry((Used.Id, "1.5")) };

        var renameError = Assert.Throws<InvalidOperationException>(() =>
            MobileCustomFieldSettings.Rename([Used, Empty], entries, Used.Id, "Duty"));
        var removeError = Assert.Throws<InvalidOperationException>(() =>
            MobileCustomFieldSettings.Remove([Used, Empty], entries, Used.Id));

        Assert.Contains("total is not zero", renameError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("total is not zero", removeError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenameTrimsAndRemoveDeactivatesWithoutDeletingHistoricalDefinition()
    {
        var renamed = MobileCustomFieldSettings.Rename([Used, Empty], [], Empty.Id, "  Exercise  ");
        var removed = MobileCustomFieldSettings.Remove(renamed, [], Empty.Id);

        Assert.Equal("Exercise", renamed.Single(field => field.Id == Empty.Id).Label);
        var retained = removed.Single(field => field.Id == Empty.Id);
        Assert.Equal("Exercise", retained.Label);
        Assert.False(retained.IsActive);
    }

    [Fact]
    public void AddUsesAnAvailableOrderAndANewStableIdentity()
    {
        var removed = Empty with { IsInactive = true };
        var newId = new CustomFieldId("cf_new_stable");

        var updated = MobileCustomFieldSettings.Add([Used, removed], "Night vision", newId);

        var added = updated.Single(field => field.Id == newId);
        Assert.Equal(2, added.Order);
        Assert.Equal("Night vision", added.Label);
        Assert.True(added.IsActive);
        Assert.Contains(updated, field => field.Id == removed.Id && !field.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Operation")]
    public void AddRejectsBlankOrDuplicateLabels(string label)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MobileCustomFieldSettings.Add([Used], label, new CustomFieldId("cf_new")));
    }

    [Fact]
    public void AddRejectsMoreThanFourActiveFields()
    {
        var fields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["One", "Two", "Three", "Four"]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            MobileCustomFieldSettings.Add(fields, "Five"));

        Assert.Contains("maximum of 4", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PortableLogbookWorkbookEntry Entry(
        params (CustomFieldId Id, string? Value)[] fields) =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 9,
            Day = 10,
            CustomFields = fields.ToDictionary(field => field.Id, field => field.Value)
        };
}
