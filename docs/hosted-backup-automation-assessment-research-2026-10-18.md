# Restricted automatic backup: schema-baseline assessment

Research date: 2026-10-18. Read-only primary-source research; no hosted commands,
credentials, grants, participant exports, or deployment. This document assesses
the design proposed in [the earlier investigation](hosted-backup-managed-export-research-2026-10-17.md).

## Decision

**Durable automatic backup is not proved feasible under the present boundaries.**
Owner-captured native schema plus fixed-view COPY remains a candidate, not a
small implementation ready for scheduling. Its hard part is demonstrating that
the captured schema still reconstructs the exported data and effective access.
A column signature, migration number, or PostgreSQL version does not establish
that. This is an engineering assessment, not a claim of impossibility.

Retain the verified supervised encrypted copy. The disposable adversarial test below
has now proved concrete failures and one narrow lock remedy. The next bounded task
is a disposable metadata-guard prototype, before another exporter or scheduler.

## Established facts

Existing project evidence proves restricted owner-permission views and typed COPY
on a disposable database; ordinary restricted schema-only pg_dump failed its
managed-table lock. The supervised real archive and recovered-key application
replay are separately recorded in local TODO/evidence. None proves this proposed
automatic format, a live hosted view boundary, or a restored Google sign-in.

Ordinary views use their owner's base-table privilege/RLS context unless
`security_invoker` is enabled. Their functions still require the caller's
appropriate function privileges. Grant SELECT only, fix the exact projection, and
test the actual owner's complete-row access. View ownership does not manufacture
permissions that the owner lacks. `SELECT *` resolves columns at creation, so new
base columns can be silently omitted without an independent guard.
[PostgreSQL CREATE VIEW](https://www.postgresql.org/docs/17/sql-createview.html).

Native `COPY (SELECT ... FROM view) TO STDOUT` preserves PostgreSQL's type
serialization without a JSON-number intermediary. Prefer the already-tested text
contract with fixed encoding/date/interval settings; binary COPY has version/type
portability limits. Restore with explicit writable columns; generated columns
must be omitted. COPY invokes destination triggers and constraints, and must stop
on errors rather than skip rows.
[PostgreSQL COPY](https://www.postgresql.org/docs/17/sql-copy.html).

A schema-only custom archive can keep native reconstruction without a custom DDL
generator. Restore **pre-data, COPY data and sequence state, post-data**, preserving
native ordering within each schema section. Post-data includes indexes, triggers,
rules and many constraints; restoring the entire schema first can activate them
before COPY. Exact target ownership, RLS, managed-schema conflicts and disabled
external jobs still require a restore rehearsal.
[PostgreSQL pg_restore](https://www.postgresql.org/docs/17/app-pgrestore.html).

Proposed custody: the owner encrypts the reviewed native baseline and records its
digest plus expected metadata contract. The routine job copies that opaque blob
and encrypts new data to the existing public recipient; it never receives a
baseline/recovery private key. Decryption and complete restore drills remain
owner-operated. This is a design inference, not a newly deployed archive format.

## What a comprehensive guard actually has to cover

The following is a proposed acceptance scope, not an implemented fingerprint:

| Surface | Required comparison or explicit unsupported-object rejection |
| --- | --- |
| Relations | Complete table/view/materialized-view/foreign-table/partition inventory; ordered columns, types, typmods, nullability, defaults, generation/identity, inheritance, persistence, RLS/FORCE flags, relation options and ownership. |
| Behavior | Constraints including validation state; indexes and expressions/predicates; triggers/enabled states; rules/view definitions; policy commands, expressions, roles and permissiveness; function bodies, language, configuration, volatility, security and owner; domains/enums/composites/operators and other used types. |
| Permissions | Schema/object/column ACLs, default ACLs, PUBLIC access, role attributes, membership options, database/role settings and effective exporter permissions. Never export role passwords. |
| Dependencies | Used objects outside the four export schemas, extension membership/configuration tables, extension versions/namespaces, collations, sequence definitions/ownership, and required large objects. Unsupported/unseen object classes must reject the run. |
| Platform | Exact supported PostgreSQL/tool/reconstruction revision, available extension binaries, managed Auth/Storage schema migrations and service compatibility, public Storage object manifests, and required non-SQL deployment/configuration sources. |

These surfaces span PostgreSQL's catalogs rather than one column query. Policy
and function metadata are separate catalogs; extension metadata includes a version
and configuration-table list. A native schema dump filtered to particular schemas
does not automatically collect outside dependencies.
[Catalog overview](https://www.postgresql.org/docs/17/catalogs-overview.html),
[pg_policy](https://www.postgresql.org/docs/17/catalog-pg-policy.html),
[pg_proc](https://www.postgresql.org/docs/17/catalog-pg-proc.html),
[pg_extension](https://www.postgresql.org/docs/17/catalog-pg-extension.html),
[pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html).

**Visibility limit:** information_schema is privilege-filtered. `columns` omits
inaccessible columns; `table_constraints` requires ownership or more than SELECT.
An empty query result must not mean "no drift." Raw catalog access and every
required deparser must be tested with the restricted hosted identity. `pg_authid`
is restricted; `pg_roles` supplies public role metadata with passwords masked.
Do not request broader password-bearing catalog access to close this gap.
[columns](https://www.postgresql.org/docs/17/infoschema-columns.html),
[table_constraints](https://www.postgresql.org/docs/17/infoschema-table-constraints.html),
[pg_authid](https://www.postgresql.org/docs/17/catalog-pg-authid.html),
[pg_roles](https://www.postgresql.org/docs/17/view-pg-roles.html).

**Dependency limit:** pg_depend is not a complete runtime-dependency oracle:
string-body functions can refer to tables without a tracked dependency. Shared
ownership/ACL dependencies also live in pg_shdepend. Extension version equality
does not alone prove identical installed binaries or external configuration;
collation behavior depends on provider/version as well.
[Dependency tracking](https://www.postgresql.org/docs/17/ddl-depend.html),
[pg_shdepend](https://www.postgresql.org/docs/17/catalog-pg-shdepend.html),
[pg_collation](https://www.postgresql.org/docs/17/catalog-pg-collation.html).

Inference: a version-pinned raw-catalog digest can be smaller than a DDL generator,
but selecting every required catalog row and dependency is still bespoke backup
logic. Hashing all catalog rows indiscriminately introduces ordinary statistics,
physical identifiers and unrelated provider changes as false alarms. Filtering
those fields creates a maintained completeness obligation. Hash sensitive
definitions at source if needed; do not emit function bodies or options containing
credentials to routine logs/evidence. This is a possible contract to prove, not a
comprehensive guard that documentation supplies ready-made.

## Concurrency and sequence limits

One repeatable-read transaction gives the data calls a stable row snapshot.
Repeating the same catalog SELECT inside it is not a new observation of committed
DDL. Therefore a persistent-drift before/after check needs fresh observer snapshots
and must compare both to the reviewed baseline. Equal observations cannot rule
out a temporary A-to-B-to-A change between observations; this is an inference from
snapshot semantics, not a claim that every such change corrupts an export.
[Transaction isolation](https://www.postgresql.org/docs/17/transaction-iso.html).

TRUNCATE and table-rewriting ALTER can make an old-snapshot table appear empty
unless accessed before that DDL starts. The documented narrow remedy is
`LOCK TABLE backup_export.fixed_view IN ACCESS SHARE MODE` before the first SELECT
establishes the snapshot: view locking recurses into base relations, checking
their privileges as the view owner by default. The caller needs SELECT on the
view, not base-table privileges. PG17's LockViewRecurse implementation agrees.
Prove complete lock coverage under the restricted role; do not merely infer it
from successful COPY. Access-share locks block access-exclusive table DDL, but do
not freeze all functions, roles or new-object creation; an advisory lock only
controls cooperating actors. Provider-wide DDL exclusion remains unproved.
[MVCC caveats](https://www.postgresql.org/docs/17/mvcc-caveats.html),
[LOCK](https://www.postgresql.org/docs/17/sql-lock.html),
[Explicit locking](https://www.postgresql.org/docs/17/explicit-locking.html),
[PG17 lock implementation](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/commands/lockcmds.c).

An event-trigger generation counter is not a complete smaller substitute: event
triggers exclude shared databases/roles/tablespaces and event triggers themselves.
It also adds privileged behavior and provider-permission questions.
[Event trigger coverage](https://www.postgresql.org/docs/17/event-trigger-definition.html).

Sequence allocation is outside row MVCC. Capture read-only state after table
exports, including `last_value`/`is_called`, and verify restored allocation safety
separately. Cached allocation can put last_value ahead of rows; gaps are expected.
Restoring max(id) alone loses allocation history and fails for empty/custom/shared
sequences. Descending/cycling sequences and concurrent resets need an explicitly
tested rule or rejection. Do not label sequence state as the exact row-snapshot
state.
[Sequence semantics](https://www.postgresql.org/docs/17/sql-createsequence.html),
[Sequence definitions](https://www.postgresql.org/docs/17/catalog-pg-sequence.html).

## Smaller alternatives and their limits

| Alternative | Assessment within the existing requirements |
| --- | --- |
| Restricted native pg_dump | Best standard mechanism if a provider-supported narrow read/lock boundary becomes available. Existing managed permission failure remains decisive; do not retest it by granting pg_read_all_data. |
| Fresh Supabase project + tracked migrations + COPY | Fewer captured definitions, but no proof of exact managed service/schema compatibility or custom-object completeness. Migration history can be bypassed; this silently weakens recovery if used without equivalence tests. |
| Supervised owner-native export | Smallest already-proved safety copy; requires an owner session and therefore is not unattended automation. Scheduling it with a stored owner password violates the boundary even on the trusted PC. |
| Paid provider backups | Additional provider-local protection, not independent zero-expected-cost backup. Storage file bytes remain separate. |

Supabase documents managed-schema customization gaps, target role-password setup,
separate Storage-file migration and Edge Function reconstruction. Hosted postgres
is not a superuser; managed access cannot be assumed from a local superuser test.
[Restore guide](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore),
[Migration tracking](https://supabase.com/docs/guides/deployment/database-migrations),
[Hosted role restrictions](https://supabase.com/docs/guides/database/postgres/roles-superuser),
[Managed backups](https://supabase.com/docs/guides/platform/backups).

## Measured project result

The existing native PostgreSQL 17.10 fixture was used on loopback 55432, with one
new marked database and one temporary restricted role. No hosted system or
participant data was used. The extended assert-based probe passed in 2.61 seconds.
Ignored reproducible source/results:
`artifacts/hosted-backup-automation-assessment-20261018/drift-probe.mjs` and
`drift-probe-extended.json`.

| Probe | Observed result |
| --- | --- |
| Default, check constraint, RLS flag, policy, view filter, grant, function body, sequence cache and synthetic managed-version record change | Every change was invisible to the prior column signature. |
| A committed added base column during an exported repeatable-read snapshot | Repeated explicit catalog query in that snapshot retained the old signature; a fresh observer detected the column. The fixed view still exposed only its original two columns. |
| TRUNCATE/repopulate before the old snapshot first accessed the table | The restricted view query saw zero rows while a fresh query saw one. A coherent row snapshot alone was insufficient. |
| Restricted caller locks its fixed view before snapshot export | View-owner permission recursion allowed the lock; a second-session base TRUNCATE failed with lock timeout SQLSTATE 55P03. |
| Ordinary INSERT while the view lock is held | Succeeded; this lock does not pause normal logbook row writes. |

The marked database and restricted role were removed. The exact native fixture was
verified empty and stopped; generated fixture files were retained. No source
exporter, production permission audit, dependency, credential or scheduler changed.

These are counterexamples and a narrow mitigation, not a complete guard. The probe
does not implement native-dump equivalence, a global DDL barrier, A-to-B-to-A
detection, complete mutation coverage or hosted permissions. It did not recreate
a hybrid archive or test Auth services.

## Single next task

Build **one disposable-only PG17 metadata-guard prototype**. Define the supported
catalog/object contract explicitly; reject unknown required object classes instead
of omitting them. Mutation-test persistent drift in columns/defaults/generated
expressions, constraints/indexes, RLS/policies, functions/triggers/views, ACLs/role
memberships, sequence definitions, extension and managed-version inventories.
Include object addition/deletion/replacement and missing metadata/deparser access.
Keep fresh before/after observers separate from the locked data snapshot. Record
the concurrency guarantee honestly; a successful sample is not proof of arbitrary
provider DDL exclusion. Stop if coverage cannot be bounded without a new DDL engine.

Only after that contract passes should the next task build the hybrid format and
prove full pre-data/COPY/sequence/post-data restoration plus shared application
replay. A hosted permission proof, billing/scheduling/alerts and complete service
recovery remain later gates. This order keeps the speculative work out of the
working native archive path and the live Preview database.

No complete guard, hosted COPY export, hybrid restore, phone reopening or
Google recovery was tested. Fail-closed rejection protects integrity but can
leave the last accepted backup aging indefinitely; alerting and a tested owner
baseline-refresh procedure are part of eventual durability, not optional polish.
