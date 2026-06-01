# Local workbook checks for public release readiness.
# Requires Microsoft Excel on Windows. Opens the workbook with macros disabled.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$AllowDevBranch,
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

Invoke-WorkbookEdit -WorkbookPath $workbookPath -ReadOnly -Operation {
    param($Workbook)

    $token = (Get-WorkbookNameText -Workbook $Workbook -Name "GitHubToken").Trim()
    if ($token -ne "") {
        $issues.Add("GitHubToken is not empty in $($Workbook.Name). Remove it before publishing.")
    }

    $branch = (Get-WorkbookNameText -Workbook $Workbook -Name "GitHubBranch").Trim()
    if ($branch -eq "") {
        $issues.Add("GitHubBranch is empty.")
    } elseif ($branch -ne "main" -and -not $AllowDevBranch) {
        $issues.Add("GitHubBranch is '$branch'. Public release workbooks should use 'main'.")
    }

    $workbookVersion = (Get-WorkbookNameText -Workbook $Workbook -Name "LogbookVersion").Trim()
    if ($workbookVersion -ne $version) {
        $issues.Add("LogbookVersion is '$workbookVersion' but version.txt is '$version'.")
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

if ($issues.Count -gt 0) {
    $message = "Workbook public-readiness checks failed:`n - " + ($issues -join "`n - ")
    throw $message
}

Write-Host "Workbook public-readiness checks passed for $workbookPath." -ForegroundColor Green
