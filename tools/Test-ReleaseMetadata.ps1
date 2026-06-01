# Static release checks that do not open Excel or modify workbooks.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

$version = Get-ReleaseVersion -RepoRoot $repoRoot
$readmePath = Join-Path $repoRoot "README.md"
$pdfPath = Join-Path $repoRoot "README.pdf"
$publicDocs = @("LICENSE.md", "SECURITY.md", "CONTRIBUTING.md")

if (-not (Test-Path $readmePath)) {
    throw "README.md not found."
}

foreach ($doc in $publicDocs) {
    $docPath = Join-Path $repoRoot $doc
    if (-not (Test-Path $docPath)) {
        throw "$doc not found."
    }
}

$readmeLines = Get-Content $readmePath -Encoding UTF8
$changelogHeadingPattern = "^### \[$([regex]::Escape($version))\] - \d{4}-\d{2}-\d{2}$"
if (-not ($readmeLines | Where-Object { $_ -match $changelogHeadingPattern })) {
    throw "README.md changelog does not contain a heading for version $version."
}

if (-not (Test-Path $pdfPath)) {
    throw "README.pdf not found."
}

$moduleExpectations = @{
    "modBoot.bas" = 'Attribute VB_Name = "modBoot"'
    "modLogbook.bas" = 'Attribute VB_Name = "modLogbook"'
    "modUpdate.bas" = 'Attribute VB_Name = "modUpdate"'
}

foreach ($entry in $moduleExpectations.GetEnumerator()) {
    $path = Join-Path $repoRoot $entry.Key
    if (-not (Test-Path $path)) {
        throw "$($entry.Key) not found."
    }

    $firstLine = Get-Content $path -TotalCount 1 -Encoding UTF8
    if ($firstLine -ne $entry.Value) {
        throw "$($entry.Key) has unexpected first line. Expected: $($entry.Value)"
    }
}

$thisWorkbookPath = Join-Path $repoRoot "ThisWorkbook.cls"
if (-not (Test-Path $thisWorkbookPath)) {
    throw "ThisWorkbook.cls not found."
}

$thisWorkbook = Get-Content $thisWorkbookPath -Raw -Encoding UTF8
if ($thisWorkbook -notmatch "Private Sub Workbook_Open\(\)") {
    throw "ThisWorkbook.cls does not contain Workbook_Open."
}

Write-Host "Static release metadata checks passed for version $version." -ForegroundColor Green
