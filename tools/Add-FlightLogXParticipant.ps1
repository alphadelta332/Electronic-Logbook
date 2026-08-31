# Owner-only enrolment for the controlled FlightLogX Android release.

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)]
    [string]$Email,

    [Parameter(Mandatory)]
    [string]$DisplayName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,62}$')]
    [string]$FirebaseGroupAlias,

    [ValidateSet('workbook_migration', 'app_only')]
    [string]$OnboardingMode = 'workbook_migration',

    [string]$RepoRoot,

    [string]$LocalAppDataRoot = $env:LOCALAPPDATA
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-NormalizedParticipantEmail {
    param([Parameter(Mandatory)][string]$Value)

    $trimmed = $Value.Trim()
    try {
        $parsed = [Net.Mail.MailAddress]::new($trimmed)
    }
    catch {
        throw 'Enter one complete Google-account email address.'
    }

    if (-not $parsed.Address.Equals($trimmed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Enter only the Google-account email address, without a display name.'
    }

    return $parsed.Address.ToLowerInvariant()
}

function Invoke-SecretSafeRestMethod {
    param(
        [Parameter(Mandatory)][string]$Operation,
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][hashtable]$Headers,
        [Parameter(Mandatory)][ValidateSet('Get', 'Post')][string]$Method,
        [object]$Body
    )

    try {
        $arguments = @{
            Uri = $Uri
            Headers = $Headers
            Method = $Method
        }
        if ($PSBoundParameters.ContainsKey('Body')) {
            $arguments.ContentType = 'application/json'
            $arguments.Body = $Body | ConvertTo-Json -Depth 8 -Compress
        }
        return Invoke-RestMethod @arguments
    }
    catch {
        throw "$Operation failed. The service response was suppressed because it may contain private account or credential details."
    }
}

function ConvertFrom-NativeJsonOutput {
    param(
        [Parameter(Mandatory)][object[]]$Output,
        [Parameter(Mandatory)][string]$Operation
    )

    $lines = @($Output | ForEach-Object { [string]$_ })
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $candidate = ($lines[$index..($lines.Count - 1)] -join [Environment]::NewLine).Trim()
        if (-not ($candidate.StartsWith('{') -or $candidate.StartsWith('['))) {
            continue
        }
        try {
            return $candidate | ConvertFrom-Json
        }
        catch {
            continue
        }
    }

    throw "$Operation did not return readable JSON."
}

function Invoke-NativeJsonCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Operation
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $Command @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) {
        throw "$Operation failed. Command output was suppressed because it may contain a participant email."
    }
    return ConvertFrom-NativeJsonOutput -Output $output -Operation $Operation
}

function Get-FirebaseGroup {
    param(
        [Parameter(Mandatory)][object]$GroupsResponse,
        [Parameter(Mandatory)][string]$Alias
    )

    $groups = if ($null -ne $GroupsResponse.result) { @($GroupsResponse.result.groups) } else { @() }
    return @($groups | Where-Object {
        ([string]$_.name).EndsWith("/groups/$Alias", [StringComparison]::Ordinal)
    })
}

function Assert-CompatibleHostedInvitation {
    param(
        [Parameter(Mandatory)][object]$Account,
        [Parameter(Mandatory)][string]$ExpectedEmail,
        [Parameter(Mandatory)][string]$ExpectedMode
    )

    if (-not ([string]$Account.invited_email).Equals($ExpectedEmail, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The existing hosted account is bound to a different invited email. No changes were made.'
    }
    if ([string]$Account.onboarding_mode -ne $ExpectedMode) {
        throw 'The existing hosted account uses a different onboarding mode. No changes were made.'
    }
    if ([string]$Account.status -notin @('invited', 'active')) {
        throw 'The existing hosted account is not eligible for enrolment. No changes were made.'
    }
}

function Write-ParticipantHandoff {
    param(
        [Parameter(Mandatory)][string]$SourceGuidePath,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][string]$ParticipantEmail,
        [Parameter(Mandatory)][string]$GroupAlias
    )

    if (-not (Test-Path -LiteralPath $SourceGuidePath -PathType Leaf)) {
        throw 'The tester-facing Android installation guide is missing from the repository.'
    }

    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $safeStem = $ParticipantEmail -replace '[^a-z0-9@._-]', '_'
    $outputPath = Join-Path $OutputDirectory ("{0}-{1}-android-install.md" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $safeStem)
    $header = @"
> Private participant handoff
>
> Invited Google Account: $ParticipantEmail
> Firebase tester group: $GroupAlias
> Prepared: $([DateTimeOffset]::Now.ToString('O'))
>
> Send this file only to the named participant through the owner's trusted contact path.

"@
    $guide = Get-Content -LiteralPath $SourceGuidePath -Raw -Encoding UTF8
    [IO.File]::WriteAllText($outputPath, $header + $guide, [Text.UTF8Encoding]::new($false))
    return $outputPath
}

function Invoke-FlightLogXParticipantEnrollment {
    $normalizedEmail = ConvertTo-NormalizedParticipantEmail -Value $Email
    $normalizedDisplayName = $DisplayName.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedDisplayName)) {
        throw 'DisplayName cannot be blank.'
    }

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $script:RepoRoot = Split-Path $PSScriptRoot -Parent
    }
    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
    $transferManifestPath = Join-Path $resolvedRepoRoot 'tools\local-development-transfer.psd1'
    $sourceGuidePath = Join-Path $resolvedRepoRoot 'docs\private-pilot-android-install.md'
    $supabaseRoot = Join-Path $LocalAppDataRoot 'ElectronicLogbook\Supabase'
    $metadataPath = Join-Path $supabaseRoot 'hosted-pilot-projects.local.json'
    $managementTokenPath = Join-Path $supabaseRoot 'access-token.txt'

    foreach ($path in @($transferManifestPath, $sourceGuidePath, $metadataPath, $managementTokenPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required owner-enrolment input is missing: $path"
        }
    }

    $transferConfig = Import-PowerShellDataFile -LiteralPath $transferManifestPath
    $firebaseProjectId = [string]$transferConfig.Expected.FirebaseProjectId
    $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $projectRef = [string]$metadata.privatePilot.project_ref
    $projectRegion = [string]$metadata.privatePilot.region
    $managementToken = (Get-Content -LiteralPath $managementTokenPath -Raw -Encoding UTF8).Trim()
    if ([string]::IsNullOrWhiteSpace($firebaseProjectId) -or
        [string]::IsNullOrWhiteSpace($projectRef) -or
        [string]::IsNullOrWhiteSpace($managementToken)) {
        throw 'The local Firebase or Supabase owner configuration is incomplete.'
    }

    $firebase = Get-Command 'firebase.cmd' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $firebase) {
        $firebase = Get-Command 'firebase' -ErrorAction Stop | Select-Object -First 1
    }
    $supabase = Get-Command 'supabase' -ErrorAction Stop | Select-Object -First 1
    $managementHeaders = @{ Authorization = "Bearer $managementToken" }

    Write-Host 'Checking the exact private hosted and Android distribution projects...' -ForegroundColor Cyan
    $projectsResponse = Invoke-SecretSafeRestMethod -Operation 'Supabase project verification' `
        -Uri 'https://api.supabase.com/v1/projects' -Headers $managementHeaders -Method Get
    $projects = [Collections.Generic.List[object]]::new()
    foreach ($projectResult in $projectsResponse) {
        if ($projectResult -is [Array]) {
            foreach ($nestedProject in $projectResult) { [void]$projects.Add($nestedProject) }
        }
        else {
            [void]$projects.Add($projectResult)
        }
    }
    $matchedProjects = @($projects | Where-Object { $_.id -eq $projectRef -or $_.ref -eq $projectRef })
    if ($matchedProjects.Count -ne 1) {
        throw "The configured private hosted project lookup returned $($matchedProjects.Count) matches instead of one."
    }
    $actualRegion = [string]$matchedProjects[0].region
    $actualStatus = [string]$matchedProjects[0].status
    if ($actualRegion -ne $projectRegion -or $actualStatus -ne 'ACTIVE_HEALTHY') {
        throw "The configured private hosted project is not ready: expected region '$projectRegion' and status 'ACTIVE_HEALTHY', received '$actualRegion' and '$actualStatus'."
    }

    $authConfig = Invoke-SecretSafeRestMethod -Operation 'Supabase Auth configuration verification' `
        -Uri "https://api.supabase.com/v1/projects/$projectRef/config/auth" `
        -Headers $managementHeaders -Method Get
    if ($authConfig.disable_signup -ne $true -or
        $authConfig.external_email_enabled -ne $true -or
        $authConfig.external_google_enabled -ne $true) {
        throw 'Public signup must be disabled and both owner-managed email and Google sign-in must be enabled before enrolment.'
    }

    $groupsResponse = Invoke-NativeJsonCommand -Command $firebase.Source `
        -Arguments @('appdistribution:groups:list', '--project', $firebaseProjectId, '--json') `
        -Operation 'Firebase tester-group verification'
    $matchedGroups = @(Get-FirebaseGroup -GroupsResponse $groupsResponse -Alias $FirebaseGroupAlias)
    if ($matchedGroups.Count -ne 1) {
        throw 'The requested Firebase tester group does not exist. Create and verify the intended group before enrolment; the command will not create a group from a possible typo.'
    }
    if ([int]$matchedGroups[0].releaseCount -lt 1) {
        throw 'The requested Firebase tester group has no distributed FlightLogX release. Distribute the approved build to that group before enrolment.'
    }

    $previousSupabaseAccessToken = $env:SUPABASE_ACCESS_TOKEN
    try {
        $env:SUPABASE_ACCESS_TOKEN = $managementToken
        $keysResponse = Invoke-NativeJsonCommand -Command $supabase.Source `
            -Arguments @('projects', 'api-keys', '--project-ref', $projectRef, '--output', 'json') `
            -Operation 'Supabase project-key retrieval'
    }
    finally {
        if ($null -eq $previousSupabaseAccessToken) {
            [Environment]::SetEnvironmentVariable('SUPABASE_ACCESS_TOKEN', $null, 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable('SUPABASE_ACCESS_TOKEN', $previousSupabaseAccessToken, 'Process')
        }
    }

    $keys = [Collections.Generic.List[object]]::new()
    $hasResultProperty = $keysResponse -isnot [Array] -and $null -ne $keysResponse.PSObject.Properties['result']
    $rawKeys = if ($hasResultProperty) { $keysResponse.result } else { $keysResponse }
    foreach ($keyResult in $rawKeys) {
        if ($keyResult -is [Array]) {
            foreach ($nestedKey in $keyResult) { [void]$keys.Add($nestedKey) }
        }
        else {
            [void]$keys.Add($keyResult)
        }
    }
    $serviceRoleKey = [string](($keys | Where-Object { $_.name -eq 'service_role' } | Select-Object -First 1).api_key)
    if ([string]::IsNullOrWhiteSpace($serviceRoleKey)) {
        throw 'The configured hosted project did not provide its owner-only service credential.'
    }

    $supabaseUrl = "https://$projectRef.supabase.co"
    $serviceHeaders = @{ apikey = $serviceRoleKey; Authorization = "Bearer $serviceRoleKey" }
    $usersResponse = Invoke-SecretSafeRestMethod -Operation 'Existing hosted identity lookup' `
        -Uri "$supabaseUrl/auth/v1/admin/users?page=1&per_page=1000" `
        -Headers $serviceHeaders -Method Get
    $matchingUsers = @($usersResponse.users | Where-Object {
        ([string]$_.email).Equals($normalizedEmail, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matchingUsers.Count -gt 1) {
        throw 'More than one hosted Auth identity matched the invited email. No changes were made.'
    }

    $accountId = if ($matchingUsers.Count -eq 1) { [string]$matchingUsers[0].id } else { $null }
    $encodedAccountId = if ($accountId) { [Uri]::EscapeDataString($accountId) } else { $null }
    $existingAccounts = @()
    if ($accountId) {
        $existingAccounts = @(Invoke-SecretSafeRestMethod -Operation 'Existing hosted invitation lookup' `
            -Uri "$supabaseUrl/rest/v1/accounts?select=account_id,invited_email,status,onboarding_mode&account_id=eq.$encodedAccountId" `
            -Headers $serviceHeaders -Method Get)
    }
    if ($existingAccounts.Count -gt 1) {
        throw 'The hosted invitation lookup returned duplicate account rows. No changes were made.'
    }
    if ($existingAccounts.Count -eq 1) {
        Assert-CompatibleHostedInvitation -Account $existingAccounts[0] `
            -ExpectedEmail $normalizedEmail -ExpectedMode $OnboardingMode
    }

    $target = "$normalizedEmail in hosted mode $OnboardingMode and Firebase group $FirebaseGroupAlias"
    if (-not $PSCmdlet.ShouldProcess($target, 'Provision FlightLogX participant')) {
        Write-Host 'WhatIf completed: all local and remote preconditions passed; no account, tester, or handoff file was changed.' -ForegroundColor Yellow
        return
    }

    if (-not $accountId) {
        $createdUser = Invoke-SecretSafeRestMethod -Operation 'Hosted Auth identity creation' `
            -Uri "$supabaseUrl/auth/v1/admin/users" -Headers $serviceHeaders -Method Post `
            -Body @{ email = $normalizedEmail; email_confirm = $true; user_metadata = @{ display_name = $normalizedDisplayName } }
        $accountId = [string]$createdUser.id
        if ([string]::IsNullOrWhiteSpace($accountId)) {
            throw 'Hosted Auth identity creation returned no account identifier.'
        }
    }

    if ($existingAccounts.Count -eq 0) {
        [void](Invoke-SecretSafeRestMethod -Operation 'Hosted invitation creation' `
            -Uri "$supabaseUrl/rest/v1/accounts" -Headers ($serviceHeaders + @{ Prefer = 'return=minimal' }) `
            -Method Post -Body @{
                account_id = $accountId
                invited_email = $normalizedEmail
                display_name = $normalizedDisplayName
                status = 'invited'
                onboarding_mode = $OnboardingMode
            })
    }

    [void](Invoke-NativeJsonCommand -Command $firebase.Source `
        -Arguments @('appdistribution:testers:add', $normalizedEmail, '--group-alias', $FirebaseGroupAlias, '--project', $firebaseProjectId, '--json') `
        -Operation 'Firebase tester enrolment')

    $testersResponse = Invoke-NativeJsonCommand -Command $firebase.Source `
        -Arguments @('appdistribution:testers:list', '--project', $firebaseProjectId, '--json') `
        -Operation 'Firebase tester membership verification'
    $expectedGroupSuffix = "/groups/$FirebaseGroupAlias"
    $verifiedTesters = @($testersResponse.result.testers | Where-Object {
        ([string]$_.name).EndsWith("/testers/$normalizedEmail", [StringComparison]::OrdinalIgnoreCase) -and
        @($_.groups | Where-Object { ([string]$_).EndsWith($expectedGroupSuffix, [StringComparison]::Ordinal) }).Count -eq 1
    })
    if ($verifiedTesters.Count -ne 1) {
        throw 'Firebase accepted the enrolment command but the tester/group membership could not be verified. The hosted invitation remains safe to retry.'
    }

    $handoffDirectory = Join-Path $LocalAppDataRoot 'ElectronicLogbook\ParticipantHandoffs'
    $handoffPath = Write-ParticipantHandoff -SourceGuidePath $sourceGuidePath `
        -OutputDirectory $handoffDirectory -ParticipantEmail $normalizedEmail `
        -GroupAlias $FirebaseGroupAlias

    Write-Host 'PASS: the same Google-account email is provisioned for hosted migration and Firebase installation.' -ForegroundColor Green
    Write-Host "Send this private guide to the participant: $handoffPath" -ForegroundColor Green
    Write-Host 'Next participant action: open the Firebase email on Android and select Get started.' -ForegroundColor Cyan
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FlightLogXParticipantEnrollment
}
