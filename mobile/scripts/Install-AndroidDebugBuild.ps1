param(
    [switch] $SkipSync,
    [switch] $SkipLaunch,
    [string] $DeviceSerial
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$androidRoot = Join-Path $mobileRoot "android"
$apkPath = Join-Path $androidRoot "app\build\outputs\apk\debug\app-debug.apk"
$packageName = "com.alphadelta.electroniclogbook.dev"

. (Join-Path $scriptRoot "AndroidDevelopmentSigning.ps1")
. (Join-Path $scriptRoot "AndroidDebugDeviceBridge.ps1")

function Find-AndroidSdk {
    $candidates = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        (Join-Path $env:LOCALAPPDATA "Android\Sdk"),
        (Join-Path $env:USERPROFILE "AppData\Local\Android\Sdk")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        $adbPath = Join-Path $candidate "platform-tools\adb.exe"
        if (Test-Path -LiteralPath $adbPath -PathType Leaf) {
            return $candidate
        }
    }

    throw "Android SDK was not found. Set ANDROID_HOME to an SDK containing platform-tools\adb.exe."
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Resolve-DeviceSerial {
    param(
        [Parameter(Mandatory = $true)] [string] $Adb,
        [string] $RequestedSerial
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedSerial)) {
        return $RequestedSerial
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $adbOutput = & $Adb "devices" 2>&1
        $adbExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($adbExitCode -ne 0) {
        throw "adb devices failed: $($adbOutput -join [Environment]::NewLine)"
    }

    $deviceLines = $adbOutput | Where-Object { $_ -match "^([^\s]+)\s+device$" }
    $serials = @($deviceLines | ForEach-Object {
        if ($_ -match "^([^\s]+)\s+device$") { $Matches[1] }
    })
    if ($serials.Count -ne 1) {
        throw "Connect exactly one authorised Android device or pass -DeviceSerial."
    }

    return $serials[0]
}

$sdkRoot = Find-AndroidSdk
$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$adb = Join-Path $sdkRoot "platform-tools\adb.exe"
$resolvedDeviceSerial = Resolve-DeviceSerial -Adb $adb -RequestedSerial $DeviceSerial
$signingIdentity = Initialize-AndroidDevelopmentSigning
Write-Host "Using durable development certificate $($signingIdentity.CertificateSha256)."

if (-not $SkipSync) {
    Push-Location $mobileRoot
    try {
        Invoke-Checked -FilePath "npm.cmd" -Arguments @("run", "sync:android")
    }
    finally {
        Pop-Location
    }
}

Push-Location $androidRoot
try {
    Invoke-Checked -FilePath ".\gradlew.bat" -Arguments @("assembleDebug")
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "Debug APK was not found at $apkPath."
}

$result = Invoke-DataPreservingDebugInstall -Adb $adb -SdkRoot $sdkRoot `
    -DeviceSerial $resolvedDeviceSerial -PackageName $packageName -ApkPath $apkPath `
    -SkipLaunch:$SkipLaunch

switch ($result.Mode) {
    "new-install" {
        Write-Host "Installed the debug app on a device with no existing debug package."
    }
    "in-place-update" {
        Write-Host "Updated the debug app in place; Android retained its existing logbook data."
    }
    "verified-signing-migration" {
        Write-Host "Migrated the existing logbook to the durable development signing identity."
        Write-Host "Verified recovery evidence: $($result.BackupPath)"
    }
    "resumed-signing-migration" {
        Write-Host "Resumed and completed the verified development signing migration."
        Write-Host "Verified recovery evidence: $($result.BackupPath)"
    }
}
