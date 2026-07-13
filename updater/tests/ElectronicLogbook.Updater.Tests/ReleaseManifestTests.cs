using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace ElectronicLogbook.Updater.Tests;

public sealed class ReleaseManifestTests
{
    private static readonly ReleaseAsset ValidAsset =
        new("Electronic_Logbook_Master.xlsm", 100, new string('a', 64));

    [Fact]
    public void ValidateManifestAcceptsMatchingMetadata()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset]);

        ReleaseClient.ValidateManifest(manifest, "v1.4.0");
    }

    [Fact]
    public void ValidateManifestRejectsMismatchedTag()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset]);

        Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.1"));
    }

    [Fact]
    public void ValidateManifestRejectsInvalidHash()
    {
        var manifest = new ReleaseManifest(
            "1.4.0",
            "v1.4.0",
            new string('b', 40),
            [ValidAsset with { Sha256 = "bad" }]);

        Assert.Throws<InvalidDataException>(() =>
            ReleaseClient.ValidateManifest(manifest, "v1.4.0"));
    }

    [Fact]
    public void SignedManifestAcceptsPinnedSigner()
    {
        var content = "{\"version\":\"2.0.0\"}"u8.ToArray();
        using var certificate = CreateSigningCertificate();
        var signature = Sign(content, certificate);

        ReleaseManifestSignatureVerifier.Verify(content, signature, certificate.Thumbprint);
    }

    [Fact]
    public void SignedManifestRejectsTamperedContentAndWrongSigner()
    {
        var content = "{\"version\":\"2.0.0\"}"u8.ToArray();
        using var certificate = CreateSigningCertificate();
        var signature = Sign(content, certificate);

        Assert.ThrowsAny<Exception>(() =>
            ReleaseManifestSignatureVerifier.Verify("tampered"u8.ToArray(), signature, certificate.Thumbprint));
        Assert.Throws<InvalidDataException>(() =>
            ReleaseManifestSignatureVerifier.Verify(content, signature, new string('A', 40)));
    }

    [Fact]
    public void SignedManifestRejectsMissingSignature()
    {
        using var certificate = CreateSigningCertificate();

        Assert.ThrowsAny<Exception>(() =>
            ReleaseManifestSignatureVerifier.Verify("content"u8.ToArray(), [], certificate.Thumbprint));
    }

    private static X509Certificate2 CreateSigningCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Release Manifest Test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static byte[] Sign(byte[] content, X509Certificate2 certificate)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.ComputeSignature(new CmsSigner(certificate));
        return cms.Encode();
    }
}
