[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$FullOutputPath,
    [string]$CompactOutputPath,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot "ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}
if ([string]::IsNullOrWhiteSpace($FullOutputPath)) {
    $FullOutputPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\Templates\LogbookExportFull.xlsx"
}
if ([string]::IsNullOrWhiteSpace($CompactOutputPath)) {
    $CompactOutputPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\Templates\LogbookExportCompact.xlsx"
}

$resolvedWorkbookPath = (Resolve-Path -LiteralPath $WorkbookPath).Path
$resolvedFullOutputPath = [System.IO.Path]::GetFullPath($FullOutputPath)
$resolvedCompactOutputPath = [System.IO.Path]::GetFullPath($CompactOutputPath)
$excelType = [type]::GetTypeFromProgID("Excel.Application")
if ($null -eq $excelType) {
    throw "Microsoft Excel desktop is required to generate the canonical export templates."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fixedPackageTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$allowedNames = @(
    "LogbookFilterHeaders",
    "LogbookHeaders",
    "LogbookSumTotals",
    "LogbookTotals"
)

function Release-ComObject {
    param([object]$Value)

    if ($null -ne $Value -and [System.Runtime.InteropServices.Marshal]::IsComObject($Value)) {
        try {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($Value)
        } catch [System.InvalidComObjectException] {
            # The same runtime callable wrapper can be returned through multiple COM paths.
        }
    }
}

function Remove-CustomProperties {
    param([object]$Workbook)

    $properties = $Workbook.CustomDocumentProperties
    try {
        for ($index = $properties.Count; $index -ge 1; $index--) {
            $properties.Item($index).Delete()
        }
    } finally {
        Release-ComObject $properties
    }
}

function Set-ExportNames {
    param(
        [object]$Workbook,
        [string]$Layout,
        [int]$TotalsRow
    )

    $names = $Workbook.Names
    try {
        $existing = @{}
        for ($index = $names.Count; $index -ge 1; $index--) {
            $name = $names.Item($index)
            $localName = ([string]$name.Name -split '!')[-1].Trim("'")
            if ($allowedNames -contains $localName) {
                $existing[$localName] = $name
                continue
            }
            try {
                [void]$name.Delete()
            } catch {
                # Modern Excel can expose internal _xlpm names that COM cannot delete.
                # The package cleanup below removes every non-export name after SaveAs.
            } finally {
                Release-ComObject $name
            }
        }

        $grandTotalRow = $TotalsRow + 1
        $definitions = if ($Layout -eq "Full") {
            [ordered]@{
                LogbookHeaders = "='Logbook'!`$C`$2:`$BL`$4"
                LogbookFilterHeaders = "=('Logbook'!`$C`$5,'Logbook'!`$G`$5:`$AU`$5)"
                LogbookSumTotals = ("='Logbook'!`$S`$$TotalsRow`:`$AW`$$TotalsRow")
                LogbookTotals = ("='Logbook'!`$I`$$TotalsRow`:`$K`$$grandTotalRow")
            }
        } else {
            [ordered]@{
                LogbookHeaders = "='Logbook'!`$C`$2:`$BF`$4"
                LogbookFilterHeaders = "=('Logbook'!`$C`$5,'Logbook'!`$G`$5:`$AO`$5)"
                LogbookSumTotals = ("='Logbook'!`$M`$$TotalsRow`:`$AQ`$$TotalsRow")
                LogbookTotals = ("='Logbook'!`$I`$$TotalsRow`:`$K`$$grandTotalRow")
            }
        }
        foreach ($definition in $definitions.GetEnumerator()) {
            try {
                if ($existing.ContainsKey([string]$definition.Key)) {
                    $existing[[string]$definition.Key].RefersTo = [string]$definition.Value
                } else {
                    [void]$names.Add([string]$definition.Key, [string]$definition.Value)
                }
            } catch {
                throw "Could not create workbook name '$($definition.Key)' with reference '$($definition.Value)': $($_.Exception.Message)"
            }
        }
        foreach ($name in $existing.Values) {
            Release-ComObject $name
        }
    } finally {
        Release-ComObject $names
    }
}

function Set-CanonicalCalculation {
    param([object]$Excel)

    # xlCalculationAutomatic
    $Excel.Calculation = -4105
    $Excel.CalculateBeforeSave = $true
}

function Convert-DisposableWorkbook {
    param(
        [object]$Excel,
        [string]$SourcePath,
        [string]$Layout,
        [string]$DestinationPath
    )

    $workbook = $null
    $sheet = $null
    $table = $null
    try {
        $workbook = $Excel.Workbooks.Open($SourcePath, 0, $false)
        if ($workbook.ProtectStructure) {
            throw "The canonical workbook structure is protected; the export template generator cannot isolate the Logbook sheet."
        }

        for ($index = $workbook.Worksheets.Count; $index -ge 1; $index--) {
            $candidate = $workbook.Worksheets.Item($index)
            try {
                if ([string]$candidate.Name -ne "Logbook") {
                    try {
                        $candidate.Visible = -1 # xlSheetVisible
                        [void]$candidate.Delete()
                    } catch {
                        throw "Could not delete worksheet '$([string]$candidate.Name)' from the disposable workbook: $($_.Exception.Message)"
                    }
                }
            } finally {
                Release-ComObject $candidate
            }
        }

        $sheet = $workbook.Worksheets.Item("Logbook")
        if ($sheet.ProtectContents -or $sheet.ProtectDrawingObjects) {
            throw "The canonical Logbook sheet is protected; the export template generator cannot sanitize a disposable copy."
        }
        $table = $sheet.ListObjects.Item("Logbook")

        for ($index = $sheet.Shapes.Count; $index -ge 1; $index--) {
            $shape = $sheet.Shapes.Item($index)
            try {
                $shape.Delete()
            } finally {
                Release-ComObject $shape
            }
        }
        [void]$sheet.Cells.ClearComments()

        while ($table.ListRows.Count -gt 1) {
            $row = $table.ListRows.Item($table.ListRows.Count)
            try {
                [void]$row.Delete()
            } finally {
                Release-ComObject $row
            }
        }

        $dataRow = $table.DataBodyRange
        try {
            try {
                # xlCellTypeConstants. Formula cells remain as the canonical row template.
                $constants = $dataRow.SpecialCells(2)
                try {
                    [void]$constants.ClearContents()
                } finally {
                    Release-ComObject $constants
                }
            } catch [System.Runtime.InteropServices.COMException] {
                # A formula-only template row has no constants to clear.
            }
        } finally {
            Release-ComObject $dataRow
        }

        if ($Layout -eq "Compact") {
            $sheet.Activate()
            $Excel.ActiveWindow.FreezePanes = $false
            $Excel.ActiveWindow.SplitRow = 0
            $Excel.ActiveWindow.SplitColumn = 0
            [void]$sheet.Range("M:R").EntireColumn.Delete()
            $table.ListColumns.Item("From").Name = "Details"
            $sheet.Columns.Item("L").ColumnWidth = 35
            $sheet.Range("L2").Value2 = "DETAILS"
        }

        $lastColumn = if ($Layout -eq "Full") { "BL" } else { "BF" }
        $totalsRow = 7
        [void]$table.Resize($sheet.Range("B5:${lastColumn}${totalsRow}"))
        [void]$sheet.Range("B9:BV104").Clear()
        [void]$sheet.Range("A1:A104").Clear()
        [void]$sheet.Range("BM1:BV104").Clear()
        $sheet.Range("1:104").EntireRow.Hidden = $false
        $sheet.Range("B6:${lastColumn}8").WrapText = $false

        if ($Layout -eq "Full") {
            $viaColumn = $table.ListColumns.Item("Via")
            $viaData = $viaColumn.DataBodyRange
            try {
                $viaData.HorizontalAlignment = -4108 # xlCenter
            } finally {
                Release-ComObject $viaData
                Release-ComObject $viaColumn
            }
        }

        $normalStyle = $workbook.Styles.Item("Normal")
        $normalInterior = $normalStyle.Interior
        try {
            $normalInterior.Pattern = 1 # xlSolid
            $normalInterior.Color = 0xF2F2F2
        } finally {
            Release-ComObject $normalInterior
            Release-ComObject $normalStyle
        }

        $sheet.Activate()
        $Excel.ActiveWindow.FreezePanes = $false
        $Excel.ActiveWindow.SplitRow = 0
        $Excel.ActiveWindow.SplitColumn = 0
        $Excel.ActiveWindow.ScrollRow = 1
        $Excel.ActiveWindow.ScrollColumn = 1
        [void]$sheet.Range("C6").Select()
        $Excel.ActiveWindow.ScrollRow = 1
        $Excel.ActiveWindow.ScrollColumn = 1
        $Excel.ActiveWindow.FreezePanes = $true
        $Excel.ActiveWindow.DisplayGridlines = $false
        $Excel.ActiveWindow.Zoom = 85

        Remove-CustomProperties -Workbook $workbook
        try { [void]$workbook.RemoveDocumentInformation(99) } catch {}
        $workbook.RemovePersonalInformation = $true
        Set-ExportNames -Workbook $workbook -Layout $Layout -TotalsRow $totalsRow

        $links = $workbook.LinkSources(1) # xlExcelLinks
        if ($null -ne $links) {
            foreach ($link in @($links)) {
                $workbook.BreakLink([string]$link, 1)
            }
        }

        Set-CanonicalCalculation -Excel $Excel
        $workbook.ForceFullCalculation = $true
        $workbook.CheckCompatibility = $false
        $sourceSignature = Get-WorkbookSignature -Excel $Excel -OpenWorkbook $workbook
        [void]$workbook.SaveAs($DestinationPath, 51) # xlOpenXMLWorkbook (.xlsx)
        return $sourceSignature
    } finally {
        if ($null -ne $workbook) {
            $workbook.Close($false)
        }
        Release-ComObject $table
        Release-ComObject $sheet
        Release-ComObject $workbook
    }
}

function Update-ZipXml {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Path,
        [scriptblock]$Update
    )

    $entry = $Archive.GetEntry($Path)
    if ($null -eq $entry) {
        return
    }

    $stream = $entry.Open()
    try {
        $document = [System.Xml.Linq.XDocument]::Load($stream)
    } finally {
        $stream.Dispose()
    }
    & $Update $document
    $stream = $entry.Open()
    try {
        $stream.SetLength(0)
        $settings = [System.Xml.XmlWriterSettings]::new()
        $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
        $settings.Indent = $false
        $settings.OmitXmlDeclaration = $false
        $writer = [System.Xml.XmlWriter]::Create($stream, $settings)
        try {
            $document.Save($writer)
        } finally {
            $writer.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Remove-ProhibitedPackageContent {
    param([string]$Path)

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $prohibited = @(
            "customXml/",
            "xl/comments",
            "xl/ctrlProps/",
            "xl/drawings/",
            "xl/externalLinks/",
            "xl/media/",
            "xl/vbaProject.bin",
            "xl/webextensions/",
            "xl/calcChain.xml",
            "docProps/custom.xml"
        )
        foreach ($entry in @($archive.Entries)) {
            if ($prohibited | Where-Object { $entry.FullName.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) {
                $entry.Delete()
            }
        }

        foreach ($entry in @($archive.Entries | Where-Object { $_.FullName.EndsWith(".rels", [StringComparison]::OrdinalIgnoreCase) })) {
            Update-ZipXml -Archive $archive -Path $entry.FullName -Update {
                param($document)
                foreach ($relationship in @($document.Root.Elements())) {
                    $target = [string]$relationship.Attribute("Target")
                    $type = [string]$relationship.Attribute("Type")
                    if ($target -match "(?i)(customXml|comments|ctrlProps|drawings|externalLinks|media|vbaProject|webextensions|calcChain)" -or
                        $type.EndsWith("/custom-properties", [StringComparison]::OrdinalIgnoreCase)) {
                        $relationship.Remove()
                    }
                }
            }
        }

        Update-ZipXml -Archive $archive -Path "[Content_Types].xml" -Update {
            param($document)
            foreach ($contentType in @($document.Root.Elements())) {
                $partName = [string]$contentType.Attribute("PartName")
                if ($partName -match "(?i)(customXml|comments|ctrlProps|drawings|externalLinks|media|vbaProject|webextensions|calcChain|docProps/custom.xml)") {
                    $contentType.Remove()
                }
            }
        }

        Update-ZipXml -Archive $archive -Path "docProps/core.xml" -Update {
            param($document)
            $cp = [System.Xml.Linq.XNamespace]"http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
            $dc = [System.Xml.Linq.XNamespace]"http://purl.org/dc/elements/1.1/"
            $dcterms = [System.Xml.Linq.XNamespace]"http://purl.org/dc/terms/"
            $xsi = [System.Xml.Linq.XNamespace]"http://www.w3.org/2001/XMLSchema-instance"
            $document.Root.SetElementValue($dc + "creator", "FlightLogX")
            $document.Root.SetElementValue($cp + "lastModifiedBy", "FlightLogX")
            $document.Root.SetElementValue($dc + "title", "FlightLogX Logbook Export Template")
            $document.Root.SetElementValue($dc + "subject", "Canonical mobile logbook export")
            foreach ($name in @("created", "modified")) {
                $element = $document.Root.Element($dcterms + $name)
                if ($null -eq $element) {
                    $element = [System.Xml.Linq.XElement]::new($dcterms + $name)
                    $document.Root.Add($element)
                }
                $element.SetAttributeValue($xsi + "type", "dcterms:W3CDTF")
                $element.Value = "2000-01-01T00:00:00Z"
            }
        }

        Update-ZipXml -Archive $archive -Path "xl/workbook.xml" -Update {
            param($document)
            $spreadsheet = [System.Xml.Linq.XNamespace]"http://schemas.openxmlformats.org/spreadsheetml/2006/main"
            $definedNames = $document.Root.Element($spreadsheet + "definedNames")
            if ($null -ne $definedNames) {
                foreach ($definedName in @($definedNames.Elements($spreadsheet + "definedName"))) {
                    if ($allowedNames -notcontains [string]$definedName.Attribute("name").Value) {
                        $definedName.Remove()
                    }
                }
            }
            foreach ($revisionPointer in @($document.Descendants() | Where-Object { $_.Name.LocalName -eq "revisionPtr" })) {
                $revisionPointer.Remove()
            }
            $calc = $document.Root.Element($spreadsheet + "calcPr")
            if ($null -eq $calc) {
                $calc = [System.Xml.Linq.XElement]::new($spreadsheet + "calcPr")
                $document.Root.Add($calc)
            }
            $calc.SetAttributeValue("calcMode", "auto")
            $calc.SetAttributeValue("fullCalcOnLoad", "1")
            $calc.SetAttributeValue("forceFullCalc", "1")
        }
    } finally {
        $archive.Dispose()
    }

    $deterministicPath = "$Path.deterministic"
    $source = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $destination = [System.IO.Compression.ZipFile]::Open($deterministicPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($sourceEntry in $source.Entries | Sort-Object FullName) {
                $destinationEntry = $destination.CreateEntry(
                    $sourceEntry.FullName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $destinationEntry.LastWriteTime = $fixedPackageTime
                $input = $sourceEntry.Open()
                $output = $destinationEntry.Open()
                try {
                    $input.CopyTo($output)
                } finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        } finally {
            $destination.Dispose()
        }
    } finally {
        $source.Dispose()
    }
    Move-Item -LiteralPath $deterministicPath -Destination $Path -Force
}

function Assert-SanitizedPackage {
    param([string]$Path)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $prohibited = @($archive.Entries.FullName | Where-Object {
            $_ -match "(?i)(^customXml/|^xl/comments|^xl/ctrlProps/|^xl/drawings/|^xl/externalLinks/|^xl/media/|^xl/vbaProject|^xl/webextensions/|^xl/calcChain.xml$|^docProps/custom.xml$)"
        })
        if ($prohibited.Count -gt 0) {
            throw "Template package still contains prohibited parts: $($prohibited -join ', ')"
        }

        $workbookEntry = $archive.GetEntry("xl/workbook.xml")
        $reader = [System.IO.StreamReader]::new($workbookEntry.Open())
        try {
            [xml]$workbookXml = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
        $namespace = [System.Xml.XmlNamespaceManager]::new($workbookXml.NameTable)
        $namespace.AddNamespace("s", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
        $sheets = @($workbookXml.SelectNodes("//s:sheets/s:sheet", $namespace))
        if ($sheets.Count -ne 1 -or [string]$sheets[0].name -ne "Logbook") {
            throw "Template must contain exactly one worksheet named Logbook."
        }
        $names = @($workbookXml.SelectNodes("//s:definedNames/s:definedName", $namespace) | ForEach-Object { [string]$_.name })
        $missingNames = @($allowedNames | Where-Object { $names -notcontains $_ })
        $unexpectedNames = @($names | Where-Object { $allowedNames -notcontains $_ })
        if ($missingNames.Count -gt 0 -or $unexpectedNames.Count -gt 0) {
            throw "Template names do not match the export contract. Missing: $($missingNames -join ', '); unexpected: $($unexpectedNames -join ', ')."
        }
    } finally {
        $archive.Dispose()
    }
}

function Get-WorkbookSignature {
    param(
        [object]$Excel,
        [string]$Path,
        [object]$OpenWorkbook
    )

    $workbook = $OpenWorkbook
    $ownsWorkbook = $false
    $sheet = $null
    $table = $null
    try {
        if ($null -eq $workbook) {
            $workbook = $Excel.Workbooks.Open($Path, 0, $true)
            $ownsWorkbook = $true
        }
        $sheet = $workbook.Worksheets.Item("Logbook")
        $table = $sheet.ListObjects.Item("Logbook")
        $sheet.Activate()

        $columns = for ($index = 1; $index -le $table.ListColumns.Count; $index++) {
            $column = $table.ListColumns.Item($index)
            $dataCell = $column.DataBodyRange.Cells.Item(1, 1)
            $totalCell = $table.TotalsRowRange.Cells.Item(1, $index)
            try {
                [ordered]@{
                    Name = [string]$column.Name
                    Width = [Math]::Round([double]$column.Range.ColumnWidth, 4)
                    Hidden = [bool]$column.Range.EntireColumn.Hidden
                    DataNumberFormat = [string]$dataCell.NumberFormat
                    DataFormula = [string]$dataCell.Formula
                    TotalNumberFormat = [string]$totalCell.NumberFormat
                    TotalFormula = [string]$totalCell.Formula
                }
            } finally {
                Release-ComObject $totalCell
                Release-ComObject $dataCell
                Release-ComObject $column
            }
        }

        $mergeAddresses = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($cell in $sheet.Range("B2:$($table.Range.Columns.Item($table.ListColumns.Count).EntireColumn.Address($false, $false).Split(':')[0] -replace '\d', '')4").Cells) {
            if ($cell.MergeCells) {
                [void]$mergeAddresses.Add([string]$cell.MergeArea.Address($false, $false))
            }
        }

        $headings = foreach ($cell in $sheet.Range("B2:$($table.Range.Columns.Item($table.ListColumns.Count).EntireColumn.Address($false, $false).Split(':')[0] -replace '\d', '')5").Cells) {
            if (-not [string]::IsNullOrWhiteSpace([string]$cell.Text)) {
                [ordered]@{
                    Address = [string]$cell.Address($false, $false)
                    Text = [string]$cell.Text
                    Fill = [long]$cell.Interior.Color
                    FontColor = [long]$cell.Font.Color
                    Bold = [bool]$cell.Font.Bold
                    HorizontalAlignment = [int]$cell.HorizontalAlignment
                    VerticalAlignment = [int]$cell.VerticalAlignment
                }
            }
        }

        $rowHeights = for ($index = 1; $index -le 8; $index++) {
            [Math]::Round([double]$sheet.Rows.Item($index).RowHeight, 4)
        }
        $names = foreach ($name in $workbook.Names) {
            if ($allowedNames -contains [string]$name.Name) {
                "$([string]$name.Name)=$([string]$name.RefersTo)"
            }
        }

        return ([ordered]@{
            WorksheetCount = [int]$workbook.Worksheets.Count
            SheetName = [string]$sheet.Name
            TableAddress = [string]$table.Range.Address($false, $false)
            TableStyle = [string]$table.TableStyle
            ShowTotals = [bool]$table.ShowTotals
            ShowAutoFilter = [bool]$table.ShowAutoFilter
            FreezeRow = [int]$Excel.ActiveWindow.SplitRow
            FreezeColumn = [int]$Excel.ActiveWindow.SplitColumn
            FreezePanes = [bool]$Excel.ActiveWindow.FreezePanes
            DisplayGridlines = [bool]$Excel.ActiveWindow.DisplayGridlines
            Columns = @($columns)
            Merges = @($mergeAddresses | Sort-Object)
            Headings = @($headings)
            RowHeights = @($rowHeights)
            Names = @($names | Sort-Object)
            GrandTotalLabel = [string]$sheet.Range("J8").Value2
            GrandTotalFormula = [string]$sheet.Range("K8").Formula
            GrandTotalFormat = [string]$sheet.Range("K8").NumberFormat
        } | ConvertTo-Json -Depth 8 -Compress)
    } finally {
        if ($ownsWorkbook -and $null -ne $workbook) {
            $workbook.Close($false)
        }
        Release-ComObject $table
        Release-ComObject $sheet
        if ($ownsWorkbook) {
            Release-ComObject $workbook
        }
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FlightLogX-ExportTemplates-" + [Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$excel = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    $excel.ScreenUpdating = $false
    try { $excel.AutomationSecurity = 3 } catch {}

    $layouts = @(
        [ordered]@{ Name = "Full"; Output = $resolvedFullOutputPath },
        [ordered]@{ Name = "Compact"; Output = $resolvedCompactOutputPath }
    )

    foreach ($layout in $layouts) {
        $sourceCopy = Join-Path $temporaryRoot "$($layout.Name)-source.xlsm"
        $generated = Join-Path $temporaryRoot "LogbookExport$($layout.Name).xlsx"
        Copy-Item -LiteralPath $resolvedWorkbookPath -Destination $sourceCopy
        $sourceSignature = Convert-DisposableWorkbook -Excel $excel -SourcePath $sourceCopy -Layout $layout.Name -DestinationPath $generated
        Remove-ProhibitedPackageContent -Path $generated
        Assert-SanitizedPackage -Path $generated

        $generatedSignature = Get-WorkbookSignature -Excel $excel -Path $generated
        if ($generatedSignature -cne $sourceSignature) {
            throw "$($layout.Name) export template changed structure while Excel saved the normalized disposable master."
        }
        if ($VerifyOnly) {
            if (-not (Test-Path -LiteralPath $layout.Output)) {
                throw "Committed $($layout.Name) template is missing: $($layout.Output)"
            }
            Assert-SanitizedPackage -Path $layout.Output
            $committedSignature = Get-WorkbookSignature -Excel $excel -Path $layout.Output
            if ($committedSignature -cne $generatedSignature) {
                throw "$($layout.Name) export template does not match the normalized canonical master. Run this script without -VerifyOnly to regenerate it."
            }
        } else {
            [System.IO.Directory]::CreateDirectory((Split-Path $layout.Output -Parent)) | Out-Null
            Copy-Item -LiteralPath $generated -Destination $layout.Output -Force
        }

        Write-Host "$($layout.Name) template verified against the normalized canonical master."
    }
} finally {
    if ($null -ne $excel) {
        $excel.Quit()
    }
    Release-ComObject $excel
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
}

if (-not $VerifyOnly) {
    Write-Host "Generated export templates:"
    Write-Host "  $resolvedFullOutputPath"
    Write-Host "  $resolvedCompactOutputPath"
}
