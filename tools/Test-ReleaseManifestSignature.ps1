# Verifies release-manifest.json.sig against the tracked release public key.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PublicKeyPemPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "updater\release-manifest-signing-public-key.pem")
)

$ErrorActionPreference = "Stop"
$manifestPath = (Resolve-Path $ManifestPath).Path
if ([string]::IsNullOrWhiteSpace($SignaturePath)) {
    $SignaturePath = "$manifestPath.sig"
}
$signaturePath = (Resolve-Path $SignaturePath).Path
$publicKeyPemPath = (Resolve-Path $PublicKeyPemPath).Path

$publicKeyPem = Get-Content -LiteralPath $publicKeyPemPath -Raw -Encoding UTF8
if (-not $publicKeyPem.Contains("BEGIN PUBLIC KEY", [System.StringComparison]::Ordinal)) {
    throw "Release manifest signing public key is not configured: $publicKeyPemPath"
}

$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
$signatureBytes = [System.IO.File]::ReadAllBytes($signaturePath)
$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem($publicKeyPem)
    $valid = $ecdsa.VerifyData(
        $manifestBytes,
        $signatureBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
} finally {
    $ecdsa.Dispose()
}

if (-not $valid) {
    throw "release-manifest.json signature verification failed."
}

Write-Host "release-manifest.json signature verified." -ForegroundColor Green
