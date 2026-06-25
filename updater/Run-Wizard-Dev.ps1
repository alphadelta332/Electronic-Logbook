[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$OutputPath,
    [string]$MasterPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "Electronic_Logbook_Master.xlsm"),
    [string]$Repository = "alphadelta332/Electronic-Logbook",
    [switch]$UseReleaseChannel
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

$dotnetArgs = @(
    "run"
    "--project"
    $wizardProject
    "--"
    "--source"
    $SourcePath
    "--output"
    $OutputPath
)

if ($UseReleaseChannel) {
    $dotnetArgs += @("--repo", $Repository)
    Write-Host "Launching wizard in STABLE channel mode" -ForegroundColor Yellow
    Write-Host "  repo: $Repository" -ForegroundColor Yellow
} else {
    if (-not (Test-Path $MasterPath)) {
        throw "Local master workbook not found: $MasterPath"
    }

    $dotnetArgs += @("--master", $MasterPath)
    Write-Host "Launching wizard in LOCAL MASTER mode" -ForegroundColor Cyan
    Write-Host "  master: $MasterPath" -ForegroundColor Cyan
}

Write-Host "  source: $SourcePath" -ForegroundColor Cyan
Write-Host "  output: $OutputPath" -ForegroundColor Cyan

& dotnet @dotnetArgs
