[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('development', 'preview')][string]$Environment)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook/Supabase'
$metadataPath = Join-Path $localRoot 'hosted-preview-projects.local.json'
if (-not (Test-Path -LiteralPath $metadataPath)) { $metadataPath = Join-Path $localRoot 'hosted-pilot-projects.local.json' }
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$token = (Get-Content -LiteralPath (Join-Path $localRoot 'access-token.txt') -Raw).Trim()
$projectRef = $metadata.$Environment.project_ref
$managementHeaders = @{ Authorization = "Bearer $token" }
$project = Invoke-RestMethod -Uri "https://api.supabase.com/v1/projects/$projectRef" -Headers $managementHeaders
$expectedName = if ($Environment -eq 'preview') { 'FlightLogX Preview' } else { 'Electronic Logbook Development' }
if ($project.name -ne $expectedName -or $project.region -ne 'ap-southeast-2' -or $project.status -ne 'ACTIVE_HEALTHY') {
    throw 'The intended Sydney project is not healthy or its identity does not match.'
}
function Invoke-ProjectSql([string]$Query) {
    try {
        Invoke-RestMethod -Method Post -Uri "https://api.supabase.com/v1/projects/$projectRef/database/query" `
            -Headers $managementHeaders -ContentType 'application/json' -Body (@{ query = $Query } | ConvertTo-Json -Compress)
    } catch { throw "Supabase SQL request failed ($($_.Exception.Response.StatusCode.value__)); response omitted to protect server credentials." }
}
$version = '20261017000000'
$cutoverVersion = '20261017010000'
$applied = @(Invoke-ProjectSql 'select version from supabase_migrations.schema_migrations;')
$pending = @(Get-ChildItem (Join-Path $repoRoot 'supabase/migrations') -Filter '*.sql' | Where-Object { $_.BaseName.Split('_')[0] -notin $applied.version })
if (@($pending | Where-Object { $_.BaseName.Split('_')[0] -notin @($version, $cutoverVersion) }).Count) { throw 'Unrelated pending migrations require separate review.' }
if ($version -notin $applied.version) {
    $migration = Get-Content (Join-Path $repoRoot "supabase/migrations/$($version)_shared_airport_catalog.sql") -Raw
    $null = Invoke-ProjectSql $migration
    $escaped = $migration.Replace("'", "''")
    $null = Invoke-ProjectSql "insert into supabase_migrations.schema_migrations(version,name,statements) values ('$version','shared_airport_catalog',array['$escaped']);"
}
$workerRoot = Join-Path $repoRoot 'supabase/activity-worker'
$existingWorker = Get-Content -LiteralPath (Join-Path $workerRoot 'wrangler.local.json') -Raw | ConvertFrom-Json
if ($existingWorker.account_id -notmatch '^[a-f0-9]{32}$' -or $existingWorker.name -ne 'flightlogx-preview-activity') {
    throw 'Verified Cloudflare activity-worker account configuration required.'
}
$publisherToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$null = Invoke-RestMethod -Method Post -Uri "https://api.supabase.com/v1/projects/$projectRef/secrets" `
    -Headers $managementHeaders -ContentType 'application/json' `
    -Body (ConvertTo-Json -InputObject @(@{name='AIRPORT_CATALOG_PUBLISH_TOKEN';value=$publisherToken}) -Compress)
$previousAccessToken = $env:SUPABASE_ACCESS_TOKEN
try {
    $env:SUPABASE_ACCESS_TOKEN = $token
    Push-Location $repoRoot
    try { $deployOutput = & supabase.cmd functions deploy airport-catalog --project-ref $projectRef --use-api --no-verify-jwt 2>&1 | Out-String }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Airport function deployment failed.' }
} finally { $env:SUPABASE_ACCESS_TOKEN = $previousAccessToken }
$keys = Invoke-RestMethod -Uri "https://api.supabase.com/v1/projects/$projectRef/api-keys?reveal=true" -Headers $managementHeaders
$serviceKey = ($keys | Where-Object { $_.type -eq 'secret' -and $_.name -eq 'default' } | Select-Object -First 1).api_key
if ([string]::IsNullOrWhiteSpace($serviceKey)) { $serviceKey = ($keys | Where-Object { $_.name -eq 'service_role' } | Select-Object -First 1).api_key }
if ([string]::IsNullOrWhiteSpace($serviceKey)) { throw 'Server credential unavailable.' }
$origin = "https://$projectRef.supabase.co"
$headers = @{ Authorization = "Bearer $serviceKey"; apikey = $serviceKey }
$null = Invoke-RestMethod -Method Post -Uri "$origin/storage/v1/object/airport-catalog/seed.json.gz" `
    -Headers ($headers + @{ 'x-upsert' = 'true'; 'cache-control' = 'max-age=31536000' }) `
    -ContentType 'application/gzip' -InFile (Join-Path $repoRoot 'mobile/src/ElectronicLogbook.Mobile/Data/airports.json.gz')
$result = Invoke-RestMethod -Method Post -Uri "$origin/functions/v1/airport-catalog" -Headers @{Authorization="Bearer $publisherToken"} -ContentType 'application/json' -Body '{}' -TimeoutSec 120
if ($result.status -notin @('published', 'current')) { throw 'Airport catalogue was not published.' }
$result | Select-Object status,cycle,count,sha256
$publishable = ($keys | Where-Object { $_.type -eq 'publishable' -and $_.name -eq 'default' } | Select-Object -First 1).api_key
if ($publishable -notmatch '^sb_publishable_[A-Za-z0-9_-]+$') { throw 'Expected publishable activity-probe key unavailable.' }
$workerConfig = [ordered]@{
    name = if ($Environment -eq 'preview') { 'flightlogx-preview-activity' } else { 'flightlogx-development-airports' }
    main = 'worker.mjs'; account_id=$existingWorker.account_id; compatibility_date='2026-10-05'
    workers_dev=$false; preview_urls=$false
    vars=@{SUPABASE_URL=$origin;SUPABASE_PUBLISHABLE_KEY=$publishable}
    triggers=@{crons=@(if ($Environment -eq 'preview') { '17 */4 * * *'; '7 * * * *' } else { '7 * * * *' })}
    observability=@{enabled=$true;head_sampling_rate=1}
}
$configPath = Join-Path $workerRoot $(if ($Environment -eq 'preview') {'wrangler.local.json'} else {'wrangler.development.local.json'})
$workerConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath
$workerOutput = & wrangler.cmd deploy --config $configPath 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cloudflare airport scheduler deployment failed; private output suppressed.' }
$secretOutput = $publisherToken | & wrangler.cmd secret put AIRPORT_CATALOG_PUBLISH_TOKEN --config $configPath 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cloudflare publisher-only secret setup failed; private output suppressed.' }
Write-Host "Airport publisher and Cloudflare hourly timer deployed to $expectedName. Confirm a real scheduled success before applying the external_airport_scheduler migration."
