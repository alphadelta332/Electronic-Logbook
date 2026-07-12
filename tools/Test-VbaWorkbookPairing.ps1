# Verifies tracked VBA source edits are paired with the master workbook.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$vbaSourceFiles = @(
    "modBoot.bas",
    "modAirports.bas",
    "modLogbook.bas",
    "modUpdate.bas",
    "ThisWorkbook.cls"
)
$masterWorkbook = "Electronic_Logbook_Master.xlsm"

function Get-GitChangedPaths {
    param(
        [string]$Root,
        [string[]]$Paths
    )

    $changed = New-Object System.Collections.Generic.HashSet[string]

    $diffNames = git -C $Root diff --name-only HEAD -- $Paths
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect git diff for VBA/workbook pairing."
    }
    foreach ($name in $diffNames) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$changed.Add($name.Replace("/", "\"))
        }
    }

    $untrackedNames = git -C $Root ls-files --others --exclude-standard -- $Paths
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect untracked files for VBA/workbook pairing."
    }
    foreach ($name in $untrackedNames) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$changed.Add($name.Replace("/", "\"))
        }
    }

    return @($changed)
}

$changedPaths = Get-GitChangedPaths -Root $repoRoot -Paths ($vbaSourceFiles + $masterWorkbook)
$changedVba = @($vbaSourceFiles | Where-Object { $changedPaths -contains $_ })
$workbookChanged = $changedPaths -contains $masterWorkbook

if ($changedVba.Count -gt 0 -and -not $workbookChanged) {
    throw @"
Tracked VBA source changed but $masterWorkbook is not changed.

Changed VBA source:
 - $($changedVba -join "`n - ")

Run this before finishing the change:
  .\tools\ImportVbaIntoWorkbook.ps1 -WorkbookPath .\$masterWorkbook

VBA source and the master .xlsm must stay paired for every master-workbook VBA change.
"@
}

Write-Host "VBA/workbook pairing check passed." -ForegroundColor Green
