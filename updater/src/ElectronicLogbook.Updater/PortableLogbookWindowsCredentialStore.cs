using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class PortableLogbookWindowsCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string CreateTargetName(LogbookId logbookId, DeviceId deviceId) =>
        $"ElectronicLogbook.Portable/{logbookId.Value}/{deviceId.Value}";

    public static void SaveKey(string targetName, PortableLogbookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(key);

        var keyBytes = key.ToBytes();
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
                throw CreateWin32Exception("Credential Manager could not save the portable logbook key.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static PortableLogbookKey? LoadKey(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Credential Manager could not read the portable logbook key.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize != PortableLogbookPackage.KeySizeBytes)
            {
                throw new InvalidDataException("Stored portable logbook key has an invalid size.");
            }

            var keyBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, keyBytes, 0, keyBytes.Length);
            try
            {
                return PortableLogbookKey.FromBytes(keyBytes);
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

    public static bool DeleteKey(string targetName)
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

        throw new Win32Exception(error, "Credential Manager could not delete the portable logbook key.");
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
