# Creates the gitignored Android hosted-sync runtime config for private-pilot rehearsal.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SupabaseUrl,

    [Parameter(Mandatory)]
    [string]$AnonKey,

    [string]$PlatformLabel = "Pixel 8 Pro",

    [string]$DisplayName = "Project owner",

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

$outputPath = Join-Path $repoRoot "mobile\src\ElectronicLogbook.Mobile\wwwroot\hosted-sync.local.json"
$config = [pscustomobject][ordered]@{
    supabaseUrl = $SupabaseUrl.TrimEnd("/")
    anonKey = $AnonKey
    platformLabel = $PlatformLabel
    displayName = $DisplayName
}

$config |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $outputPath -Encoding UTF8

Write-Host "Mobile hosted-sync local config written to $outputPath." -ForegroundColor Green
Write-Host "Anon key was written to the gitignored local file and was not printed." -ForegroundColor Yellow
