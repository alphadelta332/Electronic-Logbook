# Daily encrypted backup runner

The runner and disabled private-repository workflow are implemented. Permit version
2 combines read-only monitoring with periodic owner reviews. Scheduled backup
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
monitoring/alert delivery is not implemented here. The job stores no artifacts or
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

- `version: 2`, `checkedAt`, `expiresAt`, `jobSha256` (SHA-256 of
  `JSON.stringify(parsedJob)`), `deploymentObservedAt`, `deploymentRevisions`.
- `github`: `observedAt`, `remainingLinuxMinutes`.
- `supabase`: `observedAt`, `plan: "free"`.
- `ownerReview`: the approved review object below. Live `github.paidActionsBlocked`
  and live `supabase.egress`/`cachedEgress` fields are rejected: unsupported readings
  must not be fabricated as fresh evidence.
- `r2`: `observedAt`, `accountId`, `bucket`, `privateStandardBucket: true`,
  `policySha256` (SHA-256 of the parsed policy serialized with `JSON.stringify`),
  and `storageBytes`, `classA`, `classB`, each `{used, included}` for the **whole
  account/current allowance period**, not just the backup bucket. The policy covers
  the exact existing whole-bucket 30-day lock, expiry and multipart cleanup.

Both receipt and each underlying observation must be less than two hours old,
not future-dated, with a future expiry no more than two hours away. The independent
producer must inspect the supported actual provider state and effective deployed revisions.
Do not re-date old screenshots, substitute nominal free-plan limits for observed
usage, or publish a permanently successful constant response. Every unavailable
supported metric must make the endpoint refuse a permit. Its implementation and
approved restricted read-only provider authentication are still required before
live activation. No management credential may be added to either workflow job.

Stops reserve at least 20 standard Linux runner minutes, one 50 MiB R2 object and
100 R2 Class A/B operations. The owner review supplies the baseline Supabase budget
below. Observations
may lag; these are conservative headroom checks, not provider spending caps.
Keep the account-wide paid Actions block in the private approved review; its
evidence is not a live API observation. Refresh supported allowance telemetry
before enabling and during operation.

## Owner review and atomic source budget

`ownerReview` has `version: 1`, `approvedAt`, `expiresAt`, `jobSha256`, `sourceOrigin`,
`githubPaidActionsBlocked: true`, `supabasePlan: "free"`, and `egress`/`cachedEgress`
with `{used, included}` baseline all-project byte readings. It covers at most 31
days, cannot start in the future, and matches the prepared job/source. Each baseline
allowance must cover 31 × 64 MiB (1,984 MiB, about 2.08 decimal GB). This is not a
live shared-quota guarantee: other app use may exhaust it. Missing/expired review
stops both jobs. The review stays unchanged through capture and readback.

After preflight and initial inventory, upload POSTs job/source/run/attempt/commit
identity to `/daily-backup-reservation` on the same service. Authentication is the
random 32-byte hex `x-flightlogx-reservation-token`; its only authority is consuming
the day's allowance. The service holds only `BACKUP_RESERVATION_SHA256` and the
owner-controlled `OWNER_REVIEW_JSON`. An optional local review copy is
`%LOCALAPPDATA%/ElectronicLogbook/HostedBackup/daily-review.local.json`; transfer it
without refreshing dates. A real token/review must be provisioned separately.

`daily-controls-worker.mjs` exports the handler/class; `daily-controls.mjs` shares
validation/counter logic with the runner. The SQLite-backed `DAILY_ATTEMPTS` binding
uses stable object name `flightlogx-preview-source`. One transaction records only
the last UTC day, review hash and attempt count. Parallel callers get one reservation;
review/token/job-input rotation cannot buy a second attempt today. A 31-day period
can span 32 UTC dates, so a separate 31-attempt ceiling is enforced.

The transaction commits before the matching receipt is returned. Source reading
begins only after receipt validation. Failed capture/upload, lost receipt or an
interrupted job consumes that day's attempt; no automatic refunds/retries. The
existing client bounds snapshot to 32 MiB plus at most 12 MiB reference input,
within 64 MiB reserved daily. Readback receives the run-bound receipt and cannot
reserve another attempt. Existing archive/retention limits are unchanged.

`daily-controls.wrangler.json` uses installed Wrangler 4.147.0 and its bundled local
runtime, a SQLite namespace and compatibility date 2026-10-05. No Worker/namespace
was deployed. Verify Workers Free plan/headroom before activation; do not assume
pricing or install a paid plan. Preserve the class/namespace and fixed object identity
on updates and machine transfer. Never reset or roll back its ledger to retry.

The actual local Cloudflare integration test grants one of 16 concurrent calls,
then denies another after runtime restart. It stops the owned helper tree and
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
