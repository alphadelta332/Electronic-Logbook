# Runs the disposable local managed-recovery cryptographic round-trip harness.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$FunctionSupabaseUrl = "http://host.docker.internal:54321",
    [switch]$SkipFunctionServe
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$projectPath = Join-Path $repoRoot "supabase\tests\RecoveryEnvelopeCryptoHarness\RecoveryEnvelopeCryptoHarness.csproj"
$supabaseCommand = Get-Command supabase.cmd -ErrorAction SilentlyContinue
if ($null -eq $supabaseCommand) {
    $supabaseCommand = Get-Command supabase -ErrorAction Stop
}
$psqlCommand = Get-Command psql -ErrorAction Stop
$dockerCommand = Get-Command docker -ErrorAction Stop

$arguments = @(
    "run",
    "--project",
    $projectPath,
    "--",
    "--repo-root",
    $repoRoot,
    "--function-supabase-url",
    $FunctionSupabaseUrl,
    "--supabase-cli",
    $supabaseCommand.Source,
    "--psql-cli",
    $psqlCommand.Source,
    "--docker-cli",
    $dockerCommand.Source
)

if ($SkipFunctionServe) {
    $arguments += "--skip-function-serve"
}

Write-Host "Running disposable local recovery-envelope cryptographic harness." -ForegroundColor Cyan
Write-Host "The harness prints only redacted pass/fail evidence and deletes its temporary rows and env file." -ForegroundColor Yellow

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Recovery-envelope cryptographic harness failed."
}
