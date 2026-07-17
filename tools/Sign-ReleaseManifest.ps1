# Signs release-manifest.json with ECDSA P-256/SHA-256.
# The signature format is IEEE P1363 fixed-field concatenation.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PrivateKeyPemPath,
    [string]$PrivateKeyPem = $env:RELEASE_MANIFEST_SIGNING_PRIVATE_KEY
)

$ErrorActionPreference = "Stop"
$manifestPath = (Resolve-Path $ManifestPath).Path

if ([string]::IsNullOrWhiteSpace($SignaturePath)) {
    $SignaturePath = "$manifestPath.sig"
}

if (-not [string]::IsNullOrWhiteSpace($PrivateKeyPemPath)) {
    $PrivateKeyPem = Get-Content -LiteralPath $PrivateKeyPemPath -Raw -Encoding UTF8
}

if ([string]::IsNullOrWhiteSpace($PrivateKeyPem) -or
    -not $PrivateKeyPem.Contains("BEGIN EC PRIVATE KEY", [System.StringComparison]::Ordinal)) {
    throw "Release manifest private signing key is not configured."
}

$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem($PrivateKeyPem)
    $signature = $ecdsa.SignData(
        $manifestBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    [System.IO.File]::WriteAllBytes($SignaturePath, $signature)
} finally {
    $ecdsa.Dispose()
}

Write-Host "Signed release manifest: $SignaturePath" -ForegroundColor Green
