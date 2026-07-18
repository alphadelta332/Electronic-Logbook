# LogTen Import Fixtures

These fixtures are intentionally small and synthetic. They are for importer regression
tests and manual smoke checks, not real flight records.

`dynamic-clean.csv` uses the dynamic LogTen export headers expected by
`ImportFromLogTenFile` and contains one importable C172 row plus one blank line.

`dynamic-blocking.csv` keeps the same header set but includes rows that should be blocked:
an unknown aircraft type, invalid date, and crew-hour totals exceeding total time.

`default-full-export-detected.csv` mimics the old/default LogTen full export shape by using
the `flight_flightdate` header. The importer should reject it and tell the user to use the
dynamic export format.

## Required Dynamic Export Headers

The importer validates these headers before previewing a dynamic export:

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

The fixtures also include optional columns currently consumed by the mapper:

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
