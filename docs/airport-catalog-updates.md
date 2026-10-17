# Shared airport catalogue

Deployment status (2026-10-17): the external timer/token design below is deployed
and observed on **development only**. Preview still uses its original SQL/pg_net
timer and deployed service-key publisher; its Cloudflare keepalive is unchanged.
The development test removed pg_net successfully but exposed a separate inability
to delegate all managed Auth/Storage reads to the backup account. Do not deploy or
cut over Preview on the assumption that timer relocation alone completes backups.

Android keeps an offline airport catalogue. Development and Preview each publish a
shared copy; phones do not download or parse the upstream airport CSV independently.
Searches use the local copy and require no network request or user maintenance.

## Refresh rules

The publisher uses OurAirports' `airports.csv` from
`https://davidmegginson.github.io/ourairports-data/airports.csv`. Its AIRAC calculation
uses 28-day UTC cycles anchored on 25 January 2024. A Cloudflare timer runs hourly at
minute seven, exits when the current cycle is published, and retries failed refreshes
on later runs. This does not depend on an owner's computer or phone staying on.

The merge follows the workbook's existing rules:

- Existing airports retain their names and two/three-letter aliases; only coordinates
  are refreshed. Airports absent from the source remain available.
- Add large and medium airports, plus small airports with scheduled service.
- Use a four-character ICAO, falling back to GPS code and then source identifier.
- For additions, remove one trailing name suffix: International Airport, Air Field,
  Air Port, Air Base, Aerodrome, Airfield, Airstrip, Airport or Runway.

The function streams the CSV through the pinned standard CSV parser. Buffering the
whole source exceeds the hosted Edge worker's memory allowance. It validates records
and size bounds before publishing a compressed snapshot named by its SHA-256. It
publishes `manifest.json` last, so failed refreshes leave the last published version
usable. The bucket contains public reference data only; clients cannot write to it.

## Phone behaviour

The app restores its validated IndexedDB cache before starting, with the embedded
catalogue as fallback. It checks the public manifest on launch, resume, reconnection
and every fifteen minutes while running, until the current cycle is obtained.
Successful checks suppress further requests for that cycle; unsuccessful checks
back off for five minutes, with reconnection allowing an immediate retry.

Only a changed snapshot hash causes a catalogue download. The app checks the hash,
schema, count, duplicate codes, coordinates and size bounds, merges using the same
preservation rules, and saves the cache before replacing the active catalogue.
Network, validation or storage failure retains the previous catalogue. Airport
updates are independent of encrypted logbook data and hosted logbook sync.

There is no background phone wake-up while the app is fully closed. The server
continues updating, and the phone catches up when next opened or resumed online.

## Deployment and checks

Use the existing Supabase management token and project metadata described in
`LOCAL_DEVICE_SETUP_HANDOVER.md`. No additional local runtime is required.

```powershell
.\tools\Publish-AirportCatalog.ps1 -Environment development
.\tools\Test-AirportCatalog.ps1 -Environment development
.\tools\Publish-AirportCatalog.ps1 -Environment preview
.\tools\Test-AirportCatalog.ps1 -Environment preview
node supabase/functions/airport-catalog/catalog.test.mjs
```

The publisher tool verifies project identity, applies only the foundation migration,
deploys the function, uploads the embedded seed and deploys the Cloudflare timer.
It generates a 256-bit `AIRPORT_CATALOG_PUBLISH_TOKEN`, stores it in Supabase Edge
secrets and the matching Worker's secret binding, and accepts only that token for
publication. Cloudflare cannot use it to read or change logbooks. The powerful
server credential stays in Supabase's runtime and the seeding process's memory.
Redeployment rotates the publisher token in both services; no local token file is
needed. Use the existing Wrangler login and verified Workers Free account. The
ignored configurations contain account/project IDs and publishable keys, not secrets.

Preview reuses `flightlogx-preview-activity`, preserving its four-hour activity probe;
development uses the same code as `flightlogx-development-airports` with only the
hourly timer. Both have public HTTP/preview endpoints disabled. The scheduled handler
routes the original four-hour cron to the probe and other configured crons to airport
publication. Each invocation makes one request, fails on redirects/errors/stale cycle
or malformed responses, and logs only airport status, cycle and count. The last good
catalogue remains available on failure.

For first cutover, observe a successful real Cloudflare scheduled publication before
applying `20261017010000_external_airport_scheduler.sql`. A temporary minute timer can
exercise the same handler promptly, but restore and read back the normal hourly
schedule afterward. The migration checks for queued requests, other HTTP jobs,
webhooks and application routines, removes the old airport timer, drops pg_net
without CASCADE, and deletes its two obsolete Vault secret copies. Do not use CASCADE
or weaken the backup-account audit if an unexpected dependency stops removal.

The test tool verifies public reads, snapshot integrity, preservation against the
seed, rejected anonymous writes/publication, and removal of the legacy SQL timer and
pg_net. Separately retain Cloudflare scheduled-execution evidence and verify both
production cron settings and the secret binding without saving its value. A
current-cycle check does not prove a future AIRAC refresh has already occurred.
Run `node supabase/activity-worker/worker.test.mjs` and
`node supabase/activity-worker/airport.test.mjs` for probe isolation, publication,
token rejection, transport/response failures and redaction checks.
