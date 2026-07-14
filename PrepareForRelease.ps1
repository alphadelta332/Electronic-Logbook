# PrepareForRelease.ps1
# Sets release workbook state before merging dev to main.

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

Write-Host "=== Prepare for Release ===" -ForegroundColor Cyan
Write-Host "Version source: version.txt = $version"
Write-Host ""

Set-LogbookWorkbookState -WorkbookPath $WorkbookPath -Branch "main" -Version $version
Invoke-WorkbookMacro -WorkbookPath $WorkbookPath -MacroName "EnableProtectionForRelease" -IgnoreMissing
Set-WorkbookOpenView -WorkbookPath $WorkbookPath
# The macro and open-view steps save through Excel, which can add the local Office
# identity back into core properties. This must be the final write to the public
# workbook package.
Set-WorkbookCustomPropertyFileValue -WorkbookPath $WorkbookPath -Name "ElectronicLogbookVersion" -Value $version

if (-not $SkipWorkingCopy -and -not [string]::IsNullOrWhiteSpace($WorkingCopyPath)) {
    Write-Host ""
    Set-LogbookWorkbookState -WorkbookPath $WorkingCopyPath -Branch "main"
    Invoke-WorkbookMacro -WorkbookPath $WorkingCopyPath -MacroName "EnableProtectionForRelease" -IgnoreMissing
    Set-WorkbookOpenView -WorkbookPath $WorkingCopyPath
} elseif (-not $SkipWorkingCopy) {
    Write-Host ""
    Write-Host "No working-copy workbook configured. Add release.local.json or pass -WorkingCopyPath to include it." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Master workbook state set to branch 'main' and version '$version'." -ForegroundColor Green
Write-Host "Release protection mode persists while GitHubBranch is 'main'." -ForegroundColor Green
Write-Host "Working copy branch set to 'main' when configured; its LogbookVersion is left unchanged." -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  1. Run GenerateReadmePDF.ps1 if README.md changed"
Write-Host "  2. Commit release files on dev"
Write-Host "  3. Open a PR from dev to main"
