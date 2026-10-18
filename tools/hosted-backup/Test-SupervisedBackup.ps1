#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$RecoveryDirectory,
    [switch]$ApplicationReplay, [ValidateSet(54322, 55432)][int]$LocalPort = 54322)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($Archive) -or -not [IO.Path]::IsPathRooted($RecoveryDirectory)) { throw 'Use full archive and recovery-directory paths.' }
foreach ($path in @($Archive, "$Archive.receipt.json", (Join-Path $RecoveryDirectory 'backup-private.encrypted.pem'), (Join-Path $RecoveryDirectory 'recovery.elbbackup'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'A required backup/recovery file is missing.' }
}
Write-Host 'This restores only into a new isolated local test database, compares every table and removes that test database.'
if ($ApplicationReplay) { Write-Host 'It also uses only the USB recovery capsule keys to decrypt and replay populated logbooks through shared application code.' }
Write-Host 'Enter the password chosen for your recovery USB. It stays in this local process and is not displayed.'
$password = Read-Host 'Recovery USB password' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
try {
    $request = @{ archive = [IO.Path]::GetFullPath($Archive); identity = Join-Path $RecoveryDirectory 'backup-private.encrypted.pem';
        recoveryArchive = Join-Path $RecoveryDirectory 'recovery.elbbackup'; applicationReplay = [bool]$ApplicationReplay; localPort = $LocalPort;
        passphrase = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) } | ConvertTo-Json -Compress
    $start = [Diagnostics.ProcessStartInfo]::new('node')
    $start.ArgumentList.Add((Join-Path $PSScriptRoot 'verify-supervised.mjs'))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Write($request); $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd(); $failure = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Verification did not pass. The encrypted backup and original recovery files remain unchanged; no secrets printed.' }
    $result = $output | ConvertFrom-Json
    if ($result.status -ne 'database-restored-and-matched') { throw 'Unexpected verification result.' }
    Write-Host 'Database restored, every table matched, test database removed and protected recovery capsule authenticated.' -ForegroundColor Green
    if ($ApplicationReplay) {
        if ($result.applicationReplay.status -ne 'application-replayed') { throw 'Application replay did not pass.' }
        Write-Host 'Recovered USB keys decrypted the logbook operations and settings. Shared application replay passed.' -ForegroundColor Green
        $result.applicationReplay.logbooks | Format-Table operations, configurations, activeEntries, deletedEntries, conflicts, customFields
    }
    Write-Host 'Google sign-in and app-level recovery still require separate acceptance.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $password.Dispose(); $request = $null
}
