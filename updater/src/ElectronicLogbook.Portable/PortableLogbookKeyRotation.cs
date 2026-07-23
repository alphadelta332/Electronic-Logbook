namespace ElectronicLogbook.Portable;

public static class PortableLogbookKeyRotation
{
    public static PortableLogbookKeyRotationResult RotatePackageKey(
        ReadOnlySpan<byte> packageBytes,
        PortableLogbookKey oldKey,
        PortableLogbookKey newKey,
        LogbookId expectedLogbookId,
        PortableLogbookPackageReadOptions? readOptions = null)
    {
        ArgumentNullException.ThrowIfNull(oldKey);
        ArgumentNullException.ThrowIfNull(newKey);

        var read = PortableLogbookPackage.Read(packageBytes, oldKey, expectedLogbookId, readOptions);
        var rotatedPackageBytes = PortableLogbookPackage.Write(read.Document, newKey);
        return new PortableLogbookKeyRotationResult(
            rotatedPackageBytes,
            read.Document.LogbookId,
            read.Document.Operations.Count,
            DateTimeOffset.UtcNow);
    }
}

public sealed record PortableLogbookKeyRotationResult(
    byte[] PackageBytes,
    LogbookId LogbookId,
    int OperationCount,
    DateTimeOffset RotatedAt);
