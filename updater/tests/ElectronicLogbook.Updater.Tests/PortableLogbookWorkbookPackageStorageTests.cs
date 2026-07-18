using System.IO.Compression;
using System.Xml.Linq;
using ElectronicLogbook.Portable;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookPackageStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PortableLogbookWorkbookPackageStorageTests-{Guid.NewGuid():N}");

    public PortableLogbookWorkbookPackageStorageTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void ReadEnvelopeReturnsNullWhenWorkbookHasNoPortableStoragePart()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var envelope = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook);

        Assert.Null(envelope);
    }

    [Fact]
    public void WriteEnvelopeStoresAndReplacesPortableStoragePart()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        var firstEnvelope = CreateEnvelope("log_storage_1", key);
        var secondEnvelope = CreateEnvelope("log_storage_2", key);

        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, firstEnvelope);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, secondEnvelope);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook);

        Assert.NotNull(read);
        Assert.Equal(secondEnvelope.LogbookId, read.LogbookId);
        Assert.Equal(secondEnvelope.Summary, read.Summary);
        using var archive = ZipFile.OpenRead(workbook);
        Assert.NotNull(archive.GetEntry(PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath));
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.Single(archive.Entries, entry => entry.FullName == PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
    }

    [Fact]
    public void WriteEnvelopeRegistersCustomXmlContentTypeAndRelationship()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var envelope = CreateEnvelope("log_storage", PortableLogbookKey.Generate());

        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        using var archive = ZipFile.OpenRead(workbook);
        var contentTypes = ReadXml(archive, "[Content_Types].xml");
        var relationships = ReadXml(archive, "_rels/.rels");
        Assert.Contains(
            contentTypes.Root!.Elements().Where(element => element.Name.LocalName == "Override"),
            element => (string?)element.Attribute("PartName") == "/" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        Assert.Contains(
            relationships.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element =>
                (string?)element.Attribute("Id") == "rIdPortableLogbookStorage" &&
                (string?)element.Attribute("Target") == PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
    }

    [Fact]
    public void CopyEnvelopeCopiesPortableStorageBetweenWorkbookPackages()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var destination = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "destination.xlsm");
        var envelope = CreateEnvelope("log_copy", PortableLogbookKey.Generate());
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(source, envelope);

        var copied = PortableLogbookWorkbookPackageStorage.CopyEnvelope(source, destination);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(destination);

        Assert.True(copied);
        Assert.NotNull(read);
        Assert.Equal(envelope.LogbookId, read.LogbookId);
        Assert.Equal(envelope.Summary, read.Summary);
    }

    [Fact]
    public void CopyEnvelopeLeavesDestinationUntouchedWhenSourceHasNoPortableStorage()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var destination = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "destination.xlsm");

        var copied = PortableLogbookWorkbookPackageStorage.CopyEnvelope(source, destination);

        Assert.False(copied);
        Assert.Null(PortableLogbookWorkbookPackageStorage.ReadEnvelope(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PortableLogbookWorkbookStorageEnvelope CreateEnvelope(string logbookId, PortableLogbookKey key)
    {
        var create = new CreateEntryOperation(
            new LogbookId(logbookId),
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
        return PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []);
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} was not found.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
