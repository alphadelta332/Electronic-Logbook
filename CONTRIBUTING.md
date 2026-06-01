# Contributing

Contributions are welcome, but this project has a cautious release process because the distributed asset is a macro-enabled Excel workbook.

## Before Opening a Pull Request

- Open an issue first for large changes or changes to the workbook update process.
- Keep VBA source changes paired with the corresponding workbook changes when applicable.
- Do not include personal logbook data, private tokens, or machine-specific paths.
- Run `tools/Test-ReleaseMetadata.ps1` before submitting.
- If you changed workbook content, export the VBA source after testing so the text files match the workbook.

## Release Changes

Release PRs should use the pull request checklist. The master workbook must be checked locally before release because GitHub Actions cannot reliably inspect Excel named ranges without Excel installed.

At minimum, run:

```powershell
.\tools\Test-ReleaseMetadata.ps1
.\tools\Test-WorkbookPublicReadiness.ps1
```

Then smoke test the prepared workbook manually in Excel.
