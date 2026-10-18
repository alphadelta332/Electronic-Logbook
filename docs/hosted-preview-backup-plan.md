# Independent Preview backup and recovery contract

Status as of 18 October 2026: supervised native and logical encrypted exports,
isolated 47-table restoration, offline-key application replay and native-archive
R2 readback have been verified. **Independent automatic backups are not enabled.**
The daily runner and provider collector have focused tests, but the deployed permit
service, scheduled capture/readback, independent alerts and real Google/fresh-service
recovery acceptance remain incomplete. Keepalive activity is not a backup.

This document defines the recovery requirements and design decisions. Commands and
archive formats are in [backup tooling](hosted-backup-tools.md); the exact logical
dataset and target mapping are in the
[compatibility contract](hosted-backup-preview-logical-contract.md).

## Required recovery scope

| Data or dependency | Required result |
| --- | --- |
| Accounts, logbooks and memberships | Preserve original UUIDs, Google identity bindings, ownership, invitations and deletion/access state. Fresh sign-in must find the original logbook. |
| Operations and configuration | Preserve every encrypted revision, parent link, correction, conflict, deletion, custom-field definition and currency setting. A spreadsheet export is insufficient. |
| Devices, acknowledgements and migration receipts | Preserve author references, revocation and completed migration state. Device private keys remain on their devices. |
| Wrapped key envelopes | Preserve required versions and revocation state; retain matching recovery material independently. Google sign-in does not derive a logbook key. |
| Security and pairing records | Preserve required history without reactivating expired requests or disabled devices. |
| Schema and managed services | Reconstruct compatible Auth/Storage and application schemas, migrations, generated values, sequences, constraints and access protections. Explicitly justify managed/transient exclusions. |
| Reference objects | Preserve the required airport manifest, seed and current snapshot bytes and verify their hashes. Database Storage metadata does not contain object bytes. Unexpected buckets or objects require review. |
| Deployment and configuration | Preserve reviewed migration/Edge sources, actual deployed bundles and reconstruction metadata. Recreate Google, SMTP, service credentials and client targeting separately. Keep outbound jobs disabled during restoration. |

The logical contract conservatively requires all 47 declared tables, including empty
ones. Schema/migration drift, missing sources or incompatible target definitions
must stop the operation rather than produce a partial backup. SQL metadata and row
replay alone do not establish a complete managed-service recovery.

## Export and storage boundaries

The supervised native path uses owner credentials only on the trusted Windows PC.
Its restricted direct-database counterpart remains blocked by managed-schema reads
and effective PUBLIC privileges. Neither broader administrator credentials in a job
nor a read-only connection default repairs that permission boundary.

The selected logical path uses a fixed, parameterless, GET/read-only HTTP capability.
Its external caller receives only an opaque export token and publishable API key.
The owner controller audits the complete exposed role/function surface and installs
or removes the endpoint. Database, management, service, signing and recovery keys
remain outside the export job. The source holds table locks before one aggregate
snapshot; sequences require their separate non-MVCC checks.

The [metadata guard](hosted-backup-metadata-guard-contract.md) and
[hybrid restore fixture](hosted-backup-hybrid-restore-fixture.md) remain synthetic
experiments. Endpoint metadata equality cannot exclude arbitrary intervening DDL;
the baseline/COPY candidate is not approved for unattended Preview use.

Encrypt the entire archive before upload using the existing recipient format.
Routine jobs receive only the public recipient. Use private R2 Standard storage,
a whole-bucket 30-day lock and lifecycle, separate bucket-scoped upload/readback
identities and unique objects. Count manual archives and probes in the same limits:
at most 35 retained objects, each at most 50 MiB, within the aggregate byte budget.
Never remove a locked good copy to make room or overwrite an existing archive.

Use a separate private automation repository. Its reviewed repository identity is
pinned in the implementation/workflow; configuring another deployment requires
reviewing those pins together. Do not change this public repository's default or
release branch to enable scheduling. The runner's current attempts, receipts and
owner-review rules are specified in [daily runner](hosted-backup-daily-runner.md).

## Cost, monitoring and freshness

Zero expected recurring cost is a deployment condition, not a spending guarantee.
Verify account-wide storage/operation headroom, included runner allowance, paid
Actions blocking and alert-service allowance before activation. Missing, stale or
unsupported observations must block a permit. Use dedicated restricted metadata
credentials in the [provider collector](hosted-backup-provider-collector.md), outside
the export/readback jobs; never substitute broader owner credentials.

The current design combines fresh supported provider observations with an approved
review lasting at most 31 days for paid Actions blocking and baseline all-project
Supabase egress. Reserve 31 daily source budgets of 64 MiB, enforce one attempted
capture per UTC day and a 31-attempt ceiling atomically, and stop on expired review.
Other application use can still exhaust shared allowances. Provider telemetry may
lag; application controls and budget alerts do not establish a hard spending cap.
No paid plan or expected overage is authorised by this contract.

Independent monitoring must authenticate successful readback history and inspect
stored objects. Check freshness every four hours, alert on job failure or more than
30 hours without a verified backup, and test absent-run alerts independently of
the backup scheduler. Use configured operational email with bounded retries and
duplicate suppression. Recipient addresses and delivery receipts remain private.
Daily frequency targets a 24-hour loss window while functioning; unsynced phone
changes and corruption discovered after retention expires remain outside that bound.

## Recovery custody and acceptance

Keep the protected backup private identity, every required KEK/version, recovery
service material and service-reconstruction instructions on independent trusted
storage. Retain a second protected copy and an unlock method separate from the
medium. Never replace a KEK and call it recovery of existing envelopes. Offline
recovery sets and user recovery codes stay outside development transfer and jobs.

Acceptance requires a synthetic end-to-end drill and real-archive restoration:

1. Download from the destination, match a digest retained in trusted external job
   history, authenticate and decrypt using the independent recovery copy. Wrong
   keys, truncation and corruption must fail before target creation.
2. Restore only to an identified empty disposable target with matched service
   versions and outbound work disabled. Compare all declared datasets, typed row
   digests, sequences, migrations, reference hashes and protection settings.
3. Recover the original account/logbook, exact entries and history, totals and
   settings. Prove real Google sign-in and managed-key recovery; a headless replay
   is insufficient. Verify cross-account denial, revoked-device denial, missing-key
   failures and a disposable post-restore edit/sync.
4. Demonstrate a scheduled capture/upload/readback, independent failed/absent-run
   alerts, retained-copy protection and expiry. Record timings and backup age
   privately; repeat the synthetic full drill monthly and after relevant changes.

Preserve the retained phone and existing PC/offline/R2 copies throughout. Replacing
a production project or redirecting installed clients requires a separate recovery
decision. Test success is limited to what the drill actually exercises.

Provider references: [PostgreSQL snapshot export](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION),
[Supabase restoration](https://supabase.com/docs/guides/self-hosting/restore-from-platform),
[R2 retention locks](https://developers.cloudflare.com/r2/buckets/bucket-locks/),
[R2 metrics](https://developers.cloudflare.com/r2/platform/metrics-analytics/),
[GitHub budget controls](https://docs.github.com/en/billing/concepts/budgets-and-alerts).
Recheck provider behaviour and current account settings before deployment.
