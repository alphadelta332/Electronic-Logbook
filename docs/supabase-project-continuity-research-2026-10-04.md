# Supabase Project Pausing And Data-Continuity Research

Research date: 2026-10-04 (Australia/Sydney)

Scope: current first-party Supabase documentation relevant to the FlightLogX Preview
Free-plan pause warning. This note records platform facts, important unknowns, and the
practical continuity consequences. It contains no project URL, reference, credentials,
account identifiers, or user data.

## Executive Finding

The warning is a real production-availability risk, not evidence that the data has already
been lost. Supabase currently says that a Free project can be paused after insufficient
**user database activity over seven days**, even when the project is being used, because
low-volume use may still be below its unpublished threshold. A warning is normally sent
about one week before the pause. While paused, compute is stopped; the data is preserved
and the project can be resumed to its previous state.

For an actual shared live logbook, synthetic traffic is not a sound continuity control.
Supabase deliberately does not publish an exact qualifying threshold or promise that every
kind of request counts. Its documented way to prevent inactivity pausing altogether is to
move the organization to a paid plan. Paid projects cannot be paused for inactivity.

Upgrading to Pro solves this specific inactivity-pause failure mode, but it is not a full
disaster-recovery design. Pro supplies daily database backups with seven-day retention;
Point-in-Time Recovery (PITR) is a separately priced add-on and requires at least Small
compute. Independently maintained and restore-tested off-site exports are still needed.

Sources: [Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing),
[Production Checklist](https://supabase.com/docs/guides/deployment/going-into-prod), and
[Database Backups](https://supabase.com/docs/guides/platform/backups).

## What Supabase Currently Calls Inactive

Supabase's published test is qualitative:

- it reviews user database activity over the preceding week;
- projects with too few user queries are the clearest pause candidates;
- "a few" user database requests on each day of the previous week are *typically*
  sufficient; and
- after a warning, visiting the project in the Dashboard or generating sufficient API or
  connected-application requests can prevent that pending pause.

Supabase does **not** publish a fixed number of qualifying requests, a durable threshold,
or a contract that any particular low-volume request pattern will always prevent pausing.
Its documentation explicitly warns that a project may be actively used but still have too
little activity to avoid a pause.

[Source: Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing)

### Which activity can safely be treated as qualifying?

| Activity | What the official source supports | Safe operational conclusion |
| --- | --- | --- |
| User database queries, including normal application database requests | Explicitly identified as the activity considered | Qualifying, but no exact threshold is guaranteed |
| Project/API calls or connected-app requests that exercise the database | Explicitly suggested after a warning | Can prevent a pending pause if "sufficient"; not a long-term guarantee |
| Opening the project in Supabase Dashboard | Explicitly suggested after a warning | Can generate activity, but still requires a human and is not a production control |
| Auth-only requests or token refreshes | Auth uses the project's Postgres database, but the pausing policy does not promise that Auth-only traffic qualifies | Unknown; do not rely on it |
| Storage-only requests | Storage metadata uses the project database, but the pausing policy does not promise that Storage-only traffic qualifies | Unknown; do not rely on it |
| Realtime heartbeats, generic HTTP/network traffic, health checks, Edge Function invocations, or internal `pg_cron` jobs | Not identified as qualifying user database activity | Do not rely on them |

The Auth and Storage architecture facts do not turn those request classes into a pausing
guarantee. The platform docs show that Auth and Storage are database-backed, but the
pausing page still defines inactivity in terms of sufficient *user database activity*.

Sources: [Auth architecture](https://supabase.com/docs/guides/auth/architecture),
[Platform permissions](https://supabase.com/docs/guides/platform/permissions), and
[Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing).

### Synthetic keepalive

Supabase does say that sufficient API calls can stop a pending pause, so an external task
that performs a genuine database query may happen to keep a Free project active. However,
the official documentation does not define or endorse a synthetic keepalive as a durable
production guarantee. The threshold is deliberately non-numeric and can still classify a
low-traffic but genuinely used project as inactive. An internal scheduled query is even
less defensible because the policy refers to user activity.

Therefore:

- a keepalive can be a temporary belt-and-braces warning signal, not the primary control;
- it must never write dummy business data merely to manufacture activity;
- success must not be equated with backup, availability, or recoverability; and
- it cannot meet a requirement that the live service must never be paused. The paid plan
  is the documented control for that requirement.

## Warning, Pause, Restore, Expiry, And Deletion

Supabase currently documents this sequence:

1. A Free project has insufficient activity over a seven-day window.
2. The owner receives a warning roughly one week before pausing.
3. If activity remains insufficient, the project is paused and a confirmation email is
   sent.
4. The paused project retains its data and configuration. The owner can resume it from
   the Dashboard and it returns to its previous state.
5. Self-service in-place restore is available for up to **one year** after the pause.
6. After more than one year, the same project can no longer be restored in Studio. Before
   it is deleted, Supabase currently offers download of a database backup and Storage
   objects, which must be migrated into a newly created and reconfigured project.
7. Once a project is deleted, the database, Auth data, Storage objects, backups,
   configuration and credentials are permanently removed and Supabase says they cannot be
   recovered.

The current official sources do **not** publish a fixed date on which a long-paused project
will automatically be deleted. They only require recovery material to be downloaded
"before the project is deleted." It would therefore be unsafe to turn the one-year restore
window into an assumed deletion timer or additional retention guarantee.

Pause and deletion are therefore materially different:

| State | Data | Normal service | Recovery |
| --- | --- | --- | --- |
| Paused, within one year | Preserved | Compute is stopped; do not expect the app's hosted operations to work | Resume the same project in Dashboard |
| Paused for more than one year, not yet deleted | Export artifacts are currently offered | Same project cannot be resumed in Studio | Download database/Storage and manually migrate/configure a new project |
| Deleted | Permanently removed, including backups | Project URL and credentials no longer work | No Supabase recovery |

Sources: [Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing),
[Restore a Project Paused for More Than 1 Year](https://supabase.com/docs/guides/troubleshooting/restore-project-after-90-days-pause),
and [Deleting Your Project](https://supabase.com/docs/guides/platform/delete-project).

### The email's 90-day statement conflicts with the current docs

The received email says in-place unpausing is available for 90 days. As of this research,
the current pausing page says one year, and the official troubleshooting page is titled
"How To Restore a Project Paused for More Than 1 Year" and was last edited 2026-10-02.
Its URL still contains the legacy phrase `after-90-days-pause`, suggesting a changed policy
or stale template/slug. The conservative operational response is not to plan around either
grace period: prevent the pause and keep independent backups. If the precise legal promise
matters, obtain written confirmation from Supabase Support referencing the specific project
and warning email.

## Paid-Plan Guarantees And Limits

- Supabase says paid-plan projects cannot be paused and are not subject to automatic
  inactivity pausing. Billing is organization-wide: projects in a Pro organization receive
  the paid-plan benefits.
- Supabase's production checklist uses stronger language: upgrading to Pro guarantees the
  project will not be paused **for inactivity**.
- This is not a general uptime guarantee. Supabase's public 99.9% Platform Uptime SLA is
  explicitly for Enterprise customers covered by an Order Form, not ordinary Pro projects.
- Pro gives access to support and seven days of daily database backups. A daily backup can
  lose up to approximately one day of changes depending on incident timing.

Sources: [Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing),
[Billing](https://supabase.com/docs/guides/platform/billing-on-supabase),
[Production Checklist](https://supabase.com/docs/guides/deployment/going-into-prod),
[Database Backups](https://supabase.com/docs/guides/platform/backups), and
[Enterprise Platform Uptime SLA](https://supabase.com/sla).

## Backups And Recovery Options

### Free plan

The production checklist says downloadable routine database backups are not available on
Free. The backup guide specifically recommends that Free projects regularly run
`supabase db dump` and retain off-site backups. This is different from the special download
offered for a long-paused project and should be the operational continuity mechanism.

A logical backup should use the documented separate role, schema, and data exports. It
must be stored somewhere independent of both Supabase and the phones, encrypted, access
controlled, and periodically restored into a disposable environment to prove usability.

Sources: [Database Backups](https://supabase.com/docs/guides/platform/backups) and
[CLI Backup and Restore](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore).

### Pro daily backups

- generated automatically each day;
- last seven days accessible for Pro (14 days Team; up to 30 days Enterprise);
- restoration makes the project inaccessible for the duration of the restore; and
- database backups do not contain the actual objects stored through the Storage API, only
  their database metadata. Storage objects require a separate backup.

Physical backups also may not be directly downloadable; Supabase directs users who need a
downloadable logical artifact to the CLI dump/`pg_dump` route.

[Source: Database Backups](https://supabase.com/docs/guides/platform/backups)

### PITR

PITR is an add-on for Pro, Team, and Enterprise, requires at least Small compute, and
replaces daily backups while enabled. It supports second-level restore selection within a
configured 7-, 14-, or 28-day retention window. The current published monthly prices are
approximately USD 100, 200, and 400 respectively, in addition to the paid plan and required
compute; prices should be rechecked before purchase. Restore still causes downtime.

Supabase's feature page describes a worst-case PITR recovery-point objective of about two
minutes. That is a recovery objective, not a no-data-loss promise or high-availability
failover.

Sources: [Database Backups](https://supabase.com/docs/guides/platform/backups),
[PITR usage and pricing](https://supabase.com/docs/guides/platform/manage-your-usage/point-in-time-recovery),
and [Database backup feature](https://supabase.com/features/database-backups).

### Restore into another Supabase project

Paid projects with physical backups can use the beta "Restore to a new project" feature.
It copies the database, Auth users, roles, permissions, and encryption root key. It does
not copy Storage objects/settings, Edge Functions, Auth settings/API keys, Realtime
settings, all extension settings, or read replicas; those require reconfiguration. A
logical restore also does not carry the encryption root key automatically.

[Source: Restore to a new project](https://supabase.com/docs/guides/platform/clone-project)

### Migrate or self-host

Supabase documents CLI logical dump/restore both for moving between Supabase projects and
for restoring into self-hosted Supabase. The dump includes schema, data, roles, RLS,
database functions, triggers, and Auth users. It does not make a self-hosted replacement
drop-in: JWT/API keys, OAuth/Auth provider settings, Edge Functions, Storage objects, SMTP,
domains/DNS, and other platform configuration must be rebuilt, and users need to sign in
again because tokens change.

Self-hosting also transfers operational responsibility for security patching, monitoring,
availability, capacity, backups, and disaster recovery to the operator. The local Supabase
CLI development stack is explicitly not production-ready.

Sources: [Platform-to-self-hosted restore](https://supabase.com/docs/guides/self-hosting/restore-from-platform)
and [Self-hosting responsibilities](https://supabase.com/docs/guides/self-hosting).

## Continuity Conclusions For FlightLogX

1. **Upgrade the live Preview organization to Pro before continuing to treat it as a live
   shared logbook backend.** That is the only first-party control documented to eliminate
   inactivity pausing. A dashboard visit or keepalive is not equivalent.
2. **Do not call the platform backup the application's disaster-recovery plan.** Maintain
   encrypted, off-site logical database exports plus any separate Storage/configuration/
   Edge Function material, and automate both backup verification and restore rehearsal.
3. **A pause cannot be made invisible solely by Supabase.** If it occurs, the hosted API is
   unavailable until resume. Any no-intervention user experience must be supplied by the
   application's offline-first queue/retry behavior and must be tested against a real
   backend outage.
4. **Permanent project loss cannot be healed automatically without an independent copy and
   a prebuilt failover/migration mechanism.** A new project has a new URL/keys and requires
   service configuration. The present Supabase restoration paths are operator workflows,
   not transparent application failover.
5. **Alerting is detection, not prevention.** Supabase's Management API can report an
   inactive project and supports a `v1.project.paused` webhook event, but alerts cannot
   guarantee delivery before a pause and do not remove the need for Pro and backups.

Sources: [Management API project status](https://supabase.com/docs/reference/api/v1-list-all-projects),
[Management webhook event](https://supabase.com/docs/reference/api/v2-projects-ref-webhooks-endpoints-post),
and the sources cited above.
