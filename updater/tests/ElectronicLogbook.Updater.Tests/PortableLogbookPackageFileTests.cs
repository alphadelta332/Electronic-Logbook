using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public void WriteDeletesTempFileWhenFinalMoveFails()
    {
        var path = Path.Combine(tempDirectory, "blocked.elogbook");
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<Exception>(() =>
            PortableLogbookPackageFile.Write(path, CreateDocument(), PortableLogbookKey.Generate()));

        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ReadManifestForInspectionReturnsUnsupportedSchemaMetadataWithoutKey()
    {
        var document = CreateDocument();
        var key = PortableLogbookKey.Generate();
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var manifest = PortableLogbookPackage.ReadManifest(packageBytes) with
        {
            SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1
        };
        var path = Path.Combine(tempDirectory, "future.elogbook");
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllBytes(path, ReplaceManifest(packageBytes, manifest));

        var inspected = PortableLogbookPackageFile.ReadManifestForInspection(path);
        var strictError = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal(manifest.SchemaVersion, inspected.SchemaVersion);
        Assert.Equal(PortableLogbookPackageError.UnsupportedSchemaVersion, strictError.Error);
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
    public void ReadManifestForInspectionRejectsOversizedFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifestForInspection(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadBytesRejectsOversizedFileBeforeReturningContent()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "oversized.elogbook");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadBytes(path, new PortableLogbookPackageReadOptions(127)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.Read(path, PortableLogbookKey.Generate()));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsEmptyFileBeforePackageParsing()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "empty.elogbook");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackageFile.ReadManifestForInspection(path));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestRejectsNonElogbookExtension()
    {
        var path = Path.Combine(tempDirectory, "export.zip");

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPackageFile.ReadManifest(path));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsNonElogbookExtension()
    {
        var path = Path.Combine(tempDirectory, "export.zip");

        var exception = Assert.Throws<ArgumentException>(() => PortableLogbookPackageFile.ReadManifestForInspection(path));

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

    private static byte[] ReplaceManifest(
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest)
    {
        var newManifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        var originalManifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan("ELOGPKG1".Length, sizeof(int)));
        var remainderStart = "ELOGPKG1".Length + sizeof(int) + originalManifestLength;
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ELOGPKG1"));
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, newManifestBytes.Length);
        output.Write(manifestLength);
        output.Write(newManifestBytes);
        output.Write(packageBytes.AsSpan(remainderStart));
        return output.ToArray();
    }
}
