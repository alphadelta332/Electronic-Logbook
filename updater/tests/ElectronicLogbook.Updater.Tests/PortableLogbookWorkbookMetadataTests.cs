using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookMetadataTests
{
    [Fact]
    public void HiddenMetadataColumnsAreStableAndSeparateFromRawFlightFields()
    {
        var metadataColumns = PortableLogbookWorkbookMetadata.HiddenLogbookColumns;
        var rawWorkbookColumnNames = PortableLogbookFields.RawFlightFields
            .Select(field => field.WorkbookColumnName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(["Portable Entry ID", "Portable Current Revision ID"], metadataColumns.Select(column => column.WorkbookColumnName));
        Assert.All(metadataColumns, column => Assert.DoesNotContain(column.WorkbookColumnName, rawWorkbookColumnNames));
    }

    [Fact]
    public void WorkbookPackageStorageNamesAreVersioned()
    {
        Assert.Equal("PortableLogbookId", PortableLogbookWorkbookMetadata.LogbookIdName);
        Assert.Equal("PortableLogbookDeviceId", PortableLogbookWorkbookMetadata.DeviceIdName);
        Assert.Equal("PortableLogbookSchemaVersion", PortableLogbookWorkbookMetadata.SchemaVersionName);
        Assert.Equal("portable-logbook-history.elogbook", PortableLogbookWorkbookMetadata.OperationHistoryPartName);
        Assert.Equal("portable-logbook-import-ledger.json", PortableLogbookWorkbookMetadata.ImportLedgerPartName);
        Assert.Equal("urn:electronic-logbook:portable:v1", PortableLogbookWorkbookMetadata.CustomXmlNamespace);
    }

    [Fact]
    public void FilterUserExportColumnsOmitsPortableMetadataColumns()
    {
        var columns = new[]
        {
            "Date",
            "Reg",
            "Portable Entry ID",
            "Portable Current Revision ID",
            "Circling"
        };

        var filtered = PortableLogbookWorkbookMetadata.FilterUserExportColumns(columns);

        Assert.Equal(["Date", "Reg", "Circling"], filtered);
        Assert.True(PortableLogbookWorkbookMetadata.IsPortableMetadataColumn("portable entry id"));
    }

    [Fact]
    public void FilterUserExportColumnsOmitsPortableMetadataColumnsWithHeaderWhitespace()
    {
        var columns = new[] { "Date", " Portable Entry ID ", "Reg" };

        var filtered = PortableLogbookWorkbookMetadata.FilterUserExportColumns(columns);

        Assert.Equal(["Date", "Reg"], filtered);
        Assert.True(PortableLogbookWorkbookMetadata.IsPortableMetadataColumn(" Portable Current Revision ID "));
    }

    [Fact]
    public void CreateHiddenColumnPlanAppendsMissingMetadataColumns()
    {
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(
            ["Date", "Reg", "Circling"]);

        Assert.True(plan.RequiresMutation);
        Assert.Equal(
            ["Date", "Reg", "Circling", "Portable Entry ID", "Portable Current Revision ID"],
            plan.WorkbookColumnNames);
        Assert.Equal(
            ["Portable Entry ID", "Portable Current Revision ID"],
            plan.ColumnsToAdd.Select(column => column.WorkbookColumnName));
        Assert.Equal(
            ["Portable Entry ID", "Portable Current Revision ID"],
            plan.ColumnsToHide);
    }

    [Fact]
    public void CreateHiddenColumnPlanDoesNotDuplicateExistingMetadataColumns()
    {
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(
            ["Date", "Portable Entry ID", "Reg", "portable current revision id"]);

        Assert.False(plan.RequiresMutation);
        Assert.Equal(["Date", "Portable Entry ID", "Reg", "portable current revision id"], plan.WorkbookColumnNames);
        Assert.Empty(plan.ColumnsToAdd);
        Assert.Equal(["Portable Entry ID", "Portable Current Revision ID"], plan.ColumnsToHide);
    }

    [Fact]
    public void CreateHiddenColumnPlanDoesNotDuplicateWhitespacePaddedExistingMetadataColumns()
    {
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(
            ["Date", " Portable Entry ID ", "Reg", " Portable Current Revision ID "]);

        Assert.False(plan.RequiresMutation);
        Assert.Equal(["Date", " Portable Entry ID ", "Reg", " Portable Current Revision ID "], plan.WorkbookColumnNames);
        Assert.Empty(plan.ColumnsToAdd);
    }

    [Fact]
    public void CreateHiddenColumnPlanAddsOnlyMissingMetadataColumns()
    {
        var plan = PortableLogbookWorkbookMetadata.CreateHiddenColumnPlan(
            ["Date", "Reg", "Portable Entry ID"]);

        Assert.True(plan.RequiresMutation);
        Assert.Equal(
            ["Date", "Reg", "Portable Entry ID", "Portable Current Revision ID"],
            plan.WorkbookColumnNames);
        var column = Assert.Single(plan.ColumnsToAdd);
        Assert.Equal("Portable Current Revision ID", column.WorkbookColumnName);
    }
}
