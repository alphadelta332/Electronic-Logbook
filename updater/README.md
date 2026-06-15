# External Updater Prototype

This is an early Windows-only updater prototype for Electronic Logbook.

It creates a new updated workbook from a clean master and preserved user data. It never
renames, deletes, or overwrites the source workbook.

## Preserved Data

- Logbook raw entry columns from `Year` through `Circling`
- `CurrencyExclusions`
- Custom Logbook column headings
- Currency detection `Keywords`
- Airport `Base` flags matched by ICAO
- Routes table and route-cache state
- Date reset and warning-suppression preferences
- Logbook table style, custom column formatting, totals-area formatting, and palette

The updater also rebuilds the live Logbook totals ranges, repairs expanded-row visibility,
refreshes pivot tables, and updates the Hours Over Time chart range.

Everything else comes from the clean master workbook.

## Prototype Usage

Use a local master while developing:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater -- `
  --source "C:\Path\My Logbook.xlsm" `
  --master "C:\Path\Electronic_Logbook_Master.xlsm" `
  --output "C:\Path\My Logbook Updated.xlsm"
```

When a published release contains `release-manifest.json`, omit `--master` to download
and verify the latest release workbook:

```powershell
dotnet run --project updater/src/ElectronicLogbook.Updater -- `
  --source "C:\Path\My Logbook.xlsm" `
  --output "C:\Path\My Logbook Updated.xlsm"
```

The updater writes a JSON validation report beside the output workbook.

Run the disposable Excel migration test locally with:

```powershell
.\updater\Test-ExternalUpdater.ps1
```

## Current Limitations

- Requires Microsoft Excel for Windows.
- Uses Excel COM automation and must run while the source workbook is closed.
- Does not yet provide a full visual-diff test or normalize every possible user-customized format.
- Does not replace the existing in-workbook updater.
- The executable is not currently code-signed or distributed as a release asset.
