#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RecoveryDirectory)

$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($RecoveryDirectory)) { throw 'Use a full path on the trusted offline drive.' }
Add-Type -TypeDefinition @'
using System;
using System.Security.Cryptography;
public static class FlightLogXBackupIdentity {
    public static string[] Create(string password) {
        using var rsa = RSA.Create(3072);
        return new[] { rsa.ExportSubjectPublicKeyInfoPem(),
            rsa.ExportEncryptedPkcs8PrivateKeyPem(password.AsSpan(),
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600000)) };
    }
}
'@
Write-Host 'Create the protected USB recovery copy. No password or private key is sent to ChatGPT, GitHub or Cloudflare.'
Write-Host 'Choose a password of at least 20 characters (several unrelated words). Keep it separately from the USB drive.'
$first = Read-Host 'Recovery password' -AsSecureString
$second = Read-Host 'Enter it again' -AsSecureString
$firstPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($first)
$secondPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($second)
try {
    $passphrase = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($firstPointer)
    $confirmation = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secondPointer)
    if ($passphrase.Length -lt 20 -or $passphrase -cne $confirmation) { throw 'Passwords differ or are shorter than 20 characters. No recovery files created.' }
    $keys = [FlightLogXBackupIdentity]::Create($passphrase)
    $request = @{ directory = [IO.Path]::GetFullPath($RecoveryDirectory); recipient = $keys[0]; encryptedIdentity = $keys[1]; passphrase = $passphrase } | ConvertTo-Json -Compress
    $start = [Diagnostics.ProcessStartInfo]::new('node')
    $start.ArgumentList.Add((Join-Path $PSScriptRoot 'initialize-recovery.mjs'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Write($request)
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $failure = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Recovery copy could not be completed. Existing identity files were not replaced; no secrets printed.' }
    $result = $output | ConvertFrom-Json
    if ($result.status -ne 'created-and-verified') { throw 'Recovery verification did not pass.' }
    Write-Host 'Protected USB recovery files created and successfully decrypted for verification.' -ForegroundColor Green
    Write-Host 'Keep the password separately. Leave the USB connected until the backup setup check finishes.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($firstPointer)
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secondPointer)
    $first.Dispose(); $second.Dispose()
    $passphrase = $null; $confirmation = $null; $request = $null; $keys = $null
}
