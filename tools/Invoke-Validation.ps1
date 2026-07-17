# Canonical validation entry point for local development and CI.

[CmdletBinding()]
param(
    [ValidateSet("Fast", "Excel", "Release")]
    [string]$Tier = "Fast",
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ArtifactsPath,
    [switch]$SkipDependencyAudit,
    [switch]$SkipPublicReadinessCheck,
    [switch]$SkipReleaseArtifactVerification
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = $repoRoot
}

if ($Tier -eq "Release" -and $SkipPublicReadinessCheck) {
    throw "Release validation must not skip public-readiness checks."
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Test-ReleaseArtifacts {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path $Path).Path
    $manifestPath = Join-Path $resolvedPath "release-manifest.json"
    $manifestSignaturePath = Join-Path $resolvedPath "release-manifest.json.sig"
    $sumsPath = Join-Path $resolvedPath "SHA256SUMS.txt"
    $requiredAssets = @(
        "Electronic_Logbook_Master.xlsm",
        "README.pdf",
        "release-manifest.json",
        "release-manifest.json.sig",
        "SHA256SUMS.txt",
        "wizard-signature-report.json"
    )

    foreach ($asset in $requiredAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedPath $asset))) {
            throw "Release artifact missing: $asset in $resolvedPath"
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    & (Join-Path $repoRoot "tools\Test-ReleaseManifestSignature.ps1") `
        -ManifestPath $manifestPath `
        -SignaturePath $manifestSignaturePath `
        -PublicKeyPemPath (Join-Path $repoRoot "updater\release-manifest-signing-public-key.pem")
    $version = (Get-Content -LiteralPath (Join-Path $repoRoot "version.txt") -Raw -Encoding UTF8).Trim()
    $head = (git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve current commit for release artifact validation."
    }

    if ($manifest.version -ne $version) {
        throw "release-manifest.json version '$($manifest.version)' does not match version.txt '$version'."
    }
    if ($manifest.tag -ne "v$version") {
        throw "release-manifest.json tag '$($manifest.tag)' does not match v$version."
    }
    if ($manifest.commit -ne $head) {
        throw "release-manifest.json commit '$($manifest.commit)' does not match HEAD '$head'."
    }

    $sumLines = Get-Content -LiteralPath $sumsPath -Encoding ASCII
    $sumByName = @{}
    foreach ($line in $sumLines) {
        if ($line -match "^(?<hash>[A-Fa-f0-9]{64})\s+\*?(?<name>.+)$") {
            $sumByName[$Matches["name"].Trim()] = $Matches["hash"].ToUpperInvariant()
        }
    }

    foreach ($asset in $manifest.assets) {
        $assetPath = Join-Path $resolvedPath ([string]$asset.name)
        if (-not (Test-Path -LiteralPath $assetPath)) {
            throw "Manifest asset missing from artifacts path: $($asset.name)"
        }

        $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToUpperInvariant()
        $manifestHash = ([string]$asset.sha256).ToUpperInvariant()
        if ($actualHash -ne $manifestHash) {
            throw "Manifest hash mismatch for $($asset.name)."
        }
        if (-not $sumByName.ContainsKey([string]$asset.name)) {
            throw "SHA256SUMS.txt does not include $($asset.name)."
        }
        if ($sumByName[[string]$asset.name] -ne $actualHash) {
            throw "SHA256SUMS.txt hash mismatch for $($asset.name)."
        }
    }

    foreach ($asset in @("Electronic_Logbook_Master.xlsm", "README.pdf")) {
        if (-not ($manifest.assets | Where-Object { $_.name -eq $asset })) {
            throw "release-manifest.json does not describe required asset: $asset"
        }
    }

    foreach ($asset in @("release-manifest.json", "release-manifest.json.sig")) {
        $assetPath = Join-Path $resolvedPath $asset
        $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if (-not $sumByName.ContainsKey($asset)) {
            throw "SHA256SUMS.txt does not include $asset."
        }
        if ($sumByName[$asset] -ne $actualHash) {
            throw "SHA256SUMS.txt hash mismatch for $asset."
        }
    }

    $signatureReportPath = Join-Path $resolvedPath "wizard-signature-report.json"
    $signatureReport = Get-Content -LiteralPath $signatureReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $wizardAsset = $manifest.assets | Where-Object { $_.name -eq "ElectronicLogbook.Updater.Wizard.exe" } | Select-Object -First 1
    if ($null -eq $wizardAsset) {
        throw "release-manifest.json does not describe required asset: ElectronicLogbook.Updater.Wizard.exe"
    }
    if (([string]$signatureReport.fileName) -ne "ElectronicLogbook.Updater.Wizard.exe") {
        throw "wizard-signature-report.json does not describe the wizard executable."
    }
    if (([string]$signatureReport.sha256).ToUpperInvariant() -ne ([string]$wizardAsset.sha256).ToUpperInvariant()) {
        throw "wizard-signature-report.json SHA-256 does not match release-manifest.json."
    }

    Write-Host "Release artifacts verified in $resolvedPath." -ForegroundColor Green
}

function Get-VulnerabilityCount {
    param($Value)

    $count = 0
    if ($null -eq $Value) {
        return 0
    }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            $count += Get-VulnerabilityCount -Value $item
        }

        return $count
    }

    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -eq "vulnerabilities" -and $property.Value -is [System.Array]) {
                $count += @($property.Value).Count
            } else {
                $count += Get-VulnerabilityCount -Value $property.Value
            }
        }
    }

    return $count
}

function Invoke-DependencyAudit {
    $auditOutput = & dotnet list (Join-Path $repoRoot "ElectronicLogbook.Updater.sln") package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency vulnerability audit failed."
    }

    $audit = $auditOutput | ConvertFrom-Json
    $vulnerabilityCount = Get-VulnerabilityCount -Value $audit
    if ($vulnerabilityCount -gt 0) {
        $auditOutput | Write-Host
        throw "Dependency vulnerability audit found $vulnerabilityCount vulnerable package entr$(if ($vulnerabilityCount -eq 1) { 'y' } else { 'ies' })."
    }

    Write-Host "Dependency vulnerability audit passed." -ForegroundColor Green
}

Write-Step "Fast validation"
& (Join-Path $repoRoot "tools\Test-ReleaseMetadata.ps1") -RepoRoot $repoRoot
& (Join-Path $repoRoot "tools\Test-VbaSourceQuality.ps1") -RepoRoot $repoRoot

Invoke-DotNet -Arguments @("test", (Join-Path $repoRoot "ElectronicLogbook.Updater.sln"), "--configuration", "Release") `
    -FailureMessage "External updater tests failed."

if (-not $SkipDependencyAudit) {
    Invoke-DependencyAudit
}

if ($Tier -in @("Excel", "Release")) {
    Write-Step "Excel validation"
    & (Join-Path $repoRoot "tools\Test-WorkbookVbaParity.ps1") -RepoRoot $repoRoot
    & (Join-Path $repoRoot "tools\Test-VbaCompileDisposable.ps1")
    if (-not $SkipPublicReadinessCheck) {
        & (Join-Path $repoRoot "tools\Test-WorkbookPublicReadiness.ps1") -RepoRoot $repoRoot
    } else {
        Write-Host "Skipping release-only public-readiness checks." -ForegroundColor Yellow
    }
    & (Join-Path $repoRoot "updater\Test-ExternalUpdater.ps1") `
        -RepoRoot $repoRoot `
        -ReportPath (Join-Path $repoRoot "updater\TestResults\com-migration-report.json")
    & (Join-Path $repoRoot "updater\Test-ExternalUpdater.ps1") `
        -RepoRoot $repoRoot `
        -ReportPath (Join-Path $repoRoot "updater\TestResults\com-inplace-migration-report.json") `
        -InPlace
}

if ($Tier -eq "Release") {
    Write-Step "Release validation"
    & (Join-Path $repoRoot "updater\Test-CompatibilityMatrix.ps1") -RepoRoot $repoRoot

    if (-not $SkipReleaseArtifactVerification) {
        Test-ReleaseArtifacts -Path $ArtifactsPath
    }
}

Write-Host ""
Write-Host "$Tier validation passed." -ForegroundColor Green
