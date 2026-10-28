[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('development', 'preview')][string]$Environment)
$ErrorActionPreference = 'Stop'
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook/Supabase'
$metadataPath = Join-Path $localRoot 'hosted-preview-projects.local.json'
if (-not (Test-Path -LiteralPath $metadataPath)) { $metadataPath = Join-Path $localRoot 'hosted-pilot-projects.local.json' }
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$projectRef = $metadata.$Environment.project_ref
if ($projectRef -notmatch '^[a-z]{20}$') { throw 'Invalid aircraft project reference.' }
$accessToken = (Get-Content -LiteralPath (Join-Path $localRoot 'access-token.txt') -Raw).Trim()
$keys = Invoke-RestMethod -Uri "https://api.supabase.com/v1/projects/$projectRef/api-keys" -Headers @{ Authorization = "Bearer $accessToken" }
$anon = ($keys | Where-Object name -eq 'anon' | Select-Object -First 1).api_key
if ([string]::IsNullOrWhiteSpace($anon)) { throw 'Anonymous project key unavailable.' }
$previousOrigin = $env:AIRCRAFT_CATALOG_TEST_ORIGIN; $previousAnon = $env:AIRCRAFT_CATALOG_TEST_ANON_KEY
try {
    $env:AIRCRAFT_CATALOG_TEST_ORIGIN = "https://$projectRef.supabase.co"; $env:AIRCRAFT_CATALOG_TEST_ANON_KEY = $anon
    & node (Join-Path $PSScriptRoot 'test-aircraft-catalog.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Hosted aircraft catalogue validation failed.' }
} finally { $env:AIRCRAFT_CATALOG_TEST_ORIGIN = $previousOrigin; $env:AIRCRAFT_CATALOG_TEST_ANON_KEY = $previousAnon }
