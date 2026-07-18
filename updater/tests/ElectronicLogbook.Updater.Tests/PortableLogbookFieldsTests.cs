using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookFieldsTests
{
    [Fact]
    public void RawFlightFieldsCoverWorkbookDateThroughCirclingInOrder()
    {
        var workbookColumns = PortableLogbookFields.RawFlightFields
            .Select(field => field.WorkbookColumnName)
            .ToArray();

        Assert.Equal(
            [
                "Date",
                "Aircraft Type",
                "Reg",
                "Flight Number",
                "From",
                "To",
                "Route",
                "Details",
                "Multi-Pilot",
                "PIC",
                "Co-Pilot",
                "Dual",
                "Instructor",
                "Day",
                "Night",
                "Instrument Actual",
                "Instrument Simulated",
                "Takeoffs Day",
                "Takeoffs Night",
                "Landings Day",
                "Landings Night",
                "IFR Approaches",
                "Holding",
                "RNAV",
                "Circling"
            ],
            workbookColumns);
    }

    [Fact]
    public void RawFlightFieldIdsAreStableAndUnique()
    {
        var ids = PortableLogbookFields.RawFlightFields.Select(field => field.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("date", ids[0]);
        Assert.Equal("circling", ids[^1]);
        Assert.Equal(PortableLogbookFields.RawFlightFields.Count, PortableLogbookFields.ById.Count);
    }

    [Fact]
    public void WorkbookColumnMappingsAreUniqueAndTyped()
    {
        var columns = PortableLogbookFields.RawFlightFields.Select(field => field.WorkbookColumnName).ToArray();

        Assert.Equal(columns.Length, columns.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PortableLogbookFieldKind.Date, PortableLogbookFields.ById["date"].Kind);
        Assert.Equal(PortableLogbookFieldKind.DecimalHours, PortableLogbookFields.ById["pilotInCommand"].Kind);
        Assert.Equal(PortableLogbookFieldKind.Count, PortableLogbookFields.ById["circling"].Kind);
    }

    [Fact]
    public void CustomFieldsRemainOutsideRawFlightFieldCatalog()
    {
        Assert.DoesNotContain(PortableLogbookFields.RawFlightFields, field => field.Id.StartsWith("cf_", StringComparison.Ordinal));
        Assert.DoesNotContain(PortableLogbookFields.RawFlightFields, field => field.WorkbookColumnName.Contains("Custom", StringComparison.OrdinalIgnoreCase));
    }
}
