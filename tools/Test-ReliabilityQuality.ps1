# Static reliability checks for release-critical automation and updater sources.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path $RepoRoot).Path
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

$vbaFiles = @("modBoot.bas", "modUpdate.bas", "modLogbook.bas") |
    ForEach-Object { Join-Path $repoRoot $_ }
$vba = ($vbaFiles | ForEach-Object { Get-Content $_ -Raw -Encoding UTF8 }) -join "`n"
if ($vba -match 'GitHubToken|setRequestHeader\s+"Authorization"') {
    $failures.Add("VBA source still contains obsolete GitHub token support.")
}
if ($vba -match 'CreateObject\("MSXML2\.XMLHTTP"') {
    $failures.Add("VBA source still creates timeout-free XMLHTTP requests.")
}
foreach ($module in @("modBoot.bas", "modUpdate.bas")) {
    $text = Get-Content (Join-Path $repoRoot $module) -Raw -Encoding UTF8
    if ($text -notmatch 'setTimeouts\s+5000,\s*5000,\s*15000,\s*30000') {
        $failures.Add("$module does not define finite HTTP timeouts.")
    }
}
if ((Get-Content (Join-Path $repoRoot "modBoot.bas") -Raw) -notmatch 'VerifySignedModuleManifest') {
    $failures.Add("Bootstrap does not fail closed on a signed modUpdate manifest.")
}
if ((Get-Content (Join-Path $repoRoot "modUpdate.bas") -Raw) -notmatch 'VerifyReleaseWizardSignature') {
    $failures.Add("Release-channel wizard launch is not signature-verified.")
}

$updaterSource = Get-Content (Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater\ExcelWorkbookMigrator.cs") -Raw -Encoding UTF8
if ($updaterSource -notmatch 'SetApartmentState\(ApartmentState\.STA\)') {
    $failures.Add("Excel migration is not isolated on a dedicated STA thread.")
}
if ($updaterSource -match 'process\.Kill\(entireProcessTree: true\);\s*process\.WaitForExit') {
    $warnings.Add("Review forced Excel process cleanup; it must remain failed-migration diagnostics only.")
}

if ($failures.Count -gt 0) {
    throw "Reliability quality checks failed:`n - " + ($failures -join "`n - ")
}
foreach ($warning in $warnings) {
    Write-Warning $warning
}
Write-Host "Reliability quality checks passed." -ForegroundColor Green
