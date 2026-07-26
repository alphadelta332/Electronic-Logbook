using System.Reflection;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookMigratorPreservationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PortableLogbookMigratorPreservationTests-{Guid.NewGuid():N}");

    public PortableLogbookMigratorPreservationTests()
    {
        Directory.CreateDirectory(directory);
    }

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

    [Fact]
    public void MigratorOnlyPlansMetadataColumnPreservationForPortableSourceTables()
    {
        Assert.False(ExcelWorkbookMigrator.ShouldPreservePortableMetadataColumns(["Year", "Reg", "Circling"]));
        Assert.True(ExcelWorkbookMigrator.ShouldPreservePortableMetadataColumns([
            "Year",
            "Reg",
            "EntryID",
            "Circling"
        ]));
        Assert.True(ExcelWorkbookMigrator.ShouldPreservePortableMetadataColumns([
            "Year",
            "Reg",
            "Portable Entry ID",
            "Circling"
        ]));
    }

    [Fact]
    public void MigratorPlansMissingPortableMetadataColumnsForDestinationTable()
    {
        var plan = ExcelWorkbookMigrator.CreatePortableMetadataMigrationPlan(["Year", "Reg", "Circling"]);

        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToAdd.Select(column => column.WorkbookColumnName));
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToHide);
    }

    [Fact]
    public void MigratorDoesNotDuplicateExistingPortableMetadataColumns()
    {
        var plan = ExcelWorkbookMigrator.CreatePortableMetadataMigrationPlan([
            "Year",
            "EntryID",
            "Reg",
            "Portable Current Revision ID",
            "Circling"
        ]);

        Assert.False(plan.RequiresMutation);
        Assert.Empty(plan.ColumnsToAdd);
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToHide);
    }

    [Fact]
    public void MigratorPlansPortableMetadataCopyOnlyWhenSourceContainsStableIds()
    {
        var plan = ExcelWorkbookMigrator.CreatePortableMetadataMigrationPlan(
            [
                "Year",
                "EntryID",
                "Reg",
                "Portable Current Revision ID",
                "Circling"
            ],
            ["Year", "Reg", "Circling"]);

        Assert.True(plan.ShouldPreserve);
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnPlan.ColumnsToAdd.Select(column => column.WorkbookColumnName));
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToCopy);
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToHide);
    }

    [Fact]
    public void MigratorCopiesSoleLegacyPortableEntryIdIntoCanonicalEntryId()
    {
        var plan = ExcelWorkbookMigrator.CreatePortableMetadataMigrationPlan(
            [
                "Year",
                "Portable Entry ID",
                "Reg",
                "Portable Current Revision ID",
                "Circling"
            ],
            ["Year", "Reg", "Circling"]);

        Assert.True(plan.ShouldPreserve);
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnPlan.ColumnsToAdd.Select(column => column.WorkbookColumnName));
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToCopy);
        Assert.Equal(
            ["EntryID", "Portable Current Revision ID"],
            plan.ColumnsToHide);
    }

    [Fact]
    public void MigratorRejectsDisagreeingCanonicalAndLegacyEntryIds()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookMigrator.ValidatePortableEntryIdMigrationValues(
                ["ent_existing"],
                ["ent_legacy"]));
        var blankConflict = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookMigrator.ValidatePortableEntryIdMigrationValues(
                [""],
                ["ent_legacy"]));
        var bothBlankConflict = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookMigrator.ValidatePortableEntryIdMigrationValues(
                [""],
                [""]));

        Assert.Equal(
            "Portable Entry ID migration conflict at Logbook row 1: EntryID 'ent_existing' does not match Portable Entry ID 'ent_legacy'.",
            exception.Message);
        Assert.Equal(
            "Portable Entry ID migration conflict at Logbook row 1: EntryID '' does not match Portable Entry ID 'ent_legacy'.",
            blankConflict.Message);
        Assert.Equal(
            "Portable Entry ID migration conflict at Logbook row 1: EntryID and Portable Entry ID are both blank.",
            bothBlankConflict.Message);
    }

    [Fact]
    public void MigratorAllocatesUniqueEntryIdsForExplicitLegacyEnrollment()
    {
        var issued = 0;
        var factory = new PortableLogbookIdFactory(
            () => new EntryId($"ent_enrolled_{++issued}"),
            RevisionId.New);

        var values = ExcelWorkbookMigrator.CreateEntryIdEnrollmentValues(3, factory);

        Assert.Equal(["ent_enrolled_1", "ent_enrolled_2", "ent_enrolled_3"], values);
        Assert.Equal(3, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MigratorRejectsEmptyLegacyEnrollment()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExcelWorkbookMigrator.CreateEntryIdEnrollmentValues(0));

        Assert.Equal("rows", exception.ParamName);
    }

    [Fact]
    public void MigratorSkipsPortableMetadataCopyForLegacySourceTables()
    {
        var plan = ExcelWorkbookMigrator.CreatePortableMetadataMigrationPlan(
            ["Year", "Reg", "Circling"],
            [
                "Year",
                "Reg",
                "EntryID",
                "Portable Current Revision ID",
                "Circling"
            ]);

        Assert.False(plan.ShouldPreserve);
        Assert.Empty(plan.ColumnsToCopy);
        Assert.Empty(plan.ColumnsToHide);
        Assert.Empty(plan.ColumnPlan.ColumnsToAdd);
    }

    [Fact]
    public void MigratorCopiesPortableWorkbookStorageBetweenClosedPackages()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var output = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "output.xlsm");
        var key = PortableLogbookKey.Generate();
        var create = new CreateEntryOperation(
            new LogbookId("log_migrator_storage"),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
        var envelope = PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(source, envelope);
        PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            source,
            create.LogbookId,
            create.DeviceId,
            document.SchemaVersion);

        var copied = ExcelWorkbookMigrator.CopyPortableWorkbookStorage(source, output);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(output);
        var identity = PortableLogbookWorkbookPackageStorage.ReadWorkbookIdentityMetadata(output);

        Assert.True(copied);
        Assert.NotNull(read);
        Assert.Equal(envelope.LogbookId, read.LogbookId);
        Assert.Equal(envelope.Summary, read.Summary);
        Assert.NotNull(identity);
        Assert.Equal(create.LogbookId, identity.LogbookId);
        Assert.Equal(create.DeviceId, identity.DeviceId);
        Assert.Equal(document.SchemaVersion, identity.SchemaVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
