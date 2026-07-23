# Generates the no-cost ECDSA key pair used to sign release-manifest.json.
# Commit only the public key. Store the private key as the protected
# RELEASE_MANIFEST_SIGNING_PRIVATE_KEY GitHub environment secret.

#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$PrivateKeyPath,
    [string]$PublicKeyPath
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path $RepoRoot).Path

if ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
    $PrivateKeyPath = Join-Path $repoRoot ".github\release-manifest-signing-private-key.pem"
}
if ([string]::IsNullOrWhiteSpace($PublicKeyPath)) {
    $PublicKeyPath = Join-Path $repoRoot "updater\release-manifest-signing-public-key.pem"
}

if (Test-Path -LiteralPath $PrivateKeyPath) {
    throw "Private key already exists: $PrivateKeyPath"
}

$privateDirectory = Split-Path $PrivateKeyPath -Parent
if (-not [string]::IsNullOrWhiteSpace($privateDirectory)) {
    New-Item -ItemType Directory -Force -Path $privateDirectory | Out-Null
}

$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
try {
    [System.IO.File]::WriteAllText(
        $PrivateKeyPath,
        $ecdsa.ExportECPrivateKeyPem(),
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        $PublicKeyPath,
        $ecdsa.ExportSubjectPublicKeyInfoPem(),
        [System.Text.UTF8Encoding]::new($false))
} finally {
    $ecdsa.Dispose()
}

Write-Host ""
Write-Host "Private key written to ignored local path:" -ForegroundColor Yellow
Write-Host "  $PrivateKeyPath"
Write-Host ""
Write-Host "Public key written to tracked path:" -ForegroundColor Green
Write-Host "  $PublicKeyPath"
Write-Host ""
Write-Host "Next step: add the full private key text to the protected release environment secret:" -ForegroundColor Yellow
Write-Host "  RELEASE_MANIFEST_SIGNING_PRIVATE_KEY"
