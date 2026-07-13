[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string[]]$AssetPath,
    [string]$Tag,
    [string]$Commit,
    [string]$OutputDirectory,
    [Parameter(Mandatory)][string]$CertificateThumbprint
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = "v$Version" }
if ([string]::IsNullOrWhiteSpace($Commit)) {
    $Commit = (git -C $repoRoot rev-parse HEAD).Trim()
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $PSScriptRoot
}
if ($Tag -ne "v$Version") { throw "Tag '$Tag' does not match version '$Version'." }
if ($Commit -notmatch '^[0-9a-fA-F]{40}$') { throw "Commit must be a 40-character SHA." }

$certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
    Where-Object { $_.Thumbprint -eq $CertificateThumbprint -and $_.HasPrivateKey } |
    Select-Object -First 1
if ($null -eq $certificate) { throw "Signing certificate with private key was not found: $CertificateThumbprint" }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$assets = @(
foreach ($path in $AssetPath) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $file = Get-Item -LiteralPath $resolved
    [pscustomobject][ordered]@{
        name = $file.Name
        size = [int64]$file.Length
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
)
if ((@($assets | Select-Object -ExpandProperty name | Sort-Object -Unique).Count) -ne $assets.Count) {
    throw "Release manifest asset names must be unique."
}

$manifest = [ordered]@{
    version = $Version
    tag = $Tag
    commit = $Commit.ToLowerInvariant()
    assets = @($assets)
}
$manifestPath = Join-Path $OutputDirectory "release-manifest.json"
$signaturePath = "$manifestPath.p7s"
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 5),
    (New-Object System.Text.UTF8Encoding($false)))

Add-Type -AssemblyName System.Security
$content = [System.Security.Cryptography.Pkcs.ContentInfo]::new([System.IO.File]::ReadAllBytes($manifestPath))
$cms = [System.Security.Cryptography.Pkcs.SignedCms]::new($content, $true)
$signer = [System.Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
$signer.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
$cms.ComputeSignature($signer)
[System.IO.File]::WriteAllBytes($signaturePath, $cms.Encode())

Write-Host "Signed release manifest created:" -ForegroundColor Green
Write-Host "  $manifestPath"
Write-Host "  $signaturePath"
