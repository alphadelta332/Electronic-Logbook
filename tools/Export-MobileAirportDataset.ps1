[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot "ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\Data\airports.json.gz"
}

$resolvedWorkbookPath = (Resolve-Path $WorkbookPath).Path
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path $resolvedOutputPath -Parent
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$excel = $null
$workbook = $null
$jsonPath = [System.IO.Path]::GetTempFileName()

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    try { $excel.AutomationSecurity = 3 } catch {}

    $workbook = $excel.Workbooks.Open($resolvedWorkbookPath, $false, $true)
    $table = $workbook.Worksheets.Item("Airports").ListObjects.Item("Airports")
    $rows = [System.Collections.Generic.List[object]]::new()

    foreach ($row in $table.ListRows) {
        $range = $row.Range
        $icao = [string]$range.Cells.Item(1, $table.ListColumns.Item("ICAO").Index).Value2
        if ([string]::IsNullOrWhiteSpace($icao)) {
            continue
        }

        $rows.Add([ordered]@{
            i = $icao.Trim().ToUpperInvariant()
            n = ([string]$range.Cells.Item(1, $table.ListColumns.Item("Airport").Index).Value2).Trim()
            a = ([string]$range.Cells.Item(1, $table.ListColumns.Item("Three").Index).Value2).Trim().ToUpperInvariant()
            b = ([string]$range.Cells.Item(1, $table.ListColumns.Item("Two").Index).Value2).Trim().ToUpperInvariant()
            y = [double]$range.Cells.Item(1, $table.ListColumns.Item("Latitude").Index).Value2
            x = [double]$range.Cells.Item(1, $table.ListColumns.Item("Longitude").Index).Value2
        })
    }

    [System.IO.File]::WriteAllText(
        $jsonPath,
        ($rows | ConvertTo-Json -Compress -Depth 3),
        [System.Text.UTF8Encoding]::new($false))

    $input = [System.IO.File]::OpenRead($jsonPath)
    try {
        $output = [System.IO.File]::Create($resolvedOutputPath)
        try {
            $gzip = [System.IO.Compression.GZipStream]::new(
                $output,
                [System.IO.Compression.CompressionLevel]::Optimal,
                $true)
            try {
                $input.CopyTo($gzip)
            } finally {
                $gzip.Dispose()
            }
        } finally {
            $output.Dispose()
        }
    } finally {
        $input.Dispose()
    }

    Write-Host "Exported $($rows.Count) airports to: $resolvedOutputPath"
} finally {
    if ($null -ne $workbook) {
        $workbook.Close($false)
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
    }
    if ($null -ne $excel) {
        $excel.Quit()
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    }
    Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
}
