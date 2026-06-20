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
$maxAttempts = 3
$updaterDllPath = Join-Path $projectPath "bin\Release\net8.0-windows\ElectronicLogbook.Updater.dll"

Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

function Write-Step {
    param([string]$Message)
    Write-Host "[Test-ExternalUpdater] $Message" -ForegroundColor Cyan
}

try {
    Write-Step "Preparing disposable test workspace"
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    Copy-Item -LiteralPath $masterPath -Destination $sourcePath

    Write-Step "Seeding source workbook with known test data"
    Invoke-WorkbookEdit -WorkbookPath $sourcePath -Operation {
        param($Workbook)

        $logbook = $Workbook.Sheets("Logbook").ListObjects("Logbook")
        $logbook.TableStyle = $Workbook.TableStyles.Item("TableStyleLight16")
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

    Write-Step "Building updater (Release)"
    & dotnet build $projectPath --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build updater in Release configuration."
    }
    if (-not (Test-Path $updaterDllPath)) {
        throw "Updater binary not found at expected path: $updaterDllPath"
    }

    $sourceHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash
    $updaterSucceeded = $false
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Write-Step "Running updater attempt $attempt of $maxAttempts"
        if (Test-Path $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }

        $updaterLines = @()
        & dotnet $updaterDllPath `
            --source $sourcePath `
            --master $masterPath `
            --output $outputPath 2>&1 | Tee-Object -Variable updaterLines
        $exitCode = $LASTEXITCODE
        $updaterOutput = ($updaterLines | Out-String)

        if ($exitCode -eq 0) {
            Write-Step "Updater completed successfully"
            $updaterSucceeded = $true
            break
        }

        $looksTransientComFailure = $updaterOutput -match "0x800706BE|0x800706BA|remote procedure call|RPC server is unavailable"
        if ($looksTransientComFailure -and $attempt -lt $maxAttempts) {
            Write-Host "Transient Excel COM failure detected. Retrying..." -ForegroundColor Yellow
            continue
        }

        throw "External updater returned exit code $exitCode on attempt $attempt."
    }

    if (-not $updaterSucceeded) {
        throw "External updater failed after $maxAttempts attempts."
    }

    $afterHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash
    if ($sourceHash -ne $afterHash) {
        throw "Source workbook changed during the external update."
    }

    Write-Step "Validating updated workbook content"
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

    Write-Step "Validation complete"
    Write-Host "External updater disposable migration test passed." -ForegroundColor Green
} finally {
    Write-Step "Cleaning up temporary files"
    if (Test-Path $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
