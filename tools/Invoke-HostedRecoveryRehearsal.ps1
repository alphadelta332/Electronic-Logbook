# Runs one disposable end-to-end recovery rehearsal against the hosted development project.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$localRoot = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook\Supabase'
$metadataPath = Join-Path $localRoot 'hosted-pilot-projects.local.json'
$tokenPath = Join-Path $localRoot 'access-token.txt'
$projectPath = Join-Path $repoRoot 'supabase\tests\HostedRecoveryRehearsal\HostedRecoveryRehearsal.csproj'

foreach ($path in @($metadataPath, $tokenPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required hosted-rehearsal input is missing: $path"
    }
}

$supabase = Get-Command 'supabase' -ErrorAction Stop | Select-Object -First 1
$psql = Get-Command 'psql' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $psql) {
    $psqlPath = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
    if (-not (Test-Path -LiteralPath $psqlPath -PathType Leaf)) {
        throw 'PostgreSQL 17 psql is required for trigger-safe disposable ledger cleanup.'
    }
    $psql = Get-Item -LiteralPath $psqlPath
}
$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$projectRef = [string]$metadata.development.project_ref
if ([string]::IsNullOrWhiteSpace($projectRef)) {
    throw 'Development project metadata does not contain a project ref.'
}

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $repoRoot ('artifacts\private-pilot-hosted-recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$evidencePath = Join-Path $EvidenceDirectory 'verification.json'

$env:SUPABASE_ACCESS_TOKEN = (Get-Content -LiteralPath $tokenPath -Raw -Encoding UTF8).Trim()
$keys = @(& $supabase.Source projects api-keys --project-ref $projectRef --output json | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not read the development project API keys.'
}
$anonKey = [string](($keys | Where-Object name -eq 'anon' | Select-Object -First 1).api_key)
$serviceRoleKey = [string](($keys | Where-Object name -eq 'service_role' | Select-Object -First 1).api_key)
if ([string]::IsNullOrWhiteSpace($anonKey) -or [string]::IsNullOrWhiteSpace($serviceRoleKey)) {
    throw 'The development project did not expose legacy anon and service_role keys required by the mobile pilot client.'
}

try {
    $env:ELB_REHEARSAL_SUPABASE_URL = "https://$projectRef.supabase.co"
    $env:ELB_REHEARSAL_ANON_KEY = $anonKey
    $env:ELB_REHEARSAL_SERVICE_ROLE_KEY = $serviceRoleKey
    $env:ELB_REHEARSAL_EVIDENCE_PATH = $evidencePath
    $env:ELB_REHEARSAL_PSQL_PATH = $psql.FullName
    $env:ELB_REHEARSAL_DB_HOST = 'aws-0-ap-southeast-2.pooler.supabase.com'
    $env:ELB_REHEARSAL_DB_USER = "postgres.$projectRef"
    $env:ELB_REHEARSAL_DB_PASSWORD = [string]$metadata.development.db_password

    Write-Host 'Running a disposable hosted recovery rehearsal.' -ForegroundColor Cyan
    Write-Host 'Secrets and disposable identifiers are held only in process memory; evidence is redacted.' -ForegroundColor Yellow
    & dotnet run --project $projectPath --no-launch-profile
    if ($LASTEXITCODE -ne 0) {
        throw 'Hosted recovery rehearsal failed. Review the redacted evidence file.'
    }
}
finally {
    Remove-Item Env:ELB_REHEARSAL_SUPABASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_ANON_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_SERVICE_ROLE_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_EVIDENCE_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_PSQL_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_HOST -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_USER -ErrorAction SilentlyContinue
    Remove-Item Env:ELB_REHEARSAL_DB_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:SUPABASE_ACCESS_TOKEN -ErrorAction SilentlyContinue
}

Write-Host "Redacted evidence: $evidencePath" -ForegroundColor Green
