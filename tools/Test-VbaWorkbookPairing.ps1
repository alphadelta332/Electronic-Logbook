# Verifies embedded VBA source edits are paired with the master workbook.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$BaseRef,
    [string]$HeadRef,
    [switch]$RequireVbaSourceForWorkbookChange
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$embeddedVbaSourceFiles = @(
    "modBoot.bas",
    "modAirports.bas",
    "modLogbook.bas",
    "ThisWorkbook.cls"
)
$runtimeVbaSourceFiles = @(
    "modUpdate.bas"
)
$masterWorkbook = "Electronic_Logbook_Master.xlsm"

function ConvertTo-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return $Path.Replace("/", "\")
}

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

function Get-GitRangeChangedPaths {
    param(
        [string]$Root,
        [string]$Base,
        [string]$Head,
        [string[]]$Paths
    )

    $changed = New-Object System.Collections.Generic.HashSet[string]
    $diffNames = git -C $Root diff --name-only --find-renames $Base $Head -- $Paths
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect git diff for VBA/workbook pairing between $Base and $Head."
    }

    foreach ($name in $diffNames) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$changed.Add((ConvertTo-RepoPath $name))
        }
    }

    return @($changed)
}

function Get-GitHubComparisonRefs {
    param(
        [string]$Root
    )

    if ($env:GITHUB_EVENT_PATH -and (Test-Path $env:GITHUB_EVENT_PATH)) {
        $event = Get-Content $env:GITHUB_EVENT_PATH -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($env:GITHUB_EVENT_NAME -eq "pull_request" -or $env:GITHUB_EVENT_NAME -eq "pull_request_target") {
            if ($event.pull_request.base.sha -and $event.pull_request.head.sha) {
                $mergeBase = (git -C $Root merge-base $event.pull_request.base.sha $event.pull_request.head.sha).Trim()
                if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mergeBase)) {
                    throw "Could not resolve PR merge base for VBA/workbook pairing."
                }

                return @{
                    Base = $mergeBase
                    Head = $event.pull_request.head.sha
                    Label = "PR merge base $mergeBase to head $($event.pull_request.head.sha)"
                }
            }
        }

        if ($env:GITHUB_EVENT_NAME -eq "push") {
            if ($event.before -and $event.after -and $event.before -notmatch "^0{40}$") {
                return @{
                    Base = $event.before
                    Head = $event.after
                    Label = "push range $($event.before) to $($event.after)"
                }
            }
        }
    }

    return $null
}

function Test-PairingChangeSet {
    param(
        [string[]]$ChangedPaths,
        [string]$Label
    )

    $changedVba = @($embeddedVbaSourceFiles | Where-Object { $ChangedPaths -contains $_ })
    $workbookChanged = $ChangedPaths -contains $masterWorkbook

    if ($changedVba.Count -gt 0 -and -not $workbookChanged) {
        throw @"
Embedded VBA source changed in $Label but $masterWorkbook is not changed.

Changed VBA source:
 - $($changedVba -join "`n - ")

Run this before finishing the change:
  .\tools\ImportVbaIntoWorkbook.ps1 -WorkbookPath .\$masterWorkbook

Embedded VBA source and the master .xlsm must stay paired for every master-workbook VBA change.
"@
    }

    if ($RequireVbaSourceForWorkbookChange -and $workbookChanged -and $changedVba.Count -eq 0) {
        throw @"
$masterWorkbook changed in $Label but no embedded VBA source file changed.

If the workbook change contains VBA edits, export the matching source before finishing:
  .\tools\ExportVbaFromWorkbook.ps1 -WorkbookPath .\$masterWorkbook

If this is an intentional non-VBA workbook change, rerun without -RequireVbaSourceForWorkbookChange.
"@
    }
}

$pathsToCheck = $embeddedVbaSourceFiles + $runtimeVbaSourceFiles + $masterWorkbook

if (-not [string]::IsNullOrWhiteSpace($BaseRef) -xor -not [string]::IsNullOrWhiteSpace($HeadRef)) {
    throw "Provide both -BaseRef and -HeadRef, or neither."
}

if ([string]::IsNullOrWhiteSpace($BaseRef) -and [string]::IsNullOrWhiteSpace($HeadRef)) {
    $githubRefs = Get-GitHubComparisonRefs -Root $repoRoot
    if ($githubRefs) {
        $BaseRef = $githubRefs.Base
        $HeadRef = $githubRefs.Head
        $comparisonLabel = $githubRefs.Label
    }
}
else {
    $comparisonLabel = "$BaseRef to $HeadRef"
}

if (-not [string]::IsNullOrWhiteSpace($BaseRef) -and -not [string]::IsNullOrWhiteSpace($HeadRef)) {
    $rangeChangedPaths = Get-GitRangeChangedPaths -Root $repoRoot -Base $BaseRef -Head $HeadRef -Paths $pathsToCheck
    Test-PairingChangeSet -ChangedPaths $rangeChangedPaths -Label $comparisonLabel
}

$changedPaths = Get-GitChangedPaths -Root $repoRoot -Paths $pathsToCheck
Test-PairingChangeSet -ChangedPaths $changedPaths -Label "local working tree"

Write-Host "VBA/workbook pairing check passed." -ForegroundColor Green
