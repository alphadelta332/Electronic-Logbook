using System.Reflection;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookMigratorPreservationTests
{
    [Fact]
    public void MigratorPreservesPortableWorkbookNamesWhenPresent()
    {
        var field = typeof(ExcelWorkbookMigrator).GetField("PreservedNames", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("PreservedNames field not found.");
        var names = Assert.IsType<string[]>(field.GetValue(null));

        Assert.Contains(PortableLogbookWorkbookMetadata.LogbookIdName, names);
        Assert.Contains(PortableLogbookWorkbookMetadata.DeviceIdName, names);
        Assert.Contains(PortableLogbookWorkbookMetadata.SchemaVersionName, names);
    }
}
