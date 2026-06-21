# Static quality checks for tracked VBA source. Does not open Excel.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$sourceFiles = @(
    "modBoot.bas",
    "modLogbook.bas",
    "modUpdate.bas",
    "ThisWorkbook.cls",
    "frmVerifyCurrency.frm"
)
$issues = New-Object System.Collections.Generic.List[string]

foreach ($relativePath in $sourceFiles) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $path)) {
        $issues.Add("$relativePath is missing.")
        continue
    }

    $source = Get-Content $path -Raw -Encoding UTF8
    if ($source -notmatch "(?m)^Option Explicit\s*$") {
        $issues.Add("$relativePath does not contain Option Explicit.")
    }

    $procedureNames = @(
        [regex]::Matches(
            $source,
            "(?im)^\s*(?:Public\s+|Private\s+|Friend\s+)?(?:Sub|Function)\s+([A-Za-z_][A-Za-z0-9_]*)"
        ) | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() }
    )
    foreach ($duplicate in @($procedureNames | Group-Object | Where-Object Count -gt 1)) {
        $issues.Add("$relativePath contains duplicate procedure name '$($duplicate.Name)'.")
    }
}

if ($issues.Count -gt 0) {
    throw "VBA source quality checks failed:`n - " + ($issues -join "`n - ")
}

Write-Host "VBA source quality checks passed." -ForegroundColor Green
