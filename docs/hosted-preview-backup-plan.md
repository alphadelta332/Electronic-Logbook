# Independent Preview backup and restoration plan

Prepared 2026-10-17 (Australia/Sydney). Status: inventory verified; owner choices
recorded, with a conditional zero-expected-cost deployment gate. **No independent backup or successful database restoration
has been established.** The existing keepalive is not a backup.

## Evidence and scope

This batch covers the first subtask in the independent-backup TODO: inventory,
dependencies, a minimum unattended design, and measurable disposable restoration.
It does not provision storage, grant database access, install dependencies, change
branches, export participant rows, or touch the retained Pixel.

Live read-only Management API checks at 03:14 UTC verified the intended Sydney
Preview is `ACTIVE_HEALTHY`, running PostgreSQL `17.6.1.155`. Catalog inspection
verified these 11 public tables, all with RLS enabled. Their total relation footprint
is 2,056,192 bytes, including indexes; this is **not an archive-size measurement**.
Planner row estimates were inspected but are not treated as exact participant counts.
Redacted evidence: `artifacts/hosted-backup-plan-20261017/inventory.json` (ignored).
No participant row values, emails, IDs, ciphertext, or secret values were saved.

### Measured encrypted archive and R2 headroom

On the owner's subsequent request, a real local encrypted sizing export was made
at 03:28 UTC on 2026-10-17. That request authorized this read-only export of Preview
data; the earlier inventory alone did not include participant rows. Its measured
size is **993,601 bytes**. Thirty equal-sized daily copies total **29,808,030 bytes**
(29.8 MB); 35 copies including spare/drill capacity total **34,776,035 bytes**.
Using a conservative decimal 10,000,000,000-byte interpretation of R2's 10 GB-month
Standard allowance, those steady retained sets use **0.2981%** and **0.3478%** of
the free storage. Actual billing averages daily peak storage over the billing period;
these projections assume a stable retained set and no other R2 account consumption.

At 30 copies, an archive would need to reach about **333 MB**, or **335 times this
sample's size**, to exhaust the storage allowance by itself. With 35 copies the
corresponding threshold is about **286 MB per archive**, roughly **288 times** this
sample. These are capacity ratios, not forecasts of when the logbook will grow.
There is no measured growth rate from which to predict a calendar date for billing.

| R2 Standard dimension | Monthly free allowance | Proposed workload assessment |
| --- | --- | --- |
| Storage | 10 GB-month | About 0.030 GB at 30 retained copies; 0.035 GB at 35 copies. |
| Class A writes/list operations | 1,000,000 | Budget fewer than 1,000 for daily archive/marker uploads, listing and monitoring. That is below 0.1% of the allowance; actual calls must be instrumented. |
| Class B reads | 10,000,000 | Budget fewer than 1,000 for readback, monitoring and occasional restoration. That is below 0.01%; retries must remain bounded. |
| Internet egress | Free | Downloading the archive has no R2 egress fee. Other connected services have their own billing. |

The size assessment now supports **zero expected R2 charges at the present scale**,
subject to verifying other account usage and correct Standard-class configuration.
It does not establish a hard provider spending cap. Storage overage is listed at
USD 0.015/GB-month; Class A/B overages are USD 4.50/0.36 per million, with provider
unit rounding. [R2 pricing](https://developers.cloudflare.com/r2/pricing/)

The sample includes a PostgreSQL custom schema/data dump for `public`, `auth`,
`storage` and `supabase_migrations`; all 11 application tables; Auth users and
identities; roles without passwords; public airport manifest/current snapshot;
migration and Edge Function source; and repository Supabase configuration. The
custom database dump itself is 1,052,291 bytes before the outer packaging. PostgreSQL
compression plus outer gzip and authenticated encryption yields the reported size.
No plaintext export was written to disk or printed.

This is a **sizing archive**, not an accepted production backup format. It uses
fresh AES-256-GCM key/nonce material wrapped to the existing recovery ingress RSA
public key using OAEP-SHA256, so it remains readable with that existing private key.
No raw logbook keys, KEK, ingress private key, Vault secrets or API credentials were
exported. Managed Auth state is included conservatively; production exclusions and
format choices can change the final size. Unknown Storage files and separate service
configuration are not claimed as covered. SQL restoration/application recovery remain
untested. Public reference objects, roles and sources were sampled separately from
the consistent `pg_dump` data snapshot.

Checks passed: verified TLS CA/hostname using Supabase's public root certificate,
expected dump table coverage, encrypted-file decryption and original dump/roles digest
comparison, plus synthetic corruption/truncation rejection. The 6.32-second local run
is an observed sizing-export time, not a GitHub runner benchmark. The archive and
public CA material are generated local output under the already-excluded
`%LOCALAPPDATA%\ElectronicLogbook\Evidence\BackupMeasurement\20261017\` tree; they are
not a new secret/config prerequisite or the independent offline recovery copy.
Only redacted metrics are in
`artifacts/hosted-backup-plan-20261017/measured-size.json`; the local sizing helper is
beside that file. No R2 subscription, bucket, upload, email or scheduled job was made.

Use this margin for practical advance warnings: a provisional account-wide warning
at 1 GB and upload stop at 2 GB would both be well below 10 GB, while the present
35-copy set is only about 35 MB. Check operation allowances independently. These are
proposed application controls, not deployed controls or a guarantee against other
account users/provider changes. Live R2 usage remains unverified with the current
credentials. Billing protection and the private runner's allowance still need setup
verification before activation.

| Data/dependency | Backup or reconstruction requirement |
| --- | --- |
| `accounts`, `logbooks`, `logbook_memberships` | Preserve original identities, invitation/onboarding mode, ownership, access and deletion state. `accounts.account_id` references `auth.users.id`. |
| Supabase Auth | Preserve account UUIDs and Google identity bindings, plus any persistent authentication dependencies. A public-schema-only export cannot satisfy the account foreign key or prove Google recovery. Sessions need not survive a disaster; require fresh sign-in. Confirm exact Auth table coverage against the pinned CLI and target Auth version before export. |
| `devices`, `operation_acks` | Preserve author/device references, revocation and acknowledgement state. Android Keystore private keys are not in the database and must not be extracted. |
| `operations` | Preserve every encrypted revision, including edits/deletions/conflicts, parent links, original identifiers, nonces, tags and hashes. A materialized spreadsheet is not this ledger. |
| `configuration_revisions` | Preserve encrypted custom-field/currency configuration independently of flight operations. |
| `key_envelopes` | Preserve encrypted managed, recovery-code and device envelopes, their versions, expiry and revocation. These wrapped envelopes are necessary; no raw logbook key belongs in a backup. |
| `workbook_migrations` | Preserve completed receipts and lifecycle state so recovery does not suggest an empty replacement or require repeat migration. |
| `pairing_requests`, `security_events` | Preserve references and security history; never reactivate expired requests. Review sensitive metadata inside the outer encrypted archive. |
| Schema, roles, migrations | Preserve enums, functions, triggers, policies, grants, indexes, sequences and migration history. Include the reviewed source revision and live schema inventory; do not assume a recorded migration proves its SQL is deployed. |
| Storage | Current catalogue feature uses the public `airport-catalog` bucket. Database metadata does not contain its file bytes. Preserve/recreate the published version, hash and reference file separately; inventory all buckets before excluding any objects. Unexpected private buckets stop implementation for review. |
| Vault, cron, extensions | `supabase_vault`, `pg_cron`, `pg_net`, `citext`, `pgcrypto` and other managed extensions are deployed. The airport job depends on Vault-held URL/service credentials. Do not copy encrypted Vault rows and assume their provider key transfers. Recreate approved jobs with new target credentials; keep jobs disabled throughout restoration tests. |
| Services and client configuration | Recreate Edge Functions, Auth/Google and SMTP configuration, API keys, URLs and Android/Windows client configuration. A new project URL is not a transparent replacement for the installed app. Repointing production clients is a later recovery decision. |

Table structure and dependencies were checked against the live catalog and
`supabase/migrations/20260806000000_hosted_pilot_foundation.sql`,
`20260828010000_workbook_migration_lifecycle.sql`,
`20260829000000_encrypted_configuration_revisions.sql`, and
`20261017000000_shared_airport_catalog.sql`. See also
[the hosted runbook](hosted-preview-supabase.md) and
[the recovery threat model](account-recovery-threat-model.md).

## Key dependencies and existing tools

Google sign-in proves identity; it does not derive the logbook key. Managed recovery
needs the matching encrypted envelope and the server KEK. The recovery service uses
`RECOVERY_KEK_BASE64`, `RECOVERY_KEY_VERSION_ID` and the ingress RSA pair; see
`supabase/functions/recovery-envelope/index.ts` (`importManagedKey`,
`managedAdditionalData`, `importIngressPrivateKey`). Never generate a replacement
KEK and call it recovery of existing envelopes.

The existing Preview secret alias is
`%LOCALAPPDATA%\ElectronicLogbook\Supabase\recovery-envelope\private-pilot.env`.
Read-only checks confirmed it exists, its RSA public/private pair matches, its KEK
decodes to 32 bytes and its version is present. The local project metadata contains
a database password. These checks **do not prove** the credentials connect or the
saved KEK matches every live envelope/version. That proof remains part of recovery
validation; secret values must never enter evidence.

Existing tools to reuse:

- `create_hosted_logical_export_manifest`: owner-only count/highest-revision report;
  neither a data export nor a complete restore check. It omits configuration and key
  completeness checks and must not be presented as a backup.
- `tools/Invoke-HostedRecoveryRehearsal.ps1` and
  `supabase/tests/HostedRecoveryRehearsal`: disposable account/key/operation recovery
  checks. They currently target development and create/clean up their own fixture;
  they do not restore an external archive. Adapt carefully rather than point the
  existing cleanup workflow at restored participant data. Its wrapper currently
  expects email-only Auth, so it does not prove Google recovery.
- `tools/Test-RecoveryEnvelopeCryptoHarness.ps1` and
  `supabase/tests/hosted_preview_rls.sql`: crypto and authorization checks.
- `tools/Invoke-LocalDevelopmentTransfer.ps1`: verifies recovery secret structure
  and moves classified private configuration through trusted storage. It is not an
  automated database backup and its device snapshots are deliberately excluded.

No scheduled database export/off-site restoration implementation was found under
the repository's tools, workflows or Supabase sources. Existing local snapshots,
manual packages, installed phone state and provider keepalive do not establish this
backup contract. No existing external backup account was verified in this batch.

## Minimum unattended design proposed

1. Run one daily job at an off-hour minute, provisionally `43 16 * * *` UTC. Use a
   pinned GitHub-hosted runner/toolchain, a timeout and one-at-a-time concurrency.
   Keep its scheduler independent of the Supabase project and the owner's PC.
   Recommend a separate private automation repository: this repo is public and its
   default branch is `main`. A workflow on `dev` alone will not run on schedule.
   The owner selected a separate private repo, subject to verifying its free minutes,
   budget controls and source access before setup;
   changing this repo's default/release branch is not authorized by this plan.
2. Use a dedicated database export identity restricted to the required reads, via
   the session pooler with verified TLS. Do not place the general Management token,
   service-role key, KEK or ingress private key in the routine job. Prove the exact
   role can export every required object; do not silently fall back to admin access.
3. Export roles/schema and one coherent data snapshot using Supabase's documented
   logical-backup procedure, inspected with the pinned CLI's `--dry-run`. Inspect
   the actual selected schemas/tables: default schema-only output is insufficient.
   Avoid separate REST page/table copies. Compute counts, ordered row digests and
   revision manifests from that same snapshot, or the restored snapshot; never compare
   it to separately sampled live counts. Reject a schema/migration change during the
   export and retry. Explicitly exclude Vault secrets and transient worker/net data;
   inventory Auth/session/MFA and managed-schema treatment before fixing exclusions.
4. Package SQL, the encrypted-row manifest, migration/source versions and required
   public reference objects. Encrypt the **entire** archive before upload. The first
   implementation uses installed Node cryptographic primitives (RSA-OAEP-SHA256 and
   AES-256-GCM), avoiding the initially proposed additional `age` dependency. This also protects
   plaintext account metadata, sign-in state and schema details. The routine job
   receives only the public recipient, not the private decryption identity. Stream
   where practical; any temporary unencrypted SQL stays on the ephemeral runner,
   outside caches/logs/artifacts, and is cleaned up even on failure.
5. Upload a unique dated object to private R2 Standard storage, with public domains
   and `r2.dev` disabled. Use a bucket-scoped object credential, separate from the
   owner/admin identity. Verify upload completion, encrypted size and SHA-256 by
   readback. Never replace a previous good copy after partial failure or publish a
   success marker before verification. Markers contain only time, size, digest and
   tool/format versions; participant manifests stay inside the encrypted archive.
6. Propose a 30-day bucket lock plus lifecycle expiry after 30 days. The upload
   credential must not alter lock/lifecycle settings. R2 object read/write tokens
   are not inherently append-only; the lock supplies the deletion/overwrite barrier.
   Admins can remove locks. This covers a lost Supabase project and a compromised
   upload credential, not loss of the entire Cloudflare account.
7. Have an independent Cloudflare scheduled monitor check the latest completed
   backup every four hours. Alert after 30 hours without a verified object and on
   job failure, using email to the owner through the existing verified domain and
   Resend (recipient kept only in private configuration). Give it only read
   access and no decryption capability. Do not put backup credentials in the existing
   anonymous keepalive Worker. Test both failed-run and absent-run alerts; the latter
   cannot depend on GitHub executing a second workflow. No notifications are sent
   until sender access and the selected recipient are configured and verified. The
   owner authorized this notification channel and its setup/test; do not ask again.

Daily frequency targets at most 24 hours of hosted changes lost while jobs operate;
it is not a guaranteed bound. Propose a measured four-hour restoration target once
credentials and a target are available, then revise it to the observed rehearsal.
Unsynced phone-only changes are outside a hosted backup. Thirty-day retention cannot
recover corruption first discovered after every good copy expires.

Provider behaviour, limitations and primary citations are in
[the provider research](hosted-backup-provider-research.md). In particular, R2's
Oceania location hint **does not guarantee Australian residency**. Storage/billing
activation, free allowances, private Actions minutes and notification costs require
verification; no unconditional free-service claim is made.

## Separate owner recovery copy

Before the first backup is called recoverable, create and verify a protected offline
recovery set on trusted storage kept away from this PC. It must contain the backup
private identity, every KEK/version needed by retained archives, the existing recovery
service secret file, and enough configuration/access instructions to rebuild Auth,
Edge Functions and client targeting. Review Google/SMTP/admin recovery separately;
do not assume an excluded downloaded OAuth file or an expired login session suffices.

Keep a second protected copy and test reading/decrypting from the independent copy.
Keep its unlock method accessible after PC loss and separate from the same medium.
Never upload raw recovery secrets, raw logbook keys or owner recovery codes to the
backup bucket, GitHub, diagnostics or evidence. Existing encrypted key-envelope rows
remain inside the outer encrypted database archive. This is a separate owner setup
action; no recovery set was created in this batch.

If implementation adds an export identity, environment variables or a new
secret location, update `tools/local-development-transfer.psd1`,
`tools/Invoke-LocalDevelopmentTransfer.ps1`, its focused tests and
`LOCAL_DEVICE_SETUP_HANDOVER.md` together. Classify public configuration as local
transfer, credentials requiring reauthentication as fresh authentication, generated
archives as output, and the offline private recovery set as a deliberate exclusion
with its own protected handling. No new local prerequisite is introduced by this plan.

## Disposable restoration and acceptance

First build a disposable fixture with two isolated accounts/logbooks, create/edit/
delete operations, settings/custom fields, migration receipts, managed envelopes,
revoked devices and denied cross-account access. Export it using the production backup
path. Download that archive from the real destination and decrypt using the independent
owner recovery copy on a trusted restore machine, not a plaintext runner artifact.

Restore only to a positively identified empty disposable local stack or separate
Sydney project. Deny the live Preview/development project identities as restore
targets; reject nonempty targets and verify database fingerprints before SQL writes.
Disable cron, webhooks, email delivery and other outbound jobs before importing data.
Use matched PostgreSQL/Auth versions and the documented roles/schema/data restore
order, error-on-first-failure and trigger handling. Restore the original protection
settings and grants afterward; disabling RLS/triggers permanently is a failed test.

The evidence must pass all of these checks:

- Uploaded/downloaded encrypted bytes and digest match; truncated/corrupt archives
  and a wrong decryption identity fail closed without reporting success.
- All expected table counts, primary keys, foreign keys, ordered row digests, operation
  and configuration revision boundaries match the snapshot. Schema, policies, grants,
  functions, triggers, migrations and reference-file hash match the intended versions.
- Managed recovery from fresh disposable storage restores the same logbook/key,
  exact entries, edit/delete/conflict history, totals, custom fields and currency
  configuration, plus the completed migration receipt. Google account sign-in finds
  its original UUID/membership in the disposable target; no empty replacement is
  created. A headless crypto replay alone does not prove Google sign-in.
- Missing/wrong KEK, missing/corrupt envelope, wrong account and unauthorized logbook
  access all fail closed. Revoked/disabled devices and expired pairing requests stay
  blocked. A disposable post-restore edit uploads and recovers correctly.
- A failed upload does not remove an earlier good copy; early delete/overwrite is
  rejected by retention; expired-object lifecycle and independent stale alerts are
  demonstrated without weakening the live bucket's retention rule.
- Record backup age, archive bytes, export time, download/decrypt/restore time and
  application verification time. Evidence uses synthetic assertions and redacted
  summaries, not flight records, identities, raw keys or ciphertext.

Then restore one real Preview archive into an isolated trusted target. Compare
encrypted row digests/identity relationships privately, with no participant mail,
production client activation or phone use. With explicit owner agreement for that
rehearsal, prove decryption/replay against expected owner data in protected memory
and save only pass/fail results. The synthetic drill establishes logic; it does not
prove the real Preview KEK/envelopes or actual archive are recoverable. Existing
recovery rehearsals are not substitutes for this real uploaded-archive check.

Initial acceptance requires both drills, a successful scheduled backup rather than
only manual dispatch, an alert-delivery test and an observed retained copy. Repeat
the synthetic full drill monthly and after schema/crypto/backup-tool changes. Do not
run destructive phone tests or redirect installed clients. Full production project
replacement/client cutover remains a separate owner-controlled recovery action.

## Owner choices and deployment gates

The owner answered the planning questions on 2026-10-17:

1. Private R2 with the proposed 30-day retention is conditionally acceptable. Providing
   billing details is acceptable only if charges are not expected and the owner is
   notified if payment becomes necessary. This is not approval for a paid plan or
   unexpected overages. Do not describe conditional acceptance as verified billing
   protection or an Australian residency guarantee.
2. Include the one-time protected offline recovery copy. Select the actual trusted
   medium and verify the independent copy during recovery setup; no copy exists yet.
3. Use a separate private GitHub automation repository; check free minutes first.
   Do not change this repository's `main` or default branch.
4. Use the owner-provided email for alerts. Reuse the existing domain and Resend for
   rare operational messages, with delivery testing and duplicate suppression. Keep
   the address in ignored local/private runtime configuration, never in this document,
   tracked examples, uploaded logs or redacted evidence. Do not repurpose sign-in
   secrets or change existing authentication email behaviour.

Expected recurring cost is zero **as a design target**, not a verified account bill.
The public relation footprint alone is about 2 MB, but actual encrypted archive size
including Auth/reference objects and other account usage still need measurement.
For a conservative implementation limit, allow at most one successful archive per
UTC day, at most 35 retained archives (including drills), and at most 50 MiB per
archive. That ceiling is 1,835,008,000 bytes before packaging/reference overhead;
all included objects must count toward the same byte budget. It is comfortably below
R2's documented 10 GB-month allowance only if sufficient account-wide headroom remains.
Fail the run and alert rather than increase limits, remove locked good copies or
silently purchase capacity. A manual run must not bypass the limits. These guards
are requirements for implementation, not controls already in operation.

Before enabling billing or scheduling uploads:

- Measure a disposable archive and restore-run duration, then verify all R2 account
  storage/operation headroom, private Actions minutes and Resend plan/usage. Keep
  Actions paid usage blocked, use standard Linux runners with bounded execution,
  and avoid billable artifacts/caches. A full 31-day month with a 10-minute daily job
  would use at most 310 runner minutes before extra tests/retries; these also count.
- Add early warnings and stop new uploads well before free allowances are exhausted,
  counting other account consumers and retained/drill objects. Missing/stale usage
  telemetry must block uploads. Verify sender domain, email delivery and suppression;
  leave paid email overages and plan upgrades disabled. Never let alert retry loops
  consume unbounded requests/emails.
- Cloudflare's documented budget alerts fire when cumulative spend has crossed the
  configured dollar threshold, and do not cap usage. They are a supplementary alarm,
  **not a promise of warning before the first charge**. If the actual account controls
  cannot meet the owner's billing condition, leave metered activation disabled and
  present that specific remaining limitation before changing providers or accepting
  payment risk. Do not imply a provider spending cap exists.

Read-only account checks in this batch could not verify these settings: existing
Cloudflare OAuth returned HTTP 403/code 10000 for R2 and subscriptions, the existing
Resend credential returned HTTP 401 for domain listing, and GitHub account-plan/usage
lookups did not expose usable billing data. These results do not prove a service is
inactive, a sending credential cannot send, or an allowance is exhausted. Billing
dashboard access and sender credentials remain setup dependencies. No message was
sent, subscription activated, account/budget changed or repository created.

Primary billing and email sources: [Cloudflare budget alerts](https://developers.cloudflare.com/billing/manage/budget-alerts/),
[R2 pricing](https://developers.cloudflare.com/r2/pricing/),
[GitHub budgets and alerts](https://docs.github.com/en/billing/concepts/budgets-and-alerts),
[GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions),
and [Resend pricing](https://resend.com/pricing).

The next engineering batch is the backup/export/encryption and
synthetic restore tooling with focused failure tests and portability updates, tested
against disposable data. Provisioning and live export follow that reviewable result.
Only after actual archive restoration and unattended-operation checks pass may the
parent TODO say the Preview has independently protected data.

## First implementation batch

The owner explicitly approved R2 after the measured-size assessment. Local archive
construction, read-only database export and authenticated in-memory readback are now
implemented; [tool usage and exact scope](hosted-backup-tools.md) record the format
and limits. Five focused tests pass, including a randomly named disposable database
export; transfer checks pass 108 assertions. Node/PostgreSQL were reused, with no new
dependency. The transfer manifest, verification, tests and setup guide were updated
together for `pg_dump`, `pg_dumpall` and `pg_restore`.

The refreshed Cloudflare login first reached the R2 API with code 10042; the owner
then completed activation. A fresh R2 list now returns HTTP 200 and zero buckets.
The agent has no supported browser surface here. Private repository, notifications,
completed offline identity/copy and full restoration acceptance are still outstanding.
The selected recovery destination was
verified as a USB-connected volume; its encryption status could not be read without
administrator rights. Do not create unprotected recovery material there. The exact
owner path stays in private TODO/configuration, not this tracked plan.

Disposable SQL restoration and shared application replay now pass. All rows in 15
fixture tables match after restoration. Production operation/settings ciphers and
materialization recover six revisions, corrected/resolved history, a deletion, exact
2.75 command hours, custom fields and currency override. Negative checks reject
wrong digest/private key/KEK/account, missing identity/envelope/settings/history,
corruption and remote targets. RLS/policy definitions survive; cluster roles and ACLs
are not applied to the shared local server, so this is not full authorization or
Google/managed-service acceptance. The initial native restore failed because the
fresh database already had `public`; the corrected path removes only that new empty
default schema without CASCADE before the transactional restore. Identified failed
disposable test databases were cleaned up.

Six focused Node checks and 113 transfer assertions pass; the shared .NET replay
harness builds without warnings. Protected recovery setup tooling is also ready and
its synthetic roundtrip/overwrite/wrong-password checks pass. The owner must enter
the recovery password in the local console; it is never requested in chat or saved
beside the USB. Physical escrow is not established until the written files and
verification receipt are checked after that step. No R2 bucket/upload/schedule exists
yet. Redacted evidence: `artifacts/hosted-backup-plan-20261017/batch2.json`.
