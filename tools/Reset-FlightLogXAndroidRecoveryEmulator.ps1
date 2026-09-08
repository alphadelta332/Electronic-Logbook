[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('CaptureBaseline', 'ResetAppAndInstall', 'RestoreSnapshotAndInstall', 'Verify')]
    [string]$Action,

    [string]$DeviceSerial = 'emulator-5554',
    [string]$ExpectedAvdName = 'ElectronicLogbook_Pixel_Play_API35',
    [string]$SnapshotName = 'flightlogx_google_authenticated_clean_v1',
    [string]$PackageName = 'com.alphadelta.electroniclogbook',
    [string]$ApkPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'mobile\android\app\build\outputs\apk\preview\app-preview.apk'),
    [switch]$ApproveSnapshotRestore,
    [switch]$ApproveFlightLogXRemoval
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-Adb {
    $command = Get-Command 'adb' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }

    $sdkRoot = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    $candidate = Join-Path $sdkRoot 'platform-tools\adb.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    throw 'adb was not found. Install Android SDK platform-tools or put adb on PATH.'
}

function Invoke-Adb {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & $script:AdbPath -s $DeviceSerial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb failed for '$($Arguments -join ' ')': $($output -join ' ')"
    }
    return ($output -join "`n").Trim()
}

function Wait-ForBoot {
    & $script:AdbPath -s $DeviceSerial wait-for-device | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The emulator $DeviceSerial did not reconnect." }

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    do {
        $booted = Invoke-Adb -Arguments @('shell', 'getprop', 'sys.boot_completed')
        if ($booted -eq '1') { return }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "The emulator $DeviceSerial did not finish booting within two minutes."
}

function Assert-SafeEmulatorTarget {
    if ($DeviceSerial -notmatch '^emulator-\d+$') {
        throw "Refusing to operate on '$DeviceSerial'. This workflow is restricted to an Android emulator."
    }

    $isEmulator = Invoke-Adb -Arguments @('shell', 'getprop', 'ro.kernel.qemu')
    if ($isEmulator -ne '1') {
        throw "Refusing to operate on '$DeviceSerial' because Android does not identify it as an emulator."
    }

    $reportedName = Invoke-Adb -Arguments @('emu', 'avd', 'name')
    $actualName = (($reportedName -split "`n") | Where-Object { $_ -and $_ -ne 'OK' } | Select-Object -First 1).Trim()
    if ($actualName -ne $ExpectedAvdName) {
        throw "Refusing to operate on AVD '$actualName'. Expected '$ExpectedAvdName'."
    }
}

function Test-PackageInstalled {
    param([Parameter(Mandatory)][string]$Id)
    $result = Invoke-Adb -Arguments @('shell', 'pm', 'list', 'packages', '--user', '0', $Id)
    return @($result -split "`n" | Where-Object { $_.Trim() -eq "package:$Id" }).Count -eq 1
}

function Assert-OneGoogleAccount {
    $accountDump = Invoke-Adb -Arguments @('shell', 'dumpsys', 'account')
    $totalMatch = [regex]::Match($accountDump, '(?m)^\s*Accounts:\s*(\d+)\s*$')
    $googleCount = [regex]::Matches($accountDump, '(?m)^\s+Account \{name=.*type=com\.google\}\s*$').Count
    if (-not $totalMatch.Success -or [int]$totalMatch.Groups[1].Value -ne 1 -or $googleCount -ne 1) {
        throw 'Expected exactly one signed-in Google account. No account identifiers were printed.'
    }
}

function Test-SnapshotExists {
    $snapshots = Invoke-Adb -Arguments @('emu', 'avd', 'snapshot', 'list')
    return $snapshots -match "(?m)^--\s+$([regex]::Escape($SnapshotName))\s+"
}

function Assert-RequiredGoogleRuntime {
    if (-not (Test-PackageInstalled -Id 'com.android.vending')) {
        throw 'This AVD does not contain Google Play Store. Use the google_apis_playstore system image.'
    }
    if (-not (Test-PackageInstalled -Id 'com.google.android.gms')) {
        throw 'Google Play services is missing from this AVD.'
    }
}

function Get-RemotePackageHash {
    $packagePathOutput = Invoke-Adb -Arguments @('shell', 'pm', 'path', $PackageName)
    $packagePath = (($packagePathOutput -split "`n") | Where-Object { $_ -match '^package:' } | Select-Object -First 1) -replace '^package:', ''
    if ([string]::IsNullOrWhiteSpace($packagePath)) { throw 'The installed APK path could not be resolved.' }
    $hashOutput = Invoke-Adb -Arguments @('shell', 'sha256sum', $packagePath.Trim())
    $hash = (($hashOutput -split '\s+')[0]).Trim().ToLowerInvariant()
    if ($hash -notmatch '^[0-9a-f]{64}$') { throw 'The installed APK SHA-256 could not be read.' }
    return $hash
}

function Install-FreshFlightLogX {
    if (Test-PackageInstalled -Id $PackageName) {
        throw 'FlightLogX must be absent before the fresh installation.'
    }
    if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) { throw "Approved Preview APK not found: $ApkPath" }

    $localHash = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $installResult = Invoke-Adb -Arguments @('install', $ApkPath)
    if ($installResult -notmatch 'Success') { throw "APK installation did not report success: $installResult" }
    $remoteHash = Get-RemotePackageHash
    if ($remoteHash -ne $localHash) { throw 'Installed APK SHA-256 does not match the selected local APK.' }

    $packageDump = Invoke-Adb -Arguments @('shell', 'dumpsys', 'package', $PackageName)
    if ($packageDump -notmatch 'notLaunched=true') { throw 'FlightLogX was installed, but Android does not report a fresh, never-launched app state.' }
    $versionName = [regex]::Match($packageDump, '(?m)^\s*versionName=(.+)$').Groups[1].Value.Trim()
    $versionCode = [regex]::Match($packageDump, '(?m)^\s*versionCode=(\d+)').Groups[1].Value

    return [pscustomobject]@{
        Hash = $localHash
        VersionName = $versionName
        VersionCode = $versionCode
    }
}

$script:AdbPath = Resolve-Adb
Wait-ForBoot
Assert-SafeEmulatorTarget
Assert-RequiredGoogleRuntime

switch ($Action) {
    'CaptureBaseline' {
        if (Test-SnapshotExists) {
            throw "Snapshot '$SnapshotName' already exists. This command will not silently replace an authenticated baseline."
        }
        Assert-OneGoogleAccount
        if (Test-PackageInstalled -Id $PackageName) {
            if (-not $ApproveFlightLogXRemoval) {
                throw "FlightLogX is installed. Rerun with -ApproveFlightLogXRemoval to remove only '$PackageName' before saving the clean baseline."
            }
            [void](Invoke-Adb -Arguments @('uninstall', $PackageName))
            if (Test-PackageInstalled -Id $PackageName) { throw 'FlightLogX remained installed after the requested removal.' }
        }
        [void](Invoke-Adb -Arguments @('emu', 'avd', 'snapshot', 'save', $SnapshotName))
        Write-Host "Saved machine-local authenticated baseline '$SnapshotName'."
        Write-Host 'Treat the AVD and snapshot as a signed-in browser profile: never transfer, commit, upload, or share them.'
    }

    'ResetAppAndInstall' {
        if (-not $ApproveFlightLogXRemoval) {
            throw "Removing FlightLogX discards its local data on this disposable emulator. Rerun with -ApproveFlightLogXRemoval."
        }
        Assert-OneGoogleAccount
        if (Test-PackageInstalled -Id $PackageName) {
            [void](Invoke-Adb -Arguments @('uninstall', $PackageName))
            if (Test-PackageInstalled -Id $PackageName) { throw 'FlightLogX remained installed after the requested removal.' }
        }

        $installed = Install-FreshFlightLogX
        Write-Host "Preserved the current Google session and installed a fresh FlightLogX $($installed.VersionName) ($($installed.VersionCode))."
        Write-Host "Verified APK SHA-256: $($installed.Hash)"
        Write-Host 'No emulator snapshot was restored. Open FlightLogX and choose the retained account when prompted.'
    }

    'RestoreSnapshotAndInstall' {
        if (-not $ApproveSnapshotRestore) {
            throw 'Snapshot restore discards changes made inside this disposable emulator after the baseline. Rerun with -ApproveSnapshotRestore.'
        }
        if (-not (Test-SnapshotExists)) { throw "Snapshot '$SnapshotName' does not exist." }

        [void](Invoke-Adb -Arguments @('emu', 'avd', 'snapshot', 'load', $SnapshotName))
        Wait-ForBoot
        Assert-SafeEmulatorTarget
        Assert-RequiredGoogleRuntime
        Assert-OneGoogleAccount
        if (Test-PackageInstalled -Id $PackageName) {
            throw 'The baseline is not clean: FlightLogX is already installed. Capture a new clean baseline deliberately.'
        }

        $installed = Install-FreshFlightLogX
        Write-Host "Restored '$SnapshotName' and installed a fresh FlightLogX $($installed.VersionName) ($($installed.VersionCode))."
        Write-Host "Verified APK SHA-256: $($installed.Hash)"
        Write-Host 'Google remains signed in. Open FlightLogX and choose the retained account when prompted.'
    }

    'Verify' {
        Assert-OneGoogleAccount
        if (-not (Test-SnapshotExists)) { throw "Snapshot '$SnapshotName' does not exist." }
        Write-Host "PASS: '$ExpectedAvdName' has Google Play, one Google account, and snapshot '$SnapshotName'."
        Write-Host 'No account identifier or authentication token was printed.'
    }
}
