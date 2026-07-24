param(
    [switch] $SkipSync,
    [string] $DeviceSerial
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$androidRoot = Join-Path $mobileRoot "android"
$apkPath = Join-Path $androidRoot "app\build\outputs\apk\debug\app-debug.apk"

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

$sdkRoot = Find-AndroidSdk
$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$adb = Join-Path $sdkRoot "platform-tools\adb.exe"

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

$installArguments = @()
if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) {
    $installArguments += @("-s", $DeviceSerial)
}

$installArguments += @("install", "-r", $apkPath)

& $adb @installArguments
if ($LASTEXITCODE -ne 0) {
    throw @"
Data-preserving install failed.

This script deliberately does not clear, uninstall, or reset the app because that destroys
the private WebView and IndexedDB logbook data on the device. If Android reports
INSTALL_FAILED_UPDATE_INCOMPATIBLE, first export or otherwise preserve the device logbook
data, then use a deliberately separate reset procedure.
"@
}

Write-Host "Installed debug APK with data-preserving adb install -r."
