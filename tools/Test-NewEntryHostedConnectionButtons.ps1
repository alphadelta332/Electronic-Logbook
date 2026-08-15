# Verifies the hosted account connection button survives both New Entry layouts.
# The source workbook is never modified; all layout switching happens in a disposable copy.

[CmdletBinding()]
param(
    [string]$WorkbookPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "Electronic_Logbook_Master.xlsm")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $WorkbookPath)) {
    throw "Workbook not found: $WorkbookPath"
}

$resolvedWorkbookPath = (Resolve-Path -LiteralPath $WorkbookPath).Path
$tempWorkbookPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    "ElectronicLogbook-NewEntryConnection-{0}.xlsm" -f [guid]::NewGuid().ToString("N")
)

function Test-ConnectionAction {
    param(
        [AllowEmptyString()]
        [string]$Action
    )

    if ([string]::IsNullOrWhiteSpace($Action)) {
        return $false
    }

    return $Action.Trim().EndsWith("ConnectToElectronicLogbook", [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NewEntryConnectionLayout {
    param(
        [Parameter(Mandatory)]
        $Workbook,
        [Parameter(Mandatory)]
        [ValidateSet("Compact", "Grouped")]
        [string]$ExpectedActiveLayout
    )

    $expectedInactiveLayout = if ($ExpectedActiveLayout -eq "Compact") { "Grouped" } else { "Compact" }
    $expectedLayoutId = if ($ExpectedActiveLayout -eq "Compact") { 1 } else { 2 }
    $expectations = @(
        [pscustomobject]@{ SheetName = "New Entry"; ExpectedLayout = $ExpectedActiveLayout; ExpectedVisible = -1 },
        [pscustomobject]@{ SheetName = "New Entry Unused Layout"; ExpectedLayout = $expectedInactiveLayout; ExpectedVisible = 2 }
    )

    $configuredLayoutName = $null
    try {
        $configuredLayoutName = $Workbook.Names.Item("NewEntryLayout")
        if ([int]$configuredLayoutName.RefersToRange.Value2 -ne $expectedLayoutId) {
            throw "NewEntryLayout was not updated to $expectedLayoutId for $ExpectedActiveLayout."
        }
    }
    finally {
        if ($null -ne $configuredLayoutName) {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($configuredLayoutName) | Out-Null
        }
    }

    foreach ($expectation in $expectations) {
        $worksheet = $null
        $layoutName = $null
        $button = $null
        try {
            $worksheet = $Workbook.Worksheets.Item($expectation.SheetName)
            if ([int]$worksheet.Visible -ne $expectation.ExpectedVisible) {
                throw "$($expectation.SheetName) visibility was $($worksheet.Visible); expected $($expectation.ExpectedVisible)."
            }

            $layoutName = $worksheet.Names.Item("NewEntryLayoutKind")
            $actualLayout = ([string]$layoutName.RefersTo).Trim('=', '"')
            if ($actualLayout -ne $expectation.ExpectedLayout) {
                throw "$($expectation.SheetName) held layout '$actualLayout'; expected '$($expectation.ExpectedLayout)'."
            }

            $button = $worksheet.Shapes.Item("ConnectToElectronicLogbookButton")
            if ($button.Type -ne 6) {
                throw "$($expectation.SheetName) connection control is not a grouped shape."
            }
            if ([int]$button.Visible -ne -1) {
                throw "$($expectation.SheetName) connection control is not visible on its physical layout."
            }

            $groupAction = [string]$button.OnAction
            if (-not [string]::IsNullOrWhiteSpace($groupAction) -and
                -not (Test-ConnectionAction -Action $groupAction)) {
                throw "$($expectation.SheetName) connection group has the wrong action."
            }

            for ($index = 1; $index -le $button.GroupItems.Count; $index++) {
                $item = $null
                try {
                    $item = $button.GroupItems.Item($index)
                    if (-not (Test-ConnectionAction -Action ([string]$item.OnAction))) {
                        throw "$($expectation.SheetName) connection group item $index has the wrong action."
                    }
                }
                finally {
                    if ($null -ne $item) {
                        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($item) | Out-Null
                    }
                }
            }
        }
        finally {
            if ($null -ne $button) {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($button) | Out-Null
            }
            if ($null -ne $layoutName) {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($layoutName) | Out-Null
            }
            if ($null -ne $worksheet) {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($worksheet) | Out-Null
            }
        }
    }
}

Copy-Item -LiteralPath $resolvedWorkbookPath -Destination $tempWorkbookPath -Force

$excel = $null
$workbook = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    try {
        $excel.AutomationSecurity = 1
    }
    catch {}

    $workbook = $excel.Workbooks.Open($tempWorkbookPath, $false, $false)
    $macroPrefix = "'$($workbook.Name)'!"

    $excel.Run($macroPrefix + "SetCompactView") | Out-Null
    Assert-NewEntryConnectionLayout -Workbook $workbook -ExpectedActiveLayout "Compact"

    $excel.Run($macroPrefix + "SetGroupedView") | Out-Null
    Assert-NewEntryConnectionLayout -Workbook $workbook -ExpectedActiveLayout "Grouped"

    $excel.Run($macroPrefix + "SetCompactView") | Out-Null
    Assert-NewEntryConnectionLayout -Workbook $workbook -ExpectedActiveLayout "Compact"

    Write-Host "New Entry hosted connection button checks passed." -ForegroundColor Green
    Write-Host "  Compact -> Grouped -> Compact"
    Write-Host "  Both physical buttons retained ConnectToElectronicLogbook on every clickable group item."
}
finally {
    if ($null -ne $workbook) {
        try { $workbook.Close($false) } catch {}
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) | Out-Null
    }
    if ($null -ne $excel) {
        try { $excel.Quit() } catch {}
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()

    if (Test-Path -LiteralPath $tempWorkbookPath) {
        Remove-Item -LiteralPath $tempWorkbookPath -Force -ErrorAction SilentlyContinue
    }
}
