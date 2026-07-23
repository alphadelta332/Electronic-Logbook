[CmdletBinding()]
param(
    [string]$Tag,
    [string]$Repository = "alphadelta332/Electronic-Logbook",
    [string]$AssetDirectory,
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$ExpectedPublisher,
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
if ($Sign) {
    $publishArgs += @(
        "-Sign"
        "-CertificateThumbprint"
        $CertificateThumbprint
    )
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
        $publishArgs += @("-ExpectedPublisher", $ExpectedPublisher)
    }
    $publishArgs += @(
        "-TimestampServer"
        $TimestampServer
    )
}

Write-Host "Preparing wizard assets for $Tag..."
& powershell -NoProfile @publishArgs

$assets = @(
    (Join-Path $AssetDirectory "ElectronicLogbook.Updater.Wizard.exe")
    (Join-Path $AssetDirectory "ElectronicLogbook.Updater.Wizard.win-x64.zip")
    (Join-Path $AssetDirectory "wizard-signature-report.json")
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
