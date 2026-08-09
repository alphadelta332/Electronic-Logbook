using System.Security.Cryptography;

namespace ElectronicLogbook.Mobile;

public static class MobileRecoveryCodeEnvelope
{
    public const string Algorithm = "PBKDF2-SHA256-600000+A256GCM";
    public const string KeyVersionId = "recovery-code-v1";

    public static string GenerateRecoveryCode()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(secret)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}

public sealed record MobileRecoveryCodeSetup(
    string RecoveryCode,
    MobileRecoveryCodeEnvelopePayload Envelope);

public sealed record MobileRecoveryCodeEnvelopePayload(
    string Ciphertext,
    string Nonce,
    string Salt,
    string Algorithm,
    string KeyVersionId);
