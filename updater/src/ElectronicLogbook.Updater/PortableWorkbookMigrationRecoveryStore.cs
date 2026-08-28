using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public sealed class PortableWorkbookMigrationRecoveryMaterial : IDisposable
{
    internal PortableWorkbookMigrationRecoveryMaterial(
        string credentialTargetName,
        PortableLogbookKey logbookKey,
        PortableWorkbookRecoveryKeyPair recoveryKeyPair,
        bool resumed)
    {
        CredentialTargetName = credentialTargetName;
        LogbookKey = logbookKey;
        RecoveryKeyPair = recoveryKeyPair;
        Resumed = resumed;
    }

    public string CredentialTargetName { get; }

    public PortableLogbookKey LogbookKey { get; }

    public PortableWorkbookRecoveryKeyPair RecoveryKeyPair { get; }

    public bool Resumed { get; internal set; }

    public void Dispose() => RecoveryKeyPair.Dispose();
}

public static class PortableWorkbookMigrationRecoveryStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;
    private const int PrivateKeyLengthOffset = 40;
    private const int PayloadOffset = 44;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ELBMIG01");

    public static string CreateTargetName(LogbookId logbookId, DeviceId deviceId) =>
        $"ElectronicLogbook.WorkbookMigration/{logbookId.Value}/{deviceId.Value}";

    public static PortableWorkbookMigrationRecoveryMaterial LoadOrCreate(
        LogbookId logbookId,
        DeviceId deviceId)
    {
        var targetName = CreateTargetName(logbookId, deviceId);
        var existing = Load(targetName);
        if (existing is not null)
        {
            existing.Resumed = true;
            return existing;
        }

        var logbookKey = PortableLogbookKey.Generate();
        using var recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Create();
        Save(targetName, logbookKey, recoveryKeyPair);

        var verified = Load(targetName)
            ?? throw new InvalidDataException(
                "Credential Manager did not retain the temporary workbook migration keys.");
        if (!verified.LogbookKey.Equals(logbookKey) ||
            !string.Equals(
                verified.RecoveryKeyPair.Fingerprint,
                recoveryKeyPair.Fingerprint,
                StringComparison.Ordinal))
        {
            verified.Dispose();
            throw new InvalidDataException(
                "Credential Manager read-back did not match the temporary workbook migration keys.");
        }

        verified.Resumed = false;
        return verified;
    }

    public static PortableWorkbookMigrationRecoveryMaterial? Load(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(
                error,
                "Credential Manager could not read the temporary workbook migration keys.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize < PayloadOffset + 1 ||
                credential.CredentialBlobSize > MaximumCredentialBlobBytes)
            {
                throw new InvalidDataException(
                    "Stored temporary workbook migration keys have an invalid size.");
            }

            var payload = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, payload, 0, payload.Length);
            try
            {
                return Parse(targetName, payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        finally
        {
            ZeroCredentialBlob(credentialPointer);
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

        throw new Win32Exception(
            error,
            "Credential Manager could not delete the temporary workbook migration keys.");
    }

    private static void Save(
        string targetName,
        PortableLogbookKey logbookKey,
        PortableWorkbookRecoveryKeyPair recoveryKeyPair)
    {
        var packageKeyBytes = logbookKey.ToBytes();
        var recoveryKeyBytes = recoveryKeyPair.ExportPrivateKey();
        var payload = new byte[PayloadOffset + recoveryKeyBytes.Length];
        var unmanagedPayload = IntPtr.Zero;
        try
        {
            if (payload.Length > MaximumCredentialBlobBytes)
            {
                throw new InvalidDataException(
                    "Temporary workbook migration keys are too large for Windows Credential Manager.");
            }

            Magic.CopyTo(payload, 0);
            packageKeyBytes.CopyTo(payload, Magic.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(PrivateKeyLengthOffset, sizeof(int)),
                recoveryKeyBytes.Length);
            recoveryKeyBytes.CopyTo(payload, PayloadOffset);

            unmanagedPayload = Marshal.AllocCoTaskMem(payload.Length);
            Marshal.Copy(payload, 0, unmanagedPayload, payload.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = payload.Length,
                CredentialBlob = unmanagedPayload,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Credential Manager could not save the temporary workbook migration keys.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packageKeyBytes);
            CryptographicOperations.ZeroMemory(recoveryKeyBytes);
            CryptographicOperations.ZeroMemory(payload);
            if (unmanagedPayload != IntPtr.Zero)
            {
                Marshal.Copy(payload, 0, unmanagedPayload, payload.Length);
                Marshal.FreeCoTaskMem(unmanagedPayload);
            }
        }
    }

    private static PortableWorkbookMigrationRecoveryMaterial Parse(
        string targetName,
        byte[] payload)
    {
        if (!payload.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "Stored temporary workbook migration keys use an unsupported format.");
        }

        var privateKeyLength = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(PrivateKeyLengthOffset, sizeof(int)));
        if (privateKeyLength <= 0 || privateKeyLength != payload.Length - PayloadOffset)
        {
            throw new InvalidDataException(
                "Stored temporary workbook migration keys are incomplete.");
        }

        PortableWorkbookRecoveryKeyPair? recoveryKeyPair = null;
        byte[]? privateKeyBytes = null;
        try
        {
            var logbookKey = PortableLogbookKey.FromBytes(
                payload.AsSpan(Magic.Length, PortableLogbookPackage.KeySizeBytes));
            privateKeyBytes = payload.AsSpan(PayloadOffset, privateKeyLength).ToArray();
            recoveryKeyPair = PortableWorkbookRecoveryKeyPair.Import(privateKeyBytes);
            return new PortableWorkbookMigrationRecoveryMaterial(
                targetName,
                logbookKey,
                recoveryKeyPair,
                resumed: false);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            recoveryKeyPair?.Dispose();
            throw new InvalidDataException(
                "Stored temporary workbook migration keys are invalid.",
                ex);
        }
        finally
        {
            if (privateKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }
        }
    }

    private static void ZeroCredentialBlob(IntPtr credentialPointer)
    {
        var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
        if (credential.CredentialBlob == IntPtr.Zero ||
            credential.CredentialBlobSize <= 0 ||
            credential.CredentialBlobSize > MaximumCredentialBlobBytes)
        {
            return;
        }

        var zeros = new byte[credential.CredentialBlobSize];
        Marshal.Copy(zeros, 0, credential.CredentialBlob, zeros.Length);
    }

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
