# Local workbook checks for public release readiness.
# Requires Microsoft Excel on Windows. Opens the workbook with macros disabled.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$AllowDevBranch,
    [switch]$AllowHotfixBranch,
    [switch]$CheckExternalLinks
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
$version = Get-ReleaseVersion -RepoRoot $repoRoot
$workbookPath = $config.MasterWorkbook

$issues = New-Object System.Collections.Generic.List[string]

function Get-WorkbookNameText {
    param(
        [Parameter(Mandatory)]
        $Workbook,
        [Parameter(Mandatory)]
        [string]$Name
    )

    try {
        $value = $Workbook.Names.Item($Name).RefersToRange.Value2
        if ($null -eq $value) { return "" }
        return [string]$value
    } catch {
        $issues.Add("Named range '$Name' is missing.")
        return ""
    }
}

function Get-WorkbookPackageEntryText {
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($WorkbookPath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) { throw "Package entry '$EntryName' is missing." }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally {
        $archive.Dispose()
    }
}

Invoke-WorkbookEdit -WorkbookPath $workbookPath -ReadOnly -Operation {
    param($Workbook)

    $token = (Get-WorkbookNameText -Workbook $Workbook -Name "GitHubToken").Trim()
    if ($token -ne "") {
        $issues.Add("GitHubToken is not empty in $($Workbook.Name). Remove it before publishing.")
    }

    $branch = (Get-WorkbookNameText -Workbook $Workbook -Name "GitHubBranch").Trim()
    if ($branch -eq "") {
        $issues.Add("GitHubBranch is empty.")
    } else {
        $allowedBranches = @("main")
        if ($AllowDevBranch) { $allowedBranches += "dev" }
        if ($AllowHotfixBranch) { $allowedBranches += "hotfix" }
        if ($allowedBranches -notcontains $branch) {
            $issues.Add("GitHubBranch is '$branch'. Public release workbooks should use one of: $($allowedBranches -join ', ').")
        }
    }

    $workbookVersion = (Get-WorkbookNameText -Workbook $Workbook -Name "LogbookVersion").Trim()
    if ($workbookVersion -ne $version) {
        $issues.Add("LogbookVersion is '$workbookVersion' but version.txt is '$version'.")
    }

    try {
        if ($Workbook.RemovePersonalInformation) {
            $issues.Add("RemovePersonalInformation is enabled. Public workbooks should rely on explicit readiness checks, not Excel's modal Document Inspector save flag.")
        }
    } catch {
        $issues.Add("Could not inspect RemovePersonalInformation workbook setting.")
    }

    if ($CheckExternalLinks) {
        foreach ($connection in @($Workbook.Connections)) {
            $issues.Add("Workbook contains external connection '$($connection.Name)'. Review before publishing.")
        }

        foreach ($linkType in @(1, 2)) {
            try {
                $links = $Workbook.LinkSources($linkType)
                if ($null -ne $links) {
                    foreach ($link in @($links)) {
                        $issues.Add("Workbook contains external link '$link'. Review before publishing.")
                    }
                }
            } catch {}
        }
    }

    foreach ($worksheet in @($Workbook.Worksheets)) {
        if ($worksheet.Visible -ne -1) {
            Write-Host "Hidden sheet present: $($worksheet.Name)" -ForegroundColor Yellow
        }
    }
}

try {
    $core = [System.Xml.XmlDocument]::new()
    $core.LoadXml((Get-WorkbookPackageEntryText -WorkbookPath $workbookPath -EntryName "docProps/core.xml"))
    $coreNs = [System.Xml.XmlNamespaceManager]::new($core.NameTable)
    $coreNs.AddNamespace("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties")
    $coreNs.AddNamespace("dc", "http://purl.org/dc/elements/1.1/")
    $creator = $core.SelectSingleNode("/cp:coreProperties/dc:creator", $coreNs)
    $lastModifiedBy = $core.SelectSingleNode("/cp:coreProperties/cp:lastModifiedBy", $coreNs)
    if ($null -eq $creator -or -not [string]::IsNullOrWhiteSpace($creator.InnerText)) {
        $issues.Add("Raw docProps/core.xml Creator must be blank before publishing.")
    }
    if ($null -eq $lastModifiedBy -or -not [string]::IsNullOrWhiteSpace($lastModifiedBy.InnerText)) {
        $issues.Add("Raw docProps/core.xml Last saved by must be blank before publishing.")
    }
} catch {
    $issues.Add("Could not inspect raw docProps/core.xml: $($_.Exception.Message)")
}

if ($issues.Count -gt 0) {
    $message = "Workbook public-readiness checks failed:`n - " + ($issues -join "`n - ")
    throw $message
}

Write-Host "Workbook public-readiness checks passed for $workbookPath." -ForegroundColor Green
