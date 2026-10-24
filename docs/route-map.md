# Route map

More → Route map displays logged airport connections using the existing offline
airport catalogue, bundled MapLibre GL JS and simplified Natural Earth country
outlines. While open online, it automatically adds detailed OpenFreeMap backgrounds
(Positron in light mode, Dark in dark mode). It is an overview of recorded connections, not a navigation chart or flown
GPS track. Small islands may be absent from the simplified world background.

## Selection and filters

The map uses Charts' flight-selection rules: active dated entries up to the current
local day, positive flying time, inclusive Start/End dates, and case-insensitive
aircraft-type/registration selections. Simulator-only, deleted, invalid-date and
future entries are excluded. Date and aircraft predicates are shared with Charts;
the map does not change Charts' distance calculations.

Each flight is read in departure → Via → arrival order. Known aliases resolve to
the same airport. Unknown tokens are skipped when connecting the remaining known
airports. Unresolved tokens and missing endpoints remain in the local model for
diagnostics; the page has no explanatory copy or incomplete-routes list. Remarks
are not parsed as routes.

Repeated connections and opposite directions share one line. More frequent connections
become thicker and warmer; airport dots grow and warm with their visit counts. Both
use a linear theme-colour-to-red scale from one flight to the highest count, with
separate maxima for connections and airports. Route colour and thickness always use
each connection's full-logbook frequency and the full-logbook maximum; date,
aircraft and airport filters only hide routes. The baseline uses all entries
eligible for the map, retaining the exclusions listed above. Airport dot styling
uses visit counts within the current date/aircraft selection.
Lines range from 2.5 to 8px; dot interiors range from 12 to 24px while their button
targets remain 48px. When every count is one, everything stays in the selected theme
colour and at minimum size. Date/aircraft filtering recalculates the airport scale.
More frequent routes
and airports draw on top of less frequent ones when overlapping; tapping overlapping
dots selects the visible top dot, with the nearest 48px target used outside the dots.
Theme changes recolour the map. Rose and Plum are unavailable as accent choices;
existing selections fall back to Forest. Styling uses the bundled renderer
and existing theme colours, with no additional dependency or requests.
Lines are not selectable.
Airports selected as bases in Charts use a black dot border in light mode and a
white border in dark mode. Selection keeps its separate outer ring with a 2px gap.
Pressing an airport opens an on-map popup containing only its name, ICAO code and
visit count and shows only connections from entries involving that airport, including
each matching entry's full departure → Via → arrival route. Shared connections retain
their full-logbook colour and thickness throughout selection.
Only airport dots belonging to matching entries remain visible and selectable;
their visit counts retain the current date/aircraft-filtered totals.
Closing the popup or pressing Show all routes restores all dots and connections within the
current date/aircraft filters. Visits count distinct matching flights; repeated stops/return routes
within a flight count once for that airport, consistent with Charts. Airports remain
selectable without a line, including same-airport flights. Markers are keyboard
buttons with 48px targets. Flight changes, deletion, airport updates and local-day
changes refresh the map and close stale popups.

The page contains the map and its filter/view controls, without summary metrics,
explanatory prose, bottom lists or matching-flight panels. Supporting explanations
belong in this document. This interface supersedes the original offline-map batch.

Pan/zoom and Show all routes use a wrapped world. Connection longitudes take the
short crossing at the international date line. Overview fitting includes all known
markers and connections, including selections spanning the world.

## Local assets and requests

`mobile/package-lock.json` pins MapLibre. The existing `npm.cmd ci` dependency-restore
step also restores it; no global map CLI, account, API key or new machine prerequisite
is needed. MSBuild copies its renderer, module worker, stylesheet and BSD license
into published assets. The background geometry and provenance notice are tracked in
`mobile/src/ElectronicLogbook.Mobile/wwwroot/data/`. Natural Earth data is public
domain; its [terms](https://www.naturalearthdata.com/about/terms-of-use/) were reviewed
on 2026-10-24. Map attribution remains visible, with the MapLibre license packaged.

The service worker precaches module files as well as ordinary scripts and JSON. The
Android bundle includes these assets directly. Neither renderer initialization nor
the background needs a previous online visit to the map. Existing app font requests
are separate from map rendering. Map requests allow only app assets and the HTTPS
`tiles.openfreemap.org` style, tile, font and sprite paths, without URL credentials,
query strings, account headers, cookies or referrers. It receives airport positions and
connection counts; logbook records are processed in .NET on the device.

The detailed background adds sources/layers beneath the local route overlay; it
does not upload that overlay. Requests disclose the viewed tile area and IP address.
OpenFreeMap may use Cloudflare; its policy describes request metadata, error logs
and temporary IP logging during security incidents. Account and sync → Privacy
contains the user-facing disclosure and provider links. OpenFreeMap/OpenMapTiles/
OpenStreetMap attribution comes from the provider's tile metadata and stays visible.

The [quick start](https://openfreemap.org/quick_start/), [terms](https://openfreemap.org/tos/)
and [privacy policy](https://openfreemap.org/privacy/) were rechecked on 2026-10-24.
The service is supplied as-is and may be discontinued. This app performs ordinary
interactive requests, without a detailed offline-download feature or tile prefetch.
Its bundled Natural Earth fallback does not depend on the provider. A provider
error, loss of connectivity or 12-second loading timeout removes the online layers;
routes, markers, selection and filters remain local. Reconnecting or reopening the
screen retries the online background. Leaving the screen aborts its style fetch and
removes the map, source requests, observers and connectivity listeners.

If rendering is unavailable (for example, unsupported WebGL), a concise retry error
is shown. The map renderer is loaded only on the map screen and removed when leaving it.

## Focused verification

- `npm.cmd run test:routes` from `mobile/`: frequency scaling, geometry, date-line/world fitting and
  bundled background integrity.
- The mobile test project supports the VSTest filter
  `FullyQualifiedName~RoutesComponentTests|FullyQualifiedName~MobileRouteMapTests|FullyQualifiedName~ChartsComponentTests|FullyQualifiedName~MobileLogbookAnalyticsTests`.
- After `npm.cmd run publish:pwa`, use the existing browser audit with
  `--route routes-populated` for a disposable synthetic logbook: first map visit
  offline, cached module/worker/background assets, date-line fitting, airport popups,
  keyboard marker activation, distinct visit counts, filter/error states, absence of
  removed UI, live online tiles/attribution, startup/tile failure and external-request
  inspection (including a provider cookie canary). Network evidence is saved as
  `*-map-network.json` in the audit output. Use `--route charts-populated`
  alongside it for current control/style comparison. Browser checks establish web
  rendering only; retained-Pixel visual/touch acceptance remains owner-operated.
