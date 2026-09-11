# Prepares release metadata before workbook packaging and promotion.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$ReleaseDate = (Get-Date -Format "yyyy-MM-dd"),

    [ValidateSet("Fast", "Excel", "Release")]
    [string]$ValidationTier = "Fast",

    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),

    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$manifestPath = Join-Path $repoRoot "versions.properties"
$compatibilityVersionPath = Join-Path $repoRoot "version.txt"
$readmePath = Join-Path $repoRoot "README.md"
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Assert-CleanMergeState {
    $unmerged = git -C $repoRoot diff --name-only --diff-filter=U
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect git merge state."
    }
    if ($unmerged) {
        throw "Unmerged files are present. Resolve conflicts before release preparation."
    }
}

function Set-VersionFile {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "versions.properties not found at $manifestPath"
    }

    $manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    $currentVersion = (ConvertFrom-VersionManifestText -Text $manifestText -Source $manifestPath).SheetVersion
    if ($currentVersion -ne $Version) {
        $updatedManifest = [regex]::Replace(
            $manifestText,
            '(?m)^sheet_version=.*$',
            "sheet_version=$Version")
        [IO.File]::WriteAllText($manifestPath, $updatedManifest, [Text.UTF8Encoding]::new($false))
        Write-Host "versions.properties sheet_version: $currentVersion -> $Version" -ForegroundColor Green
    } else {
        Write-Host "versions.properties sheet_version already set to $Version." -ForegroundColor Green
    }

    [IO.File]::WriteAllText($compatibilityVersionPath, $Version, [Text.UTF8Encoding]::new($false))
    Write-Host "version.txt compatibility value = $Version" -ForegroundColor Green
}

function Add-ChangelogSkeleton {
    if (-not (Test-Path -LiteralPath $readmePath)) {
        throw "README.md not found at $readmePath"
    }

    $readme = Get-Content -LiteralPath $readmePath -Raw -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $headingPattern = '(?m)^### \[' + $escapedVersion + '\] - (?<date>\d{4}-\d{2}-\d{2})$'
    $existingHeading = [regex]::Match($readme, $headingPattern)
    if ($existingHeading.Success) {
        Write-Host "README.md already has changelog heading for $Version ($($existingHeading.Groups['date'].Value))." -ForegroundColor Green
        return
    }

    $changelogHeadingPattern = "(?m)^## Changelog\s*$"
    $changelogHeading = [regex]::Match($readme, $changelogHeadingPattern)
    if (-not $changelogHeading.Success) {
        throw "README.md does not contain a '## Changelog' section."
    }

    $insertAt = $changelogHeading.Index + $changelogHeading.Length
    $skeleton = @"

### [$Version] - $ReleaseDate
#### General
- TBD

"@

    $updatedReadme = $readme.Insert($insertAt, $skeleton)
    [System.IO.File]::WriteAllText($readmePath, $updatedReadme, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Inserted README.md changelog skeleton for $Version." -ForegroundColor Green
}

function Test-ReleaseDocumentation {
    $versionText = (Get-VersionManifest -RepoRoot $repoRoot).SheetVersion
    if ($versionText -ne $Version) {
        throw "versions.properties sheet_version contains '$versionText', expected '$Version'."
    }

    $readmeLines = Get-Content -LiteralPath $readmePath -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $headingPattern = '^### \[' + $escapedVersion + '\] - \d{4}-\d{2}-\d{2}$'
    $headings = @($readmeLines | Where-Object { $_ -match $headingPattern })
    if ($headings.Count -ne 1) {
        throw "README.md must contain exactly one changelog heading for $Version. Found $($headings.Count)."
    }

    $firstVersionHeading = $readmeLines | Where-Object { $_ -match "^### \[[0-9]+\.[0-9]+\.[0-9]+\] - \d{4}-\d{2}-\d{2}$" } | Select-Object -First 1
    if (-not ([string]$firstVersionHeading).StartsWith("### [$Version] - ", [System.StringComparison]::Ordinal)) {
        throw "README.md changelog for $Version is not the first release entry under ## Changelog."
    }

    Write-Host "Release documentation is consistent for $Version." -ForegroundColor Green
}

Write-Step "Release metadata"
Assert-CleanMergeState
Set-VersionFile
Add-ChangelogSkeleton
Test-ReleaseDocumentation

if (-not $SkipValidation) {
    Write-Step "$ValidationTier validation"
    & (Join-Path $repoRoot "tools\Invoke-Validation.ps1") -Tier $ValidationTier -RepoRoot $repoRoot
}

Write-Host ""
Write-Host "Release preparation metadata complete for $Version." -ForegroundColor Green
Write-Host "Review README.md changelog text before packaging or promotion."
