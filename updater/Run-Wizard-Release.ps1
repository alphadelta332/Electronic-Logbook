[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$OutputPath,
    [string]$Repository = "alphadelta332/Electronic-Logbook"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." )).Path
$wizardProject = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater.Wizard"

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Electronic Logbook - Working Copy.xlsm"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $sourceDirectory = Split-Path $SourcePath -Parent
    $sourceBaseName = [System.IO.Path]::GetFileNameWithoutExtension($SourcePath)
    $OutputPath = Join-Path $sourceDirectory ("{0}_Updated_{1}.xlsm" -f $sourceBaseName, (Get-Date -Format "yyyyMMdd-HHmmss"))
}

if (-not (Test-Path $SourcePath)) {
    throw "Source workbook not found: $SourcePath"
}

Write-Host "Launching wizard in STABLE channel mode" -ForegroundColor Yellow
Write-Host "  repo: $Repository" -ForegroundColor Yellow
Write-Host "  source: $SourcePath" -ForegroundColor Yellow
Write-Host "  output: $OutputPath" -ForegroundColor Yellow

& dotnet run --project $wizardProject -- `
    --source $SourcePath `
    --output $OutputPath `
    --repo $Repository
