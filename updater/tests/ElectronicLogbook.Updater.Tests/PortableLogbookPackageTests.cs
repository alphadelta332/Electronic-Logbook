using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookPackageTests
{
    [Fact]
    public void WriteReadRoundTripsEncryptedPortableDocument()
    {
        var document = CreateDocument();
        var key = FixedKey(1);

        var packageBytes = PortableLogbookPackage.Write(document, key);
        var result = PortableLogbookPackage.Read(packageBytes, key, document.LogbookId);

        Assert.Equal(document.LogbookId, result.Manifest.LogbookId);
        Assert.Equal(document.Operations.Count, result.Manifest.OperationCount);
        Assert.Equal(document.LogbookId, result.Document.LogbookId);
        Assert.Equal(document.Operations.Select(operation => operation.RevisionId), result.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void ReadRejectsWrongEncryptionKey()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(2), document.LogbookId));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void ReadRejectsTamperedManifest()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        packageBytes[PortablePackageTestFormat.HeaderSize + 3] ^= 0x01;

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(1), document.LogbookId));

        Assert.True(
            exception.Error is PortableLogbookPackageError.AuthenticationFailed or PortableLogbookPackageError.InvalidManifest,
            $"Unexpected package error: {exception.Error}");
    }

    [Fact]
    public void ReadRejectsTamperedCiphertext()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        packageBytes[^1] ^= 0x01;

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(1), document.LogbookId));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void ReadRejectsTruncatedPackage()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        var truncated = packageBytes[..^8];

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(truncated, FixedKey(1), document.LogbookId));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void ReadRejectsWrongLogbookBeforeDecryptingPayload()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(1), new LogbookId("log_other")));

        Assert.Equal(PortableLogbookPackageError.WrongLogbook, exception.Error);
    }

    [Fact]
    public void WriteRejectsInvalidDocument()
    {
        var create = CreateOperation();
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create with { LogbookId = new LogbookId("log_other") }]);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Write(document, FixedKey(1)));

        Assert.Equal(PortableLogbookPackageError.InvalidDocument, exception.Error);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("schema")]
    public void ReadRejectsUnsupportedManifestVersions(string versionKind)
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        var manifest = ReadManifest(packageBytes);
        manifest = versionKind == "format"
            ? manifest with { FormatVersion = PortableLogbookPackage.FormatVersion + 1 }
            : manifest with { SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1 };
        var modified = ReplaceManifestForAuthenticationFailure(packageBytes, manifest);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(modified, FixedKey(1), document.LogbookId));

        Assert.Equal(
            versionKind == "format"
                ? PortableLogbookPackageError.UnsupportedFormatVersion
                : PortableLogbookPackageError.UnsupportedSchemaVersion,
            exception.Error);
    }

    [Fact]
    public void WriteRequiresThirtyTwoByteKey()
    {
        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Write(CreateDocument(), new byte[31]));

        Assert.Equal(PortableLogbookPackageError.InvalidKey, exception.Error);
    }

    private static PortableLogbookDocument CreateDocument()
    {
        var fieldId = new CustomFieldId("cf_training_kind");
        var create = CreateOperation(new Dictionary<CustomFieldId, string?> { [fieldId] = "Training" });
        return PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            [new CustomFieldDefinition(fieldId, "Training kind", 1)],
            [create]);
    }

    private static CreateEntryOperation CreateOperation(IReadOnlyDictionary<CustomFieldId, string?>? customFields = null) =>
        new(
            new LogbookId("log_test"),
            new EntryId("ent_1"),
            new RevisionId("rev_create"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m,
                CustomFields = customFields ?? new Dictionary<CustomFieldId, string?>()
            });

    private static byte[] FixedKey(byte seed)
    {
        var key = new byte[PortableLogbookPackage.KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        Array.Fill(key, seed);
        return key;
    }

    private static PortableLogbookPackageManifest ReadManifest(byte[] packageBytes)
    {
        var manifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan(PortablePackageTestFormat.MagicSize, sizeof(int)));
        var manifestBytes = packageBytes.AsSpan(PortablePackageTestFormat.HeaderSize, manifestLength);
        return JsonSerializer.Deserialize<PortableLogbookPackageManifest>(manifestBytes, PortableLogbookJson.SerializerOptions)
            ?? throw new InvalidOperationException("Manifest could not be read.");
    }

    private static byte[] ReplaceManifestForAuthenticationFailure(
        byte[] packageBytes,
        PortableLogbookPackageManifest manifest)
    {
        var newManifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        var originalManifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan(PortablePackageTestFormat.MagicSize, sizeof(int)));
        var remainderStart = PortablePackageTestFormat.HeaderSize + originalManifestLength;
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ELOGPKG1"));
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, newManifestBytes.Length);
        output.Write(manifestLength);
        output.Write(newManifestBytes);
        output.Write(packageBytes.AsSpan(remainderStart));
        return output.ToArray();
    }

    private static class PortablePackageTestFormat
    {
        public const int MagicSize = 8;
        public const int HeaderSize = MagicSize + sizeof(int);
    }
}
