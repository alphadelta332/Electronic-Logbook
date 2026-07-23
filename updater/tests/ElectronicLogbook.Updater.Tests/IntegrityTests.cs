using System.Security.Cryptography;

namespace ElectronicLogbook.Updater.Tests;

public sealed class IntegrityTests
{
    [Fact]
    public async Task VerifyFileAcceptsMatchingSizeAndHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "verified content");
            var bytes = await File.ReadAllBytesAsync(path);
            var asset = new ReleaseAsset(
                Path.GetFileName(path),
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

            await Integrity.VerifyFileAsync(path, asset, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyFileRejectsMismatchedHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "unverified content");
            var asset = new ReleaseAsset(
                Path.GetFileName(path),
                new FileInfo(path).Length,
                new string('0', 64));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                Integrity.VerifyFileAsync(path, asset, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyFileRejectsMismatchedSize()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "unexpected size");
            var bytes = await File.ReadAllBytesAsync(path);
            var asset = new ReleaseAsset(
                Path.GetFileName(path),
                bytes.Length + 1,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                Integrity.VerifyFileAsync(path, asset, CancellationToken.None));

            Assert.Contains("size", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
