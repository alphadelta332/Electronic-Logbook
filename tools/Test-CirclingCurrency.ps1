# Evaluates the master workbook's real circling formula with disposable entries in Excel.
[CmdletBinding()]
param([string]$WorkbookPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "Electronic_Logbook_Master.xlsm"))

$ErrorActionPreference = "Stop"
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $WorkbookPath).Path)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry("xl/workbook.xml").Open())
    try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$definition = @($xml.workbook.definedNames.definedName | Where-Object name -eq "CirclingExpirySEA")
if ($definition.Count -ne 1) { throw "Expected one CirclingExpirySEA definition." }
# Supply preclassified engine hours through the disposable SEA column.
$formula = "=" + ([string]$definition[0].'#text').Replace("_xlfn._xlws.", "").Replace("_xlfn.", "").Replace("_xlpm.", "").Replace("LogbookSEA", "Logbook[SEA]")
$alias = $xml.workbook.definedNames.definedName | Where-Object name -eq "CirclingExpiryMEA"
if ([string]$alias.'#text' -ne "CirclingExpirySEA") { throw "MEA must share the circling formula." }

$excel = $null
$workbook = $null
$autoFill = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    $excel.AutomationSecurity = 3
    $autoFill = $excel.AutoCorrect.AutoFillFormulasInLists
    $excel.AutoCorrect.AutoFillFormulasInLists = $false
    $workbook = $excel.Workbooks.Add()
    $sheet = $workbook.Worksheets.Item(1)
    $headers = @("Date", "IPC", "OPC", "Circling", "SEA")
    for ($column = 0; $column -lt $headers.Count; $column++) {
        $sheet.Cells.Item(1, $column + 1).Value2 = $headers[$column]
    }
    $table = $sheet.ListObjects.Add(1, $sheet.Range("A1:E4"), $null, 1)
    $table.Name = "Logbook"
    $sheet.Range("G1").Formula2 = $formula

    # Date, IPC, OPC, circling count, engine-hour qualification; all entries are synthetic.
    $mayIpc = @([datetime]"2026-05-01", $true, $false, 1, 1)
    $cases = @(
        @{ Name = "IPC establishes"; Rows = @(,$mayIpc); Expected = [datetime]"2027-05-31" },
        @{ Name = "OPC establishes"; Rows = @(,@([datetime]"2026-05-01", $false, $true, 1, 1)); Expected = [datetime]"2027-05-31" },
        @{ Name = "Later IPC with circling"; Rows = @($mayIpc, @([datetime]"2026-06-01", $true, $false, 1, 1)); Expected = [datetime]"2027-06-30" },
        @{ Name = "Later OPC with circling"; Rows = @($mayIpc, @([datetime]"2026-06-01", $false, $true, 1, 1)); Expected = [datetime]"2027-06-30" },
        @{ Name = "Later IPC without circling resets"; Rows = @($mayIpc, @([datetime]"2026-06-01", $true, $false, 0, 1)); Expected = $null },
        @{ Name = "Later OPC without circling preserves"; Rows = @($mayIpc, @([datetime]"2026-06-01", $false, $true, 0, 1)); Expected = [datetime]"2027-05-31" },
        @{ Name = "IPC resets prior OPC"; Rows = @(@([datetime]"2026-05-01", $false, $true, 1, 1), @([datetime]"2026-06-01", $true, $false, 0, 1)); Expected = $null },
        @{ Name = "Restoration excludes old renewal credit"; Rows = @(@([datetime]"2025-06-01", $true, $false, 1, 1), @([datetime]"2026-03-01", $true, $false, 0, 1), @([datetime]"2026-04-01", $false, $true, 1, 1)); Expected = [datetime]"2027-04-30" },
        @{ Name = "Early renewal preserved"; Rows = @(@([datetime]"2025-06-10", $true, $false, 1, 1), @([datetime]"2026-04-30", $true, $false, 1, 1)); Expected = [datetime]"2027-06-30" },
        @{ Name = "Engine qualification preserved"; Rows = @(,@([datetime]"2026-05-01", $true, $false, 1, 0)); Expected = $null }
    )
    $failures = @()
    foreach ($case in $cases) {
        [void]$table.DataBodyRange.ClearContents()
        for ($row = 0; $row -lt $case.Rows.Count; $row++) {
            for ($column = 0; $column -lt 5; $column++) {
                $value = $case.Rows[$row][$column]
                if ($value -is [datetime]) { $value = $value.ToOADate() }
                $sheet.Cells.Item($row + 2, $column + 1).Formula2 = "=" + [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture)
            }
        }
        $excel.CalculateFull()
        $actual = $sheet.Range("G1").Value2
        $expected = if ($null -eq $case.Expected) { 0.0 } else { $case.Expected.ToOADate() }
        if ($actual -ne $expected) {
            $failures += "$($case.Name): expected $expected, got $actual"
        } else { Write-Host "PASS: $($case.Name)" }
    }
    if ($failures.Count) { throw ($failures -join "`n") }
    Write-Host "Circling workbook checks passed: $($cases.Count)."
} finally {
    if ($null -ne $workbook) {
        $workbook.Close($false)
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($workbook)
    }
    if ($null -ne $excel) {
        if ($null -ne $autoFill) { $excel.AutoCorrect.AutoFillFormulasInLists = $autoFill }
        $excel.Quit()
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
    }
}
