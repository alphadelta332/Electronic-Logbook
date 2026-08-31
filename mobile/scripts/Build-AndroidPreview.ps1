[CmdletBinding()]
param(
    [switch] $SkipSync,
    [ValidateRange(0, 9999)]
    [Alias("PilotBuildRevision")]
    [int] $PreviewBuildRevision = 0
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$mobileRoot = Split-Path -Parent $scriptRoot
$androidRoot = Join-Path $mobileRoot "android"
$apkPath = Join-Path $androidRoot "app\build\outputs\apk\preview\app-preview.apk"
$outputMetadataPath = Join-Path $androidRoot "app\build\outputs\apk\preview\output-metadata.json"
$packageName = "com.alphadelta.electroniclogbook"
$versionPath = Join-Path (Split-Path -Parent $mobileRoot) "version.txt"

. (Join-Path $scriptRoot "AndroidPreviewSigning.ps1")

function Find-PreviewAndroidSdk {
    $candidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += Join-Path $env:LOCALAPPDATA "Android\Sdk"
    }

    foreach ($candidate in $candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate "build-tools") -PathType Container) {
            return $candidate
        }
    }

    throw "Android SDK Build-Tools were not found. Set ANDROID_HOME before building the Preview APK."
}

function Find-PreviewBuildTool {
    param(
        [Parameter(Mandatory = $true)] [string] $SdkRoot,
        [Parameter(Mandatory = $true)] [string] $FileName
    )

    $tool = Get-ChildItem -LiteralPath (Join-Path $SdkRoot "build-tools") -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName $FileName } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($tool)) {
        throw "$FileName was not found in Android SDK Build-Tools."
    }

    return $tool
}

function Get-PreviewFileSha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $stream = [IO.File]::OpenRead($Path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
        $stream.Dispose()
    }
}

$sdkRoot = Find-PreviewAndroidSdk
$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$signingIdentity = Initialize-AndroidPreviewSigning
Write-Host "Using permanent FlightLogX Preview certificate $($signingIdentity.CertificateSha256)."

$productVersion = (Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8).Trim()
if ($productVersion -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "version.txt must contain a numeric major.minor.patch version."
}
$major = [int] $Matches[1]
$minor = [int] $Matches[2]
$patch = [int] $Matches[3]
if ($major -gt 20 -or $minor -gt 99 -or $patch -gt 99) {
    throw "Android versions require major <= 20, minor <= 99, and patch <= 99."
}
$expectedVersionCode = ($major * 100000000) + ($minor * 1000000) + ($patch * 10000) + $PreviewBuildRevision

if (-not $SkipSync) {
    Push-Location $mobileRoot
    try {
        & npm.cmd run sync:android
        if ($LASTEXITCODE -ne 0) { throw "Android synchronization failed." }
    }
    finally { Pop-Location }
}

Push-Location $androidRoot
try {
    $gradleArguments = @("assemblePreview")
    if ($PreviewBuildRevision -gt 0) {
        $gradleArguments += "-PflightLogXPreviewBuildRevision=$PreviewBuildRevision"
    }
    & .\gradlew.bat @gradleArguments
    if ($LASTEXITCODE -ne 0) { throw "Signed Preview APK build failed." }
}
finally { Pop-Location }

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "The Preview APK was not produced at the expected path."
}
if (-not (Test-Path -LiteralPath $outputMetadataPath -PathType Leaf)) {
    throw "The Preview APK output metadata was not produced."
}

$outputMetadata = Get-Content -LiteralPath $outputMetadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($outputMetadata.applicationId -ne $packageName -or $outputMetadata.variantName -ne "preview") {
    throw "The built APK metadata does not identify the permanent FlightLogX Preview package."
}
$builtVersionCode = [int64] $outputMetadata.elements[0].versionCode
$builtVersionName = [string] $outputMetadata.elements[0].versionName
if ($builtVersionCode -ne $expectedVersionCode -or $builtVersionName -ne $productVersion) {
    throw "The built APK version metadata does not match the requested Preview build revision."
}

$apkSigner = Find-PreviewBuildTool -SdkRoot $sdkRoot -FileName "apksigner.bat"
$signerOutput = & $apkSigner verify --verbose --print-certs $apkPath 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Android apksigner rejected the Preview APK."
}
$signerFingerprintLine = $signerOutput | Where-Object { $_ -match "certificate SHA-256 digest:\s*([0-9a-fA-F]+)" } | Select-Object -First 1
if ($null -eq $signerFingerprintLine -or $signerFingerprintLine -notmatch "certificate SHA-256 digest:\s*([0-9a-fA-F]+)") {
    throw "The signed APK certificate fingerprint could not be read."
}
$apkFingerprint = $Matches[1].ToLowerInvariant()
if ($apkFingerprint -ne $signingIdentity.CertificateSha256) {
    throw "The Preview APK was signed by an unexpected certificate. Do not distribute it."
}

$aapt = Find-PreviewBuildTool -SdkRoot $sdkRoot -FileName "aapt.exe"
$badgingOutput = & $aapt dump badging $apkPath 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Android aapt could not inspect the Preview APK package."
}
$packageLine = $badgingOutput | Where-Object { $_ -match "^package:\s+name='([^']+)'" } | Select-Object -First 1
if ($null -eq $packageLine -or $packageLine -notmatch "^package:\s+name='([^']+)'" -or $Matches[1] -ne $packageName) {
    throw "The APK manifest does not contain the permanent FlightLogX package name."
}

$apkHash = Get-PreviewFileSha256 -Path $apkPath
Write-Host "Signed Preview APK verified: $apkPath"
Write-Host "Package: $packageName"
Write-Host "Version: $builtVersionName ($builtVersionCode; Preview revision $PreviewBuildRevision)"
Write-Host "Certificate SHA-256: $apkFingerprint"
Write-Host "APK SHA-256: $apkHash"

return [pscustomobject]@{
    ApkPath = $apkPath
    PackageName = $packageName
    VersionName = $builtVersionName
    VersionCode = $builtVersionCode
    PreviewBuildRevision = $PreviewBuildRevision
    CertificateSha256 = $apkFingerprint
    ApkSha256 = $apkHash
}
