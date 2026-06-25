function Get-ReleaseConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $repoRoot = (Resolve-Path $RepoRoot).Path
    $config = [ordered]@{
        MasterWorkbook = Join-Path $repoRoot "Electronic_Logbook_Master.xlsm"
        WorkingCopyWorkbook = ""
    }

    $localConfigPath = Join-Path $repoRoot "release.local.json"
    if (Test-Path $localConfigPath) {
        $localConfig = Get-Content $localConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $localConfig.PSObject.Properties) {
            if ($config.Contains($property.Name)) {
                $value = [string]$property.Value
                if (-not [System.IO.Path]::IsPathRooted($value) -and -not [string]::IsNullOrWhiteSpace($value)) {
                    $value = Join-Path $repoRoot $value
                }
                $config[$property.Name] = $value
            }
        }
    }

    return [pscustomobject]$config
}

function Get-ReleaseVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $versionPath = Join-Path $RepoRoot "version.txt"
    if (-not (Test-Path $versionPath)) {
        throw "version.txt not found at $versionPath"
    }

    $version = (Get-Content $versionPath -Raw -Encoding UTF8).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "version.txt must contain a semantic version like 1.2.3. Found '$version'."
    }

    return $version
}

function Close-ExcelComObjects {
    param(
        $Excel,
        $Workbook,
        [bool]$Save
    )

    if ($null -ne $Workbook) {
        try {
            if ($Save) {
                $Workbook.Close($true)
            } else {
                $Workbook.Close($false)
            }
        } catch {}
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Workbook) | Out-Null
    }

    if ($null -ne $Excel) {
        try { $Excel.Quit() } catch {}
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Excel) | Out-Null
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
}

function Invoke-WorkbookEdit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [scriptblock]$Operation,
        [switch]$ReadOnly,
        [switch]$Visible
    )

    if (-not (Test-Path $WorkbookPath)) {
        throw "Workbook not found: $WorkbookPath"
    }

    $resolvedPath = (Resolve-Path $WorkbookPath).Path
    $excel = $null
    $workbook = $null
    $save = $false

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = [bool]$Visible
        $excel.DisplayAlerts = $false
        $excel.EnableEvents = $false
        try {
            # msoAutomationSecurityForceDisable prevents Workbook_Open from firing during tooling.
            $excel.AutomationSecurity = 3
        } catch {}

        $workbook = $excel.Workbooks.Open($resolvedPath, $false, [bool]$ReadOnly)
        & $Operation $workbook $excel
        $save = -not $ReadOnly
    } finally {
        Close-ExcelComObjects -Excel $excel -Workbook $workbook -Save $save
    }
}

function Set-WorkbookNameValue {
    param(
        [Parameter(Mandatory)]
        $Workbook,
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Value
    )

    try {
        $Workbook.Names.Item($Name).RefersToRange.Value2 = $Value
    } catch {
        # If the workbook/sheet is protected, writing through RefersToRange can fail
        # even when the name exists. Retry after unprotecting with the default blank
        # password used by release-mode workbook protection.
        try {
            $Workbook.Unprotect("")
            foreach ($ws in $Workbook.Worksheets) {
                $ws.Unprotect("")
            }
            $Workbook.Names.Item($Name).RefersToRange.Value2 = $Value
        } catch {
            throw "Could not set named range '$Name' in $($Workbook.Name). The name may be missing or protected."
        }
    }
}

function Set-LogbookWorkbookState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [ValidateSet("dev", "main")]
        [string]$Branch,
        [AllowEmptyString()]
        [string]$Version
    )

    Write-Host "Processing: $WorkbookPath"
    Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -Operation {
        param($Workbook)

        Set-WorkbookNameValue -Workbook $Workbook -Name "GitHubBranch" -Value $Branch
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            Set-WorkbookNameValue -Workbook $Workbook -Name "LogbookVersion" -Value $Version
        }
    }

    Write-Host "  GitHubBranch = $Branch" -ForegroundColor Green
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        Write-Host "  LogbookVersion = $Version" -ForegroundColor Green
    } else {
        Write-Host "  LogbookVersion unchanged" -ForegroundColor Yellow
    }
}

function Assert-VbaProjectAccess {
    param(
        [Parameter(Mandatory)]
        $Workbook
    )

    try {
        $null = $Workbook.VBProject.VBComponents.Count
    } catch {
        throw "Excel blocked access to the VBA project. Enable Trust Center > Macro Settings > Trust access to the VBA project object model."
    }
}

function Invoke-WorkbookMacro {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [Parameter(Mandatory)]
        [string]$MacroName,
        [switch]$Visible,
        [switch]$IgnoreMissing
    )

    if (-not (Test-Path $WorkbookPath)) {
        throw "Workbook not found: $WorkbookPath"
    }

    $resolvedPath = (Resolve-Path $WorkbookPath).Path
    $excel = $null
    $workbook = $null

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = [bool]$Visible
        $excel.DisplayAlerts = $false
        $excel.EnableEvents = $false

        # Enable macro execution for explicit release/testing macro calls.
        try { $excel.AutomationSecurity = 1 } catch {}

        $workbook = $excel.Workbooks.Open($resolvedPath, $false, $false)
        $qualifiedMacro = "'$($workbook.Name)'!$MacroName"

        try {
            $excel.Run($qualifiedMacro)
        } catch {
            if ($IgnoreMissing) {
                Write-Host "  Skipped macro '$MacroName' for $resolvedPath (not available or disabled)." -ForegroundColor Yellow
                return
            }

            throw "Could not run macro '$MacroName' for $resolvedPath. $_"
        }

        $workbook.Save()
        Write-Host "  Macro '$MacroName' executed for $resolvedPath" -ForegroundColor Green
    } finally {
        Close-ExcelComObjects -Excel $excel -Workbook $workbook -Save $false
    }
}

function Set-WorkbookOpenView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkbookPath,
        [string]$WorksheetName = "New Entry"
    )

    $targetWorksheetName = $WorksheetName
    $operation = {
        param($Workbook, $Excel)

        $Workbook.Activate()
        try {
            $Workbook.Worksheets.Item($targetWorksheetName).Activate()
        } catch {
            $Workbook.Worksheets.Item(1).Activate()
        }

        $Workbook.Save()

    }.GetNewClosure()

    Write-Host "Setting open view: $WorkbookPath"
    Invoke-WorkbookEdit -WorkbookPath $WorkbookPath -Operation $operation

    Write-Host "  Active sheet = $targetWorksheetName" -ForegroundColor Green
}

Export-ModuleMember -Function Get-ReleaseConfig, Get-ReleaseVersion, Invoke-WorkbookEdit, Set-WorkbookNameValue, Set-LogbookWorkbookState, Set-WorkbookOpenView, Assert-VbaProjectAccess, Invoke-WorkbookMacro
