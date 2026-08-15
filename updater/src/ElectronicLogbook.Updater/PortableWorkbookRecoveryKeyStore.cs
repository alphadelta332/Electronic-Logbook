using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed class PortableWorkbookRecoveryKeyPair : IDisposable
{
    public const string AlgorithmName = "RSA-OAEP-256";

    private byte[] privateKey;
    private bool disposed;

    private PortableWorkbookRecoveryKeyPair(byte[] privateKey)
    {
        this.privateKey = privateKey;
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKey, out _);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        PublicKey = Convert.ToBase64String(publicKey);
        Fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
    }

    public string PublicKey { get; }

    public string Fingerprint { get; }

    public string Algorithm => AlgorithmName;

    public static PortableWorkbookRecoveryKeyPair Create()
    {
        using var rsa = RSA.Create(2048);
        return new PortableWorkbookRecoveryKeyPair(rsa.ExportPkcs8PrivateKey());
    }

    public static PortableWorkbookRecoveryKeyPair Import(byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return new PortableWorkbookRecoveryKeyPair(privateKey.ToArray());
    }

    public byte[] ExportPrivateKey()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return privateKey.ToArray();
    }

    public PortableLogbookKey DecryptPackageKey(string wrappedKey)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedKey);

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKey, out _);
        byte[] plaintext;
        try
        {
            plaintext = rsa.Decrypt(Convert.FromBase64String(wrappedKey), RSAEncryptionPadding.OaepSHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidDataException("The hosted recovery envelope could not be decrypted by this workbook device.", ex);
        }

        try
        {
            if (plaintext.Length != PortableLogbookPackage.KeySizeBytes)
            {
                throw new InvalidDataException("The hosted recovery envelope contains an invalid logbook key.");
            }

            return PortableLogbookKey.FromBytes(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(privateKey);
        privateKey = [];
        disposed = true;
    }
}

public static class PortableWorkbookRecoveryKeyStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;

    public static string CreateTargetName(LogbookId logbookId, DeviceId deviceId) =>
        $"ElectronicLogbook.WorkbookRecovery/{logbookId.Value}/{deviceId.Value}";

    public static void Save(string targetName, PortableWorkbookRecoveryKeyPair keyPair)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(keyPair);

        var keyBytes = keyPair.ExportPrivateKey();
        if (keyBytes.Length > MaximumCredentialBlobBytes)
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            throw new InvalidDataException("The workbook recovery key is too large for Windows Credential Manager.");
        }

        var blob = Marshal.AllocCoTaskMem(keyBytes.Length);
        try
        {
            Marshal.Copy(keyBytes, 0, blob, keyBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = keyBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw CreateWin32Exception("Credential Manager could not save the workbook recovery key.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static PortableWorkbookRecoveryKeyPair? Load(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Credential Manager could not read the workbook recovery key.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                throw new InvalidDataException("Stored workbook recovery key is empty.");
            }

            var keyBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, keyBytes, 0, keyBytes.Length);
            try
            {
                return PortableWorkbookRecoveryKeyPair.Import(keyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static bool Delete(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(error, "Credential Manager could not delete the workbook recovery key.");
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
