param(
    [switch] $SkipSync,
    [string] $DeviceSerial
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$androidRoot = Join-Path $mobileRoot "android"
$apkPath = Join-Path $androidRoot "app\build\outputs\apk\acceptance\app-acceptance.apk"

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
    Invoke-Checked -FilePath ".\gradlew.bat" -Arguments @("assembleAcceptance")
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "Acceptance APK was not found at $apkPath."
}

$installArguments = @()
if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) {
    $installArguments += @("-s", $DeviceSerial)
}

$installArguments += @("install", "-r", $apkPath)
& $adb @installArguments
if ($LASTEXITCODE -ne 0) {
    throw "Data-preserving acceptance install failed. It deliberately does not clear, uninstall, reset, or overwrite the existing .dev app."
}

Write-Host "Installed isolated acceptance APK (com.alphadelta.electroniclogbook.acceptance)."
