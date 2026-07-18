using System.Security.Cryptography;

namespace ElectronicLogbook.Portable;

public sealed class PortableLogbookKey : IEquatable<PortableLogbookKey>
{
    private readonly byte[] bytes;

    private PortableLogbookKey(byte[] bytes)
    {
        this.bytes = bytes;
    }

    public static PortableLogbookKey Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(PortableLogbookPackage.KeySizeBytes);
        return new PortableLogbookKey(bytes);
    }

    public static PortableLogbookKey FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PortableLogbookPackage.KeySizeBytes)
        {
            throw new ArgumentException(
                $"Portable logbook key must be {PortableLogbookPackage.KeySizeBytes} bytes.",
                nameof(bytes));
        }

        return new PortableLogbookKey(bytes.ToArray());
    }

    public static PortableLogbookKey FromRecoveryCode(string recoveryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);

        try
        {
            return FromBytes(Base64UrlDecode(recoveryCode));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException("Recovery code is not a valid portable logbook key.", nameof(recoveryCode), ex);
        }
    }

    public byte[] ToBytes() => bytes.ToArray();

    public string ToRecoveryCode() => Base64UrlEncode(bytes);

    public bool Equals(PortableLogbookKey? other) =>
        other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) =>
        obj is PortableLogbookKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(bytes[0], bytes[1], bytes[^2], bytes[^1], bytes.Length);

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
