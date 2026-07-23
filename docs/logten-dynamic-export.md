# LogTen Dynamic Export Format

The LogTen importer is implemented but remains internal. Use this note to configure
test exports and future user-facing instructions without relying on LogTen's default
full export.

## Required Columns

The dynamic export must include these columns exactly, case-insensitively:

- `Date`
- `Aircraft ID`
- `Aircraft Type`
- `From`
- `To`
- `Total Time`
- `Simulator`
- `PIC/P1 Crew`
- `Day Ldg`
- `Night Ldg`
- `Approach 1`
- `Approach 2`

## Optional Columns

The importer currently consumes these optional columns when they are present:

- `Route`
- `Night`
- `PIC`
- `P1u/s`
- `SIC`
- `SIC/P2 Crew`
- `Observer`
- `Actual Inst`
- `Flight #`
- `Remarks`
- `IPC/ICC`
- `Flight Review`

## File Shape

- Supported delimiters are comma and tab. The importer chooses the delimiter from the
  header row.
- Files are read as UTF-8; a UTF-8 BOM is tolerated.
- Blank rows are ignored and counted in the preview/import result.
- LogTen default full exports are intentionally rejected when they use the
  `flight_flightdate` field shape. Create a custom dynamic export instead.

## Value Rules

- Dates must be readable by Excel/VBA on the import machine.
- Hour values may be decimal hours or `H:MM` durations.
- A simulator-only row uses blank `Aircraft Type`, zero `Total Time`, and non-zero
  `Simulator`; it imports as type `SIM`.
- Non-simulator rows must use an aircraft type already configured in the workbook.
- `PIC`, `P1u/s`, and `SIC` cannot exceed `Total Time` when added together.
- `Approach 1` and `Approach 2` use `count;type`, for example `1;ILS`.

## Duplicate Key

Potential duplicates are identified from the mapped date, aircraft type, registration,
flight ID, route fields, remarks, and mapped hour buckets. Duplicate rows are skipped;
they are not imported a second time.
