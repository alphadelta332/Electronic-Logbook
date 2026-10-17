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
$applied = @(Invoke-ProjectSql 'select version from supabase_migrations.schema_migrations;')
$pending = @(Get-ChildItem (Join-Path $repoRoot 'supabase/migrations') -Filter '*.sql' | Where-Object { $_.BaseName.Split('_')[0] -notin $applied.version })
if (@($pending | Where-Object { $_.BaseName -notlike "$($version)_*" }).Count) { throw 'Unrelated pending migrations require separate review.' }
if ($version -notin $applied.version) {
    $migration = Get-Content (Join-Path $repoRoot "supabase/migrations/$($version)_shared_airport_catalog.sql") -Raw
    $null = Invoke-ProjectSql $migration
    $escaped = $migration.Replace("'", "''")
    $null = Invoke-ProjectSql "insert into supabase_migrations.schema_migrations(version,name,statements) values ('$version','shared_airport_catalog',array['$escaped']);"
}
$previousAccessToken = $env:SUPABASE_ACCESS_TOKEN
try {
    $env:SUPABASE_ACCESS_TOKEN = $token
    Push-Location $repoRoot
    try { & supabase.cmd functions deploy airport-catalog --project-ref $projectRef --use-api --no-verify-jwt }
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
foreach ($secret in @(@{ Name = 'airport_catalog_url'; Value = $origin }, @{ Name = 'airport_catalog_service_role'; Value = $serviceKey })) {
    $name = $secret.Name
    $value = $secret.Value.Replace("'", "''")
    $query = "do `$body`$ declare existing_id uuid; begin select id into existing_id from vault.secrets where name='$name'; if existing_id is null then perform vault.create_secret('$value','$name'); else perform vault.update_secret(existing_id,'$value','$name'); end if; end `$body`$;"
    $null = Invoke-ProjectSql $query
}
$result = Invoke-RestMethod -Method Post -Uri "$origin/functions/v1/airport-catalog" -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 120
if ($result.status -notin @('published', 'current')) { throw 'Airport catalogue was not published.' }
$result | Select-Object status,cycle,count,sha256
$null = Invoke-ProjectSql "select net.http_post(url := '$origin/functions/v1/airport-catalog', headers := jsonb_build_object('Content-Type','application/json','Authorization','Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name='airport_catalog_service_role')), body := '{}'::jsonb, timeout_milliseconds := 120000);"
Write-Host "Verified airport publisher deployed to $expectedName; scheduled job installed."
