# Contributing

Contributions are welcome, but this project has a cautious release process because the distributed asset is a macro-enabled Excel workbook.

## Before Opening a Pull Request

- Open an issue first for large changes or changes to the workbook update process.
- Keep every master-workbook VBA source change paired with the corresponding `Electronic_Logbook_Master.xlsm` change. After editing tracked VBA source, run `.\tools\ImportVbaIntoWorkbook.ps1 -WorkbookPath .\Electronic_Logbook_Master.xlsm`; string-only and other small VBA edits still require import. `tools\Test-VbaWorkbookPairing.ps1` enforces this before VBA source-quality and release metadata checks pass.
- Do not include personal logbook data, private tokens, or machine-specific paths.
- Run `tools/Invoke-Validation.ps1 -Tier Fast` before submitting.
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
