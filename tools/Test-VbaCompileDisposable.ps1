# Imports tracked VBA source into a disposable workbook copy and runs Debug > Compile.

[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [switch]$KeepTempWorkbook
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
}

if (-not (Test-Path $WorkbookPath)) {
    throw "Workbook not found: $WorkbookPath"
}

$tempWorkbook = Join-Path $env:TEMP ("ELB_compile_" + (Get-Date -Format yyyyMMdd_HHmmss) + ".xlsm")
Copy-Item $WorkbookPath $tempWorkbook -Force

try {
    & (Join-Path $repoRoot "tools\ImportVbaIntoWorkbook.ps1") -WorkbookPath $tempWorkbook -IncludeModUpdate

    $excel = $null
    $workbook = $null
    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
        $excel.EnableEvents = $false
        try {
            # Keep macros available for VBE compile while EnableEvents prevents
            # Workbook_Open prompts from blocking unattended validation.
            $excel.AutomationSecurity = 1
        } catch {}

        $workbook = $excel.Workbooks.Open($tempWorkbook, $false, $false)

        $compile = $excel.VBE.CommandBars.Item("Menu Bar").Controls.Item("Debug").Controls |
            Where-Object { $_.Id -eq 578 } |
            Select-Object -First 1

        if ($null -eq $compile) {
            throw "VBE compile command was not found."
        }

        $firstEnabled = [bool]$compile.Enabled
        if ($firstEnabled) {
            $compile.Execute() | Out-Null
        }

        $compileAfter = $excel.VBE.CommandBars.Item("Menu Bar").Controls.Item("Debug").Controls |
            Where-Object { $_.Id -eq 578 } |
            Select-Object -First 1
        if ($null -eq $compileAfter) {
            throw "VBE compile command was not found after execution."
        }

        $secondEnabled = [bool]$compileAfter.Enabled
        Write-Host "Disposable VBA compile pass complete." -ForegroundColor Green
        Write-Host "  FirstEnabled=$firstEnabled"
        Write-Host "  SecondEnabled=$secondEnabled"

        if ($secondEnabled) {
            throw "Compile command remained enabled after execution; check VBA project for unresolved compile issues."
        }
    }
    finally {
        if ($null -ne $workbook) {
            try { $workbook.Close($false) } catch {}
            [System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
        }
        if ($null -ne $excel) {
            try { $excel.Quit() } catch {}
            [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
        }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
    }
}
finally {
    if (-not $KeepTempWorkbook -and (Test-Path $tempWorkbook)) {
        Remove-Item $tempWorkbook -Force -ErrorAction SilentlyContinue
    }
}
