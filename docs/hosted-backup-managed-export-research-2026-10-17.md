# Managed Auth/Storage backup export: evidence and next decision

Research date: 2026-10-17. Primary documentation plus linked local disposable
evidence. This research made no live changes, handled no credentials and exported
no participant data. Hosted facts below come from the earlier development
evidence, not a new live inspection.

## Recommendation

Priority: establish a verified manual safety copy before adding more unattended
machinery. Prepare a supervised full encrypted export and isolated restoration
using the existing trusted PC and independent offline identity. Keep routine
automation's no-administrator-credential rule intact. Any private-key password must
be entered locally, not in chat. This is an immediate engineering sequence, not a
claim that such a copy or recovery has already been accepted.

Keep Supabase. The disposable view test now proves a promising restricted *data*
read mechanism, but also disproves the proposed ordinary schema-only dump with
the restricted login. Do not call this a drop-in fix or deploy it to Preview.

The smallest credible development follow-up is a hybrid: an owner-prepared,
reviewed schema baseline kept with reconstruction sources, plus restricted
typed data exports through fixed views. Prove restoration and reject any live
schema/version drift until that baseline is refreshed on the trusted machine.
This keeps administrator credentials out of unattended external automation, but
adds a real maintenance obligation. A synthetic restore using an owner-prepared
schema proves serialization only; it does not prove this ongoing obligation.

Do not write a custom DDL generator or switch to definer functions to overcome
the schema-dump failure: neither is the smallest demonstrated complete remedy.
For immediate independent protection, a supervised encrypted export and a
disposable restore on the trusted owner machine is the simpler fallback. It is
not an unattended backup service. A provider move is not justified by this
evidence; paid provider copies alone do not meet independence either.

## What is established

The existing [development result](../artifacts/hosted-backup-plan-20261017/airport-fallback-result.md)
records that removing `pg_net` cleared the write/outside-read/definer checks, but
ordinary grants still could not cover managed relations: `auth` schema usage was
not delegable, only 16/27 Auth and 2/8 Storage table/view SELECT grants were
delegable. The temporary role was removed. Preview remains unchanged.

This is consistent with the managed platform model. Hosted `postgres` is an
administrator without superuser status; managed Auth/Storage objects belong to
Supabase service roles. Changing managed ownership is not a sensible remedy:
Supabase warns that breaking these ownership assumptions can cause later service
failures. [Hosted superuser restrictions](https://supabase.com/docs/guides/database/postgres/roles-superuser),
[Supabase platform permissions](https://supabase.com/docs/guides/platform/permissions).

## Why the obvious permission shortcuts do not fit

Supabase now documents a backup account using `pg_read_all_data` plus `BYPASSRLS`.
It also explicitly warns that a read-only default can be switched off, `PUBLIC`
may permit `pg_net` writes, and large-object creation remains possible. Its CLI
dump wrapper switches to `postgres`, so it recommends direct `pg_dump` for that
account. This is a supported broad-read recipe, not proof of our stricter
read-only and no-Vault boundary. [Supabase backup-role troubleshooting](https://supabase.com/docs/guides/troubleshooting/pg_dump-fails-with-query-would-be-affected-by-row-level-security-policy-1d5783).

`pg_read_all_data` grants SELECT over every table, view and sequence, and usage
over every schema; explicit revocations on one object do not subtract that broad
ability. It does not itself bypass RLS. Vault exposes decrypted values through a
view, and Supabase says access to that view provides access to plaintext secrets.
Therefore neither schema-filtering the *dump* nor excluding Vault from the
*archive* proves the credential cannot read Vault. The backup troubleshooting
page's encrypted-dump statement does not establish this access boundary.
[PostgreSQL predefined roles](https://www.postgresql.org/docs/17/predefined-roles.html),
[Vault access model](https://supabase.com/docs/guides/database/vault),
[PostgreSQL privileges](https://www.postgresql.org/docs/17/ddl-priv.html).

`NOINHERIT` alone is not a sandbox. A member can activate a role with `SET ROLE`
where its membership permits SET; PostgreSQL 17 defaults that option to true.
Disabling both INHERIT and SET prevents using the broad role at all. A definer
function cannot run `SET ROLE` internally. Consequently, granting broad membership
to the backup login and asking it to use it responsibly is not a remedy.
[PostgreSQL role membership](https://www.postgresql.org/docs/17/role-membership.html),
[SET ROLE](https://www.postgresql.org/docs/17/sql-set-role.html).

## Smaller first candidate: fixed SELECT views

The [disposable feasibility result](../artifacts/hosted-backup-plan-20261017/view-export-feasibility.json)
now verifies owner-permission views can expose every forced-RLS row without a
base SELECT grant option, caller Auth schema usage or caller BYPASSRLS. Writes,
base/Vault reads and role escalation were denied, and a sequence-state view
worked. However, the real restricted `pg_dump --schema-only` failed on
`LOCK TABLE auth.users, storage.objects IN ACCESS SHARE MODE`, reporting denied
Auth schema access. These are disposable PostgreSQL results, not hosted or
complete recovery acceptance. The temporary databases and roles were removed.

The extended synthetic test then supplied an owner-prepared schema to isolate data
serialization from the unresolved DDL problem. Native COPY restored bigint values
above JavaScript's safe-integer limit, arrays, bytea, nulls, Unicode, generated
columns and sequence allocation. A concurrent later source insert was excluded by
the shared snapshot. A new-column change was detectable; that is not a full schema
fingerprint implementation. The final evidence status is explicitly
`typed-data-route-proved-schema-export-unresolved`. No hosted grants, records,
configuration or deployment were changed by this test.

PostgreSQL documents that ordinary views check base-relation privileges and RLS
using the view owner, while the caller needs schema usage only on the schema
containing the view. Keep `security_invoker` false. Use simple fixed base-relation
projections; functions embedded in a view do not automatically execute as the
view owner. Simple views can be automatically updatable, so grant SELECT alone
and prove DML is denied. `SELECT *` freezes columns at view creation, so additions
must fail the schema-drift check rather than be silently omitted.
[PostgreSQL CREATE VIEW](https://www.postgresql.org/docs/17/sql-createview.html).

The owner has grant options on its own view. Combined with the documented
view-owner check, this supports the inference that base-table SELECT grant
options are unnecessary for granting SELECT on a newly owned export view. The
disposable test confirms this; hosted permission coverage remains to be proved.
[PostgreSQL GRANT](https://www.postgresql.org/docs/17/sql-grant.html).

Sequence state can be queried as a special single-row relation; a fixed view
projecting `last_value` and `is_called` is worth testing. Avoid `nextval` and
`setval` in source export because they change state. Sequence changes are not
rolled back and their allocation/caching has separate semantics from table
snapshots. Prove restore preserves safe allocation, including empty tables and
cached sequences. [CREATE SEQUENCE](https://www.postgresql.org/docs/17/sql-createsequence.html).

A typed `COPY (SELECT ... FROM export_view) TO STDOUT` can avoid a JSON conversion
layer, but this remains a custom data restore contract requiring separately
prepared schema reconstruction. The restricted schema-only dump has now failed;
it is not an available solution. Ordinary `pg_dump` does not dump view contents
as table data, so merely adding views does not fix the normal dump command.
[PostgreSQL pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html).

## Definer exporter: documented but no longer the next recommendation

This is an engineering inference from documented function semantics, not a
Supabase-certified backup procedure. It would not solve the failed ordinary DDL
dump and adds privileged code beyond the simpler view mechanism. A hosted test would have to establish that
the intended function owner can actually read every required relation. A function
cannot acquire permissions its owner lacks. A function can use its owner's
privileges while its caller receives only permission to run that fixed function;
both PostgreSQL and Supabase document this feature.
[PostgreSQL CREATE FUNCTION](https://www.postgresql.org/docs/17/sql-createfunction.html),
[Supabase database functions](https://supabase.com/docs/guides/database/functions).

- Put it in an unexposed, trusted schema. Use a fixed allowlist of required base
  tables/columns. Do not accept SQL, predicates, arbitrary relation identifiers,
  or caller-controlled code. Keep Vault, operational credentials and recovery
  secrets outside the output; explicitly decide which encrypted recovery records
  are required for restoration instead of silently omitting required data.
- Use a safe fixed `search_path`, qualify objects, revoke default `PUBLIC`
  EXECUTE, and grant only the backup login access in the same transaction. The
  login must not own, replace, create dependencies for, or become the function
  owner. PostgreSQL documents both the search-path attack and default PUBLIC
  execution risk. [Definer-function security](https://www.postgresql.org/docs/17/sql-createfunction.html).
- Audit the login's entire effective permission surface, including inherited
  PUBLIC privileges and other definer functions. Changing the transaction default
  must not enable writes. Test prohibited operations and role switching with
  synthetic canaries, without printing real secrets.
- Fail closed on schema drift, missing tables/columns, unexpected row filtering,
  failed serialization, or partial output. Auth rows themselves contain sensitive
  authentication material: an export-specific credential can still disclose its
  allowed dataset, even when it cannot read Vault or modify the database.

## Export is only half of recovery

A JSON row export is not a normal `pg_dump` archive. The implementation must pair
it with object definitions, sequence state where used, migration history, and a
tested restore process. `pg_dump --schema-only` exports definitions; its data
section normally also contains sequence values and large objects. A scoped dump
does not automatically include all dependencies. These facts limit how much work
can safely be replaced with a custom function.
[PostgreSQL pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html).

Use one repeatable-read snapshot for all data calls; keep any exporting
transaction open. This protects row coherence, not the correctness of a separate
schema baseline. DDL/sequence handling must be tested separately; sharing a
snapshot does not make an arbitrary exporter correct. PostgreSQL documents shared
snapshot lifetime and how separate transactions can otherwise see different states.
[Snapshot synchronization](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION),
[pg_dump snapshot option](https://www.postgresql.org/docs/17/app-pgdump.html).

Managed Auth/Storage schema versions and local modifications must match the
restore target. Supabase's restore guide treats migration history, managed-schema
customizations, roles, Edge Functions and Storage objects as separate concerns.
Storage SQL tables contain metadata, not file bytes; preserve required files
through a separately proven read-only route and restore them using the Storage
API. Do not call a successful database export a complete system recovery.
[Supabase backup/restore guide](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore),
[Storage schema design](https://supabase.com/docs/guides/storage/schema/design).

### Two reconstruction choices, neither an established complete solution

1. **Provider-created managed schemas plus tracked customizations.** Supabase's
   official restore process starts by creating a new project. Managed Auth and
   Storage definitions are supplied by that platform; custom triggers/policies
   must be restored separately, and migration history has its own restore step.
   This supports investigating a fresh matched platform baseline plus application
   migrations and data COPY, rather than inventing Auth/Storage DDL. The docs do
   not certify our custom COPY format, guarantee future managed-version
   compatibility, or prove complete independent restoration.
   [Supabase restore process](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore).
2. **Owner-captured schema baseline plus restricted data.** Capture definitions
   using the existing trusted administrative route, inspect for secrets, and
   freeze them with the reconstruction revision. The unattended account only
   exports allowed data and validates a reviewed metadata/version fingerprint.
   Any drift blocks acceptance until a new baseline is captured and restored.
   This is a proposed project design, not a documented Supabase feature. It avoids
   a custom DDL generator but needs proof that its drift check covers every
   restoration-relevant definition, permission and managed dependency.

Tracked migrations help, but are not proof of live equivalence: Supabase explains
that migration files and the applied-history table are separate systems, and
remote edits can bypass history. Its current diff/pull handling also excludes
some custom objects inside managed schemas. Keep all customizations in reviewed
source and compare live definitions; do not simply assume migrations reproduce
the source. [Supabase migration workflow](https://supabase.com/docs/guides/deployment/database-migrations).

These choices should be assessed against maintenance cost before implementing
more machinery. An owner-prepared synthetic schema plus restored COPY data is a
useful serialization test, but cannot demonstrate that tomorrow's unattended
archive includes a correct schema or that a real Supabase Auth login works after
restoration. A custom catalog-to-DDL generator would be a substantially broader
project and is not recommended to get past this blocker.

Schema definitions and migration-history SQL can themselves contain embedded
credentials. That is a further reason to inspect the exact export contract and
test secret canaries; excluding Vault alone is insufficient. This is a risk
inference, not a finding of an actual secret leak in this repository.

## Native backups and practical fallback

Supabase's daily managed backups cover Pro/Team/Enterprise, and its documentation
recommends off-site CLI exports for free projects. Native database backups omit
Storage object bytes, and deleting a project also deletes its associated provider
backups. They can be an additional safety layer if spending is approved, but they
do not satisfy the current zero-expected-cost independent-copy requirement by
themselves. [Supabase database backups](https://supabase.com/docs/guides/platform/backups).

The data-permission mechanism has passed a disposable test. The outstanding
decision is whether a limited, maintainable schema-baseline/reconstruction
contract can pass full recovery acceptance without putting administrative keys
in unattended external automation. Keep Preview unchanged until that is proved;
use the supervised export fallback if immediate independent protection is needed.

No hosted view export or full recovery was proved. Ordinary restricted schema
dump failed; a custom data mechanism alone does not complete independent backup.
