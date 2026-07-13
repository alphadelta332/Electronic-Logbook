[CmdletBinding()]
param(
    [string]$ModulePath,
    [string]$OutputDirectory,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [string]$Ref
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ModulePath)) { $ModulePath = Join-Path $repoRoot "modUpdate.bas" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $repoRoot }
if ([string]::IsNullOrWhiteSpace($Ref)) { $Ref = (git -C $repoRoot rev-parse HEAD).Trim() }
if ($Ref -notmatch '^[0-9a-fA-F]{40}$') { throw "Ref must be a 40-character SHA." }

$certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
    Where-Object { $_.Thumbprint -eq $CertificateThumbprint -and $_.HasPrivateKey } |
    Select-Object -First 1
if ($null -eq $certificate) { throw "Signing certificate with private key was not found: $CertificateThumbprint" }

$module = Get-Item -LiteralPath (Resolve-Path -LiteralPath $ModulePath)
$manifest = [ordered]@{
    version = ((Get-Content (Join-Path $repoRoot "version.txt") -Raw -Encoding UTF8).Trim())
    ref = $Ref.ToLowerInvariant()
    assets = @([ordered]@{
        name = $module.Name
        size = [int64]$module.Length
        sha256 = (Get-FileHash -LiteralPath $module.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestPath = Join-Path $OutputDirectory "modUpdate-manifest.json"
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Add-Type -AssemblyName System.Security
$cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new([System.IO.File]::ReadAllBytes($manifestPath)), $true)
$cms.ComputeSignature([System.Security.Cryptography.Pkcs.CmsSigner]::new($certificate))
[System.IO.File]::WriteAllBytes("$manifestPath.p7s", $cms.Encode())
Write-Host "Signed modUpdate manifest created: $manifestPath" -ForegroundColor Green
