param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $DeviceLabel,

    [string] $SeedWorkbookPath = "artifacts\portable-roundtrip-20260722-090535\RoundTrip-Seeded-Disposable.xlsm",

    [string] $RecoveryCodeFile = "artifacts\portable-roundtrip-20260722-090535\Gate1-Recovery-Code.txt",

    [string] $OutputRoot = "artifacts\mobile-real-device-acceptance-20260722"
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$repoRoot = Split-Path -Parent $mobileRoot
$updaterProject = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater\ElectronicLogbook.Updater.csproj"

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found at $Path"
    }
}

function Invoke-PortableCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath
    )

    $stdoutPath = "$OutputPath.stdout.tmp"
    $stderrPath = "$OutputPath.stderr.tmp"
    $processArguments = @("run", "--project", $updaterProject, "--") + $Arguments
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $processArguments `
        -NoNewWindow `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    $output = @()
    if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
        $output += Get-Content -LiteralPath $stdoutPath
        Remove-Item -LiteralPath $stdoutPath -Force
    }

    if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
        $output += Get-Content -LiteralPath $stderrPath
        Remove-Item -LiteralPath $stderrPath -Force
    }

    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($OutputPath, ($output -join [Environment]::NewLine), $utf8WithoutBom)
    if ($process.ExitCode -ne 0) {
        throw "Portable command failed with exit code $($process.ExitCode). Output written to $OutputPath"
    }
}

$resolvedPackage = Resolve-RepoPath $PackagePath
$resolvedSeedWorkbook = Resolve-RepoPath $SeedWorkbookPath
$resolvedRecoveryCodeFile = Resolve-RepoPath $RecoveryCodeFile
$resolvedOutputRoot = Resolve-RepoPath $OutputRoot

Assert-FileExists -Path $resolvedPackage -Description "Mobile acceptance package"
Assert-FileExists -Path $resolvedSeedWorkbook -Description "Seed disposable workbook"
Assert-FileExists -Path $resolvedRecoveryCodeFile -Description "Recovery code file"
Assert-FileExists -Path $updaterProject -Description "Updater project"

$safeDeviceLabel = -join ($DeviceLabel.ToCharArray() | ForEach-Object {
    if ([char]::IsLetterOrDigit($_) -or $_ -eq "-" -or $_ -eq "_") {
        $_
    }
    else {
        "-"
    }
})
if ([string]::IsNullOrWhiteSpace($safeDeviceLabel)) {
    throw "DeviceLabel must contain at least one file-name-safe character."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDirectory = Join-Path $resolvedOutputRoot "$safeDeviceLabel-package-validation-$timestamp"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$workbookCopy = Join-Path $outputDirectory "$safeDeviceLabel-Disposable-Apply.xlsm"
Copy-Item -LiteralPath $resolvedSeedWorkbook -Destination $workbookCopy -Force

$previewOutput = Join-Path $outputDirectory "preview.json"
$applyOutput = Join-Path $outputDirectory "apply.json"
$statusOutput = Join-Path $outputDirectory "status-after.json"

Invoke-PortableCommand -Arguments @(
    "portable",
    "import-preview",
    "--workbook",
    $workbookCopy,
    "--recovery-code-file",
    $resolvedRecoveryCodeFile,
    "--package",
    $resolvedPackage,
    "--json"
) -OutputPath $previewOutput

Invoke-PortableCommand -Arguments @(
    "portable",
    "import-apply",
    "--workbook",
    $workbookCopy,
    "--recovery-code-file",
    $resolvedRecoveryCodeFile,
    "--package",
    $resolvedPackage,
    "--json"
) -OutputPath $applyOutput

Invoke-PortableCommand -Arguments @(
    "portable",
    "status",
    "--workbook",
    $workbookCopy,
    "--json"
) -OutputPath $statusOutput

Write-Host "Mobile acceptance package validated."
Write-Host "Device: $DeviceLabel"
Write-Host "Package: $resolvedPackage"
Write-Host "Disposable workbook: $workbookCopy"
Write-Host "Preview: $previewOutput"
Write-Host "Apply: $applyOutput"
Write-Host "Status: $statusOutput"
