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
        ArgumentNullException.ThrowIfNull(document);
        ValidateKey(key);

        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                "Portable logbook document is invalid.");
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
            DateTimeOffset.UtcNow,
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
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, manifestBytes);

        using var output = new MemoryStream();
        output.Write(Magic);
        Span<byte> manifestLength = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(manifestLength, manifestBytes.Length);
        output.Write(manifestLength);
        output.Write(manifestBytes);
        output.Write(nonce);
        output.Write(tag);
        output.Write(ciphertext);
        return output.ToArray();
    }

    public static byte[] Write(PortableLogbookDocument document, PortableLogbookKey key)
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

    public static PortableLogbookPackageReadResult Read(
        ReadOnlySpan<byte> packageBytes,
        ReadOnlySpan<byte> key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ValidateKey(key);
        options ??= PortableLogbookPackageReadOptions.Default;
        var (manifest, offset, manifestBytes) = ReadManifestAndPayloadOffset(packageBytes, options);

        ValidateManifest(manifest, expectedLogbookId);

        var nonce = packageBytes.Slice(offset, NonceSizeBytes);
        offset += NonceSizeBytes;
        var tag = packageBytes.Slice(offset, TagSizeBytes);
        offset += TagSizeBytes;
        var ciphertext = packageBytes[offset..];
        var compressedPlaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, compressedPlaintext, manifestBytes);
        }
        catch (CryptographicException ex)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.AuthenticationFailed,
                "Package authentication failed.",
                ex);
        }

        var documentJson = Encoding.UTF8.GetString(Decompress(compressedPlaintext));
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

        ValidateManifestAgainstDocument(manifest, document, expectedLogbookId);

        var validation = PortableLogbookValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.InvalidDocument,
                "Portable logbook document is invalid after package read.");
        }

        return new PortableLogbookPackageReadResult(manifest, document);
    }

    public static PortableLogbookPackageReadResult Read(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookKey key,
        LogbookId? expectedLogbookId = null,
        PortableLogbookPackageReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Read(packageBytes, key.ToBytes(), expectedLogbookId, options);
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
        if (manifest.FormatVersion != FormatVersion)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedFormatVersion,
                $"Package format version {manifest.FormatVersion} is not supported.");
        }

        if (manifest.SchemaVersion != PortableLogbookDocument.CurrentSchemaVersion)
        {
            throw new PortableLogbookPackageException(
                PortableLogbookPackageError.UnsupportedSchemaVersion,
                $"Package schema version {manifest.SchemaVersion} is not supported.");
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

public sealed record PortableLogbookPackageReadResult(
    PortableLogbookPackageManifest Manifest,
    PortableLogbookDocument Document);

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
    AuthenticationFailed,
    InvalidPayload,
    ManifestPayloadMismatch
}
