# Orchestrates release preparation without running workbook smoke tests.

[CmdletBinding()]
param(
    [switch]$SkipVbaImport,
    [switch]$SkipPdf,
    [switch]$SkipWorkbookPrep,
    [switch]$SkipWorkingCopy,
    [switch]$SkipVbaParity,
    [switch]$SkipPublicReadinessCheck,
    [switch]$SkipGitChecks
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "=== Electronic Logbook Release Checklist ===" -ForegroundColor Cyan
Write-Host ""

if (-not $SkipGitChecks) {
    $branch = (git -C $repoRoot branch --show-current).Trim()
    if ($branch -ne "dev") {
        throw "Release checklist should be run from dev. Current branch: $branch"
    }

    $unmerged = git -C $repoRoot diff --name-only --diff-filter=U
    if ($unmerged) {
        throw "Unmerged files are present. Resolve conflicts before release prep."
    }

    git -C $repoRoot diff --check
    if ($LASTEXITCODE -ne 0) {
        throw "git diff --check found whitespace/conflict-marker issues."
    }

    $status = git -C $repoRoot status --short
    if ($status) {
        Write-Host "Working tree has changes that should be reviewed before commit:" -ForegroundColor Yellow
        $status | ForEach-Object { Write-Host "  $_" }
        Write-Host ""
    }
}

& (Join-Path $PSScriptRoot "Test-ReleaseMetadata.ps1") -RepoRoot $repoRoot
& (Join-Path $PSScriptRoot "Test-VbaSourceQuality.ps1") -RepoRoot $repoRoot

if (-not $SkipVbaImport) {
    Write-Host ""
    & (Join-Path $PSScriptRoot "ImportVbaIntoWorkbook.ps1")
}

if (-not $SkipPdf) {
    Write-Host ""
    & (Join-Path $repoRoot "GenerateReadmePDF.ps1") -RepoPath $repoRoot
}

if (-not $SkipWorkbookPrep) {
    Write-Host ""
    & (Join-Path $repoRoot "PrepareForRelease.ps1") -SkipWorkingCopy:$SkipWorkingCopy
}

if (-not $SkipVbaParity) {
    Write-Host ""
    & (Join-Path $PSScriptRoot "Test-WorkbookVbaParity.ps1") -RepoRoot $repoRoot
}

if (-not $SkipPublicReadinessCheck) {
    Write-Host ""
    & (Join-Path $PSScriptRoot "Test-WorkbookPublicReadiness.ps1") -RepoRoot $repoRoot
}

Write-Host ""
Write-Host "Automated release prep complete." -ForegroundColor Green
Write-Host "Manual gates still required:"
Write-Host "  1. Test the updated copy in Excel"
Write-Host "  2. Review git diff/status"
Write-Host "  3. Commit to dev"
Write-Host "  4. Open and merge PR from dev to main"
$version = (Get-Content (Join-Path $repoRoot "version.txt") -Raw -Encoding UTF8).Trim()
Write-Host "  5. Tag the release as v$version"
