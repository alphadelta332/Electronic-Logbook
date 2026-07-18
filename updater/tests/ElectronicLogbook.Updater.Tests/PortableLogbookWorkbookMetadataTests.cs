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
}
