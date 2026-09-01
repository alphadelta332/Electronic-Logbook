# Creates the gitignored Android hosted-sync runtime config for Preview rehearsal.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SupabaseUrl,

    [Parameter(Mandatory)]
    [string]$AnonKey,

    [string]$PlatformLabel = "Pixel 8 Pro",

    [string]$DisplayName = "Project owner",

    [string]$GoogleWebClientId,

    [string]$GoogleAuthDirectory = (Join-Path $env:LOCALAPPDATA "ElectronicLogbook\Google Auth"),

    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$parsedSupabaseUrl = $null
if (-not [Uri]::TryCreate($SupabaseUrl, [UriKind]::Absolute, [ref]$parsedSupabaseUrl)) {
    throw "SupabaseUrl must be an absolute URL such as https://project-ref.supabase.co."
}

if ([string]::IsNullOrWhiteSpace($AnonKey)) {
    throw "AnonKey is required."
}

if ([string]::IsNullOrWhiteSpace($GoogleWebClientId)) {
    $googleWebClientIdPath = Join-Path $GoogleAuthDirectory "webclientid.txt"
    if (Test-Path -LiteralPath $googleWebClientIdPath) {
        $GoogleWebClientId = (Get-Content -LiteralPath $googleWebClientIdPath -Raw -Encoding UTF8).Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($GoogleWebClientId) -and
    $GoogleWebClientId -notmatch '^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$') {
    throw "GoogleWebClientId must be a Google OAuth Web client ID."
}

$outputPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\wwwroot\hosted-sync.local.json"
$config = [pscustomobject][ordered]@{
    supabaseUrl = $SupabaseUrl.TrimEnd("/")
    anonKey = $AnonKey
    platformLabel = $PlatformLabel
    displayName = $DisplayName
    googleWebClientId = if ([string]::IsNullOrWhiteSpace($GoogleWebClientId)) { $null } else { $GoogleWebClientId }
}

$config |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $outputPath -Encoding UTF8

Write-Host "Mobile hosted-sync local config written to $outputPath." -ForegroundColor Green
Write-Host "Anon key was written to the gitignored local file and was not printed." -ForegroundColor Yellow
Write-Host "Only the public Google Web client ID was written; the Google client secret was not read or copied." -ForegroundColor Yellow
