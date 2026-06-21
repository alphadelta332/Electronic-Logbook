using System.Security.Cryptography;

namespace ElectronicLogbook.Updater;

public static class Integrity
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task VerifyFileAsync(
        string path,
        ReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != asset.Size)
        {
            throw new InvalidDataException(
                $"{asset.Name} size mismatch. Expected {asset.Size}, found {info.Length}.");
        }

        var actualHash = await Sha256Async(path, cancellationToken);
        if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{asset.Name} SHA-256 verification failed.");
        }
    }
}
