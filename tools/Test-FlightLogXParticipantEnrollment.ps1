[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $PSScriptRoot 'Add-FlightLogXParticipant.ps1'
$manifestPath = Join-Path $PSScriptRoot 'local-development-transfer.psd1'
$runbookPath = Join-Path $repoRoot 'docs\flightlogx-preview-runbook.md'
$handoverPath = Join-Path $repoRoot 'LOCAL_DEVICE_SETUP_HANDOVER.md'
$passed = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
    $script:passed++
}

. $scriptPath -Email 'test@example.invalid' -DisplayName 'Test Participant' -FirebaseGroupAlias 'test-group'

$normalized = ConvertTo-NormalizedParticipantEmail -Value '  PERSON@Example.com  '
Assert-True ($normalized -eq 'person@example.com') 'email normalization must be stable and case-insensitive'

$invalidEmailRejected = $false
try { [void](ConvertTo-NormalizedParticipantEmail -Value 'Person <person@example.com>') }
catch { $invalidEmailRejected = $true }
Assert-True $invalidEmailRejected 'display-name email syntax must be rejected'

$compatible = [pscustomobject]@{
    invited_email = 'person@example.com'
    onboarding_mode = 'workbook_migration'
    status = 'invited'
}
Assert-CompatibleHostedInvitation -Account $compatible -ExpectedEmail 'PERSON@example.com' -ExpectedMode 'workbook_migration'
$passed++

$conflictRejected = $false
try {
    Assert-CompatibleHostedInvitation -Account $compatible -ExpectedEmail 'other@example.com' -ExpectedMode 'workbook_migration'
}
catch { $conflictRejected = $true }
Assert-True $conflictRejected 'a hosted invitation bound to another email must be rejected'

$groupsResponse = [pscustomobject]@{
    result = [pscustomobject]@{
        groups = @(
            [pscustomobject]@{ name = 'projects/123/groups/release-canaries'; releaseCount = 1 },
            [pscustomobject]@{ name = 'projects/123/groups/release-owner'; releaseCount = 2 }
        )
    }
}
$group = @(Get-FirebaseGroup -GroupsResponse $groupsResponse -Alias 'release-owner')
Assert-True ($group.Count -eq 1 -and $group[0].releaseCount -eq 2) 'Firebase group lookup must match the exact alias'

$noisyArrayJson = ConvertFrom-NativeJsonOutput -Operation 'synthetic native command' -Output @(
    '[debug] command metadata',
    '[',
    '  { "name": "service_role", "api_key": "not-a-real-key" }',
    ']'
)
Assert-True (@($noisyArrayJson).Count -eq 1 -and $noisyArrayJson[0].name -eq 'service_role') 'native JSON parser must handle diagnostic noise followed by a top-level array'

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('FlightLogXEnrollmentTests-' + [Guid]::NewGuid().ToString('N'))
try {
    $sourceGuide = Join-Path $temporaryRoot 'guide.md'
    $outputDirectory = Join-Path $temporaryRoot 'handoffs'
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    [IO.File]::WriteAllText($sourceGuide, "# Safe install guide`n", [Text.UTF8Encoding]::new($false))
    $handoffPath = Write-ParticipantHandoff -SourceGuidePath $sourceGuide `
        -OutputDirectory $outputDirectory -ParticipantEmail 'person@example.com' -GroupAlias 'release-owner'
    $handoff = Get-Content -LiteralPath $handoffPath -Raw -Encoding UTF8
    Assert-True ($handoff -match 'person@example\.com' -and $handoff -match '# Safe install guide') 'handoff must identify the invited email and include the approved guide'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

$source = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
Assert-True ($source -match 'SupportsShouldProcess\s*=\s*\$true') 'owner command must support a non-mutating WhatIf preflight'
Assert-True ($source -match 'hosted-preview-projects\.local\.json' -and $source -match 'hosted-pilot-projects\.local\.json') 'owner command must prefer canonical Preview metadata while accepting the legacy filename'
Assert-True ($source -match '\$metadata\.preview' -and $source -match '\$metadata\.privatePilot') 'owner command must prefer the canonical Preview project key while accepting the legacy key'
Assert-True ($source -match 'auth/v1/admin/users') 'owner command must provision or reuse the Supabase Auth identity'
Assert-True ($source -match 'rest/v1/accounts') 'owner command must provision or verify the hosted invitation row'
Assert-True ($source -match 'appdistribution:testers:add') 'owner command must add the tester through Firebase App Distribution'
Assert-True ($source -match 'appdistribution:testers:list') 'owner command must verify Firebase group membership after mutation'
Assert-True ($source -match 'releaseCount') 'owner command must reject a Firebase group without a distributed release'
Assert-True ($source -notmatch 'db_password|psql') 'owner command must not require direct database tooling or its password'

$manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
Assert-True ($manifest.Expected.OwnerEnrollmentScript -eq 'tools/Add-FlightLogXParticipant.ps1') 'transfer manifest must name the owner enrolment entrypoint'
Assert-True ($manifest.Expected.ParticipantHandoffDirectory -eq 'ElectronicLogbook\ParticipantHandoffs') 'transfer manifest must classify the generated private handoff location'

$runbook = Get-Content -LiteralPath $runbookPath -Raw -Encoding UTF8
$handover = Get-Content -LiteralPath $handoverPath -Raw -Encoding UTF8
Assert-True ($runbook -match 'Add-FlightLogXParticipant\.ps1') 'private runbook must give the exact owner command'
Assert-True ($handover -match 'Add-FlightLogXParticipant\.ps1') 'device handover must document owner enrolment prerequisites'

Write-Host "FlightLogX participant enrolment tests passed: $passed" -ForegroundColor Green
