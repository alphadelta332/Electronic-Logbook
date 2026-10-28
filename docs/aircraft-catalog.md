# Aircraft reference catalogue

Source ingestion and shared publication tooling are implemented. Deployment acceptance
and the mobile selector remain separate; this page does not confirm phone delivery.

The baseline is [Mictronics aircraft-database](https://github.com/Mictronics/aircraft-database),
using only `types.json` and `LICENSE` from `indexedDB.zip`. Flight records retain
their existing Type string; the catalogue supplies names and reference metadata.
The source is a community database, not an official complete ICAO or AIRAC-certified
database. Classification and wake metadata are retained as supplied. Engine counts
are nullable: missing, generic and coupled/ambiguous encodings remain unknown.

## Owner label tweaks

Names default to Mictronics. Owner-approved display-name changes belong in
`data/aircraft-catalog/label-overrides.json`, keyed by ICAO code, for example:

```json
{
  "C210": "CESSNA 210 Centurion"
}
```

This is an example. The overrides file contains only owner-approved changes.
Tell the maintainer the code and desired label, or edit that file. An override must
have a nonempty name and refer to a code in the named catalogue. It changes the
display label without changing the saved code, engine metadata or historical flights.
The source name is preserved in the snapshot; refreshes carry the separate override
map forward. No routine manual review of 50 codes is required. A retired code with
an override requires review before accepting a replacement snapshot.

## Reproduce and validate

Use the existing Node/npm installation. From the repository root:

```powershell
npm.cmd ci --prefix supabase/functions/aircraft-catalog
node tools/import-aircraft-catalog.mjs
node --test supabase/functions/aircraft-catalog/catalog.test.mjs
```

The importer defaults to commit `d31104a82bdc429be7fe94726247fc45aa0c49a4`, dated
25 October 2026, and verifies initial archive SHA-256
`fdff9ab076f279658d21aa4bc61f7b2b8310410000a8e51b7cd3c29989a094f7`.
`--revision latest` resolves current upstream main to an immutable commit for later
refreshes; `--revision <40-character-hash>` replays another revision. `--output <path>`
uses a separate output for comparison. Validation completes before atomic replacement
of the default `data/aircraft-catalog/mictronics.json.gz`; a failure keeps the old file.

The pinned source has 2,788 records. The owner excluded 14 source codes with blank
names from suggestions: BALL, DRON, EMER, FFLO, GND, GYRO, PARA, SERV, SHIP, TWR,
UHEL, ULAC, VFHC and ZZZZ. This leaves 2,774 named records. Manual entry remains
available. New unnamed codes fail validation; an excluded code which later acquires
a valid name becomes eligible again. Duplicate codes are rejected, including escaped
JSON duplicates. Archive, selected decompressed members, JSON, code/name/metadata,
record count and upstream licence are checked before accepting a production snapshot.

The schema-1 snapshot contains `source`, `count`, `sourceCount`, `omittedCodes`,
`labelOverrides` and code-sorted `aircraft` rows. Row fields are `i` (code), `n`
(source name), `c` (source classification), `w` (source wake category) and `e`
(nullable engine count). `source` records immutable revision/date, archive/type/licence
SHA-256 values, linked provenance, attribution and the full licence text.

The source's [ODC Attribution licence](https://github.com/Mictronics/aircraft-database/blob/d31104a82bdc429be7fe94726247fc45aa0c49a4/LICENSE)
and notices travel inside the derived snapshot. Search must show linked Mictronics
attribution. A changed upstream licence requires review; source selection does not
establish accuracy, complete coverage or rights in independently protected contents.

## Shared publication

The `aircraft-catalog` public Storage bucket is separate from airport and encrypted
logbook data. Clients have no write policy. The Edge publisher accepts only its
dedicated `AIRCRAFT_CATALOG_PUBLISH_TOKEN`, kept in Supabase and the matching
Cloudflare Worker's secret binding. No source-access credential is required locally.

```powershell
pwsh -NoProfile -File tools/Publish-AircraftCatalog.ps1 -Environment development
pwsh -NoProfile -File tools/Test-AircraftCatalog.ps1 -Environment development
node --test supabase/functions/aircraft-catalog/*.test.mjs supabase/activity-worker/aircraft.test.mjs
```

Use `preview` only after inspecting that environment's existing timer. The deployment
tool compares remote/local schedules before appending the aircraft hourly minute-11
timer. Existing airport and activity-probe schedules and secrets are preserved. It
applies only the aircraft bucket migration; it does not perform an airport scheduler,
pg_net or backup-privilege cutover. Development and Preview have separate publications.

The publisher uses the airport 28-day UTC cycle. A completed cycle suppresses source
downloads; hourly calls retry failed cycles. The latest upstream main revision is
resolved through GitHub's public API, then an immutable archive is fetched. Upstream
HTTP failures, including GitHub's shared unauthenticated API quota, fail closed and
are retried by a later timer; no token is silently borrowed from another service.
Manifest, source, decompressed size, record count and checksum checks precede
publication. A content-hashed compressed snapshot is uploaded first and the manifest
last. Failed source/storage checks retain the prior publication. Sanitized logs report
the failed stage, error type and HTTP/quota status without credentials or response bodies.

An approved label-map change can cause a new server publication within a cycle.
This alone does not make a phone which has already completed that cycle check again;
the mobile update lifecycle's completed-cycle policy still applies.
