#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'ElectronicLogbook/HostedBackup/r2-credentials.local.json'
if (Test-Path -LiteralPath $destination) { throw 'R2 credentials already exist. They will not be replaced.' }
$accountConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../supabase/activity-worker/wrangler.local.json') -Raw | ConvertFrom-Json
if ($accountConfig.account_id -notmatch '^[a-f0-9]{32}$') { throw 'Verified Cloudflare account configuration is missing.' }
$account = $accountConfig.account_id
Write-Host 'Create two restricted R2 access keys. Do not send any keys to chat.'
Write-Host 'The Cloudflare R2 page will open in your browser.'
Start-Process "https://dash.cloudflare.com/$account/r2/overview" -WindowStyle Normal
$credentials = @{ accountId = $account; bucket = 'flightlogx-preview-backups' }
foreach ($kind in @('upload', 'restore')) {
    $permission = if ($kind -eq 'upload') { 'Object Read & Write' } else { 'Object Read only' }
    Write-Host ''
    Write-Host "KEY: flightlogx-backup-$kind"
    Write-Host 'On R2 Overview, find Account Details, then API Tokens > Manage.'
    Write-Host 'Choose Create Account API token. Enter the key name shown above.'
    Write-Host "Permissions: choose $permission. Do not select an Admin permission."
    Write-Host 'Select Apply to specific buckets only, then flightlogx-preview-backups.'
    Write-Host 'Keep the default TTL and leave IP filtering empty (GitHub runner addresses change).'
    Write-Host 'Create the token. Copy Access Key ID and Secret Access Key into the prompts below.'
    Write-Host 'Use those two S3 values, not the API token value. Paste with Ctrl+V or right-click.'
    $values = @{}
    foreach ($field in @('accessKeyId', 'secretAccessKey')) {
        $label = if ($field -eq 'accessKeyId') { 'Access Key ID' } else { 'Secret Access Key' }
        $secure = Read-Host $label -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $values[$field] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim() }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
    }
    $credentials[$kind] = $values
}
try {
    $start = [Diagnostics.ProcessStartInfo]::new('node')
    $start.ArgumentList.Add((Join-Path $PSScriptRoot 'r2-credentials.mjs'))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Write(($credentials | ConvertTo-Json -Depth 4 -Compress))
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $failure = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'R2 key capture failed. No existing credentials were replaced and no secrets were printed.' }
    Write-Host 'R2 keys saved locally. Reply done in chat so I can test the permissions.' -ForegroundColor Green
} finally { $credentials = $null; $values = $null }
