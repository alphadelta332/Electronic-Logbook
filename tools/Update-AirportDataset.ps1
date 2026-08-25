# Refreshes the bundled Airports table from the workbook's VBA airport updater.

[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [switch]$Visible
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot "ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}

Write-Host "Refreshing airport dataset in: $WorkbookPath"

$resolvedPath = (Resolve-Path $WorkbookPath).Path
$excel = $null
$workbook = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = [bool]$Visible
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    try { $excel.AutomationSecurity = 1 } catch {}

    $workbook = $excel.Workbooks.Open($resolvedPath, $false, $false)

    foreach ($worksheet in $workbook.Worksheets) {
        $worksheet.Unprotect("")
    }
    $workbook.Unprotect("")

    $beforeRows = $workbook.Worksheets.Item("Airports").ListObjects.Item("Airports").ListRows.Count
    $changed = $excel.Run("'$($workbook.Name)'!RefreshAirportDataset", $workbook, $true)
    $afterRows = $workbook.Worksheets.Item("Airports").ListObjects.Item("Airports").ListRows.Count

    if ($changed) {
        $excel.Run("'$($workbook.Name)'!MarkRoutesDirty", $workbook)
    }

    $excel.Run("'$($workbook.Name)'!EnsureWorkbookProtectionOnOpen")

    Write-Host "  Rows before: $beforeRows"
    Write-Host "  Rows after:  $afterRows"
    Write-Host "  Changed:     $changed"
    $workbook.Save()
} finally {
    if ($null -ne $workbook) {
        $workbook.Close($false)
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
    }
    if ($null -ne $excel) {
        $excel.Quit()
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
}

Write-Host "Airport dataset refresh complete." -ForegroundColor Green

& (Join-Path $PSScriptRoot "Export-MobileAirportDataset.ps1") -WorkbookPath $resolvedPath
