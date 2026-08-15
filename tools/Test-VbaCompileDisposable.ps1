# Imports tracked VBA source into a disposable workbook copy and forces VBA to compile it.

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

        # CommandBarControl.Enabled is not a reliable compile signal in current Excel:
        # command ID 578 remains enabled after Execute even for a valid project. Running
        # a temporary public function forces VBA to compile the entire project first.
        # A syntax error in any other module prevents this probe from running.
        $probeComponent = $workbook.VBProject.VBComponents.Add(1)
        $probeComponent.Name = "ELBCompileProbe"
        $probeComponent.CodeModule.AddFromString(@"
Option Explicit

Public Function ELBDisposableCompileProbe() As Boolean
    ELBDisposableCompileProbe = True
End Function
"@)

        $probeResult = $excel.Run("'" + $workbook.Name + "'!ELBDisposableCompileProbe")
        if ($probeResult -ne $true) {
            throw "Disposable VBA compile probe returned an unexpected result: $probeResult"
        }

        Write-Host "Disposable VBA compile pass complete." -ForegroundColor Green
        Write-Host "  Project-wide compile probe passed."
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
