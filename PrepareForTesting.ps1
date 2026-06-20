# PrepareForTesting.ps1
# Sets workbook state for testing updates from the dev branch.

[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$WorkingCopyPath,
    [switch]$SkipWorkingCopy
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
$version = Get-ReleaseVersion -RepoRoot $repoRoot

if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}

if ([string]::IsNullOrWhiteSpace($WorkingCopyPath)) {
    $WorkingCopyPath = $config.WorkingCopyWorkbook
}

Write-Host "=== Prepare for Testing ===" -ForegroundColor Cyan
Write-Host "Version source: version.txt = $version"
Write-Host ""

Set-LogbookWorkbookState -WorkbookPath $WorkbookPath -Branch "dev" -Version $version
Invoke-WorkbookMacro -WorkbookPath $WorkbookPath -MacroName "DisableProtectionForDevelopment" -IgnoreMissing

if (-not $SkipWorkingCopy -and -not [string]::IsNullOrWhiteSpace($WorkingCopyPath)) {
    Write-Host ""
    Set-LogbookWorkbookState -WorkbookPath $WorkingCopyPath -Branch "dev"
    Invoke-WorkbookMacro -WorkbookPath $WorkingCopyPath -MacroName "DisableProtectionForDevelopment" -IgnoreMissing
} elseif (-not $SkipWorkingCopy) {
    Write-Host ""
    Write-Host "No working-copy workbook configured. Add release.local.json or pass -WorkingCopyPath to include it." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Master workbook state set to branch 'dev' and version '$version'." -ForegroundColor Green
Write-Host "Development protection mode disabled when the macro is available." -ForegroundColor Green
Write-Host "Working copy branch set to 'dev' when configured; its LogbookVersion is left unchanged." -ForegroundColor Green
