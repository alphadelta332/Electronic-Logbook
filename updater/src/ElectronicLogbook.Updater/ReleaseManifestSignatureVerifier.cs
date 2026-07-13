using System.Reflection;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace ElectronicLogbook.Updater;

public static class ReleaseManifestSignatureVerifier
{
    public static readonly string PinnedThumbprint = LoadPinnedCertificate().Thumbprint;

    public static void VerifyPinned(byte[] content, byte[] detachedSignature)
    {
        Verify(content, detachedSignature, PinnedThumbprint);
    }

    public static void Verify(byte[] content, byte[] detachedSignature, string expectedThumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThumbprint);
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(detachedSignature);
        cms.CheckSignature(verifySignatureOnly: true);

        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } signer)
        {
            throw new InvalidDataException("Release manifest must contain one signing certificate.");
        }

        if (!string.Equals(signer.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Release manifest signer does not match the pinned release certificate.");
        }
    }

    private static X509Certificate2 LoadPinnedCertificate()
    {
        var assembly = typeof(ReleaseManifestSignatureVerifier).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith("release-signing.cer", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException("Pinned release certificate resource could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new X509Certificate2(buffer.ToArray());
    }
}
