# Disposable PG17 metadata guard contract

This is a synthetic, same-source experiment for the baseline/COPY candidate. It
cannot connect to hosted PostgreSQL: the entrypoint requires `disposable: true`,
loopback port 55432 and a marked fixture database name. No exporter, native archive
format, hosted permissions, credentials or schedule uses it.

Implementation: `tools/hosted-backup/metadata-guard.mjs`. Reproduce with the existing
isolated native PG17 fixture running on loopback 55432:

```powershell
node --test tools/hosted-backup/metadata-guard.integration.test.mjs
```

The test creates and removes its own marked database and random restricted role.
Only synthetic records are used. It does not start/stop PostgreSQL or touch Docker.
The role has access to fixed owner-permission views, no base schema/table access,
no role membership and no password-catalog access. Mutation observations switch
to that restricted role before evaluating the contract.

## Supported boundary

The owner would retain a native schema baseline. The guard compares subsequent
observations of **that same database**; it does not compare a restored database's
OIDs, generate DDL, discover a minimal dependency closure, or establish that an
unreviewed initial baseline is safe. The prototype inventories all 64 PG17 catalog
tables and rejects additions/deletions in that inventory. It fingerprints 58 catalog
tables plus the password-free `pg_roles` view, covering all their rows, including
objects outside the export schemas. This conservative whole-database boundary
avoids silently dropping cross-schema or shared ownership/ACL dependencies.

| Required surface | Contract |
| --- | --- |
| Relations | Permanent ordinary tables, views, indexes, sequences and composite types in `public`, `auth`, `storage`, `supabase_migrations`, `backup_export`; normal system/TOAST relations. Inventory, OIDs, owner, columns/type/typmod/nullability/defaults/generated/identity metadata, RLS/FORCE flags, options and ACLs are included. |
| Behavior/dependencies | Entire `pg_attrdef`, `pg_constraint`, `pg_index`, `pg_policy`, `pg_proc`, `pg_trigger`, `pg_rewrite`, `pg_depend`, `pg_shdepend` and remaining supported catalog rows. This includes compiled expressions, function bodies/settings/language/security/volatility, enum/domain/type/operator/collation/text-search metadata and dependency identities. Function/view deparsers must also be callable and their results are hashed. |
| Access | Schema/object/column/default ACLs, role attributes/membership options, parameter ACLs and database/role settings. `pg_authid` is never read; password changes alone are deliberately outside this metadata contract. Revoking required catalog/view/deparser access is an error, never an empty fingerprint. |
| Sequences | Positive, noncycling definitions and ownership are fingerprinted. `nextval` state is deliberately outside metadata equality; later restoration must capture and prove safe sequence allocation separately. Descending/cycling sequences reject initial inspection. |
| Extensions/runtime | Only `plpgsql` and synthetic-fixture `hstore`; versions, namespaces, configuration tables and membership dependencies are fingerprinted. Only standard internal/C/SQL/PLpgSQL languages. Exact server version and database identity are compared. Extension binaries, OS/runtime configuration and managed-service versions are **not** proved by catalog equality. |
| Managed versions | Mandatory, nonempty, nonnull `backup_export.managed_versions(version)` projection, hashed as ordered JSON strings. This synthetic view exposes one migration ledger. A real design must explicitly enumerate every required Auth/Storage/application ledger and deployment/configuration source; this fixture is not that hosted inventory. |

Unsupported partitioning/inheritance, materialized/foreign/temporary/unlogged
relations, large objects, FDWs/foreign servers/user mappings, event triggers,
publications/subscriptions and replication origins reject initial inspection.
Unknown namespaces/extensions/languages/catalog inventories, nonstandard
tablespaces and non-extension C/internal functions reject too. No secrets from user
mappings or subscriptions are read: the guard only counts mappings through
`pg_user_mappings` and subscriptions through their public `oid` column. Required
unreadable metadata always stops the observation; no fallback to owner access.

Each catalog is reduced to a sorted SHA-256 digest and row count **inside
PostgreSQL**. Only those digests/counts and nonsecret inventory/version/identity
fields leave the query. Routine definitions, settings and ACL contents do not
enter evidence. Object IDs deliberately remain: drop/recreate of an identical
function requires a refreshed reviewed baseline. There is no DDL engine.

## Deliberate exclusions and conservative stops

The six omitted catalog tables are `pg_authid` (use `pg_roles`), `pg_largeobject`
(metadata must prove none exist), `pg_statistic`/`pg_statistic_ext_data` (planner
samples), and `pg_subscription`/`pg_user_mapping` (unsupported, separately counted).
`pg_class` excludes only `relfilenode`, `relpages`, `reltuples`, `relallvisible`,
`relfrozenxid`, `relminmxid`; `pg_database` excludes `datfrozenxid`, `datminmxid`;
`pg_roles` excludes the masked password field. INSERT, ANALYZE and `nextval` are
tested to leave metadata equality intact. The exclusion list is part of the
maintained PG17 contract, not a general PostgreSQL compatibility guarantee.

Whole-catalog scope can reject unrelated provider changes. Regranting equivalent
PUBLIC privileges can replace NULL default ACLs with explicit ACLs and therefore
also stop the guard. Neither case should silently refresh the baseline. A new
baseline must be owner-reviewed and restored successfully before automation
accepts it. No refresh workflow is implemented here.

Native PostgreSQL definitions of catalog contents and mutable planner/physical
fields: [catalog inventory](https://www.postgresql.org/docs/17/catalogs.html),
[pg_class](https://www.postgresql.org/docs/17/catalog-pg-class.html),
[pg_sequence](https://www.postgresql.org/docs/17/catalog-pg-sequence.html).
The completeness boundary includes persisted string function bodies, but does not
infer runtime references from them; PostgreSQL documents that those references
are not necessarily tracked in
[pg_depend](https://www.postgresql.org/docs/17/ddl-depend.html).

## Mutation and concurrency evidence

The executable test covers defaults/types/nullability/generated expressions,
constraints/validation/index expressions and predicates, RLS/FORCE/policy
expressions/commands/roles/permissiveness, function bodies/settings/security/
volatility, trigger enablement, view filters/security, table/column/default ACLs,
role attributes/membership/settings, database settings, ownership, sequence
cache/increment/ownership, extension addition/version/namespace, managed-version
data, object addition/deletion/identical replacement, outside dependencies,
enum/domain addition and relation options. A server patch mismatch is simulated
in the observation; no server upgrade is performed. Missing/empty/unreadable
managed metadata, unreadable catalogs/deparsers, unknown catalogs/extensions,
unsupported relations and large objects fail closed.

The locked transaction explicitly locks **both** fixed data/version views before
exporting its row snapshot. Base TRUNCATE times out, while ordinary INSERT works.
A committed function-body replacement is not blocked. Raw catalog rows queried
through an imported old snapshot retain the old function definition, whereas
`pg_get_functiondef` sees its new definition. Combining raw catalogs and deparsers
therefore does not turn an old snapshot into one coherent current metadata view.
Fresh independent observations detect persistent changes. A body changed and
changed back before the next fresh observation passes endpoint equality: this
test explicitly proves that limit.

There is no claim of arbitrary provider DDL exclusion, extension-binary identity,
full service reconstruction, live hosted permission sufficiency or complete
Preview coverage. A hybrid restore experiment may use only this declared synthetic
boundary; it must not promote this prototype into production scheduling. A later
hosted scope review may still reject the candidate. The existing supervised
native backup remains the proved safety copy.
