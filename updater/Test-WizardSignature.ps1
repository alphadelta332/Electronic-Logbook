[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [string]$ExpectedPublisher,
    [string]$ReportPath,
    [switch]$RequireValidSignature,
    [switch]$RequireTimestamp
)

$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$hash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
$signature = Get-AuthenticodeSignature -LiteralPath $resolvedPath
$signer = $signature.SignerCertificate
$timestamp = $signature.TimeStamperCertificate

$publisherMatches = $true
if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
    $publisherMatches = $false
    if ($null -ne $signer) {
        $publisherMatches = $signer.Subject -like "*$ExpectedPublisher*"
    }
}

$timestampPresent = $null -ne $timestamp
$isValid = $signature.Status -eq "Valid"
$problems = @()
if ($RequireValidSignature -and -not $isValid) {
    $problems += "Authenticode signature is not valid: $($signature.Status) $($signature.StatusMessage)"
}
if ($RequireTimestamp -and -not $timestampPresent) {
    $problems += "Authenticode timestamp is missing."
}
if (-not $publisherMatches) {
    $problems += "Signer publisher does not match expected publisher '$ExpectedPublisher'."
}

$report = [ordered]@{
    path = $resolvedPath
    fileName = Split-Path $resolvedPath -Leaf
    sha256 = $hash
    status = [string]$signature.Status
    statusMessage = [string]$signature.StatusMessage
    isValid = $isValid
    expectedPublisher = $ExpectedPublisher
    publisherMatches = $publisherMatches
    signer = if ($null -eq $signer) {
        $null
    } else {
        [ordered]@{
            subject = $signer.Subject
            issuer = $signer.Issuer
            thumbprint = $signer.Thumbprint
            notBefore = $signer.NotBefore.ToUniversalTime().ToString("o")
            notAfter = $signer.NotAfter.ToUniversalTime().ToString("o")
        }
    }
    timestamp = if ($null -eq $timestamp) {
        $null
    } else {
        [ordered]@{
            subject = $timestamp.Subject
            issuer = $timestamp.Issuer
            thumbprint = $timestamp.Thumbprint
            notBefore = $timestamp.NotBefore.ToUniversalTime().ToString("o")
            notAfter = $timestamp.NotAfter.ToUniversalTime().ToString("o")
        }
    }
    timestampPresent = $timestampPresent
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    problems = @($problems)
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDirectory = Split-Path $ReportPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
}

if ($problems.Count -gt 0) {
    throw ($problems -join " ")
}

$report
