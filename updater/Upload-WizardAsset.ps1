[CmdletBinding()]
param(
    [string]$Tag,
    [string]$Repository = "alphadelta332/Electronic-Logbook",
    [string]$AssetDirectory,
    [switch]$SkipBuild,
    [string]$CertificateThumbprint,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$Clobber,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $version = (Get-Content (Join-Path $repoRoot "version.txt") -Raw -Encoding UTF8).Trim()
    $Tag = "v$version"
}

if ([string]::IsNullOrWhiteSpace($AssetDirectory)) {
    $AssetDirectory = Join-Path $repoRoot "updater\dist"
}

$publishArgs = @(
    "-File"
    (Join-Path $PSScriptRoot "Publish-WizardAsset.ps1")
    "-OutputDirectory"
    $AssetDirectory
)
if ($SkipBuild) {
    $publishArgs += "-SkipBuild"
}
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $publishArgs += @("-CertificateThumbprint", $CertificateThumbprint)
}
$publishArgs += @("-TimestampServer", $TimestampServer)

Write-Host "Preparing wizard assets for $Tag..."
& powershell -NoProfile @publishArgs

$assets = @(
    (Join-Path $AssetDirectory "ElectronicLogbook.Updater.Wizard.exe")
    (Join-Path $AssetDirectory "ElectronicLogbook.Updater.Wizard.win-x64.zip")
)

$masterWorkbook = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
if (-not (Test-Path $masterWorkbook)) {
    throw "Master workbook not found for release manifest: $masterWorkbook"
}
$version = (Get-Content (Join-Path $repoRoot "version.txt") -Raw -Encoding UTF8).Trim()
$manifestAssets = @($masterWorkbook) + $assets
$manifestCertificateThumbprint = $CertificateThumbprint
if ([string]::IsNullOrWhiteSpace($manifestCertificateThumbprint)) {
    $manifestCertificateThumbprint = (Get-Content (Join-Path $PSScriptRoot "release-signing.json") -Raw | ConvertFrom-Json).sha1Thumbprint
}
& (Join-Path $PSScriptRoot "New-ReleaseManifest.ps1") `
    -Version $version `
    -Tag $Tag `
    -AssetPath $manifestAssets `
    -OutputDirectory $AssetDirectory `
    -CertificateThumbprint $manifestCertificateThumbprint
$tagCommit = (git -C $repoRoot rev-parse "$Tag^{commit}").Trim()
& (Join-Path $PSScriptRoot "New-ModUpdateManifest.ps1") `
    -ModulePath (Join-Path $repoRoot "modUpdate.bas") `
    -OutputDirectory $AssetDirectory `
    -Ref $tagCommit `
    -CertificateThumbprint $manifestCertificateThumbprint
$assets += @(
    (Join-Path $AssetDirectory "release-manifest.json"),
    (Join-Path $AssetDirectory "release-manifest.json.p7s"),
    (Join-Path $AssetDirectory "modUpdate-manifest.json"),
    (Join-Path $AssetDirectory "modUpdate-manifest.json.p7s")
)

foreach ($asset in $assets) {
    if (-not (Test-Path $asset)) {
        throw "Wizard asset not found: $asset"
    }
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is not installed or not on PATH."
}

$uploadArgs = @(
    "release"
    "upload"
    $Tag
) + $assets + @(
    "--repo"
    $Repository
)
if ($Clobber) {
    $uploadArgs += "--clobber"
}

if ($DryRun) {
    Write-Host "Dry run: gh $($uploadArgs -join ' ')" -ForegroundColor Yellow
    exit 0
}

& gh release view $Tag --repo $Repository --json tagName | Out-Null
& gh @uploadArgs

Write-Host "Wizard assets uploaded to $Repository release $Tag." -ForegroundColor Green
