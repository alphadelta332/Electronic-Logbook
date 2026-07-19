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
    public void EncryptionPlanCanBeAssembledWithExternalAesGcmOutput()
    {
        var document = CreateDocument();
        var key = FixedKey(1);
        var createdAt = DateTimeOffset.Parse("2026-07-19T04:05:06Z");
        var plan = PortableLogbookPackage.CreateEncryptionPlan(document, createdAt);
        var ciphertext = new byte[plan.CompressedPlaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(plan.Nonce, plan.CompressedPlaintext, ciphertext, tag, plan.ManifestBytes);

        var packageBytes = PortableLogbookPackage.Assemble(plan, ciphertext, tag);
        var result = PortableLogbookPackage.Read(packageBytes, key, document.LogbookId);

        Assert.Equal(createdAt, result.Manifest.CreatedAt);
        Assert.Equal(document.LogbookId, result.Document.LogbookId);
        Assert.Equal(document.Operations.Count, result.Document.Operations.Count);
    }

    [Fact]
    public void DecryptionPlanCanBeReadWithExternalAesGcmOutput()
    {
        var document = CreateDocument();
        var key = FixedKey(1);
        var packageBytes = PortableLogbookPackage.Write(document, key);
        var plan = PortableLogbookPackage.CreateDecryptionPlan(packageBytes, document.LogbookId);
        var compressedPlaintext = new byte[plan.Ciphertext.Length];
        using var aes = new AesGcm(key, plan.Tag.Length);
        aes.Decrypt(plan.Nonce, plan.Ciphertext, plan.Tag, compressedPlaintext, plan.ManifestBytes);

        var result = PortableLogbookPackage.ReadDecrypted(plan, compressedPlaintext, document.LogbookId);

        Assert.Equal(document.LogbookId, result.Manifest.LogbookId);
        Assert.Equal(document.Operations.Count, result.Document.Operations.Count);
        Assert.Equal(document.Operations.Select(operation => operation.RevisionId), result.Document.Operations.Select(operation => operation.RevisionId));
    }

    [Fact]
    public void DecryptionPlanRejectsWrongLogbookBeforeExternalDecryption()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));

        var error = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.CreateDecryptionPlan(packageBytes, new LogbookId("log_other")));

        Assert.Equal(PortableLogbookPackageError.WrongLogbook, error.Error);
    }

    [Fact]
    public void AssembleRejectsInvalidExternalCryptoOutput()
    {
        var plan = PortableLogbookPackage.CreateEncryptionPlan(
            CreateDocument(),
            DateTimeOffset.Parse("2026-07-19T04:05:06Z"));

        var tagError = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Assemble(plan, new byte[plan.CompressedPlaintext.Length], new byte[15]));
        var ciphertextError = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Assemble(plan, new byte[plan.CompressedPlaintext.Length + 1], new byte[16]));

        Assert.Equal(PortableLogbookPackageError.InvalidTag, tagError.Error);
        Assert.Equal(PortableLogbookPackageError.InvalidCiphertext, ciphertextError.Error);
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
    public void ReadRejectsPackageLargerThanConfiguredLimit()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        var options = new PortableLogbookPackageReadOptions(packageBytes.Length - 1);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(1), document.LogbookId, options));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
    }

    [Fact]
    public void ReadRejectsEmptyPackageBeforeParsing()
    {
        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read([], FixedKey(1)));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadManifestForInspectionRejectsEmptyPackageBeforeParsing()
    {
        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.ReadManifestForInspection([]));

        Assert.Equal(PortableLogbookPackageError.PackageEmpty, exception.Error);
    }

    [Fact]
    public void ReadRejectsInvalidMagic()
    {
        var bytes = new byte[64];
        Array.Fill<byte>(bytes, 1);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(bytes, FixedKey(1)));

        Assert.Equal(PortableLogbookPackageError.InvalidMagic, exception.Error);
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

    [Theory]
    [InlineData("compression")]
    [InlineData("encryption")]
    public void ReadRejectsUnsupportedManifestAlgorithms(string algorithmKind)
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        var manifest = ReadManifest(packageBytes);
        manifest = algorithmKind == "compression"
            ? manifest with { Compression = "brotli" }
            : manifest with { Encryption = "AES-128-CBC" };
        var modified = ReplaceManifestForAuthenticationFailure(packageBytes, manifest);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(modified, FixedKey(1), document.LogbookId));

        Assert.Equal(
            algorithmKind == "compression"
                ? PortableLogbookPackageError.UnsupportedCompression
                : PortableLogbookPackageError.UnsupportedEncryption,
            exception.Error);
    }

    [Fact]
    public void ReadRejectsManifestPayloadMismatchAfterAuthenticatedDecrypt()
    {
        var document = CreateDocument();
        var manifest = new PortableLogbookPackageManifest(
            PortableLogbookPackage.FormatVersion,
            document.LogbookId,
            document.SchemaVersion,
            document.JurisdictionProfile,
            document.JurisdictionProfileVersion,
            CustomFieldCount: document.CustomFieldDefinitions.Count,
            OperationCount: document.Operations.Count + 1,
            CreatedAt: DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Compression: "gzip",
            Encryption: "AES-256-GCM");
        var packageBytes = WritePackageWithManifest(document, manifest, FixedKey(1));

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.Read(packageBytes, FixedKey(1), document.LogbookId));

        Assert.Equal(PortableLogbookPackageError.ManifestPayloadMismatch, exception.Error);
    }

    [Fact]
    public void ReadManifestReturnsPublicPackageMetadataWithoutKey()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));

        var manifest = PortableLogbookPackage.ReadManifest(packageBytes);

        Assert.Equal(document.LogbookId, manifest.LogbookId);
        Assert.Equal(document.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal(document.JurisdictionProfile, manifest.JurisdictionProfile);
        Assert.Equal(document.JurisdictionProfileVersion, manifest.JurisdictionProfileVersion);
        Assert.Equal(document.Operations.Count, manifest.OperationCount);
    }

    [Fact]
    public void ReadManifestForInspectionReturnsUnsupportedSchemaManifestWithoutKey()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));
        var manifest = ReadManifest(packageBytes) with
        {
            SchemaVersion = PortableLogbookDocument.CurrentSchemaVersion + 1
        };
        var modified = ReplaceManifestForAuthenticationFailure(packageBytes, manifest);

        var inspected = PortableLogbookPackage.ReadManifestForInspection(modified);
        var strictError = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.ReadManifest(modified));

        Assert.Equal(manifest.SchemaVersion, inspected.SchemaVersion);
        Assert.Equal(PortableLogbookPackageError.UnsupportedSchemaVersion, strictError.Error);
    }

    [Fact]
    public void ReadManifestRejectsOversizedPackageBeforeParsingManifest()
    {
        var document = CreateDocument();
        var packageBytes = PortableLogbookPackage.Write(document, FixedKey(1));

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookPackage.ReadManifest(packageBytes, new PortableLogbookPackageReadOptions(packageBytes.Length - 1)));

        Assert.Equal(PortableLogbookPackageError.PackageTooLarge, exception.Error);
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

    private static byte[] WritePackageWithManifest(
        PortableLogbookDocument document,
        PortableLogbookPackageManifest manifest,
        byte[] key)
    {
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(PortableLogbookJson.Serialize(document));
        using var compressedStream = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(plaintext);
        }

        var nonce = new byte[12];
        Array.Fill<byte>(nonce, 9);
        var tag = new byte[16];
        var ciphertext = new byte[compressedStream.ToArray().Length];
        using (var aes = new System.Security.Cryptography.AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, compressedStream.ToArray(), ciphertext, tag, manifestBytes);
        }

        using var output = new MemoryStream();
        output.Write(System.Text.Encoding.ASCII.GetBytes("ELOGPKG1"));
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, manifestBytes.Length);
        output.Write(manifestLength);
        output.Write(manifestBytes);
        output.Write(nonce);
        output.Write(tag);
        output.Write(ciphertext);
        return output.ToArray();
    }

    private static class PortablePackageTestFormat
    {
        public const int MagicSize = 8;
        public const int HeaderSize = MagicSize + sizeof(int);
    }
}
