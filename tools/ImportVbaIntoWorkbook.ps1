# Imports tracked VBA source into the workbook.
# Release default imports modBoot, modLogbook, and ThisWorkbook only.

[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [switch]$IncludeModUpdate,
    [switch]$Visible
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot "ReleaseTools.psm1") -Force

$config = Get-ReleaseConfig -RepoRoot $repoRoot
$version = Get-ReleaseVersion -RepoRoot $repoRoot

if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}

$standardModules = @("modBoot.bas", "modLogbook.bas")
if ($IncludeModUpdate) {
    $standardModules += "modUpdate.bas"
}

Write-Host "Importing VBA into: $WorkbookPath"
Write-Host "Version source: version.txt = $version"
if (-not $IncludeModUpdate) {
    Write-Host "modUpdate.bas is not embedded by default; modBoot downloads it at runtime." -ForegroundColor Yellow
}

Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -Visible:$Visible -Operation {
    param($Workbook)

    Assert-VbaProjectAccess -Workbook $Workbook
    $components = $Workbook.VBProject.VBComponents

    foreach ($moduleFile in $standardModules) {
        $modulePath = Join-Path $repoRoot $moduleFile
        if (-not (Test-Path $modulePath)) {
            throw "VBA source not found: $modulePath"
        }

        $moduleName = [System.IO.Path]::GetFileNameWithoutExtension($moduleFile)
        try {
            $existing = $components.Item($moduleName)
            $components.Remove($existing)
        } catch {}

        $components.Import($modulePath) | Out-Null
        Write-Host "  Imported $moduleFile"
    }

    if (-not $IncludeModUpdate) {
        try {
            $existingUpdate = $components.Item("modUpdate")
            $components.Remove($existingUpdate)
            Write-Host "  Removed embedded modUpdate.bas"
        } catch {}
    }

    $thisWorkbookPath = Join-Path $repoRoot "ThisWorkbook.cls"
    if (-not (Test-Path $thisWorkbookPath)) {
        throw "VBA source not found: $thisWorkbookPath"
    }

    $thisWorkbookComponent = $components.Item("ThisWorkbook")
    $codeModule = $thisWorkbookComponent.CodeModule
    if ($codeModule.CountOfLines -gt 0) {
        $codeModule.DeleteLines(1, $codeModule.CountOfLines)
    }
    $thisWorkbookCode = Get-Content $thisWorkbookPath -Raw -Encoding UTF8
    $codeModule.AddFromString($thisWorkbookCode)
    Write-Host "  Updated ThisWorkbook.cls"

    Set-WorkbookNameValue -Workbook $Workbook -Name "LogbookVersion" -Value $version
    Write-Host "  Stamped LogbookVersion = $version"
}

Write-Host "VBA import complete." -ForegroundColor Green
