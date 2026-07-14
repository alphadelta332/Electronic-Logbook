# Runs Excel COM migration tests from every supported release tag to the current master.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$PolicyPath,
    [string]$MasterPath,
    [switch]$SkipBuild,
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
if ([string]::IsNullOrWhiteSpace($PolicyPath)) {
    $PolicyPath = Join-Path $repoRoot "updater\compatibility-policy.json"
}
if ([string]::IsNullOrWhiteSpace($MasterPath)) {
    $MasterPath = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
}

$projectPath = Join-Path $repoRoot "updater\src\ElectronicLogbook.Updater"
$updaterDllPath = Join-Path $projectPath "bin\Release\net8.0-windows\ElectronicLogbook.Updater.dll"
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "ElectronicLogbookCompatibility-{0}" -f [guid]::NewGuid().ToString("N")
)
$maxAttempts = 3

Import-Module (Join-Path $repoRoot "tools\ReleaseTools.psm1") -Force

function Write-Step {
    param([string]$Message)
    Write-Host "[Test-CompatibilityMatrix] $Message" -ForegroundColor Cyan
}

function ConvertTo-SemVer {
    param([Parameter(Mandatory)][string]$Value)

    $trimmed = $Value.Trim()
    $match = [regex]::Match($trimmed, '^(?:v)?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$')
    if (-not $match.Success) {
        throw "Version must use semantic version format X.Y.Z or vX.Y.Z: $Value"
    }

    [version]::new(
        [int]$match.Groups["major"].Value,
        [int]$match.Groups["minor"].Value,
        [int]$match.Groups["patch"].Value)
}

function Export-GitFile {
    param(
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$PathInRepo,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = "git"
    $escapedRepoRoot = $repoRoot.Replace('"', '\"')
    $escapedGitPath = "${Tag}:$PathInRepo".Replace('"', '\"')
    $psi.Arguments = "-C ""$escapedRepoRoot"" show ""$escapedGitPath"""
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($psi)
    try {
        $destination = [System.IO.File]::Open(
            $DestinationPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $process.StandardOutput.BaseStream.CopyTo($destination)
        } finally {
            $destination.Dispose()
        }

        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "git show failed for ${Tag}:$PathInRepo. $stderr"
        }
    } finally {
        $process.Dispose()
    }
}

function Get-ListObject {
    param(
        [Parameter(Mandatory)]$Workbook,
        [Parameter(Mandatory)][string]$Name
    )

    foreach ($worksheet in $Workbook.Worksheets) {
        foreach ($table in $worksheet.ListObjects) {
            if ([string]::Equals(
                    [string]$table.Name,
                    $Name,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                return $table
            }
        }
    }

    return $null
}

function Get-ListColumnOrNull {
    param(
        [Parameter(Mandatory)]$Table,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        try {
            return $Table.ListColumns.Item($name)
        } catch {}
    }

    return $null
}

function Get-LogbookCustomColumn {
    param(
        [Parameter(Mandatory)]
        $Table
    )

    $hoursColumn = Get-ListColumnOrNull -Table $Table -Names @("SeIcusDay")
    if ($null -eq $hoursColumn) {
        return $null
    }

    foreach ($anchorName in @("OPC", "Details", "Remarks")) {
        $anchor = Get-ListColumnOrNull -Table $Table -Names @($anchorName)
        if ($null -ne $anchor -and $anchor.Index + 1 -lt $hoursColumn.Index) {
            return $Table.ListColumns.Item($anchor.Index + 1)
        }
    }

    return $null
}

function Get-DataBodyRowCount {
    param([Parameter(Mandatory)]$Table)

    if ($null -eq $Table.DataBodyRange) {
        return 0
    }

    return [int]$Table.DataBodyRange.Rows.Count
}

function Test-ListColumnExists {
    param(
        [Parameter(Mandatory)]$Table,
        [Parameter(Mandatory)][string]$Name
    )

    try {
        $null = $Table.ListColumns.Item($Name)
        return $true
    } catch {
        return $false
    }
}

function Ensure-DataRow {
    param([Parameter(Mandatory)]$Table)

    if ($null -eq $Table.DataBodyRange -or [int]$Table.ListRows.Count -eq 0) {
        $null = $Table.ListRows.Add()
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    if (-not (Test-Path -LiteralPath $PolicyPath)) {
        throw "Compatibility policy not found: $PolicyPath"
    }
    if (-not (Test-Path -LiteralPath $MasterPath)) {
        throw "Master workbook not found: $MasterPath"
    }
    if ($null -eq [type]::GetTypeFromProgID("Excel.Application")) {
        throw "Microsoft Excel is not installed. Run with -SkipCompatibilityMatrix or use a Windows runner with Excel."
    }

    $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
    $minimumVersion = ConvertTo-SemVer $policy.minimumSupportedVersion
    if ($policy.source -ne "git-tags") {
        throw "Unsupported compatibility policy source: $($policy.source)"
    }

    $currentVersionText = (Get-Content -LiteralPath (Join-Path $repoRoot "version.txt") -Raw).Trim()
    $currentVersion = ConvertTo-SemVer $currentVersionText
    $tags = @(git -C $repoRoot tag --list "v*" --sort=version:refname)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list git tags."
    }

    $supportedTags = @(
        foreach ($tag in $tags) {
            try {
                $tagVersion = ConvertTo-SemVer $tag
                if ($tagVersion -ge $minimumVersion -and $tagVersion -lt $currentVersion) {
                    $tag
                }
            } catch {
                Write-Host "Ignoring non-semver tag: $tag" -ForegroundColor Yellow
            }
        }
    )

    if ($supportedTags.Count -eq 0) {
        Write-Step "No supported historical tags found before current version $currentVersionText."
        return
    }

    if (-not $SkipBuild) {
        Write-Step "Building updater (Release)"
        & dotnet build $projectPath --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build updater in Release configuration."
        }
    }
    if (-not (Test-Path -LiteralPath $updaterDllPath)) {
        throw "Updater binary not found: $updaterDllPath"
    }

    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    Write-Step "Compatibility floor: $($policy.minimumSupportedVersion)"
    Write-Step "Testing tags: $($supportedTags -join ', ')"

    foreach ($tag in $supportedTags) {
        Write-Step "Preparing source workbook from $tag"
        $safeTag = $tag -replace '[^A-Za-z0-9_.-]', '_'
        $caseDirectory = Join-Path $testDirectory $safeTag
        New-Item -ItemType Directory -Path $caseDirectory | Out-Null
        $sourcePath = Join-Path $caseDirectory "Source-$safeTag.xlsm"
        $outputPath = Join-Path $caseDirectory "Updated-$safeTag.xlsm"
        $reportPath = Join-Path $caseDirectory "Updated-$safeTag.update-report.json"
        $marker = "COMPAT-$safeTag"

        Export-GitFile -Tag $tag -PathInRepo "Electronic_Logbook_Master.xlsm" -DestinationPath $sourcePath

        $sourceFacts = Invoke-WorkbookEdit -WorkbookPath $sourcePath -Operation {
            param($Workbook)

            $logbook = Get-ListObject -Workbook $Workbook -Name "Logbook"
            if ($null -eq $logbook) {
                throw "Logbook table not found in $tag."
            }
            try { $logbook.Parent.Unprotect() } catch {}
            Ensure-DataRow -Table $logbook

            $idColumn = Get-ListColumnOrNull -Table $logbook -Names @("Reg", "Flight ID")
            if ($null -eq $idColumn) {
                throw "No Reg or Flight ID column found in $tag."
            }
            $idColumn.DataBodyRange.Cells(1, 1).Value2 = $marker

            $yearColumn = Get-ListColumnOrNull -Table $logbook -Names @("Year")
            if ($null -ne $yearColumn) {
                $yearColumn.DataBodyRange.Cells(1, 1).Value2 = 2026
            }

            $customColumn = Get-LogbookCustomColumn -Table $logbook
            if ($null -ne $customColumn) {
                $customColumn.Name = "Compat Header"
            }

            # When a source already has the native checkbox columns (2.0.0+),
            # their format must be retained rather than borrowed from legacy
            # Custom 1. Capture the source alignment for a migration assertion.
            $checkboxVerticalAlignment = @{}
            foreach ($checkboxName in @("FR", "IPC", "OPC")) {
                $checkboxColumn = Get-ListColumnOrNull -Table $logbook -Names @($checkboxName)
                if ($null -ne $checkboxColumn) {
                    $checkboxVerticalAlignment[$checkboxName] = [int]$checkboxColumn.DataBodyRange.Cells(1, 1).VerticalAlignment
                }
            }

            $keywords = Get-ListObject -Workbook $Workbook -Name "Keywords"
            $hasKeywords = $false
            if ($null -ne $keywords) {
                try { $keywords.Parent.Unprotect() } catch {}
                Ensure-DataRow -Table $keywords
                $keywordColumn = Get-ListColumnOrNull -Table $keywords -Names @("IPC")
                if ($null -ne $keywordColumn) {
                    $keywordColumn.DataBodyRange.Cells(1, 1).Value2 = "COMPAT IPC"
                    $hasKeywords = $true
                }
            }

            $routes = Get-ListObject -Workbook $Workbook -Name "Routes"
            $routeRows = 0
            if ($null -ne $routes) {
                try { $routes.Parent.Unprotect() } catch {}
                $row = $routes.ListRows.Add()
                $row.Range.Cells(1, 1).Value2 = "YCOM"
                $row.Range.Cells(1, 2).Value2 = "YTAG"
                $routeRows = Get-DataBodyRowCount -Table $routes
            }

            try {
                $Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 = 3
            } catch {}

            [pscustomobject]@{
                LogbookRows = Get-DataBodyRowCount -Table $logbook
                RouteRows = $routeRows
                HasKeywords = $hasKeywords
                Marker = $marker
                CheckboxVerticalAlignment = $checkboxVerticalAlignment
            }
        }

        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        $updaterSucceeded = $false
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            Write-Step "Migrating $tag (attempt $attempt of $maxAttempts)"
            Remove-Item -LiteralPath $outputPath, $reportPath -Force -ErrorAction SilentlyContinue

            $updaterLines = @()
            & dotnet $updaterDllPath `
                --source $sourcePath `
                --master $MasterPath `
                --output $outputPath `
                --report $reportPath 2>&1 | Tee-Object -Variable updaterLines
            $exitCode = $LASTEXITCODE
            $updaterOutput = ($updaterLines | Out-String)

            if ($exitCode -eq 0) {
                $updaterSucceeded = $true
                break
            }

            $looksTransientComFailure = $updaterOutput -match "0x800706BE|0x800706BA|remote procedure call|RPC server is unavailable"
            if ($looksTransientComFailure -and $attempt -lt $maxAttempts) {
                Write-Host "Transient Excel COM failure detected. Retrying..." -ForegroundColor Yellow
                continue
            }

            throw "Updater failed for $tag with exit code $exitCode.`n$updaterOutput"
        }

        Assert-Condition -Condition $updaterSucceeded -Message "Updater failed for $tag."

        $afterHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        Assert-Condition -Condition ($sourceHash -eq $afterHash) -Message "Source workbook changed during migration for $tag."
        Assert-Condition -Condition (Test-Path -LiteralPath $outputPath) -Message "Output workbook missing for $tag."

        Invoke-WorkbookEdit -WorkbookPath $outputPath -ReadOnly -Operation {
            param($Workbook)

            $version = [string]$Workbook.Names.Item("LogbookVersion").RefersToRange.Value2
            Assert-Condition -Condition ($version -eq $currentVersionText) -Message "Output version for $tag was '$version', expected '$currentVersionText'."

            $logbook = Get-ListObject -Workbook $Workbook -Name "Logbook"
            Assert-Condition -Condition ($null -ne $logbook) -Message "Output Logbook table missing for $tag."
            Assert-Condition -Condition ((Get-DataBodyRowCount -Table $logbook) -eq [int]$sourceFacts.LogbookRows) -Message "Logbook row count was not preserved for $tag."

            $idColumn = Get-ListColumnOrNull -Table $logbook -Names @("Reg", "Flight ID")
            Assert-Condition -Condition ($null -ne $idColumn) -Message "Output has no Reg or Flight ID column for $tag."
            Assert-Condition -Condition ([string]$idColumn.DataBodyRange.Cells(1, 1).Value2 -eq [string]$sourceFacts.Marker) -Message "Logbook marker was not preserved for $tag."

            Assert-Condition -Condition (Test-ListColumnExists -Table $logbook -Name "Compat Header") -Message "Custom Logbook heading was not preserved for $tag."

            foreach ($checkboxName in $sourceFacts.CheckboxVerticalAlignment.Keys) {
                $checkboxColumn = Get-ListColumnOrNull -Table $logbook -Names @($checkboxName)
                Assert-Condition -Condition ($null -ne $checkboxColumn) -Message "$checkboxName column missing after migration for $tag."
                $actualAlignment = [int]$checkboxColumn.DataBodyRange.Cells(1, 1).VerticalAlignment
                $expectedAlignment = [int]$sourceFacts.CheckboxVerticalAlignment[$checkboxName]
                Assert-Condition -Condition ($actualAlignment -eq $expectedAlignment) -Message "$checkboxName format was not preserved for $tag."
            }

            if ([bool]$sourceFacts.HasKeywords) {
                $keywords = Get-ListObject -Workbook $Workbook -Name "Keywords"
                Assert-Condition -Condition ($null -ne $keywords) -Message "Keywords table missing after migration for $tag."
                $keywordColumn = Get-ListColumnOrNull -Table $keywords -Names @("IPC")
                Assert-Condition -Condition ($null -ne $keywordColumn) -Message "Keywords IPC column missing after migration for $tag."
                Assert-Condition -Condition ([string]$keywordColumn.DataBodyRange.Cells(1, 1).Value2 -eq "COMPAT IPC") -Message "Keywords data was not preserved for $tag."
            }

            if ([int]$sourceFacts.RouteRows -gt 0) {
                $routes = Get-ListObject -Workbook $Workbook -Name "Routes"
                Assert-Condition -Condition ($null -ne $routes) -Message "Routes table missing after migration for $tag."
                Assert-Condition -Condition ((Get-DataBodyRowCount -Table $routes) -eq [int]$sourceFacts.RouteRows) -Message "Routes row count was not preserved for $tag."
            }

            try {
                Assert-Condition -Condition ([int]$Workbook.Names.Item("DateAfterExport").RefersToRange.Value2 -eq 3) -Message "DateAfterExport was not preserved for $tag."
            } catch {
                throw "DateAfterExport missing after migration for $tag."
            }

            $logbookTotals = $Workbook.Names.Item("LogbookTotals").RefersToRange
            Assert-Condition -Condition ($logbookTotals.Rows.Count -eq 2 -and $logbookTotals.Row -eq $logbook.TotalsRowRange.Row) -Message "LogbookTotals was not anchored to live totals for $tag."
        }

        Write-Step "$tag migrated successfully"
    }

    Write-Host "Compatibility matrix passed for $($supportedTags.Count) supported tag(s)." -ForegroundColor Green
} finally {
    if ($KeepTemp) {
        Write-Host "Keeping compatibility temp directory: $testDirectory" -ForegroundColor Yellow
    } elseif (Test-Path -LiteralPath $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
