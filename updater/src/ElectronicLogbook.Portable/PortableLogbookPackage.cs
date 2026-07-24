using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElectronicLogbook.Portable;

public static class PortableLogbookPackage
{
    public const int FormatVersion = 1;
    public const int KeySizeBytes = 32;
    public const int DefaultMaxPackageBytes = 64 * 1024 * 1024;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int MaxManifestBytes = 64 * 1024;
    private static readonly byte[] Magic = "ELOGPKG1"u8.ToArray();

    public static byte[] Write(PortableLogbookDocument document, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var plan = CreateEncryptionPlan(document, DateTimeOffset.UtcNow);
        var ciphertext = new byte[plan.CompressedPlaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(plan.Nonce, plan.CompressedPlaintext, ciphertext, tag, plan.ManifestBytes);

        return Assemble(plan, ciphertext, tag);
    }

    public static byte[] Write(PortableLogbookDocumentV2 document, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var plan = CreateEncryptionPlan(document, DateTimeOffset.UtcNow);
        var ciphertext = new byte[plan.CompressedPlaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(plan.Nonce, plan.CompressedPlaintext, ciphertext, tag, plan.ManifestBytes);

        return Assemble(plan, ciphertext, tag);
    }

    public static PortableLogbookPackageEncryptionPlan CreateEncryptionPlan(
        PortableLogbookDocument document,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                $"Portable logbook document is invalid: {FormatValidationErrors(validation)}");
        }

        var plaintext = Compress(Encoding.UTF8.GetBytes(PortableLogbookJson.Serialize(document)));
        var manifest = new PortableLogbookPackageManifest(
            FormatVersion,
            document.LogbookId,
            document.SchemaVersion,
            document.JurisdictionProfile,
            document.JurisdictionProfileVersion,
            document.CustomFieldDefinitions.Count,
            document.Operations.Count,
            createdAt,
            "gzip",
            "AES-256-GCM");
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        if (manifestBytes.Length > MaxManifestBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.ManifestTooLarge,
                "Package manifest is too large.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        return new PortableLogbookPackageEncryptionPlan(manifest, manifestBytes, nonce, plaintext);
    }

    public static PortableLogbookPackageEncryptionPlan CreateEncryptionPlan(
        PortableLogbookDocumentV2 document,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validation = PortableLogbookValidatorV2.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                $"Portable logbook document is invalid: {FormatValidationErrors(validation)}");
        }

        var plaintext = Compress(Encoding.UTF8.GetBytes(PortableLogbookJson.SerializeV2(document)));
        var manifest = new PortableLogbookPackageManifest(
            FormatVersion,
            document.LogbookId,
            document.SchemaVersion,
            document.JurisdictionProfile,
            document.JurisdictionProfileVersion,
            document.CustomFieldDefinitions.Count,
            document.Operations.Count,
            createdAt,
            "gzip",
            "AES-256-GCM");
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PortableLogbookJson.SerializerOptions);
        if (manifestBytes.Length > MaxManifestBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.ManifestTooLarge,
                "Package manifest is too large.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        return new PortableLogbookPackageEncryptionPlan(manifest, manifestBytes, nonce, plaintext);
    }

    public static byte[] Assemble(
        PortableLogbookPackageEncryptionPlan plan,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (tag.Length != TagSizeBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidTag,
                $"Package authentication tag must be {TagSizeBytes} bytes.");
        }

        if (ciphertext.Length != plan.CompressedPlaintext.Length)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidCiphertext,
                "Package ciphertext length does not match the encryption plan.");
        }

        using var output = new MemoryStream();
        output.Write(Magic);
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, plan.ManifestBytes.Length);
        output.Write(manifestLength);
        output.Write(plan.ManifestBytes);
        output.Write(plan.Nonce);
        output.Write(tag);
        output.Write(ciphertext);
        return output.ToArray();
    }

    public static byte[] Write(PortableLogbookDocument document, PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Write(document, key.ToBytes());
    }

    public static byte[] Write(PortableLogbookDocumentV2 document, PortableLogbookKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Write(document, key.ToBytes());
    }

    public static PortableLogbookPackageManifest ReadManifest(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookPackageReadOptions? options = null)
    {
        options ??= PortableLogbookPackageReadOptions.Default;
        var (manifest, _, _) = ReadManifestAndPayloadOffset(packageBytes, options);
        ValidateManifest(manifest, expectedLogbookId: null);
        return manifest;
    }

    public static PortableLogbookPackageManifest ReadManifestForInspection(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookPackageReadOptions? options = null)
    {
        options ??= PortableLogbookPackageReadOptions.Default;
        var (manifest, _, _) = ReadManifestAndPayloadOffset(packageBytes, options);
        ValidateManifestForInspection(manifest);
        return manifest;
    }

    public static PortableLogbookPackageReadResult Read(
        ReadOnlySpan<byte> packageBytes,
        ReadOnlySpan<byte> key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ValidateKey(key);
        var plan = CreateDecryptionPlan(packageBytes, expectedLogbookId, options);
        var compressedPlaintext = new byte[plan.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(plan.Nonce, plan.Ciphertext, plan.Tag, compressedPlaintext, plan.ManifestBytes);
        }
        catch (CryptographicException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.AuthenticationFailed,
                "Package authentication failed.",
                ex);
        }

        return ReadDecrypted(plan, compressedPlaintext, expectedLogbookId);
    }

    public static PortableLogbookPackageReadResultV2 ReadV2(
        ReadOnlySpan<byte> packageBytes,
        ReadOnlySpan<byte> key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ValidateKey(key);
        var plan = CreateDecryptionPlanV2(packageBytes, expectedLogbookId, options);
        var compressedPlaintext = new byte[plan.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(plan.Nonce, plan.Ciphertext, plan.Tag, compressedPlaintext, plan.ManifestBytes);
        }
        catch (CryptographicException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.AuthenticationFailed,
                "Package authentication failed.",
                ex);
        }

        return ReadDecryptedV2(plan, compressedPlaintext, expectedLogbookId);
    }

    public static PortableLogbookPackageDecryptionPlan CreateDecryptionPlan(
        ReadOnlySpan<byte> packageBytes,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        options ??= PortableLogbookPackageReadOptions.Default;
        var (manifest, offset, manifestBytes) = ReadManifestAndPayloadOffset(packageBytes, options);

        ValidateManifest(manifest, expectedLogbookId);

        var nonce = packageBytes.Slice(offset, NonceSizeBytes).ToArray();
        offset += NonceSizeBytes;
        var tag = packageBytes.Slice(offset, TagSizeBytes).ToArray();
        offset += TagSizeBytes;
        var ciphertext = packageBytes[offset..].ToArray();

        return new PortableLogbookPackageDecryptionPlan(manifest, manifestBytes, nonce, tag, ciphertext);
    }

    public static PortableLogbookPackageDecryptionPlan CreateDecryptionPlanV2(
        ReadOnlySpan<byte> packageBytes,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        options ??= PortableLogbookPackageReadOptions.Default;
        var (manifest, offset, manifestBytes) = ReadManifestAndPayloadOffset(packageBytes, options);

        ValidateManifestV2(manifest, expectedLogbookId);

        var nonce = packageBytes.Slice(offset, NonceSizeBytes).ToArray();
        offset += NonceSizeBytes;
        var tag = packageBytes.Slice(offset, TagSizeBytes).ToArray();
        offset += TagSizeBytes;
        var ciphertext = packageBytes[offset..].ToArray();

        return new PortableLogbookPackageDecryptionPlan(manifest, manifestBytes, nonce, tag, ciphertext);
    }

    public static PortableLogbookPackageReadResult ReadDecrypted(
        PortableLogbookPackageDecryptionPlan plan,
        ReadOnlySpan<byte> compressedPlaintext,
        LogbookId? expectedLogbookId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var documentJson = Encoding.UTF8.GetString(Decompress(compressedPlaintext.ToArray()));
        PortableLogbookDocument document;
        try
        {
            document = PortableLogbookJson.Deserialize(documentJson)
                ?? throw new PortableLogbookPackageException(PortableLogbookPackageError.InvalidPayload, "Package payload is invalid.");
        }
        catch (JsonException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidPayload,
                "Package payload is invalid.",
                ex);
        }

        ValidateManifestAgainstDocument(plan.Manifest, document, expectedLogbookId);

        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                $"Portable logbook document is invalid after package read: {FormatValidationErrors(validation)}");
        }

        return new PortableLogbookPackageReadResult(plan.Manifest, document);
    }

    public static PortableLogbookPackageReadResultV2 ReadDecryptedV2(
        PortableLogbookPackageDecryptionPlan plan,
        ReadOnlySpan<byte> compressedPlaintext,
        LogbookId? expectedLogbookId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var documentJson = Encoding.UTF8.GetString(Decompress(compressedPlaintext.ToArray()));
        PortableLogbookDocumentV2 document;
        try
        {
            document = PortableLogbookJson.DeserializeV2(documentJson)
                ?? throw new PortableLogbookPackageException(PortableLogbookPackageError.InvalidPayload, "Package payload is invalid.");
        }
        catch (JsonException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidPayload,
                "Package payload is invalid.",
                ex);
        }

        ValidateManifestAgainstDocument(plan.Manifest, document, expectedLogbookId);

        var validation = PortableLogbookValidatorV2.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                $"Portable logbook document is invalid after package read: {FormatValidationErrors(validation)}");
        }

        return new PortableLogbookPackageReadResultV2(plan.Manifest, document);
    }

    private static string FormatValidationErrors(PortableLogbookValidationResult validation) =>
        string.Join(
            "; ",
            validation.Errors
                .Take(5)
                .Select(error => $"{error.Code}: {error.Message}"));

    public static PortableLogbookPackageReadResult Read(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookKey key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Read(packageBytes, key.ToBytes(), expectedLogbookId, options);
    }

    public static PortableLogbookPackageReadResultV2 ReadV2(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookKey key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ReadV2(packageBytes, key.ToBytes(), expectedLogbookId, options);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidKey,
                $"Package key must be {KeySizeBytes} bytes.");
        }
    }

    private static (PortableLogbookPackageManifest Manifest, int PayloadOffset, byte[] ManifestBytes) ReadManifestAndPayloadOffset(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookPackageReadOptions options)
    {
        if (packageBytes.Length > options.MaxPackageBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.PackageTooLarge,
                $"Package is larger than the configured {options.MaxPackageBytes} byte limit.");
        }

        if (packageBytes.Length == 0)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.PackageEmpty,
                "Package is empty.");
        }

        if (packageBytes.Length < Magic.Length + sizeof(int) + NonceSizeBytes + TagSizeBytes + 1)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.TruncatedPackage,
                "Package is truncated.");
        }

        if (!packageBytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidMagic,
                "Package header is not recognised.");
        }

        var offset = Magic.Length;
        var manifestLength = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (manifestLength <= 0 || manifestLength > MaxManifestBytes)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.ManifestTooLarge,
                "Package manifest length is invalid.");
        }

        if (packageBytes.Length < offset + manifestLength + NonceSizeBytes + TagSizeBytes + 1)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.TruncatedPackage,
                "Package is truncated.");
        }

        var manifestBytes = packageBytes.Slice(offset, manifestLength).ToArray();
        offset += manifestLength;
        try
        {
            var manifest = JsonSerializer.Deserialize<PortableLogbookPackageManifest>(manifestBytes, PortableLogbookJson.SerializerOptions)
                ?? throw new PortableLogbookPackageException(PortableLogbookPackageError.InvalidManifest, "Package manifest is invalid.");
            return (manifest, offset, manifestBytes);
        }
        catch (JsonException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidManifest,
                "Package manifest is invalid.",
                ex);
        }
    }

    private static void ValidateManifest(PortableLogbookPackageManifest manifest, LogbookId? expectedLogbookId)
    {
        ValidateManifestForInspection(manifest);

        if (manifest.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedSchemaVersion,
                $"Package schema version {manifest.SchemaVersion} is not supported.");
        }

        ValidateExpectedLogbook(manifest, expectedLogbookId);
    }

    private static void ValidateManifestV2(PortableLogbookPackageManifest manifest, LogbookId? expectedLogbookId)
    {
        ValidateManifestForInspection(manifest);

        if (manifest.SchemaVersion != PortableLogbookDocumentV2.CurrentSchemaVersion)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedSchemaVersion,
                $"Package schema version {manifest.SchemaVersion} is not supported. Re-import the authoritative workbook to create a workbook-faithful mobile package.");
        }

        ValidateExpectedLogbook(manifest, expectedLogbookId);
    }

    private static void ValidateManifestForInspection(PortableLogbookPackageManifest manifest)
    {
        if (manifest.FormatVersion != FormatVersion)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedFormatVersion,
                $"Package format version {manifest.FormatVersion} is not supported.");
        }

        if (!string.Equals(manifest.Compression, "gzip", StringComparison.Ordinal))
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedCompression,
                $"Package compression '{manifest.Compression}' is not supported.");
        }

        if (!string.Equals(manifest.Encryption, "AES-256-GCM", StringComparison.Ordinal))
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedEncryption,
                $"Package encryption '{manifest.Encryption}' is not supported.");
        }
    }

    private static void ValidateExpectedLogbook(PortableLogbookPackageManifest manifest, LogbookId? expectedLogbookId)
    {
        if (expectedLogbookId is not null && manifest.LogbookId != expectedLogbookId.Value)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.WrongLogbook,
                "Package belongs to a different logbook.");
        }
    }

    private static void ValidateManifestAgainstDocument(
        PortableLogbookPackageManifest manifest,
        PortableLogbookDocument document,
        LogbookId? expectedLogbookId)
    {
        if (manifest.LogbookId != document.LogbookId ||
            manifest.SchemaVersion != document.SchemaVersion ||
            manifest.JurisdictionProfile != document.JurisdictionProfile ||
            manifest.JurisdictionProfileVersion != document.JurisdictionProfileVersion ||
            manifest.CustomFieldCount != document.CustomFieldDefinitions.Count ||
            manifest.OperationCount != document.Operations.Count)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.ManifestPayloadMismatch,
                "Package manifest does not match the encrypted payload.");
        }

        if (expectedLogbookId is not null && document.LogbookId != expectedLogbookId.Value)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.WrongLogbook,
                "Package payload belongs to a different logbook.");
        }
    }

    private static void ValidateManifestAgainstDocument(
        PortableLogbookPackageManifest manifest,
        PortableLogbookDocumentV2 document,
        LogbookId? expectedLogbookId)
    {
        if (manifest.LogbookId != document.LogbookId ||
            manifest.SchemaVersion != document.SchemaVersion ||
            manifest.JurisdictionProfile != document.JurisdictionProfile ||
            manifest.JurisdictionProfileVersion != document.JurisdictionProfileVersion ||
            manifest.CustomFieldCount != document.CustomFieldDefinitions.Count ||
            manifest.OperationCount != document.Operations.Count)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.ManifestPayloadMismatch,
                "Package manifest does not match the encrypted payload.");
        }

        if (expectedLogbookId is not null && document.LogbookId != expectedLogbookId.Value)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.WrongLogbook,
                "Package payload belongs to a different logbook.");
        }
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidPayload,
                "Package payload compression is invalid.",
                ex);
        }
    }
}

public sealed record PortableLogbookPackageManifest(
    int FormatVersion,
    LogbookId LogbookId,
    int SchemaVersion,
    string JurisdictionProfile,
    int JurisdictionProfileVersion,
    int CustomFieldCount,
    int OperationCount,
    DateTimeOffset CreatedAt,
    string Compression,
    string Encryption);

public sealed record PortableLogbookPackageEncryptionPlan(
    PortableLogbookPackageManifest Manifest,
    byte[] ManifestBytes,
    byte[] Nonce,
    byte[] CompressedPlaintext);

public sealed record PortableLogbookPackageDecryptionPlan(
    PortableLogbookPackageManifest Manifest,
    byte[] ManifestBytes,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed record PortableLogbookPackageReadResult(
    PortableLogbookPackageManifest Manifest,
    PortableLogbookDocument Document);

public sealed record PortableLogbookPackageReadResultV2(
    PortableLogbookPackageManifest Manifest,
    PortableLogbookDocumentV2 Document);

public sealed record PortableLogbookPackageReadOptions(int MaxPackageBytes)
{
    public static PortableLogbookPackageReadOptions Default { get; } =
        new(PortableLogbookPackage.DefaultMaxPackageBytes);
}

public sealed class PortableLogbookPackageException : Exception
{
    public PortableLogbookPackageException(
        PortableLogbookPackageError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public PortableLogbookPackageError Error { get; }
}

public enum PortableLogbookPackageError
{
    InvalidKey,
    InvalidDocument,
    InvalidMagic,
    TruncatedPackage,
    ManifestTooLarge,
    InvalidManifest,
    UnsupportedFormatVersion,
    UnsupportedSchemaVersion,
    UnsupportedCompression,
    UnsupportedEncryption,
    WrongLogbook,
    PackageTooLarge,
    PackageEmpty,
    AuthenticationFailed,
    InvalidPayload,
    ManifestPayloadMismatch,
    InvalidTag,
    InvalidCiphertext
}
