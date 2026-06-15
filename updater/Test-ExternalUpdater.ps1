# Runs a disposable Excel migration test against the local master workbook.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$masterPath = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
$projectPath = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater"
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "ElectronicLogbookUpdaterE2E-{0}" -f [guid]::NewGuid().ToString("N")
)
$sourcePath = Join-Path $testDirectory "Source.xlsm"
$outputPath = Join-Path $testDirectory "Updated.xlsm"

Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

try {
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    Copy-Item -LiteralPath $masterPath -Destination $sourcePath

    Invoke-WorkbookEdit -WorkbookPath $sourcePath -Operation {
        param($Workbook)

        $logbook = $Workbook.Sheets("Logbook").ListObjects("Logbook")
        $logbook.TableStyle = "TableStyleLight16"
        $logbook.ListColumns("Custom 1").Name = "Updater Test"
        $logbook.ListColumns("Reg").DataBodyRange.Cells(1, 1).Value2 = "TESTREG"
        $newLogbookRow = $logbook.ListRows.Add()
        $newLogbookRow.Range.Cells(1, $logbook.ListColumns("Year").Index).Value2 = 2026
        $newLogbookRow.Range.Cells(1, $logbook.ListColumns("Reg").Index).Value2 = "TESTREG2"

        $keywords = $Workbook.Sheets("Currency + Recency").ListObjects("Keywords")
        $keywords.ListColumns("IPC").DataBodyRange.Cells(1, 1).Value2 = "TEST IPC"

        $airports = $Workbook.Sheets("Airports").ListObjects("Airports")
        $airports.ListColumns("Base").DataBodyRange.Cells(1, 1).Value2 = "Yes"

        $routes = $Workbook.Sheets("Routes").ListObjects("Routes")
        $route = $routes.ListRows.Add()
        $route.Range.Cells(1, 1).Value2 = "YTEST"
        $route.Range.Cells(1, 2).Value2 = "YDEST"

        $Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 = 3
    }

    $sourceHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash
    & dotnet run --project $projectPath --configuration Release -- `
        --source $sourcePath `
        --master $masterPath `
        --output $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "External updater returned exit code $LASTEXITCODE."
    }

    $afterHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash
    if ($sourceHash -ne $afterHash) {
        throw "Source workbook changed during the external update."
    }

    Invoke-WorkbookEdit -WorkbookPath $outputPath -ReadOnly -Operation {
        param($Workbook)

        $logbook = $Workbook.Sheets("Logbook").ListObjects("Logbook")
        $keywords = $Workbook.Sheets("Currency + Recency").ListObjects("Keywords")
        $airports = $Workbook.Sheets("Airports").ListObjects("Airports")
        $routes = $Workbook.Sheets("Routes").ListObjects("Routes")

        if ($logbook.ListColumns.Item(10).Name -ne "Updater Test") {
            throw "Custom Logbook heading was not preserved."
        }
        if ($logbook.ListColumns("Reg").DataBodyRange.Cells(1, 1).Value2 -ne "TESTREG") {
            throw "Logbook entry data was not preserved."
        }
        if ($logbook.ListRows.Count -ne 3) {
            throw "Logbook row count was not preserved."
        }
        if ($logbook.TableStyle.Name -ne "TableStyleLight16") {
            throw "Logbook table style was not preserved."
        }
        if ($logbook.Range.Rows.Hidden) {
            throw "Expanded Logbook rows were left hidden."
        }
        $logbookTotals = $Workbook.Names.Item("LogbookTotals").RefersToRange
        if ($logbookTotals.Rows.Count -ne 2 -or
            $logbookTotals.Row -ne $logbook.TotalsRowRange.Row) {
            throw "LogbookTotals was not anchored to the live two-row totals area."
        }
        if ($logbook.ListColumns("Reg").DataBodyRange.Cells(3, 1).Value2 -ne "TESTREG2") {
            throw "Expanded Logbook entry data was not preserved."
        }
        if ($keywords.ListColumns("IPC").DataBodyRange.Cells(1, 1).Value2 -ne "TEST IPC") {
            throw "Keywords data was not preserved."
        }
        if ($airports.ListColumns("Base").DataBodyRange.Cells(1, 1).Value2 -ne "Yes") {
            throw "Airport base selection was not preserved."
        }
        if ($routes.ListRows.Count -ne 1) {
            throw "Routes data was not preserved."
        }
        if ($Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 -ne 3) {
            throw "Named preference was not preserved."
        }
    }

    Write-Host "External updater disposable migration test passed." -ForegroundColor Green
} finally {
    if (Test-Path $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
