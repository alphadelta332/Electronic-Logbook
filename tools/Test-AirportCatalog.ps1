[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('development', 'preview')][string]$Environment)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook/Supabase'
$path = Join-Path $localRoot 'hosted-preview-projects.local.json'
if (-not (Test-Path $path)) { $path = Join-Path $localRoot 'hosted-pilot-projects.local.json' }
$meta = Get-Content $path -Raw | ConvertFrom-Json
$projectRef = $meta.$Environment.project_ref
$origin = "https://$projectRef.supabase.co"
$root = "$origin/storage/v1/object/public/airport-catalog/"
$manifest = Invoke-RestMethod -Uri ($root + 'manifest.json?validation=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
$epoch = [datetime]'2024-01-25'
$cycle = $epoch.AddDays([math]::Floor(([datetime]::UtcNow.Date - $epoch).TotalDays / 28) * 28).ToString('yyyy-MM-dd')
if ($manifest.schemaVersion -ne 1 -or $manifest.cycle -ne $cycle -or $manifest.count -lt 5000 -or $manifest.sha256 -notmatch '^[a-f0-9]{64}$' -or $manifest.path -ne "snapshots/$($manifest.sha256).json.gz") { throw 'Invalid public manifest.' }
$response = Invoke-WebRequest -Uri ($root + $manifest.path)
$compressed = $response.Content
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($compressed)).ToLowerInvariant()
if ($hash -ne $manifest.sha256) { throw 'Snapshot checksum mismatch.' }
function Expand-Airports([byte[]]$Bytes) {
    $inputStream = [IO.MemoryStream]::new($Bytes, $false)
    $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($gzip)
    try { return @($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose(); $inputStream.Dispose() }
}
$airports = Expand-Airports $compressed
$seed = Expand-Airports ([IO.File]::ReadAllBytes((Join-Path $repoRoot 'mobile/src/ElectronicLogbook.Mobile/Data/airports.json.gz')))
if ($airports.Count -ne $manifest.count) { throw 'Snapshot count mismatch.' }
$byCode = @{}
foreach ($airport in $airports) {
    if ($byCode.ContainsKey($airport.i) -or [string]::IsNullOrWhiteSpace($airport.n) -or [math]::Abs($airport.y) -gt 90 -or [math]::Abs($airport.x) -gt 180) { throw 'Invalid snapshot airport.' }
    $byCode[$airport.i] = $airport
}
$coordinateUpdates = 0
foreach ($airport in $seed) {
    $current = $byCode[$airport.i]
    if ($null -eq $current -or $current.n -cne $airport.n -or $current.a -cne $airport.a -or $current.b -cne $airport.b) { throw 'An existing airport, name or alias was replaced.' }
    if ($current.x -ne $airport.x -or $current.y -ne $airport.y) { $coordinateUpdates++ }
}
$unauthorized = Invoke-WebRequest -Method Post -Uri "$origin/functions/v1/airport-catalog" -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
if ($unauthorized.StatusCode -ne 401) { throw 'Unauthenticated publisher was not rejected.' }
$token = (Get-Content (Join-Path $localRoot 'access-token.txt') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$keys = Invoke-RestMethod -Uri "https://api.supabase.com/v1/projects/$projectRef/api-keys" -Headers $headers
$anon = ($keys | Where-Object name -eq 'anon' | Select-Object -First 1).api_key
$write = Invoke-WebRequest -Method Post -Uri "$origin/storage/v1/object/airport-catalog/anonymous-write-check.json" -Headers @{apikey=$anon;Authorization="Bearer $anon"} -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
if ($write.StatusCode -lt 400) { throw 'Unauthenticated public-bucket write was accepted.' }
function Sql([string]$Query) {
    Invoke-RestMethod -Method Post -Uri "https://api.supabase.com/v1/projects/$projectRef/database/query" -Headers $headers -ContentType 'application/json' -Body (@{query=$Query}|ConvertTo-Json -Compress)
}
$legacy = Sql "select (select count(*) from cron.job where jobname='flightlogx-airport-catalog') as jobs, (select count(*) from pg_extension where extname='pg_net') as extensions;"
if ($legacy.jobs -ne 0 -or $legacy.extensions -ne 0) { throw 'Legacy airport timer or pg_net remains; external cutover is incomplete.' }
[pscustomobject]@{ Environment=$Environment; Cycle=$cycle; Airports=$airports.Count; RetainedSeed=$seed.Count; CoordinateUpdates=$coordinateUpdates; Added=$airports.Count-$seed.Count; CompressedBytes=$compressed.Length; Sha256=$hash; UnauthorizedPublisher=401; UnauthorizedWrite=$write.StatusCode; LegacySchedulerRemoved=$true }
