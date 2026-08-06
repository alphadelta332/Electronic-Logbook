using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class PortableHostedCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string CreateTargetName(LogbookId logbookId, DeviceId deviceId) =>
        $"ElectronicLogbook.Hosted/{logbookId.Value}/{deviceId.Value}";

    public static void Save(string targetName, PortableHostedCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(credential);

        var blobBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credential, JsonDefaults.Indented));
        var blob = Marshal.AllocCoTaskMem(blobBytes.Length);
        try
        {
            Marshal.Copy(blobBytes, 0, blob, blobBytes.Length);
            var native = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = blobBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref native, 0))
            {
                throw CreateWin32Exception("Credential Manager could not save the hosted logbook credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blobBytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static PortableHostedCredential? Load(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Credential Manager could not read the hosted logbook credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                throw new InvalidDataException("Stored hosted logbook credential is empty.");
            }

            var blobBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blobBytes, 0, blobBytes.Length);
            try
            {
                return JsonSerializer.Deserialize<PortableHostedCredential>(
                    Encoding.UTF8.GetString(blobBytes),
                    JsonDefaults.Web);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(blobBytes);
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

        throw new Win32Exception(error, "Credential Manager could not delete the hosted logbook credential.");
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

public sealed record PortableHostedCredential(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);
