function Get-ReleaseConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $repoRoot = (Resolve-Path $RepoRoot).Path
    $config = [ordered]@{
        MasterWorkbook = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
        WorkingCopyWorkbook = ""
    }

    $localConfigPath = Join-Path $repoRoot "release.local.json"
    if (Test-Path $localConfigPath) {
        $localConfig = Get-Content $localConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $localConfig.PSObject.Properties) {
            if ($config.Contains($property.Name)) {
                $value = [string]$property.Value
                if (-not [System.IO.Path]::IsPathRooted($value) -and -not [string]::IsNullOrWhiteSpace($value)) {
                    $value = Join-Path $repoRoot $value
                }
                $config[$property.Name] = $value
            }
        }
    }

    return [pscustomobject]$config
}

function Get-ReleaseVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $versionPath = Join-Path $RepoRoot "version.txt"
    if (-not (Test-Path $versionPath)) {
        throw "version.txt not found at $versionPath"
    }

    $version = (Get-Content $versionPath -Raw -Encoding UTF8).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "version.txt must contain a semantic version like 1.2.3. Found '$version'."
    }

    return $version
}

function Close-ExcelComObjects {
    param(
        $Excel,
        $Workbook,
        [bool]$Save
    )

    if ($null -ne $Workbook) {
        try {
            if ($Save) {
                $Workbook.Close($true)
            } else {
                $Workbook.Close($false)
            }
        } catch {}
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Workbook) | Out-Null
    }

    if ($null -ne $Excel) {
        try { $Excel.Quit() } catch {}
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Excel) | Out-Null
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
}

function Invoke-WorkbookEdit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [scriptblock]$Operation,
        [switch]$ReadOnly,
        [switch]$Visible
    )

    if (-not (Test-Path $WorkbookPath)) {
        throw "Workbook not found: $WorkbookPath"
    }

    $resolvedPath = (Resolve-Path $WorkbookPath).Path
    $excel = $null
    $workbook = $null
    $save = $false

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = [bool]$Visible
        $excel.DisplayAlerts = $false
        $excel.EnableEvents = $false
        try {
            # msoAutomationSecurityForceDisable prevents Workbook_Open from firing during tooling.
            $excel.AutomationSecurity = 3
        } catch {}

        $workbook = $excel.Workbooks.Open($resolvedPath, $false, [bool]$ReadOnly)
        & $Operation $workbook $excel
        $save = -not $ReadOnly
    } finally {
        Close-ExcelComObjects -Excel $excel -Workbook $workbook -Save $save
    }
}

function Set-WorkbookNameValue {
    param(
        [Parameter(Mandatory)]
        $Workbook,
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Value
    )

    try {
        $Workbook.Names.Item($Name).RefersToRange.Value2 = $Value
    } catch {
        # If the workbook/sheet is protected, writing through RefersToRange can fail
        # even when the name exists. Retry after unprotecting with the default blank
        # password used by release-mode workbook protection.
        try {
            $Workbook.Unprotect("")
            foreach ($ws in $Workbook.Worksheets) {
                $ws.Unprotect("")
            }
            $Workbook.Names.Item($Name).RefersToRange.Value2 = $Value
        } catch {
            throw "Could not set named range '$Name' in $($Workbook.Name). The name may be missing or protected."
        }
    }
}

function Set-WorkbookCustomPropertyFileValue {
    <#
    .SYNOPSIS
    Sets a custom document property directly in an OOXML workbook package.

    .DESCRIPTION
    Excel writes the local Office identity into docProps/core.xml on save. This
    final package-only pass records the release version in custom properties,
    sets the built-in title, and removes Creator / Last saved by without
    triggering Excel's RemovePersonalInformation save prompt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Value
    )

    if (-not (Test-Path $WorkbookPath)) {
        throw "Workbook not found: $WorkbookPath"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedPath = (Resolve-Path $WorkbookPath).Path
    $archive = $null

    function Get-PackageXml {
        param($Archive, [string]$EntryName)
        $entry = $Archive.GetEntry($EntryName)
        if ($null -eq $entry) { return $null }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    function Set-PackageXml {
        param($Archive, [string]$EntryName, [System.Xml.XmlDocument]$Document)
        $existing = $Archive.GetEntry($EntryName)
        if ($null -ne $existing) { $existing.Delete() }
        $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $settings = [System.Xml.XmlWriterSettings]::new()
        $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
        $settings.Indent = $false
        $writer = [System.Xml.XmlWriter]::Create($entry.Open(), $settings)
        try { $Document.Save($writer) } finally { $writer.Dispose() }
    }

    try {
        $archive = [System.IO.Compression.ZipFile]::Open($resolvedPath, [System.IO.Compression.ZipArchiveMode]::Update)

        $core = [System.Xml.XmlDocument]::new()
        $core.LoadXml((Get-PackageXml -Archive $archive -EntryName "docProps/core.xml"))
        $coreNs = [System.Xml.XmlNamespaceManager]::new($core.NameTable)
        $coreNs.AddNamespace("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties")
        $coreNs.AddNamespace("dc", "http://purl.org/dc/elements/1.1/")
        foreach ($xpath in @("/cp:coreProperties/dc:creator", "/cp:coreProperties/cp:lastModifiedBy")) {
            $node = $core.SelectSingleNode($xpath, $coreNs)
            if ($null -eq $node) {
                $prefix = if ($xpath -match "creator") { "dc" } else { "cp" }
                $localName = if ($xpath -match "creator") { "creator" } else { "lastModifiedBy" }
                $node = $core.CreateElement($prefix, $localName, $coreNs.LookupNamespace($prefix))
                [void]$core.DocumentElement.AppendChild($node)
            }
            $node.InnerText = ""
        }
        $title = $core.SelectSingleNode("/cp:coreProperties/dc:title", $coreNs)
        if ($null -eq $title) {
            $title = $core.CreateElement("dc", "title", $coreNs.LookupNamespace("dc"))
            [void]$core.DocumentElement.AppendChild($title)
        }
        $title.InnerText = "Electronic Logbook v$Value"
        Set-PackageXml -Archive $archive -EntryName "docProps/core.xml" -Document $core

        $customXml = Get-PackageXml -Archive $archive -EntryName "docProps/custom.xml"
        $custom = [System.Xml.XmlDocument]::new()
        if ($null -eq $customXml) {
            [void]$custom.AppendChild($custom.CreateXmlDeclaration("1.0", "UTF-8", "yes"))
            [void]$custom.AppendChild($custom.CreateElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"))
        } else {
            $custom.LoadXml($customXml)
        }
        $customNs = [System.Xml.XmlNamespaceManager]::new($custom.NameTable)
        $customNs.AddNamespace("c", "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties")
        $customNs.AddNamespace("vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes")
        $property = $custom.SelectSingleNode("/c:Properties/c:property[@name='$Name']", $customNs)
        if ($null -eq $property) {
            $property = $custom.CreateElement("property", $customNs.LookupNamespace("c"))
            $property.SetAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}")
            $property.SetAttribute("pid", [string](2 + @($custom.SelectNodes("/c:Properties/c:property", $customNs)).Count))
            $property.SetAttribute("name", $Name)
            [void]$custom.DocumentElement.AppendChild($property)
        }
        $property.RemoveAll()
        $property.SetAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}")
        if (-not $property.HasAttribute("pid")) { $property.SetAttribute("pid", "2") }
        $property.SetAttribute("name", $Name)
        $propertyValue = $custom.CreateElement("vt", "lpwstr", $customNs.LookupNamespace("vt"))
        $propertyValue.InnerText = $Value
        [void]$property.AppendChild($propertyValue)
        Set-PackageXml -Archive $archive -EntryName "docProps/custom.xml" -Document $custom

        if ($null -eq $customXml) {
            $contentTypes = [System.Xml.XmlDocument]::new()
            $contentTypes.LoadXml((Get-PackageXml -Archive $archive -EntryName "[Content_Types].xml"))
            $contentNs = [System.Xml.XmlNamespaceManager]::new($contentTypes.NameTable)
            $contentNs.AddNamespace("ct", "http://schemas.openxmlformats.org/package/2006/content-types")
            if ($null -eq $contentTypes.SelectSingleNode("/ct:Types/ct:Override[@PartName='/docProps/custom.xml']", $contentNs)) {
                $override = $contentTypes.CreateElement("Override", $contentNs.LookupNamespace("ct"))
                $override.SetAttribute("PartName", "/docProps/custom.xml")
                $override.SetAttribute("ContentType", "application/vnd.openxmlformats-officedocument.custom-properties+xml")
                [void]$contentTypes.DocumentElement.AppendChild($override)
                Set-PackageXml -Archive $archive -EntryName "[Content_Types].xml" -Document $contentTypes
            }

            $relationships = [System.Xml.XmlDocument]::new()
            $relationships.LoadXml((Get-PackageXml -Archive $archive -EntryName "_rels/.rels"))
            $relNs = [System.Xml.XmlNamespaceManager]::new($relationships.NameTable)
            $relNs.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships")
            if ($null -eq $relationships.SelectSingleNode("/r:Relationships/r:Relationship[@Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties']", $relNs)) {
                $relationship = $relationships.CreateElement("Relationship", $relNs.LookupNamespace("r"))
                $relationship.SetAttribute("Id", "rIdCustomProperties")
                $relationship.SetAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties")
                $relationship.SetAttribute("Target", "docProps/custom.xml")
                [void]$relationships.DocumentElement.AppendChild($relationship)
                Set-PackageXml -Archive $archive -EntryName "_rels/.rels" -Document $relationships
            }
        }
    } finally {
        if ($null -ne $archive) { $archive.Dispose() }
    }

    Write-Host "  Package metadata sanitised and $Name set to $Value" -ForegroundColor Green
}

function Set-LogbookWorkbookState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [ValidateSet("dev", "main")]
        [string]$Branch,
        [AllowEmptyString()]
        [string]$Version
    )

    Write-Host "Processing: $WorkbookPath"
    Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -Operation {
        param($Workbook)

        # Keep public/user-started workbooks usable. Release safety is enforced by
        # explicit readiness checks, not Excel's modal Document Inspector save flag.
        $Workbook.RemovePersonalInformation = $false
        Set-WorkbookNameValue -Workbook $Workbook -Name "GitHubBranch" -Value $Branch
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            Set-WorkbookNameValue -Workbook $Workbook -Name "LogbookVersion" -Value $Version
        }
    }

    Write-Host "  RemovePersonalInformation = False" -ForegroundColor Green
    Write-Host "  GitHubBranch = $Branch" -ForegroundColor Green
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        Write-Host "  LogbookVersion = $Version" -ForegroundColor Green
    } else {
        Write-Host "  LogbookVersion unchanged" -ForegroundColor Yellow
    }
}

function Assert-VbaProjectAccess {
    param(
        [Parameter(Mandatory)]
        $Workbook
    )

    try {
        $null = $Workbook.VBProject.VBComponents.Count
    } catch {
        throw "Excel blocked access to the VBA project. Enable Trust Center > Macro Settings > Trust access to the VBA project object model."
    }
}

function Invoke-WorkbookMacro {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [string]$MacroName,
        [switch]$Visible,
        [switch]$IgnoreMissing
    )

    if (-not (Test-Path $WorkbookPath)) {
        throw "Workbook not found: $WorkbookPath"
    }

    $resolvedPath = (Resolve-Path $WorkbookPath).Path
    $excel = $null
    $workbook = $null

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = [bool]$Visible
        $excel.DisplayAlerts = $false
        $excel.EnableEvents = $false

        # Enable macro execution for explicit release/testing macro calls.
        try { $excel.AutomationSecurity = 1 } catch {}

        $workbook = $excel.Workbooks.Open($resolvedPath, $false, $false)
        $qualifiedMacro = "'$($workbook.Name)'!$MacroName"

        try {
            $excel.Run($qualifiedMacro)
        } catch {
            if ($IgnoreMissing) {
                Write-Host "  Skipped macro '$MacroName' for $resolvedPath (not available or disabled)." -ForegroundColor Yellow
                return
            }

            throw "Could not run macro '$MacroName' for $resolvedPath. $_"
        }

        $workbook.Save()
        Write-Host "  Macro '$MacroName' executed for $resolvedPath" -ForegroundColor Green
    } finally {
        Close-ExcelComObjects -Excel $excel -Workbook $workbook -Save $false
    }
}

function Set-WorkbookOpenView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [string]$WorksheetName = "New Entry"
    )

    $targetWorksheetName = $WorksheetName
    $operation = {
        param($Workbook, $Excel)

        $Workbook.Activate()
        try {
            $Workbook.Worksheets.Item($targetWorksheetName).Activate()
        } catch {
            $Workbook.Worksheets.Item(1).Activate()
        }

        $Workbook.Save()

    }.GetNewClosure()

    Write-Host "Setting open view: $WorkbookPath"
    Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -Operation $operation

    Write-Host "  Active sheet = $targetWorksheetName" -ForegroundColor Green
}

Export-ModuleMember -Function Get-ReleaseConfig, Get-ReleaseVersion, Invoke-WorkbookEdit, Set-WorkbookNameValue, Set-WorkbookCustomPropertyFileValue, Set-LogbookWorkbookState, Set-WorkbookOpenView, Assert-VbaProjectAccess, Invoke-WorkbookMacro
