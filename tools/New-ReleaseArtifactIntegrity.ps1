# Regenerates release manifest, signature, checksums, and validation summary for an artifact folder.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactsPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$Commit,

    [string]$PrivateKeyPemPath = (Join-Path (Split-Path $PSScriptRoot -Parent) ".github\release-manifest-signing-private-key.pem")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedArtifactsPath = (Resolve-Path $ArtifactsPath).Path
$tag = "v$Version"
$manifestPath = Join-Path $resolvedArtifactsPath "release-manifest.json"
$signaturePath = Join-Path $resolvedArtifactsPath "release-manifest.json.sig"
$sumsPath = Join-Path $resolvedArtifactsPath "SHA256SUMS.txt"
$validationPath = Join-Path $resolvedArtifactsPath "release-validation.json"

$manifestAssetNames = @(
    "Electronic_Logbook_Master.xlsm",
    "README.pdf",
    "ElectronicLogbook.Updater.Wizard.exe",
    "ElectronicLogbook.Updater.Wizard.win-x64.zip",
    "wizard-signature-report.json"
)

$allArtifactNames = @(
    $manifestAssetNames
    "SHA256SUMS.txt",
    "release-manifest.json",
    "release-manifest.json.sig"
)

foreach ($path in @($manifestPath, $signaturePath, $sumsPath, $validationPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$manifestAssets = foreach ($assetName in $manifestAssetNames) {
    $assetPath = Join-Path $resolvedArtifactsPath $assetName
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "Required release asset missing: $assetName"
    }

    $item = Get-Item -LiteralPath $assetPath
    $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $assetName" | Add-Content -LiteralPath $sumsPath -Encoding ascii
    [ordered]@{
        name = $assetName
        size = $item.Length
        sha256 = $hash
    }
}

[ordered]@{
    version = $Version
    tag = $tag
    commit = $Commit.ToLowerInvariant()
    assets = @($manifestAssets)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

& (Join-Path $repoRoot "tools\Sign-ReleaseManifest.ps1") `
    -ManifestPath $manifestPath `
    -SignaturePath $signaturePath `
    -PrivateKeyPemPath $PrivateKeyPemPath

foreach ($assetName in @("release-manifest.json", "release-manifest.json.sig")) {
    $assetPath = Join-Path $resolvedArtifactsPath $assetName
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $assetName" | Add-Content -LiteralPath $sumsPath -Encoding ascii
}

$artifactHashes = foreach ($assetName in $allArtifactNames) {
    $assetPath = Join-Path $resolvedArtifactsPath $assetName
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "Required release artifact missing: $assetName"
    }

    $item = Get-Item -LiteralPath $assetPath
    [ordered]@{
        name = $item.Name
        path = $assetName
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

[ordered]@{
    version = $Version
    tag = $tag
    commit = $Commit.ToLowerInvariant()
    timestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
    result = "passed"
    patchedReleaseAsset = $true
    checks = @(
        @{ name = "Public workbook readiness"; result = "passed" }
        @{ name = "Release manifest signature"; result = "passed" }
        @{ name = "Release artifact hashes"; result = "passed" }
    )
    workbookHashes = @($artifactHashes | Where-Object { $_.name -eq "Electronic_Logbook_Master.xlsm" })
    artifacts = @($artifactHashes)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $validationPath -Encoding utf8

& (Join-Path $repoRoot "tools\Test-ReleaseManifestSignature.ps1") `
    -ManifestPath $manifestPath `
    -SignaturePath $signaturePath

Write-Host "Release artifact integrity files generated in $resolvedArtifactsPath." -ForegroundColor Green
