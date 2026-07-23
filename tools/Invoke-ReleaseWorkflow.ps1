# Dispatches and optionally watches the protected release workflow.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$CommitSha,

    [string]$Repository = "alphadelta332/Electronic-Logbook",

    [string]$Workflow = "publish-release.yml",

    [string]$Ref = "main",

    [switch]$ApproveEnvironmentGates,

    [switch]$NoWatch,

    [switch]$SkipDispatch
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$expectedTag = "v$Version"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Invoke-GhJson {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & gh @Arguments 2>$null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        if ($AllowFailure) {
            $global:LASTEXITCODE = 0
            return $null
        }

        throw "gh $($Arguments -join ' ') failed with exit code $exitCode."
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        return $null
    }

    return $output | ConvertFrom-Json
}

function Assert-GitHubCli {
    gh auth status | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated."
    }
}

function Resolve-ReleaseCommit {
    if (-not [string]::IsNullOrWhiteSpace($CommitSha)) {
        if ($CommitSha -notmatch '^[0-9a-fA-F]{40}$') {
            throw "CommitSha must be a full 40-character SHA."
        }

        return $CommitSha.ToLowerInvariant()
    }

    git -C $repoRoot fetch origin main --tags --force
    if ($LASTEXITCODE -ne 0) {
        throw "Could not fetch origin/main."
    }

    $resolved = (git -C $repoRoot rev-parse origin/main).Trim()
    if ($LASTEXITCODE -ne 0 -or $resolved -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve origin/main."
    }

    return $resolved
}

function Assert-CommitOnMain {
    param([Parameter(Mandatory)][string]$ResolvedCommit)

    git -C $repoRoot fetch origin main --tags --force
    if ($LASTEXITCODE -ne 0) {
        throw "Could not fetch origin/main."
    }

    $commitObject = (git -C $repoRoot rev-parse "$ResolvedCommit^{commit}").Trim()
    if ($LASTEXITCODE -ne 0 -or $commitObject -ne $ResolvedCommit) {
        throw "Could not resolve selected release commit: $ResolvedCommit"
    }

    git -C $repoRoot merge-base --is-ancestor $ResolvedCommit origin/main
    if ($LASTEXITCODE -ne 0) {
        throw "Selected release commit $ResolvedCommit is not on origin/main."
    }
}

function Assert-ReleaseMetadata {
    param([Parameter(Mandatory)][string]$ResolvedCommit)

    $versionAtCommit = (git -C $repoRoot show "$ResolvedCommit`:version.txt").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read version.txt at $ResolvedCommit."
    }
    if ($versionAtCommit -ne $Version) {
        throw "version.txt at $ResolvedCommit is '$versionAtCommit', expected '$Version'."
    }

    $readmeAtCommit = git -C $repoRoot show "$ResolvedCommit`:README.md"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read README.md at $ResolvedCommit."
    }

    $headingPattern = '(?m)^### \[' + [regex]::Escape($Version) + '\] - \d{4}-\d{2}-\d{2}$'
    $headings = @([regex]::Matches(($readmeAtCommit -join "`n"), $headingPattern))
    if ($headings.Count -ne 1) {
        throw "README.md at $ResolvedCommit must contain exactly one changelog heading for $Version. Found $($headings.Count)."
    }

    Write-Host "Release metadata at $ResolvedCommit matches $Version." -ForegroundColor Green
}

function Assert-NoExistingRelease {
    git -C $repoRoot ls-remote --exit-code --tags origin $expectedTag *> $null
    $tagExitCode = $LASTEXITCODE
    if ($tagExitCode -eq 0) {
        throw "Remote tag $expectedTag already exists."
    }
    if ($tagExitCode -ne 2) {
        throw "Could not inspect remote tag $expectedTag."
    }
    $global:LASTEXITCODE = 0

    $release = Invoke-GhJson -Arguments @(
        "release", "view", $expectedTag,
        "--repo", $Repository,
        "--json", "tagName,url"
    ) -AllowFailure
    if ($null -ne $release) {
        throw "GitHub release $expectedTag already exists: $($release.url)"
    }

    Write-Host "No existing $expectedTag tag or GitHub release found." -ForegroundColor Green
}

function Assert-ReleaseRunnerOnline {
    $runnerResponse = Invoke-GhJson -Arguments @(
        "api",
        "repos/$Repository/actions/runners",
        "--paginate"
    )

    $matching = @($runnerResponse.runners | Where-Object {
        $labels = @($_.labels | ForEach-Object { ([string]$_.name).ToLowerInvariant() })
        $_.status -eq "online" -and
            $labels -contains "self-hosted" -and
            $labels -contains "windows" -and
            $labels -contains "excel"
    })

    if ($matching.Count -eq 0) {
        throw "No online self-hosted runner with labels self-hosted, windows, excel was found for $Repository."
    }

    $matching | ForEach-Object {
        Write-Host "Runner online: $($_.name) (busy=$($_.busy))" -ForegroundColor Green
    }
}

function Start-ReleaseWorkflow {
    param([Parameter(Mandatory)][string]$ResolvedCommit)

    $before = Invoke-GhJson -Arguments @(
        "run", "list",
        "--repo", $Repository,
        "--workflow", $Workflow,
        "--limit", "20",
        "--json", "databaseId"
    )
    $beforeIds = @($before | ForEach-Object { [string]$_.databaseId })

    gh workflow run $Workflow --repo $Repository --ref $Ref -f "commit_sha=$ResolvedCommit"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not dispatch $Workflow."
    }

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        Start-Sleep -Seconds 3
        $runs = Invoke-GhJson -Arguments @(
            "run", "list",
            "--repo", $Repository,
            "--workflow", $Workflow,
            "--limit", "20",
            "--json", "databaseId,event,headSha,status,url"
        )

        $newRun = @($runs | Where-Object {
            $beforeIds -notcontains [string]$_.databaseId -and
                $_.event -eq "workflow_dispatch"
        } | Select-Object -First 1)
        if ($newRun.Count -gt 0) {
            Write-Host "Release workflow dispatched: $($newRun[0].url)" -ForegroundColor Green
            return [string]$newRun[0].databaseId
        }
    }

    throw "Workflow was dispatched, but the new run could not be found."
}

function Approve-PendingDeployments {
    param([Parameter(Mandatory)][string]$RunId)

    $pending = Invoke-GhJson -Arguments @(
        "api",
        "repos/$Repository/actions/runs/$RunId/pending_deployments"
    ) -AllowFailure

    if ($null -eq $pending -or @($pending).Count -eq 0) {
        return
    }

    $approvable = @($pending | Where-Object { $_.current_user_can_approve })
    if ($approvable.Count -eq 0) {
        Write-Host "Run $RunId is waiting for an environment approval by another reviewer." -ForegroundColor Yellow
        return
    }

    $environmentIds = @($approvable | ForEach-Object { [int64]$_.environment.id })
    $body = @{
        environment_ids = $environmentIds
        state = "approved"
        comment = "Approved by Invoke-ReleaseWorkflow.ps1 for $expectedTag"
    } | ConvertTo-Json -Compress

    $body | gh api `
        --method POST `
        "repos/$Repository/actions/runs/$RunId/pending_deployments" `
        --input - | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not approve pending deployment for run $RunId."
    }

    Write-Host "Approved release environment gate for run $RunId." -ForegroundColor Green
}

function Wait-ReleaseWorkflow {
    param([Parameter(Mandatory)][string]$RunId)

    while ($true) {
        if ($ApproveEnvironmentGates) {
            Approve-PendingDeployments -RunId $RunId
        }

        $run = Invoke-GhJson -Arguments @(
            "run", "view", $RunId,
            "--repo", $Repository,
            "--json", "status,conclusion,url"
        )

        if ($run.status -eq "completed") {
            if ($run.conclusion -ne "success") {
                throw "Release workflow $RunId completed with conclusion '$($run.conclusion)': $($run.url)"
            }

            Write-Host "Release workflow completed successfully: $($run.url)" -ForegroundColor Green
            return
        }

        Write-Host "Release workflow $RunId status: $($run.status)"
        Start-Sleep -Seconds 20
    }
}

function Assert-PublishedRelease {
    param([Parameter(Mandatory)][string]$ResolvedCommit)

    $release = Invoke-GhJson -Arguments @(
        "release", "view", $expectedTag,
        "--repo", $Repository,
        "--json", "tagName,url,isDraft,isPrerelease,targetCommitish,assets"
    )

    if ($release.tagName -ne $expectedTag) {
        throw "Published release tag is '$($release.tagName)', expected '$expectedTag'."
    }
    if ($release.isDraft) {
        throw "Published release $expectedTag is still a draft."
    }
    if ($release.targetCommitish -ne $ResolvedCommit) {
        throw "Published release target is '$($release.targetCommitish)', expected '$ResolvedCommit'."
    }

    $requiredAssets = @(
        "Electronic_Logbook_Master.xlsm",
        "README.pdf",
        "ElectronicLogbook.Updater.Wizard.exe",
        "ElectronicLogbook.Updater.Wizard.win-x64.zip",
        "wizard-signature-report.json",
        "SHA256SUMS.txt",
        "release-manifest.json",
        "release-manifest.json.sig",
        "release-validation.json"
    )

    $assetNames = @($release.assets | ForEach-Object { [string]$_.name })
    foreach ($asset in $requiredAssets) {
        if ($assetNames -notcontains $asset) {
            throw "Published release is missing asset: $asset"
        }
    }

    Write-Host "Published release verified: $($release.url)" -ForegroundColor Green
}

Write-Step "GitHub CLI"
Assert-GitHubCli

Write-Step "Resolve release commit"
$resolvedCommit = Resolve-ReleaseCommit
Write-Host "Commit: $resolvedCommit"

Write-Step "Preflight"
Assert-CommitOnMain -ResolvedCommit $resolvedCommit
Assert-ReleaseMetadata -ResolvedCommit $resolvedCommit
Assert-NoExistingRelease
Assert-ReleaseRunnerOnline

if ($SkipDispatch) {
    Write-Host "SkipDispatch set; release workflow was not dispatched." -ForegroundColor Yellow
    return
}

Write-Step "Dispatch"
$runId = Start-ReleaseWorkflow -ResolvedCommit $resolvedCommit

if (-not $NoWatch) {
    Write-Step "Watch"
    Wait-ReleaseWorkflow -RunId $runId

    Write-Step "Verify release"
    Assert-PublishedRelease -ResolvedCommit $resolvedCommit
} else {
    Write-Host "Release workflow run id: $runId" -ForegroundColor Yellow
}
