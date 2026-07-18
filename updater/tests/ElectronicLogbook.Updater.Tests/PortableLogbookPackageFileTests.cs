using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageFileTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "elogbook-package-file-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void WriteAndReadRoundTripElogbookFile()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var path = Path.Combine(tempDirectory, "export.elogbook");

        PortableLogbookPackageFile.Write(path, document, key);
        var result = PortableLogbookPackageFile.Read(path, key, document.LogbookId);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(document.LogbookId, result.Document.LogbookId);
    }

    [Fact]
    public void ReadManifestReturnsFilePackageMetadataWithoutKey()
    {
        var document = CreateDocument();
        var path = Path.Combine(tempDirectory, "export.elogbook");

        PortableLogbookPackageFile.Write(path, document, PortableLogbookKey.Generate());
        var manifest = PortableLogbookPackageFile.ReadManifest(path);

        Assert.Equal(document.LogbookId, manifest.LogbookId);
        Assert.Equal(document.Operations.Count, manifest.OperationCount);
    }

    [Theory]
    [InlineData("export.zip")]
    [InlineData("export")]
    public void WriteRejectsNonElogbookExtension(string fileName)
    {
        var path = Path.Combine(tempDirectory, fileName);

        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookPackageFile.Write(path, CreateDocument(), PortableLogbookKey.Generate()));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.Read(
                path,
                PortableLogbookKey.Generate(),
                expectedLogbookId: null,
                new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsNonElogbookExtension()
    {
        var path = Path.Combine(tempDirectory, "export.zip");

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadPropagatesMissingPackageFileAsFileNotFound()
    {
        var path = Path.Combine(tempDirectory, "missing.elogbook");

        Assert.Throws<FileNotFoundException>(() => PortableLogbookPackageFile.Read(path, PortableLogbookKey.Generate()));
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var create = new CreateEntryOperation(
            new LogbookId("log_file"),
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

        return PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
    }
}
