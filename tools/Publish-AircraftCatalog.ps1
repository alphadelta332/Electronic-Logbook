[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('development', 'preview')][string]$Environment)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook/Supabase'
$metadataPath = Join-Path $localRoot 'hosted-preview-projects.local.json'
if (-not (Test-Path -LiteralPath $metadataPath)) { $metadataPath = Join-Path $localRoot 'hosted-pilot-projects.local.json' }
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$accessToken = (Get-Content -LiteralPath (Join-Path $localRoot 'access-token.txt') -Raw).Trim()
$projectRef = $metadata.$Environment.project_ref
if ($projectRef -notmatch '^[a-z]{20}$') { throw 'Invalid aircraft project reference.' }
$managementHeaders = @{ Authorization = "Bearer $accessToken" }
$project = Invoke-RestMethod -Uri "https://api.supabase.com/v1/projects/$projectRef" -Headers $managementHeaders
$expectedName = if ($Environment -eq 'preview') { 'FlightLogX Preview' } else { 'Electronic Logbook Development' }
if ($project.name -ne $expectedName -or $project.region -ne 'ap-southeast-2' -or $project.status -ne 'ACTIVE_HEALTHY') {
    throw 'The intended Sydney project is not healthy or its identity does not match.'
}
$workerRoot = Join-Path $repoRoot 'supabase/activity-worker'
$configPath = Join-Path $workerRoot $(if ($Environment -eq 'preview') { 'wrangler.local.json' } else { 'wrangler.development.local.json' })
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$expectedWorker = if ($Environment -eq 'preview') { 'flightlogx-preview-activity' } else { 'flightlogx-development-airports' }
$origin = "https://$projectRef.supabase.co"
if ($config.name -ne $expectedWorker -or $config.account_id -notmatch '^[a-f0-9]{32}$' -or $config.vars.SUPABASE_URL -ne $origin) {
    throw 'Worker identity/project mismatch; existing configuration must be inspected first.'
}
$loginOutput = & wrangler.cmd whoami 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Wrangler authentication unavailable; run wrangler.cmd login.' }
$cfToken = $env:CLOUDFLARE_API_TOKEN
if ([string]::IsNullOrWhiteSpace($cfToken)) {
    $authPath = Join-Path $env:APPDATA 'xdg.config/.wrangler/config/default.toml'
    $match = [regex]::Match((Get-Content -LiteralPath $authPath -Raw), '(?m)^oauth_token\s*=\s*"([^"]+)"')
    if (-not $match.Success) { throw 'Wrangler OAuth credential unavailable.' }
    $cfToken = $match.Groups[1].Value
}
$cfHeaders = @{ Authorization = "Bearer $cfToken" }
$schedulesUrl = "https://api.cloudflare.com/client/v4/accounts/$($config.account_id)/workers/scripts/$expectedWorker/schedules"
$remote = Invoke-RestMethod -Uri $schedulesUrl -Headers $cfHeaders
$remoteCrons = @($remote.result.schedules.cron | Sort-Object)
$localCrons = @($config.triggers.crons | Sort-Object)
if (-not $remote.success -or ($remoteCrons -join ',') -ne ($localCrons -join ',')) {
    throw 'Remote/local Worker timers differ; inspect before changing the existing schedules.'
}
function Invoke-AircraftSql([string]$Query) {
    try {
        Invoke-RestMethod -Method Post -Uri "https://api.supabase.com/v1/projects/$projectRef/database/query" `
            -Headers $managementHeaders -ContentType 'application/json' -Body (@{ query = $Query } | ConvertTo-Json -Compress)
    } catch { throw "Aircraft SQL request failed ($([int]$_.Exception.Response.StatusCode)); response omitted." }
}
$version = '20261028000000'
$applied = @(Invoke-AircraftSql "select version from supabase_migrations.schema_migrations where version='$version';")
if ($version -notin $applied.version) {
    $migration = Get-Content (Join-Path $repoRoot "supabase/migrations/$($version)_shared_aircraft_catalog.sql") -Raw
    $escaped = $migration.Replace("'", "''")
    $null = Invoke-AircraftSql "begin; $migration insert into supabase_migrations.schema_migrations(version,name,statements) values ('$version','shared_aircraft_catalog',array['$escaped']); commit;"
}
$bucket = @(Invoke-AircraftSql "select id, public, file_size_limit from storage.buckets where id='aircraft-catalog';")
if ($bucket.id -ne 'aircraft-catalog' -or -not $bucket.public -or $bucket.file_size_limit -ne 1048576) {
    throw 'Aircraft reference bucket was not created with the expected public-read/size settings.'
}
$publisherToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$null = Invoke-RestMethod -Method Post -Uri "https://api.supabase.com/v1/projects/$projectRef/secrets" `
    -Headers $managementHeaders -ContentType 'application/json' `
    -Body (ConvertTo-Json -InputObject @(@{ name = 'AIRCRAFT_CATALOG_PUBLISH_TOKEN'; value = $publisherToken }) -Compress)
$previousToken = $env:SUPABASE_ACCESS_TOKEN
try {
    $env:SUPABASE_ACCESS_TOKEN = $accessToken
    Push-Location $repoRoot
    try { $deployOutput = & supabase.cmd functions deploy aircraft-catalog --project-ref $projectRef --use-api --no-verify-jwt 2>&1 | Out-String }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Aircraft function deployment failed; private output suppressed.' }
} finally { $env:SUPABASE_ACCESS_TOKEN = $previousToken }
$result = Invoke-RestMethod -Method Post -Uri "$origin/functions/v1/aircraft-catalog" `
    -Headers @{ Authorization = "Bearer $publisherToken" } -ContentType 'application/json' -Body '{}' -TimeoutSec 120
if ($result.status -notin @('published', 'current')) { throw 'Aircraft catalogue was not published.' }
$result | Select-Object status, cycle, count, sha256
$secretOutput = $publisherToken | & wrangler.cmd secret put AIRCRAFT_CATALOG_PUBLISH_TOKEN --config $configPath 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Aircraft Worker secret setup failed; private output suppressed.' }
$originalConfig = Get-Content -LiteralPath $configPath -Raw
$config.triggers.crons = @($config.triggers.crons | Where-Object { $_ -ne '11 * * * *' }) + @('11 * * * *')
$config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath
$workerOutput = & wrangler.cmd deploy --config $configPath 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    [IO.File]::WriteAllText($configPath, $originalConfig)
    throw 'Aircraft scheduler deployment failed; previous local timer configuration restored. Inspect remote state before retry.'
}
$remote = Invoke-RestMethod -Uri $schedulesUrl -Headers $cfHeaders
if (-not $remote.success -or (@($remote.result.schedules.cron | Sort-Object) -join ',') -ne (@($config.triggers.crons | Sort-Object) -join ',')) {
    throw 'Deployed aircraft timers did not match the read-back configuration.'
}
Write-Host "Aircraft publisher deployed to $expectedName; hourly minute-11 timer added, existing timers retained."
