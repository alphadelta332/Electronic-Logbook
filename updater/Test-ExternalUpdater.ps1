# Runs a disposable Excel migration test against the local master workbook.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$repoMasterPath = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
$projectPath = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater"
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "ElectronicLogbookUpdaterE2E-{0}" -f [guid]::NewGuid().ToString("N")
)
$sourcePath = Join-Path $testDirectory "Source.xlsm"
$masterPath = Join-Path $testDirectory "Master.xlsm"
$outputPath = Join-Path $testDirectory "Updated.xlsm"
$maxAttempts = 3
$updaterDllPath = Join-Path $projectPath "bin\Release\net8.0-windows\ElectronicLogbook.Updater.dll"
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repoRoot "updater\TestResults\com-migration-report.json"
}

Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

function Write-Step {
    param([string]$Message)
    Write-Host "[Test-ExternalUpdater] $Message" -ForegroundColor Cyan
}

function Write-ComMigrationReport {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Status,
        [string]$FailureMessage = "",
        [string[]]$Checks = @()
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $report = [ordered]@{
        schemaVersion = 1
        testName = "ExternalUpdaterDisposableComMigration"
        status = $Status
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        checks = @($Checks)
    }
    if (-not [string]::IsNullOrWhiteSpace($FailureMessage)) {
        $report.failureMessage = $FailureMessage
    }

    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$coveredChecks = @(
    "source workbook hash unchanged",
    "custom Logbook heading preserved",
    "Logbook entry values and expanded rows preserved",
    "Logbook table style preserved",
    "Logbook totals anchor preserved",
    "Keywords data preserved",
    "Routes data preserved",
    "base-airport selection preserved",
    "named preference preserved",
    "route-cache invalidation preserved",
    "pivot cache refreshed",
    "HoursByYear date grouping restored"
)
try {
    Write-Step "Preparing disposable test workspace"
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    Copy-Item -LiteralPath $repoMasterPath -Destination $sourcePath
    Copy-Item -LiteralPath $repoMasterPath -Destination $masterPath

    Write-Step "Seeding source workbook with known test data"
    $sourceTableStyle = Invoke-WorkbookEdit -WorkbookPath $sourcePath -Operation {
        param($Workbook)

        foreach ($worksheet in $Workbook.Worksheets) {
            try { $worksheet.Unprotect("") } catch {}
        }

        $logbook = $Workbook.Sheets("Logbook").ListObjects("Logbook")
        $customColumnIndex = $logbook.ListColumns("OPC").Index + 1
        $logbook.ListColumns.Item($customColumnIndex).Name = "Updater Test"
        $logbook.ListColumns("Reg").DataBodyRange.Cells(1, 1).Value2 = "TESTREG"
        $testBaseIcao = [string]$Workbook.Sheets("Airports").ListObjects("Airports").ListColumns("ICAO").DataBodyRange.Cells(1, 1).Value2
        if ([string]::IsNullOrWhiteSpace($testBaseIcao)) {
            throw "Could not find an airport to seed base-airport migration coverage."
        }
        $logbook.ListColumns("From").DataBodyRange.Cells(1, 1).Value2 = $testBaseIcao
        $newLogbookRow = $logbook.ListRows.Add()
        $newLogbookRow.Range.Cells(1, $logbook.ListColumns("Year").Index).Value2 = 2026
        $newLogbookRow.Range.Cells(1, $logbook.ListColumns("Reg").Index).Value2 = "TESTREG2"

        $keywords = $Workbook.Sheets("Currency + Recency").ListObjects("Keywords")
        $keywords.ListColumns("IPC").DataBodyRange.Cells(1, 1).Value2 = "TEST IPC"

        $routes = $Workbook.Sheets("Routes").ListObjects("Routes")
        $route = $routes.ListRows.Add()
        $route.Range.Cells(1, 1).Value2 = "YTEST"
        $route.Range.Cells(1, 2).Value2 = "YDEST"

        $baseAirports = $Workbook.Sheets("Stats").ListObjects("BaseAirportsTop10")
        $baseAirports.ListColumns("ICAO").DataBodyRange.Cells(1, 1).Value2 = $testBaseIcao
        $baseAirports.ListColumns("Base").DataBodyRange.Cells(1, 1).Value2 = $true

        $Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 = 3
        $Workbook.Names.Item("RoutesDirty").RefersToRange.Value2 = $false

        [string]$logbook.TableStyle.Name
    }

    Write-Step "Seeding disposable master workbook route cache state"
    Invoke-WorkbookEdit -WorkbookPath $masterPath -Operation {
        param($Workbook)

        foreach ($worksheet in $Workbook.Worksheets) {
            try { $worksheet.Unprotect("") } catch {}
        }

        $Workbook.Names.Item("RoutesDirty").RefersToRange.Value2 = $true
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
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            # Native stderr is updater output, not a PowerShell failure. Capture it
            # so the retry policy below can classify transient Excel COM errors.
            $ErrorActionPreference = "Continue"
            & dotnet $updaterDllPath `
                --source $sourcePath `
                --master $masterPath `
                --output $outputPath 2>&1 | Tee-Object -Variable updaterLines
            $exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
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
        $routes = $Workbook.Sheets("Routes").ListObjects("Routes")
        $baseAirports = $Workbook.Sheets("Stats").ListObjects("BaseAirportsTop10")
        $chartData = $Workbook.Sheets("ChartData")

        $customColumnIndex = $logbook.ListColumns("OPC").Index + 1
        if ($logbook.ListColumns.Item($customColumnIndex).Name -ne "Updater Test") {
            throw "Custom Logbook heading was not preserved."
        }
        if ($logbook.ListColumns("Reg").DataBodyRange.Cells(1, 1).Value2 -ne "TESTREG") {
            throw "Logbook entry data was not preserved."
        }
        if ($logbook.ListRows.Count -ne 3) {
            throw "Logbook row count was not preserved."
        }
        if ($logbook.TableStyle.Name -ne $sourceTableStyle) {
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
        if ($routes.ListRows.Count -ne 1) {
            throw "Routes data was not preserved."
        }
        $testBaseIcao = [string]$logbook.ListColumns("From").DataBodyRange.Cells(1, 1).Value2
        $preservedBase = $false
        for ($row = 1; $row -le $baseAirports.ListRows.Count; $row++) {
            $icao = [string]$baseAirports.ListColumns("ICAO").DataBodyRange.Cells($row, 1).Value2
            if ($icao -eq $testBaseIcao) {
                $preservedBase = [bool]$baseAirports.ListColumns("Base").DataBodyRange.Cells($row, 1).Value2
                break
            }
        }
        if (-not $preservedBase) {
            throw "Base-airport selection was not preserved."
        }
        if ($Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 -ne 3) {
            throw "Named preference was not preserved."
        }
        if (-not [bool]$Workbook.Names.Item("RoutesDirty").RefersToRange.Value2) {
            throw "RoutesDirty did not preserve the master route-cache invalidation state."
        }
        $logbookPivot = $chartData.PivotTables("Top5HoursByType")
        if ([int]$logbookPivot.PivotCache().RecordCount -lt [int]$logbook.ListRows.Count) {
            throw "Logbook pivot cache was not refreshed after migration."
        }
        $hoursByYear = $chartData.PivotTables("HoursByYear")
        try {
            $null = $hoursByYear.PivotFields("Years (Date)")
        } catch {
            throw "HoursByYear date grouping was not restored after migration."
        }
    }

    Write-Step "Validation complete"
    Write-ComMigrationReport -Path $ReportPath -Status "passed" -Checks $coveredChecks
    Write-Step "COM migration report written to $ReportPath"
    Write-Host "External updater disposable migration test passed." -ForegroundColor Green
} catch {
    $failure = $_
    try {
        Write-ComMigrationReport -Path $ReportPath -Status "failed" -FailureMessage $failure.Exception.Message -Checks $coveredChecks
    } catch {
        Write-Warning "Could not write COM migration failure report: $($_.Exception.Message)"
    }
    $PSCmdlet.ThrowTerminatingError($failure)
} finally {
    Write-Step "Cleaning up temporary files"
    if (Test-Path $testDirectory) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Remove-Item -LiteralPath $testDirectory -Recurse -Force -ErrorAction Stop
                break
            } catch {
                if ($attempt -eq 3) {
                    Write-Warning "Could not remove temporary test directory: $testDirectory"
                } else {
                    Start-Sleep -Seconds 2
                }
            }
        }
    }
}
