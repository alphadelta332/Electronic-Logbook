# Exports VBA source from a workbook back to tracked source files.

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
if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = $config.MasterWorkbook
}

$standardModules = @("modBoot", "modLogbook")
$userForms = @("frmVerifyCurrency")
if ($IncludeModUpdate) {
    $standardModules += "modUpdate"
}

Write-Host "Exporting VBA from: $WorkbookPath"

Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -ReadOnly -Visible:$Visible -Operation {
    param($Workbook)

    Assert-VbaProjectAccess -Workbook $Workbook
    $components = $Workbook.VBProject.VBComponents

    foreach ($moduleName in $standardModules) {
        $destination = Join-Path $repoRoot "$moduleName.bas"
        if (Test-Path $destination) {
            Remove-Item -LiteralPath $destination -Force
        }
        $components.Item($moduleName).Export($destination)
        Write-Host "  Exported $moduleName.bas"
    }

    foreach ($formName in $userForms) {
        $destination = Join-Path $repoRoot "$formName.frm"
        $binaryCompanion = Join-Path $repoRoot "$formName.frx"
        if (Test-Path $destination) {
            Remove-Item -LiteralPath $destination -Force
        }
        if (Test-Path $binaryCompanion) {
            Remove-Item -LiteralPath $binaryCompanion -Force
        }
        $components.Item($formName).Export($destination)
        Write-Host "  Exported $formName.frm"
    }

    $thisWorkbookComponent = $components.Item("ThisWorkbook")
    $codeModule = $thisWorkbookComponent.CodeModule
    $destination = Join-Path $repoRoot "ThisWorkbook.cls"
    if ($codeModule.CountOfLines -gt 0) {
        $code = $codeModule.Lines(1, $codeModule.CountOfLines)
    } else {
        $code = ""
    }
    Set-Content -LiteralPath $destination -Value $code -Encoding UTF8
    Write-Host "  Exported ThisWorkbook.cls"
}

Write-Host "VBA export complete." -ForegroundColor Green
