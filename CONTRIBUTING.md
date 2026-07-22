# Contributing

Contributions are welcome, but this project has a cautious release process because the distributed asset is a macro-enabled Excel workbook.

## Development Setup

This is a Windows-focused project. The workbook and external updater rely on desktop
Excel COM automation, so the full validation workflow cannot run on macOS or Linux.

Install the following tools before working on the relevant area:

- Microsoft Excel for Windows (Microsoft 365 on Windows 11 is the primary supported
  environment; Excel 2021 and newer perpetual editions are supported).
- PowerShell 7 or Windows PowerShell for the repository scripts.
- .NET 8 SDK for the updater, portable package logic, and Blazor mobile app.
- Node.js with npm for the Capacitor Android shell. The committed
  `mobile/package-lock.json` pins its JavaScript dependencies.
- Android Studio and an Android SDK only when building or testing the Android package.

Restore the dependencies from the repository root and mobile directory:

```powershell
dotnet restore ElectronicLogbook.Updater.sln
Set-Location mobile
npm ci
```

Return to the repository root before running the PowerShell validation scripts. No
Python environment or `requirements.txt` is needed: .NET dependencies are declared in
the project files and mobile dependencies are declared in `mobile/package.json`.

### Safe Local Checks

For ordinary source changes, start with the fast validation tier:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Fast
```

Run focused .NET tests for updater or mobile changes:

```powershell
dotnet test updater\tests\ElectronicLogbook.Updater.Tests\ElectronicLogbook.Updater.Tests.csproj --no-restore
dotnet test mobile\tests\ElectronicLogbook.Mobile.Tests\ElectronicLogbook.Mobile.Tests.csproj
```

Use the Excel tier only on a disposable workbook copy when practical. It requires
desktop Excel, and the workbook under test must be closed. For a `dev`-channel workbook,
omit the release-only public-readiness check:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Excel -SkipPublicReadinessCheck
```

## Before Opening a Pull Request

- Open an issue first for large changes or changes to the workbook update process.
- Keep every master-workbook VBA source change paired with the corresponding `Electronic_Logbook_Master.xlsm` change. After editing tracked VBA source, run `.\tools\ImportVbaIntoWorkbook.ps1 -WorkbookPath .\Electronic_Logbook_Master.xlsm`; string-only and other small VBA edits still require import. `tools\Test-VbaWorkbookPairing.ps1` enforces this before VBA source-quality and release metadata checks pass.
- Do not include personal logbook data, private tokens, or machine-specific paths.
- Run `tools/Invoke-Validation.ps1 -Tier Fast` before submitting.
- Do not smoke-test or modify `Electronic_Logbook_Master.xlsm` destructively. Use a
  disposable copy, and do not modify user-generated `*_Updated.xlsm` workbooks.
- If you changed workbook content, export the VBA source after testing so the text files match the workbook.

## Release Changes

Release PRs should use the pull request checklist. The master workbook must be checked locally before release because GitHub Actions cannot reliably inspect Excel named ranges without Excel installed.

At minimum, run:

```powershell
.\tools\Invoke-Validation.ps1 -Tier Excel
```

Then smoke test the prepared workbook manually in Excel.

## External Updater

Build and run the updater unit tests with:

```powershell
dotnet test ElectronicLogbook.Updater.sln --configuration Release
```

The updater is an experimental Windows-only prototype. It must never overwrite, rename, or
delete the source workbook. See `updater/README.md` for its supported migration contract.
