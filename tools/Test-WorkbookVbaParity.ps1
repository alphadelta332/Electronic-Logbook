# Checks that embedded workbook VBA matches tracked source without modifying the source workbook.
# Requires Microsoft Excel and Trust Center access to the VBA project object model.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$WorkbookPath
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}
$workbookPath = (Resolve-Path $WorkbookPath).Path
$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    "ElectronicLogbookParity-{0}.xlsm" -f [guid]::NewGuid().ToString("N")
)
$componentNames = @("modBoot", "modLogbook", "ThisWorkbook", "frmVerifyCurrency")
$issues = New-Object System.Collections.Generic.List[string]

function Get-WorkbookVbaSnapshot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string[]]$Components,
        [switch]$CheckForEmbeddedUpdater
    )

    $snapshot = @{}
    Invoke-WorkbookEdit -WorkbookPath $Path -ReadOnly -Operation {
        param($Workbook)

        Assert-VbaProjectAccess -Workbook $Workbook
        $vbaComponents = $Workbook.VBProject.VBComponents

        if ($CheckForEmbeddedUpdater) {
            try {
                $null = $vbaComponents.Item("modUpdate")
                $issues.Add("modUpdate is embedded in $($Workbook.Name); release workbooks must download it at runtime.")
            } catch {}
        }

        foreach ($componentName in $Components) {
            try {
                $component = $vbaComponents.Item($componentName)
            } catch {
                $issues.Add("Workbook component '$componentName' is missing from $($Workbook.Name).")
                continue
            }

            $codeModule = $component.CodeModule
            if ($codeModule.CountOfLines -gt 0) {
                $snapshot[$componentName] = $codeModule.Lines(1, $codeModule.CountOfLines)
            } else {
                $snapshot[$componentName] = ""
            }
        }
    }
    return $snapshot
}

try {
    Copy-Item -LiteralPath $workbookPath -Destination $tempPath -Force

    $embeddedSnapshot = Get-WorkbookVbaSnapshot `
        -Path $workbookPath `
        -Components $componentNames `
        -CheckForEmbeddedUpdater

    & (Join-Path $repoRoot "tools\ImportVbaIntoWorkbook.ps1") -WorkbookPath $tempPath
    $trackedSnapshot = Get-WorkbookVbaSnapshot -Path $tempPath -Components $componentNames

    foreach ($componentName in $componentNames) {
        if (-not $embeddedSnapshot.ContainsKey($componentName) -or
            -not $trackedSnapshot.ContainsKey($componentName)) {
            continue
        }

        if ($embeddedSnapshot[$componentName] -cne $trackedSnapshot[$componentName]) {
            $issues.Add("Workbook component '$componentName' differs from tracked source.")
        }
    }
} finally {
    if (Test-Path $tempPath) {
        Remove-Item -LiteralPath $tempPath -Force
    }
}

if ($issues.Count -gt 0) {
    throw "Workbook VBA parity checks failed:`n - " + ($issues -join "`n - ")
}

Write-Host "Workbook VBA matches tracked release source." -ForegroundColor Green
