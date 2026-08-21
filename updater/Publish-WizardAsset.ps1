[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$ExpectedPublisher,
    [string]$HostedSyncConfigPath,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater.Wizard\ElectronicLogbook.Updater.Wizard.csproj"
$resolvedHostedSyncConfigPath = $null

if (-not [string]::IsNullOrWhiteSpace($HostedSyncConfigPath)) {
    $resolvedHostedSyncConfigPath = (Resolve-Path -LiteralPath $HostedSyncConfigPath).Path
    $hostedSyncConfig = Get-Content -LiteralPath $resolvedHostedSyncConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $hostedSyncUrl = $null
    if (-not [Uri]::TryCreate([string]$hostedSyncConfig.supabaseUrl, [UriKind]::Absolute, [ref]$hostedSyncUrl) -or
        ($hostedSyncUrl.Scheme -ne [Uri]::UriSchemeHttps -and -not $hostedSyncUrl.IsLoopback)) {
        throw "Hosted sync configuration must contain a secure Supabase URL."
    }
    if ([string]::IsNullOrWhiteSpace([string]$hostedSyncConfig.anonKey)) {
        throw "Hosted sync configuration must contain the public anon key."
    }
}

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
    $publishArguments = @(
        "publish",
        $projectPath,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "/p:PublishSingleFile=true",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:EnableCompressionInSingleFile=true",
        "/p:PublishTrimmed=false",
        "-o", $publishDir)
    if ($null -ne $resolvedHostedSyncConfigPath) {
        $publishArguments += "/p:HostedSyncConfigPath=$resolvedHostedSyncConfigPath"
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Wizard publish failed."
    }
}

$publishedExe = Join-Path $publishDir "ElectronicLogbook.Updater.Wizard.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Wizard publish output not found: $publishedExe"
}

if ($null -ne $resolvedHostedSyncConfigPath) {
    & $publishedExe --validate-hosted-configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Published wizard did not load its embedded hosted sync configuration."
    }
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
if ($null -ne $resolvedHostedSyncConfigPath) {
    Write-Host "  Hosted sync: embedded public client configuration"
} else {
    Write-Warning "Hosted sync configuration was not embedded; account connection will be unavailable unless runtime environment variables are set."
}
if ($Sign) {
    Write-Host "  Signature: Authenticode signed with $CertificateThumbprint"
}
Write-Host "After the release tag exists, upload with updater\Upload-WizardAsset.ps1." -ForegroundColor Yellow
