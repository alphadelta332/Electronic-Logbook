# Internal backup monitoring policy

`tools/hosted-backup/daily-monitor.mjs` implements a local, tested policy and durable
attempt ledger. `daily-monitor-observation.mjs` adds internal GitHub-history and
R2-byte inspection adapters, tested with synthetic provider responses and the local
Worker runtime. `daily-alert-delivery.mjs` connects reserved alert attempts to the
existing restricted Resend sender. `daily-watchdog*` supplies the separate disabled
Worker, receiver-timed heartbeat and durable missing-monitor observer. It is **not
an accepted operational monitor**. Both Workers are deployed; the first native
scheduled acceptance on22October2026 failed before its first state checkpoint.
Both monitoring schedules/switches were stopped afterward; backup automation
remains disabled. Current acceptance state is recorded in local TODO/evidence.
Synthetic observations do not establish protection.

Internal checks emit `backup-monitor-checkpoint` JSON before and after execution;
the private heartbeat receiver emits one after saving its receiver time. Inspect
these through authenticated real-time tails on each existing Worker. The fixed
allowlist contains only UTC days, timestamps, counters and boolean completion/cache/
source flags. It excludes conditions, incident IDs, hashes, configuration, provider
responses, addresses, credentials and records. Reads use a storage transaction and
never change ledgers or invoke providers. Failed checks also emit their persisted
post-check counters when the runtime permits; CPU termination may prevent that log.
An absent completion log is not success. Disabled checks emit nothing. No public
inspection/trigger route, new service, persistent log sink or credential is added.
Keep `observability.enabled=false`; authenticated live tails do not require storing
logs. Capture the bounded acceptance session privately, and verify remote source
and namespace identities before trusting those events. Logging alone is not proof
of inbox delivery, restored data or runtime CPU headroom.

The locally tested collector also emits one separate `backup-provider-failure`
event for the first error that rejects an admitted refresh. Its only fields are `version: 1`, `event`,
`adapter`, `stage` and `status`. Adapters are `collector`, `github`, `supabase`,
`cloudflare` or `cache`; stages are `credentials`, `request`, `http`, `body`,
`json`, `pagination`, `validation`, `lease` or `allowance`. Status is an integer
HTTP code from 100 through 599 when observed at the failed request stage, otherwise
null. Validation failures do not imply a failed HTTP status. Error provenance
is held privately in memory; arbitrary thrown objects cannot supply log fields.
No error message, URL, configured identity, response body or header is logged.
This event leaves the checkpoint schema, durable keys, counters, cache invalidation,
alerts and heartbeats unchanged. Success and refresh admission denials emit no
failure event. Logging errors cannot replace the collection error or skip cleanup.
This addition still requires disabled deployment and native acceptance; runtime
termination may prevent emission, so a missing event does not prove success.

Private POST transport validates zero bytes by reading the stream, rather than
requiring a null body. Empty streams are valid; actual payload bytes reject before
state access and are cancelled without buffering. The primary internal check and
the observer's check/heartbeat share this validation. Public routes are unchanged.

Worker HTTP adapters use `redirect: manual` and require the expected success status;
redirect responses reject without forwarding credentials. Workers does not support
the Fetch standard's `error` mode: it throws before sending a request. Isolated
Worker tests construct a native `Request` before supplying synthetic HTTP replies,
so mocks cannot silently accept unsupported runtime options. The Node-only backup
runner retains its supported `error` mode.

`daily-primary.mjs` connects the primary scheduled handler to the existing stable
`DAILY_ATTEMPTS` object. `MONITOR_ENABLED=false` and no Cron Triggers keep dispatch
disabled without provider, namespace, email or heartbeat I/O. Only the internal
object route accepts checks; the public router rejects monitoring/refresh routes.
The four-hour monitor lease is reserved before collection. A matching fresh cache
is reused; otherwise the existing hourly collector attempts a refresh, preserving
all counters and invalidating failed evidence. Original provider timestamps feed
the version-3 cost policy alongside the unchanged historical owner review.
Monitoring evaluates usage warnings/stops internally. The separate GET permit route
uses the same policy on cached observations, gated by Worker BACKUP_ENABLED=false.
Public reads never refresh providers, reserve source grants or start monitoring.
Changed source, account, job hash, reviewed R2 period or deployment inputs deny.
Invalid collection/configuration becomes evidence-unavailable; failed cost policy
becomes usage-stopped. History and stored ciphertext use the existing individually
charged adapters. Accepted checks call the private heartbeat; failed/suppressed
unaccepted delivery cannot forge completion or refund attempts.

Remote `PRIMARY_MONITOR_JSON` contains reviewed `provider` collector configuration
and `history` adapter configuration. Secret `PROVIDER_CREDENTIALS_JSON` contains
the existing dedicated `github`, `cloudflare`, `supabaseProject` and
`supabaseOrganization` monitoring tokens. Secret `MONITOR_CREDENTIALS_JSON` holds
the separate Actions-read `github` token and read-only `r2` accessKeyId/secretAccessKey.
Reuse existing `OWNER_REVIEW_JSON`, `ALERT_DELIVERY_JSON`, `RESEND_SEND_TOKEN` and
`MONITOR_HEARTBEAT`. Provision these remote bindings through fresh authentication;
no new local file, credential scope, runtime or operational state namespace is added.
Staging/reconstruction include the composition and regression sources. Synthetic
local runtime checks cannot establish live provider acceptance or protection.

The internal `observeBackup` adapter makes two authenticated GitHub reads: latest
workflow run and latest successful run. It pins the workflow ID/path, private source
repository/head, reviewed commit, branch and prepared job SHA-256. It then checks
the exact attempt's two successful upload/readback jobs and downloads only the
readback job's timestamped version-2 receipt. A skipped job, pending uploader claim,
wrong run/attempt/commit, changed source, missing/duplicate receipt, inconsistent
time or leaked capability denies. Receipt verification time is retained unchanged.
Successful-run logs being absent/expired/inaccessible is unavailable evidence,
never a success or an empty history. The API's empty run list alone establishes
absence. A newer failed run is retained independently of the older good backup.

Job-log downloads use GitHub's one-minute signed redirect. Only HTTPS Azure Blob
or the exact GitHub results-receiver host is accepted; userinfo/ports/another
redirect deny. The signed request receives no GitHub token. JSON/log reads are
bounded to 256 KiB, timed out, without hidden retries. GitHub origins/paths are
constructed internally, never taken from an unauthenticated observation.

The R2 adapter uses GET-only SigV4 and the existing read-only monitoring S3 identity,
fixed account origin/private bucket and exact `daily/YYYY-MM-DD.elbbackup` receipt
key. No list/write/redirect/retry is available. It reads at most the expected bytes
(maximum 50 MiB), rejects status/length/digest mismatch and hashes the actual stored
ciphertext with SHA-256. Only a match supplies `lastVerified`; this verifies the
stored bytes against an authenticated readback receipt, not fresh decryption or
Google/account recovery. No public ingestion route exists. Call through
`checkBackupMonitor` and its supplied `r2`/`github` allowances; calling the adapters
outside that internal composition does not create a durable allowance.

Reviewed internal configuration needs `workflowId`, `workflowPath`, `branch`,
`commit`, `jobSha256` and `accountId`. There is no new local input file or live
configuration yet. The `usage` argument must come from the existing reviewed
permit/cache policy, never an external caller. Missing/invalid usage evidence
must be converted to the monitor's evidence-unavailable condition by that caller.
The adapter does not authorize backups or collect provider billing observations.

The policy reports backup age strictly greater than 30 hours, absent runs or a
last run older than 26 hours, failed/cancelled/timed-out runs and running jobs older
than one hour. Usage warnings/stops, invalid/expired reviews, review expiry within
48 hours and weekly acknowledgement at least six days old also require attention.
Unknown/malformed/future evidence denies; observations must be under ten minutes old.
Collection errors produce a generic evidence-unavailable condition with no raw
provider errors, account details, tokens, addresses or record data in alert text.

Keep `monitor-state` in the existing `DAILY_ATTEMPTS` SQLite namespace and stable
`flightlogx-preview-source` object alongside `source-budget`, `component-requests`
and `provider-cache`. Do not reset any of them during review, configuration or
credential rotation. At most six checks start per UTC day, at least four hours
apart. Five-minute leases prevent late publication and delivery reservation.
Every R2 outbound attempt must pass individually through the supplied `r2`
callback, including any retry: at most two per check and twelve per UTC day,
charged transactionally before I/O. The adapter must enforce the approved read-only
bucket/object boundary; this generic internal callback does not sign or restrict URLs.
No R2 call or source-export grant is required merely to report missing-run evidence.
The internal adapter uses one R2 read for a verified receipt. GitHub API/log
downloads also pass individually through the monitor's `github` callback: at most
five/check and thirty/UTC day, including failed requests and the signed log download.
The persistent `component-requests.counts.monitorGithub` is independent of the
collector's GitHub allowance. It survives review/token/config rotation with the
same namespace/object and needs no new durable key. No public refresh is exposed.

Delivery attempts are reserved durably before I/O. The primary monitor has one
attempt per UTC day and16 per UTC calendar month; the separate observer has one/day
and15/month. Combined ceilings remain two/day and31/month, with two attempts per
unchanged incident for each sender. A condition-set change
starts a new incident without resetting daily/monthly counters; recovery clears
the active condition set. Accepted incidents are suppressed while unchanged.
Uncertain/failed replies get at most one further attempt within 24 hours of the
incident, with the same immutable body and idempotency key; there is no refund.
The daily split means retries require a new UTC day within that 24-hour window;
some failures cannot be retried. A send crossing UTC midnight is denied rather
than charged to the previous day/month. Rate limits can delay or suppress alerts.
The internal `sendBackupAlert` adapter sends
exactly that body/key with a timeout, no redirects and no hidden retries. API
acceptance is not inbox delivery. Suppression after exhausted attempts requires
an independent observer, not a success claim from this process.

`sendBackupAlert(storage, config, token, message)` only accepts the exact canonical
message for the active monitor incident and a previously reserved live attempt.
It transactionally spends that attempt again at the wire boundary: a duplicated
callback cannot issue another request. `monitor-state.resend` retains the incident,
SHA-256 of the exact outgoing body, and last spent attempt. Retrying after a lost
reply/key rotation retains the same body/idempotency key; changed recipient/body
rejects before I/O and cannot refund the monitor reservation. There is one fixed
POST endpoint (`https://api.resend.com/emails`), a ten-second timeout, a bounded
4 KiB response, no redirects/retries/batch/attachments/custom headers and no raw
provider diagnostics. Only a valid returned email UUID counts as API acceptance.

The sender is `FlightLogX backups <backups@auth.flightlogx.app>` using the existing
production domain-scoped sending-only key. Configuration is version 1 and one
owner-approved `recipient`; lists, headers, extra options and malformed addresses
reject. Optional local `alert-delivery.local.json` lives beside the existing backup
recipient/review files under LOCALAPPDATA/ElectronicLogbook/HostedBackup, classified
as private local-transfer state. Keep actual addresses out of tracked sources and
safe diagnostics; no raw key or recipient is stored in the attempt ledger. The
existing Resend key stays in its separate declared file. No new dependency or
environment variable. Do not expose this internal sender through an ingestion route.

For supervised delivery acceptance only, an isolated diagnostic ledger can seed
`deliveryTest:true` before the real policy reserves its first attempt. This labels
the subject `FlightLogX backup alert test` and explicitly states that backup
automation remains disabled. No deployed operational namespace is seeded/reset;
the real source policy has no public test switch. Keep the diagnostic ledger so
repeated local invocations cannot refund attempts. The separate observer's live
outage-alert acceptance remains required. Its ledger does not depend on an
unavailable primary Worker and its reserved delivery share cannot double the
approved total ceilings.

## Separate Cloudflare watchdog

`daily-watchdog-worker.mjs` has its own scheduled handler and `MISSING_MONITOR`
SQLite namespace, with stable object name `flightlogx-preview-monitor`. Preserve
both `monitor-state` and `monitor-heartbeat` through restart, deployment and
recipient/configuration/key rotation. Never reuse the primary namespace or derive
an object name from a review/token.

The controls Worker's `MONITOR_HEARTBEAT` service binding targets the watchdog's
named `MonitorHeartbeat` RPC entrypoint. The primary timer's local composition
passes `() => env.MONITOR_HEARTBEAT.completed()` as the sixth `checkBackupMonitor`
argument. Checks with failed delivery, or suppression of an incident never accepted,
do not publish heartbeats. Already-accepted incidents can continue heartbeats
without duplicate alerts. A failed heartbeat transport never refunds an alert.

The receiver writes its own time at most once per four hours. Ordinary HTTP
always returns404; no public heartbeat URL, caller time or shared token exists.
The observer checks only its own saved heartbeat. Absence or silence strictly
greater than six hours raises `missing-monitor`; malformed/future state raises
unavailable evidence. Four-hour checks normally notice that threshold by eight
hours after the last heartbeat, subject to scheduler delays. Only bounded Resend
delivery calls an external provider: no primary ledger, R2, GitHub, source grant or
provider-read credential is needed. Observer idempotency keys use `backup-watchdog/`;
the same canonical-body, lease and transactionally spent wire rules apply.

`daily-watchdog.wrangler.json` has `WATCHDOG_ENABLED=false`, no Cron Triggers,
workers.dev/preview URLs disabled and observability disabled. Future deployment
needs `RESEND_SEND_TOKEN` from the existing restricted sender and
`ALERT_DELIVERY_JSON` from the approved private recipient config. Deploy the
disabled watchdog before resolving the primary service binding. After live
acceptance, use a separate four-hour Cron Trigger on each Worker. No deployment,
namespace, secret or timer is created by these files. The primary timer's
provider-cache/usage/history composition requires live deployment acceptance.

Inspect operational counters before commissioning. Larger legacy primary counters
are retained; if its16/month share is already exceeded, wait until the next UTC
month before activating the observer. If its1/day share is exceeded, also wait
until the next UTC day. Never reset counters to manufacture headroom
or upload diagnostic SQLite as operational state. Reconstruction capture includes
all five new `daily-watchdog*` files through the existing dirty/untracked source
inventory; synchronize them into the private automation repository before deployment.

Both Workers share Cloudflare availability. A platform-wide outage can stop both
checks and alerts; this accepted limitation has no guaranteed one-day maximum.
No cross-provider availability, automatic backup retry or live protection is claimed.
RPC isolation follows [Cloudflare service bindings](https://developers.cloudflare.com/workers/runtime-apis/bindings/service-bindings/rpc/);
each timer uses its own [scheduled handler](https://developers.cloudflare.com/workers/runtime-apis/handlers/scheduled/).

Local tests cover isolated Worker/SQLite restart and secret rotation, named RPC,
ordinary HTTP denial, missing-primary alerts and shared ceilings using synthetic
Resend responses. They do not prove a live scheduled outage or inbox delivery.

Supervised delivery acceptance on 20 October 2026 sent one explicitly labelled
test through the real policy/sender after owner recipient confirmation. Resend
accepted it and the dashboard independently recorded Sent/Delivered at 08:59
Sydney. Retained diagnostic counters show one daily/monthly/incident/wire attempt;
reinvoking the helper returned the saved receipt before any send path. Owner
confirmed receipt in junk, marked the sender as not junk and accepted receipt.
Initial inbox placement did not pass; future inbox placement is unproved. No
additional test or DNS change was made. Safe evidence:
`artifacts/hosted-backup-automation-assessment-20261018/alert-delivery-live-20261020.json`.
This does not establish scheduled monitoring, an independent outage watcher or
backup activation. Keep the isolated ledger and operational counters intact.

Activation remains blocked on live scoped history/R2 validation, a truly
live missing-monitor acceptance, actual Resend Free plan
and shared usage headroom, verified sender domain, and owner-confirmed delivery.
Missing account/domain inspection access blocks activation; do not widen a
sending credential's scope or substitute owner authentication.
The supervised account inspection gate was resolved on 20 October 2026 through
the existing browser session and GitHub sign-in. Transactional Free showed
0/100 daily and 0/3,000 monthly usage, with transactional pay-as-you-go off and
disabled. Both `auth.flightlogx.app` and `auth-dev.flightlogx.app` were Verified.
These are current shared-account observations; recheck headroom before activation.
Existing `FlightLogX private pilot auth` has Sending access scoped to
`auth.flightlogx.app`. Its exact earlier domain-list request log reports
`restricted_api_key`, message: "This API key is restricted to only send emails".
HTTP 401 here enforces the intended sending-only scope; it does not prove that
the key is invalid or require granting domain-read access. Browser inspection
does not put owner/browser credentials into unattended automation. No new key,
permission expansion or email was sent. Actual send/inbox and independent
missing-monitor acceptance remain open. Evidence:
`artifacts/hosted-backup-automation-assessment-20261018/resend-dashboard-validation-20261020.json`
and `.md`. Do not ask the owner to transcribe these inspected facts again.
The saved Plan-read token cannot read the private repository. The separate
Actions-read token was owner-provisioned and validated read-only on 2026-10-20.
Preserve its intended fine-grained scope for only
`alphadelta332/flightlogx-preview-backups`, with Actions Read plus GitHub's required
Metadata Read, no account permissions or write access. Do not substitute the CLI
login or broaden the Plan token. Its declared local path is
`%LOCALAPPDATA%/ElectronicLogbook/HostedBackup/github-actions-read-token.txt`;
fresh authentication, excluded from development transfer. Live metadata/history/
exact-attempt reads succeeded; file-content and Actions secret-list reads both
returned 403. These are tested boundaries, not exhaustive scope/write-denial proof.
The only existing workflow run is the old skipped gate check, with no successful
readback or logs. Production history correctly returns no verified backup. Positive
signed-log/receipt acceptance must be checked against the first approved actual
daily run; do not enable backups merely to manufacture this proof.

The existing monitoring S3 identity downloaded the exact real 1,509,208-byte native
safety archive and matched the trusted SHA-256; the production GET-only signer
received 404 for a missing daily probe object and rejected it. No upload/restore
key substitution, write/delete/policy operation, fresh decryption or success receipt
was involved. Other-bucket and write denial were not destructively probed.
Evidence: `artifacts/hosted-backup-automation-assessment-20261018/`
`monitor-live-readonly-20261020.json` and its assert-based `.mjs` runner. Seven
GitHub/two R2 supervised reads were charged before I/O through the production
component limit helper into retained local SQLite preflight counters. This local
diagnostic accounting is separate from the undeployed operational namespace; do
not describe it as a running independent monitor or reset the remote durable keys.
Evidence/script/SQLite contain no authentication and are regenerated diagnostic
outputs under the existing excluded artifacts boundary. Retain the SQLite across
repeated preflight calls so requests are not refunded within the UTC day.

To reprovision the token after transfer or revocation:

1. Open [GitHub's fine-grained token form](https://github.com/settings/personal-access-tokens/new).
2. Name it `FlightLogX backup monitor`; choose resource owner `alphadelta332`.
   Expiration is the owner's choice, separate from the 31-day backup review.
3. Under Repository access, choose Only select repositories and select
   `flightlogx-preview-backups`.
4. Under Repository permissions, select Actions: Read-only. Keep Metadata:
   Read-only (required by GitHub) and leave every other permission unselected.
5. Generate the token. Keep it private; do not paste it into chat or evidence.
   Arrange hidden local capture before leaving the token page. Then run the scoped
   read-only live validation; token creation alone does not complete acceptance.

No runtime or dependencies are added. Tests and retained synthetic SQLite are regenerated
outputs, excluded from development transfer.

Provider references: [Resend API key scopes](https://resend.com/docs/api-reference/api-keys/create-api-key),
[Resend pricing](https://resend.com/pricing) and
[24-hour idempotency](https://resend.com/changelog/idempotency-keys).
Published allowances do not verify this account's plan or remaining balance.
History/download semantics and Actions Read scope:
[GitHub workflow jobs](https://docs.github.com/en/rest/actions/workflow-jobs),
[workflow runs](https://docs.github.com/en/rest/actions/workflow-runs),
[GitHub log-storage hosts](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
Object GET/signing surface: [R2 S3 compatibility](https://developers.cloudflare.com/r2/api/s3/api/).
