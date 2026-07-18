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

        var copied = ExcelWorkbookMigrator.CopyPortableWorkbookStorage(source, output);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(output);

        Assert.True(copied);
        Assert.NotNull(read);
        Assert.Equal(envelope.LogbookId, read.LogbookId);
        Assert.Equal(envelope.Summary, read.Summary);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
