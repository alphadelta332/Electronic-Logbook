# Verifies the local ignored release-manifest private key matches the tracked public key.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path $RepoRoot).Path
$privateKeyPath = Join-Path $repoRoot ".github\release-manifest-signing-private-key.pem"
$publicKeyPath = Join-Path $repoRoot "updater\release-manifest-signing-public-key.pem"

if (-not (Test-Path -LiteralPath $privateKeyPath)) {
    throw "Local private key not found: $privateKeyPath"
}
if (-not (Test-Path -LiteralPath $publicKeyPath)) {
    throw "Tracked public key not found: $publicKeyPath"
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("ElectronicLogbookManifestKeyVerify-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null

try {
    $manifestPath = Join-Path $tempDirectory "release-manifest.json"
    $signaturePath = Join-Path $tempDirectory "release-manifest.json.sig"
    [System.IO.File]::WriteAllText(
        $manifestPath,
        "release-manifest signing verification",
        [System.Text.UTF8Encoding]::new($false))

    & (Join-Path $repoRoot "tools\Sign-ReleaseManifest.ps1") `
        -ManifestPath $manifestPath `
        -SignaturePath $signaturePath `
        -PrivateKeyPemPath $privateKeyPath | Out-Null
    & (Join-Path $repoRoot "tools\Test-ReleaseManifestSignature.ps1") `
        -ManifestPath $manifestPath `
        -SignaturePath $signaturePath `
        -PublicKeyPemPath $publicKeyPath | Out-Null
} finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Local release-manifest signing key pair verified." -ForegroundColor Green
