[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$ExpectedPublisher,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater.Wizard\ElectronicLogbook.Updater.Wizard.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "updater\dist"
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$publishDir = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater.Wizard\bin\Release\net8.0-windows\win-x64\publish-single-file"
$assetExe = Join-Path $OutputDirectory "ElectronicLogbook.Updater.Wizard.exe"
$assetZip = Join-Path $OutputDirectory "ElectronicLogbook.Updater.Wizard.win-x64.zip"
$signatureReportPath = Join-Path $OutputDirectory "wizard-signature-report.json"

if (-not $SkipBuild) {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true `
        /p:PublishTrimmed=false `
        -o $publishDir
}

$publishedExe = Join-Path $publishDir "ElectronicLogbook.Updater.Wizard.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Wizard publish output not found: $publishedExe"
}

if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "Use -CertificateThumbprint when -Sign is specified."
    }

    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint -eq $CertificateThumbprint } |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "Code-signing certificate not found: $CertificateThumbprint"
    }

    $signature = Set-AuthenticodeSignature `
        -FilePath $publishedExe `
        -Certificate $certificate `
        -TimestampServer $TimestampServer `
        -HashAlgorithm SHA256
    if ($signature.Status -ne "Valid") {
        throw "Wizard executable signing failed: $($signature.Status) $($signature.StatusMessage)"
    }
}

$signatureArgs = @{
    Path = $publishedExe
    ReportPath = $signatureReportPath
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
    $signatureArgs.ExpectedPublisher = $ExpectedPublisher
}
if ($Sign) {
    $signatureArgs.RequireValidSignature = $true
    $signatureArgs.RequireTimestamp = $true
}
& (Join-Path $PSScriptRoot "Test-WizardSignature.ps1") @signatureArgs | Out-Null

Copy-Item $publishedExe $assetExe -Force
if (Test-Path $assetZip) {
    Remove-Item $assetZip -Force
}

Compress-Archive -Path $publishedExe -DestinationPath $assetZip -Force

Write-Host "Wizard assets ready:" -ForegroundColor Green
Write-Host "  EXE: $assetExe"
Write-Host "  ZIP: $assetZip"
Write-Host "  Signature report: $signatureReportPath"
if ($Sign) {
    Write-Host "  Signature: Authenticode signed with $CertificateThumbprint"
}
Write-Host "After the release tag exists, upload with updater\Upload-WizardAsset.ps1." -ForegroundColor Yellow
