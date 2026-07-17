# Runs the updater unit tests with coverlet and prints aggregate plus non-COM coverage.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepoRoot).Path
$testProject = Join-Path $repoRoot "updater\tests\ElectronicLogbook.Updater.Tests\ElectronicLogbook.Updater.Tests.csproj"
$resultsDirectory = Join-Path $repoRoot "updater\TestResults"

function Format-Percent {
    param([double]$Value)
    return "{0:N2}%" -f ($Value * 100)
}

function Get-CoverageSummary {
    param(
        [Parameter(Mandatory)]
        [object[]]$Classes,
        [Parameter(Mandatory)]
        [scriptblock]$Filter
    )

    $coveredLines = 0
    $validLines = 0
    $coveredBranches = 0
    $validBranches = 0

    foreach ($class in $Classes | Where-Object $Filter) {
        foreach ($line in @($class.lines.line)) {
            $validLines++
            if ([int]$line.hits -gt 0) {
                $coveredLines++
            }

            if ($line.branch -eq "true") {
                $conditionCoverage = [string]$line."condition-coverage"
                if ($conditionCoverage -match "\((\d+)/(\d+)\)") {
                    $coveredBranches += [int]$Matches[1]
                    $validBranches += [int]$Matches[2]
                }
            }
        }
    }

    [pscustomobject]@{
        LinesCovered = $coveredLines
        LinesValid = $validLines
        LineRate = if ($validLines -gt 0) { $coveredLines / $validLines } else { 0 }
        BranchesCovered = $coveredBranches
        BranchesValid = $validBranches
        BranchRate = if ($validBranches -gt 0) { $coveredBranches / $validBranches } else { 0 }
    }
}

& dotnet test $testProject --collect:"XPlat Code Coverage" --results-directory $resultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Updater tests failed."
}

$coveragePath = Get-ChildItem -LiteralPath $resultsDirectory -Filter "coverage.cobertura.xml" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $coveragePath) {
    throw "Coverage output was not found under $resultsDirectory."
}

$coverage = [xml](Get-Content -LiteralPath $coveragePath.FullName -Raw)
$classes = @($coverage.coverage.packages.package.classes.class)
$aggregate = Get-CoverageSummary -Classes $classes -Filter { $true }
$nonCom = Get-CoverageSummary -Classes $classes -Filter {
    $_.filename -notmatch "ExcelWorkbookMigrator\.cs|Program\.cs"
}

Write-Host ""
Write-Host "Coverage report: $($coveragePath.FullName)" -ForegroundColor Cyan
Write-Host ("Aggregate: {0} line ({1}/{2}), {3} branch ({4}/{5})" -f `
    (Format-Percent $aggregate.LineRate),
    $aggregate.LinesCovered,
    $aggregate.LinesValid,
    (Format-Percent $aggregate.BranchRate),
    $aggregate.BranchesCovered,
    $aggregate.BranchesValid)
Write-Host ("Non-COM library: {0} line ({1}/{2}), {3} branch ({4}/{5})" -f `
    (Format-Percent $nonCom.LineRate),
    $nonCom.LinesCovered,
    $nonCom.LinesValid,
    (Format-Percent $nonCom.BranchRate),
    $nonCom.BranchesCovered,
    $nonCom.BranchesValid)
