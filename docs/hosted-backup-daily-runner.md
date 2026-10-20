# Daily encrypted backup runner

The runner and disabled private-repository workflow are implemented. Permit version
3 combines reported usage with version-2 reviewed billing baselines. Scheduled backup
acceptance is incomplete: the owner-side deployment/usage evidence
service, prepared real reconstruction inputs and scoped runtime secrets are not
installed. No owner-wide, service, signing or recovery credential
belongs in automation. Approved restricted read-only metadata credentials belong
only in the future separate collector, never either export/readback job.

## Runtime and isolation

`daily-backup.mjs upload daily-job.public.json` uses `SOURCE_CAPABILITY`,
`BACKUP_RESERVATION_TOKEN`, `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY` from the upload identity. It captures
through the existing fixed HTTP client, downloads snapshot-bound airport objects,
encrypts with the existing recipient format and uploads ciphertext. Only ciphertext
is staged on disk; curl credentials go through stdin, never process arguments or a
saved configuration. Curl receives a minimal environment without the source token.

`daily-backup.mjs readback daily-job.public.json` is a separate job with the existing
R2 read-only identity and `BACKUP_READBACK_REQUEST` from the upload job output.
It receives neither `SOURCE_CAPABILITY` nor `BACKUP_RESERVATION_TOKEN`. The CLI binds the request to GitHub's
`GITHUB_REPOSITORY`, `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT` and `GITHUB_SHA`.
The request expires after 30 minutes. Only an exact size/SHA-256 readback prints a
`status: verified` receipt to the successful job log and summary. It cannot decrypt
the archive and does not claim restoration or Google sign-in.

Trust that receipt only with the authenticated successful readback job from this
private repository at its reviewed workflow commit/run/attempt. A copied JSON blob
or an uploader's pending request is not independent proof. A later freshness monitor
must validate that GitHub run/job/log provenance and independently inspect R2;
monitoring/alert delivery is not connected here. The internal policy and durable
ledger are documented in [backup monitoring](hosted-backup-monitor.md); live
independent observations and scheduled delivery acceptance remain open. The separate
disabled watchdog accepts private completion heartbeats and keeps its own alert
ledger; it shares Cloudflare outage risk. It has no schedule or public heartbeat
endpoint. Primary/observer delivery shares are1/1 daily and16/15 monthly, preserving
the combined ceiling. Neither sender can reset counters during rotation. The job stores no artifacts or
caches and puts no unsigned success marker in R2.

## Disabled workflow

`tools/hosted-backup/daily-workflow.json` is valid JSON-form YAML. It is copied to
`.github/workflows/daily-backup.yml` only in the separate private repository pinned
in that workflow and `daily-backup.mjs`. It has manual dispatch only, no cron/push/
PR trigger, and requires both the expected private repository and
`vars.BACKUP_ENABLED == 'true'`. Keep that variable unset or `false` during setup.
Both jobs use standard Ubuntu 24.04 runners with five-minute timeouts, read-only
repository permissions, checkout credentials disabled and no caches/artifacts.
Checkout v7.0.1 and setup-node v7.1.0 were verified through their official release/
tag APIs on 18 October 2026 and pinned by full commit ID; Node 24 is already required.

The six future Actions secrets are `SOURCE_CAPABILITY`, `BACKUP_RESERVATION_TOKEN`,
`R2_UPLOAD_ACCESS_KEY_ID`, `R2_UPLOAD_SECRET_ACCESS_KEY`,
`R2_READBACK_ACCESS_KEY_ID`, `R2_READBACK_SECRET_ACCESS_KEY`.
The last pair is the independently scoped read-only identity. Each job receives
only its own pair. Provision through GitHub's encrypted secrets facility after
live permission tests, never through command arguments, git, logs or transfer files.

## Prepared public inputs

`daily-job.public.json` has exactly `version: 1`, `repository`, `accountId`, `bucket`,
`permitUrl`, `deploymentRevisions` and `source`. Destination identities are fixed to
the reviewed repository/bucket pinned in the implementation. `permitUrl` must be an owner-reviewed HTTPS Workers
endpoint ending in `/daily-backup-permit`, with no query/fragment/credentials or
redirect. It is configured public input, not a URL supplied by a workflow caller.
TLS authenticates this evidence endpoint; its contents are not self-authenticating
when copied elsewhere.

`source` is the existing logical-client request **without** `capability`:
`origin`, `publishableKey`, public `recipient`, `contract`, base64 `sources`, and
`sourceManifest`. Use the established owner-side reconstruction inventory, including
required dirty/untracked files. The manifest must cover exactly the source names;
every byte hash is checked before network access. Include the separately encrypted
`deployment-sources.elbbackup`. `deploymentRevisions` pins the actual two deployed
functions by `{slug, version, updated_at}`, captured outside the routine job using
the established supervised owner boundary. No owner controller runs inside Actions.

## Fresh evidence contract — deployment prerequisite

Before source export, after capture and before independent readback, the runner
GETs `permitUrl` with redirects disabled and a 16 KiB response ceiling. Required
JSON fields are:

- `version: 3`, `checkedAt`, `expiresAt`, `jobSha256` (SHA-256 of
  `JSON.stringify(parsedJob)`), `deploymentObservedAt`, `deploymentRevisions`.
- `github`: `observedAt`, `plan: "free"`, `queryStart`, `queryEnd` and
  `actionsMinutes` entries containing supported `sku`, nonnegative `grossQuantity`
  and provider-reported `netAmount: 0`. The query must cover the reviewed period
  start through `observedAt`; gross quantities are not normalized remaining minutes.
- `supabase`: `observedAt`, `plan: "free"`.
- `ownerReview`: the approved review object below. Live `github.remainingLinuxMinutes`,
  `github.paidActionsBlocked` and live `supabase.egress`/`cachedEgress` are rejected: unsupported readings
  must not be fabricated as fresh evidence.
- `r2`: `observedAt`, `accountId`, `bucket`, `privateStandardBucket: true`,
  `policySha256` (SHA-256 of the parsed policy serialized with `JSON.stringify`),
  `currentBytes`, `sampledPeakBytes`, `newestStorageMeasurementAt`, `pendingUploads: 0`,
  `infrequentAccessBytes: 0`, and exactly two ordered `windows`, each
  `{from, until, requests}`. The first spans the reviewed R2 period start through
  `observedAt`; the second spans the 31 days ending at `observedAt`. Both are
  account-wide, not bucket-filtered, and must include any required range splits.
  Exact billing allowance fields `storageBytes`, `classA` and `classB` are rejected.
  The policy still covers the exact 30-day lock, expiry and multipart cleanup.

Both receipt and each underlying observation must be less than two hours old,
not future-dated, with a future expiry no more than two hours away. The independent
producer must inspect the supported actual provider state and effective deployed revisions.
Do not re-date old screenshots, substitute nominal free-plan limits for observed
usage, or publish a permanently successful constant response. Every unavailable
supported metric must make the endpoint refuse a permit. Its implementation and
approved restricted read-only provider authentication are still required before
live activation. No management credential may be added to either workflow job.

`daily-cost-policy.mjs` shares validation between controller and runner. Reported
account bytes are the greater of current total and summed sampled peaks. Adding
the next 50 MiB object must stay strictly below 2 GiB. Either request window must
stay below 10,000 total requests. The validator returns warning codes
`r2-storage` at 1 GiB and `r2-operations` at 5,000 requests; email delivery and
deduplication still require the monitoring implementation. Pending multipart or
Infrequent Access usage denies. These observations can lag or be sampled: the
version-3 contract targets expected zero cost, not exact remaining allowances or
a hard R2 spending cap. The budget of 20 planned Actions minutes per permitted
attempt is covered by the starting review; rejected job starts/other account work
remain outside that reservation. Keep the account-wide paid Actions block.

## Owner review and atomic source budget

`ownerReview` has `version: 2`, `approvedAt`, `expiresAt`, `jobSha256`, `sourceOrigin`,
`sourceExpiresAt`, `weeklyCheckedAt`,
`githubPaidActionsBlocked: true`, `supabasePlan: "free"`, and `egress`/`cachedEgress`
with `{used, included}` baseline all-project byte readings. It covers at most 31
days, cannot start in the future, and matches the prepared job/source. It expires
no later than the source capability or either reviewed billing-period end. Each baseline
allowance must cover 31 × 64 MiB (1,984 MiB, about 2.08 decimal GB). This is not a
live shared-quota guarantee: other app use may exhaust it. Missing/expired review
stops both jobs. Additional historical supervised baselines are:

- `github`: `observedAt`, `periodStart`, `periodEnd`, `remainingLinuxMinutes`
  (integer at least 640: 31 planned attempts times 20 minutes plus 20 spare).
- `r2`: `observedAt`, `periodStart`, `periodEnd`, `storageGbMonths` (at most 1),
  `requests` (integer below 10,000 total across classes), and
  `storageBillableGbMonths`, `classABillable`, `classBBillable`, `usageCostUsd`, all zero.

Baseline observations must be inside their periods and no more than 24 hours
before `approvedAt`. They remain historical after approval; never re-date them on
collection. `weeklyCheckedAt` must be on/after approval, not future, and less than
seven days old. Hash `ownerReviewBudget(review)`, which excludes only this weekly
acknowledgement, for grant identity. Updating a weekly check does not renew approval,
baseline freshness, expiry or the 31-attempt budget, and does not invalidate an
otherwise valid readback. Changing the actual budget review during capture denies.
Before the first source grant under a new policy/budget, the controller additionally
requires both billing baselines to have been inspected within the preceding
24 hours. A recent weekly acknowledgement cannot activate an old baseline.

After preflight and before any R2 request, upload POSTs version 2 and the
job/source/run/attempt/commit identity to `/daily-backup-reservation` on the same
service. Authentication is the
random 32-byte hex `x-flightlogx-reservation-token`; its only authority is consuming
the day's allowance. The service holds only `BACKUP_RESERVATION_SHA256` and the
owner-controlled `OWNER_REVIEW_JSON`. An optional local review copy is
`%LOCALAPPDATA%/ElectronicLogbook/HostedBackup/daily-review.local.json`; transfer it
without refreshing dates. A real token/review must be provisioned separately.

`daily-controls-worker.mjs` exports the handler/class; `daily-controls.mjs` shares
validation/counter logic with the runner. The SQLite-backed `DAILY_ATTEMPTS` binding
uses stable object name `flightlogx-preview-source`. One transaction records
the last UTC day, budget-review hash/version, attempt count, reservation digest and readback-use
flag. Parallel callers get one reservation;
review/token/job-input rotation cannot buy a second attempt today. A 31-day period
can span 32 UTC dates, so a separate 31-attempt ceiling is enforced.
When upgrading a ledger without a budget version, carry its attempt count for a
review approved no later than its last reserved UTC day. A genuinely fresh reviewed
baseline approved on a later day can renew the per-review budget; the last reserved
day is never cleared. Weekly acknowledgement alone cannot do this.

The transaction commits before the matching version-2 receipt is returned. Both R2
inventory and source reading begin only after receipt validation. Failed inventory,
capture/upload, lost receipt or an interrupted job consumes that day's attempt;
no automatic refunds/retries. The
existing client bounds snapshot to 32 MiB plus at most 12 MiB reference input,
within 64 MiB reserved daily. Readback receives the run-bound receipt and cannot
reserve another attempt. Existing archive/retention limits are unchanged.

The reservation includes a random 32-byte `readbackToken`, scoped only to claiming
its single readback. Store only the full receipt's SHA-256 in the ledger; pass the
receipt through the private job output, never URLs/logs/evidence/local transfer.
Before downloading, readback POSTs version 2, its job/source/run identity and that
exact receipt to `/daily-backup-readback`. It receives neither source capability
nor the reservation secret. A transaction checks the issued digest, identity,
unchanged review and 30-minute expiry, marks the grant consumed, then returns a
digest-bound claim. The client validates the claim before GET. Concurrent/repeated
claims, failed downloads and lost/malformed replies cannot buy another download.
Verified receipts omit `readbackToken` before logging. A later day's source grant
supersedes the previous pending readback; it cannot reset the source attempt count.

Both grant requests/receipts use protocol version 2; older clients/receipts deny.
An existing ledger without the new grant fields remains authoritative for its day
and attempt count, but cannot authorize an unissued readback. Deploy the runner and
service together while disabled; preserve namespace/object identity, migration tag
and ledger. Source authentication also runs at the Worker boundary before forwarding
to the Durable Object. The readback route validates its receipt inside the object;
invalid traffic can still consume Workers Free capacity, not trigger R2 requests.

Before **each** workflow R2 attempt the runner now POSTs its version-2 caller and
full private reservation, plus an `operation`, to `/daily-backup-request`. The
controller atomically charges the independent `component-requests` ledger and
marks that operation spent in `source-budget` before returning a digest-bound
claim. The ordered upload operations are `list-before`, `list-after`, `upload`;
`readback` requires both the upload operation and consumed readback grant. Each
operation is one-use even if its reply or subsequent I/O fails. Unknown, altered,
expired, out-of-order and duplicate claims deny. No outbound R2 call precedes its
validated claim. The daily workflow ceiling is eight attempts; the current pair
normally consumes four. This ceiling and the future monitor ceiling of twelve do
not reset on review/token/job rotation. Losing a claim never grants a retry.

`daily-request-controls.mjs` also provides the internal collector cache/limits:
21 starts/day, at least an hour apart, with 11 Cloudflare reads per collection and
231 attempted collector Cloudflare calls/day. Together with workflow8/monitor12,
this is at most251 cooperating Cloudflare/R2 attempts/day, below256. GitHub and
Supabase have separate42/day bounds. No public collection/refresh route exists;
cache-only reads deny missing/stale/changed/failed-refresh evidence. See
`hosted-backup-provider-collector.md`. Preserve `provider-cache`,
`component-requests` and `source-budget` in the same namespace/object. Synchronize
runner/controller/imported collector/cache sources in the private repository while
disabled before any deployment; its older client is not deployment-ready.

`daily-controls.wrangler.json` uses installed Wrangler 4.147.0 and its bundled local
runtime, a SQLite namespace and compatibility date 2026-10-05. No Worker/namespace
was deployed. Verify Workers Free plan/headroom before activation; do not assume
pricing or install a paid plan. Preserve the class/namespace and fixed object identity
on updates and machine transfer. Never reset or roll back its ledger to retry.

The actual local Cloudflare integration test grants one of 16 concurrent source
calls and one of 16 readback claims, then denies both after runtime restart. It stops the owned helper tree and
verifies its port closes. Synthetic SQLite under `%TEMP%/elb-daily-controls-*` is
retained regenerated evidence, excluded from transfer. It contains no participant
data or production credentials. Retain failed-cleanup fixtures for local inspection;
never broaden cleanup beyond the owned temporary tree.

References: [transactional SQLite storage](https://developers.cloudflare.com/durable-objects/api/sqlite-storage-api/),
[Durable Objects Free limits](https://developers.cloudflare.com/durable-objects/platform/pricing/).

## Retention, failures and validation

Both complete bucket inventories are required to be untruncated. Count every object,
including manual archives and probes: at most 35 objects and 35 × 50 MiB total after
the new upload. A fixed `daily/YYYY-MM-DD.elbbackup` key permits one attempt that
creates an object per UTC day. Re-list after capture, send `If-None-Match: *` and
retain the existing bucket lock. Existing copies are never overwritten/deleted;
an uploaded orphan after a failed readback remains locked and needs explicit
inspection/reverification. Automatic retries cannot accumulate replacement archives.
This runner never changes bucket policy, retention or multipart settings.

```powershell
node --test tools/hosted-backup/daily-backup.test.mjs tools/hosted-backup/archive.test.mjs
node --test tools/hosted-backup/daily-controls.test.mjs tools/hosted-backup/daily-controls.integration.test.mjs
node --test tools/hosted-backup/daily-cost-policy.test.mjs
pwsh -NoProfile -File tools/Test-LocalDevelopmentTransfer.ps1
```

Synthetic tests execute production capture/encryption/readback and decrypt the test
archive with an independently held synthetic private key. R2 storage and provider
HTTP responses are controlled substitutes; curl configuration/boundaries are also
tested. They do not prove a live R2 request or hosted snapshot.
Real scoped denial, scheduled capture/upload/readback and existing-key restoration
remain deployment acceptance. Preserve current backup/USB files and retained phone.

Primary references: [R2 S3 compatibility and conditional PutObject](https://developers.cloudflare.com/r2/api/s3/api/),
[GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax),
[checkout releases](https://github.com/actions/checkout/releases/tag/v7.0.1) and
[setup-node releases](https://github.com/actions/setup-node/releases/tag/v7.1.0).
