# Shared airport catalogue

Android keeps an offline airport catalogue. Development and Preview each publish a
shared copy; phones do not download or parse the upstream airport CSV independently.
Searches use the local copy and require no network request or user maintenance.

## Refresh rules

The publisher uses OurAirports' `airports.csv` from
`https://davidmegginson.github.io/ourairports-data/airports.csv`. Its AIRAC calculation
uses 28-day UTC cycles anchored on 25 January 2024. A server cron job runs hourly at
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

The publisher tool verifies project identity, applies only the dedicated migration,
deploys the function, uploads the embedded seed and configures the remote Vault
secrets `airport_catalog_url` and `airport_catalog_service_role`. The latter uses the
project's default server secret exposed to the function runtime. Neither secret
belongs in mobile config, transfer archives or tracked files. After rotating the
server credential, rerun deployment to refresh Vault and validate the scheduled path.

The test tool verifies public reads, snapshot integrity, preservation against the
seed, rejected anonymous writes, rejected unauthenticated publication, and the exact
cron SQL-to-function path. A manual execution proves that path; it does not prove a
future scheduled AIRAC refresh has already occurred.
