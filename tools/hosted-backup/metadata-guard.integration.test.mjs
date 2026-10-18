import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { inspectDisposableMetadata, metadataGuardSql, requireSameMetadata } from './metadata-guard.mjs';
import { withDatabaseSnapshot } from './postgres.mjs';
import { dropDisposable } from './restore.mjs';

test('PG17 disposable metadata contract rejects drift and proves observer/lock limits', async t => {
  const pgBin = process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin';
  const local = { host: '127.0.0.1', port: 55432, user: 'postgres', password: 'postgres', pgBin };
  const target = { database: 'elb_backup_fixture_' + randomUUID().replaceAll('-', ''), token: randomUUID().replaceAll('-', '') };
  const reader = 'elb_metadata_' + randomUUID().replaceAll('-', '');
  const password = randomBytes(32).toString('hex');
  const connection = { ...local, database: target.database, user: reader, password, disposable: true };
  function run(sql, user = 'postgres', database = target.database) {
    return spawnSync(pgBin + '/psql' + (process.platform === 'win32' ? '.exe' : ''),
      ['-X','-w','-qAt','-v','ON_ERROR_STOP=1','-d',database], {
        input: sql, encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 16 * 1024 * 1024,
        env: { ...process.env, PGHOST: local.host, PGPORT: String(local.port), PGUSER: user,
          PGPASSWORD: user === reader ? password : local.password, PGSSLMODE: 'disable', PGOPTIONS: '' }
      });
  }
  function sql(statement, user, database) {
    const result = run(statement, user, database);
    assert.equal(result.status, 0, 'Synthetic metadata fixture SQL failed: ' + result.stderr.replaceAll(password, '[redacted]'));
    return result.stdout.trim();
  }
  let created = false, roleCreated = false;
  const detected = [];
  try {
    sql(`create database ${target.database} template template0;`, 'postgres', 'postgres'); created = true;
    sql(`comment on database ${target.database} is 'flightlogx-backup-restore:${target.token}';`, 'postgres', 'postgres');
    sql(`create role ${reader} login password '${password}' noinherit nobypassrls;`); roleCreated = true;
    sql(`create schema auth; create schema backup_export; create schema supabase_migrations;
      create extension hstore version '1.7';
      create table auth.sample(id bigint primary key, note text default 'original', amount integer, spare integer,
        doubled integer generated always as (amount*2) stored, constraint positive check(amount>0));
      alter table auth.sample enable row level security; alter table auth.sample force row level security;
      create policy readable on auth.sample for select using(id>0);
      insert into auth.sample(id,note,amount) values(1,'synthetic',2);
      create function auth.fixture_function() returns integer language sql as 'select 1';
      create function auth.fixture_trigger() returns trigger language plpgsql as 'begin return NEW; end';
      create trigger sample_trigger before insert on auth.sample for each row execute function auth.fixture_trigger();
      create sequence auth.sample_sequence cache 1 owned by auth.sample.id;
      create table supabase_migrations.schema_migrations(version text primary key);
      insert into supabase_migrations.schema_migrations values('synthetic-v1');
      create view backup_export.sample as select id,note,amount,doubled from auth.sample;
      create view backup_export.managed_versions as select version from supabase_migrations.schema_migrations;
      grant usage on schema backup_export to ${reader}; grant select on all tables in schema backup_export to ${reader};`);
    assert.notEqual(run('select * from auth.sample;', reader).status, 0);
    assert.notEqual(run('select rolpassword from pg_authid;', reader).status, 0);
    assert.equal(sql('select count(*) from backup_export.sample;', reader), '1');
    sql(metadataGuardSql(), reader);
    const baseline = inspectDisposableMetadata(connection);
    requireSameMetadata(baseline, inspectDisposableMetadata(connection));
    assert.equal(JSON.stringify(baseline).includes(password), false);
    assert.equal(JSON.stringify(baseline).includes('synthetic-v1'), false);

    // Roll back each mutation so every case challenges exactly the same baseline.
    for (const [name, mutation] of [
      ['default', "alter table auth.sample alter column note set default 'changed'"],
      ['type', 'alter table auth.sample alter column spare type bigint'],
      ['nullability', 'alter table auth.sample alter column note set not null'],
      ['generated-expression', 'alter table auth.sample drop column doubled cascade; alter table auth.sample add column doubled integer generated always as (amount*3) stored'],
      ['constraint-expression', 'alter table auth.sample drop constraint positive; alter table auth.sample add constraint positive check(amount>1)'],
      ['constraint-validation', 'alter table auth.sample add constraint extra check(id>0) not valid'],
      ['index-expression-predicate', 'create index changed_index on auth.sample ((lower(note))) where id>0'],
      ['row-security', 'alter table auth.sample disable row level security'],
      ['force-row-security', 'alter table auth.sample no force row level security'],
      ['policy-expression', 'alter policy readable on auth.sample using(id>2)'],
      ['policy-roles', `alter policy readable on auth.sample to ${reader}`],
      ['policy-command', 'drop policy readable on auth.sample; create policy readable on auth.sample for update using(true)'],
      ['policy-permissiveness', 'drop policy readable on auth.sample; create policy readable on auth.sample as restrictive using(true)'],
      ['function-body', "create or replace function auth.fixture_function() returns integer language sql as 'select 2'"],
      ['function-config', "alter function auth.fixture_function() set search_path='auth'"],
      ['function-security', 'alter function auth.fixture_function() security definer'],
      ['function-volatility', 'alter function auth.fixture_function() immutable'],
      ['trigger-enabled', 'alter table auth.sample disable trigger sample_trigger'],
      ['view-filter', 'create or replace view backup_export.sample as select id,note,amount,doubled from auth.sample where id>2'],
      ['view-security', 'alter view backup_export.sample set(security_invoker=true)'],
      ['acl', `revoke select on backup_export.sample from ${reader}`],
      ['column-acl', `grant select(note) on auth.sample to ${reader}`],
      ['default-acl', `alter default privileges in schema auth grant select on tables to ${reader}`],
      ['role-attributes', `alter role ${reader} bypassrls`],
      ['role-membership', `grant pg_read_all_stats to ${reader}`],
      ['role-setting', `alter role ${reader} set statement_timeout='2s'`],
      ['database-setting', `alter database ${target.database} set statement_timeout='3s'`],
      ['ownership', `alter view backup_export.sample owner to ${reader}`],
      ['sequence-cache', 'alter sequence auth.sample_sequence cache 10'],
      ['sequence-increment', 'alter sequence auth.sample_sequence increment 2'],
      ['sequence-ownership', 'alter sequence auth.sample_sequence owned by none'],
      ['extension-addition', 'create extension pg_trgm'],
      ['extension-version', "alter extension hstore update to '1.8'"],
      ['extension-namespace', 'alter extension hstore set schema auth'],
      ['managed-version', "update supabase_migrations.schema_migrations set version='synthetic-v2'"],
      ['object-addition', 'create table auth.new_provider_object(id int)'],
      ['object-deletion', 'drop function auth.fixture_function()'],
      ['object-replacement', "drop function auth.fixture_function(); create function auth.fixture_function() returns integer language sql as 'select 1'"],
      ['outside-dependency', "create schema outside_dependency; create table outside_dependency.data(id int); create view auth.outside_view as select * from outside_dependency.data"],
      ['enum', "create type auth.new_enum as enum ('first','second')"],
      ['domain', 'create domain auth.positive_int as integer check(value>0)'],
      ['relation-options', 'alter table auth.sample set(fillfactor=80)'],
      ['sequence-unsupported-cycle', 'alter sequence auth.sample_sequence cycle']
    ]) {
      const changed = JSON.parse(sql(`begin; ${mutation}; set local role ${reader}; ${metadataGuardSql()} rollback;`));
      assert.throws(() => requireSameMetadata(baseline, changed), /drift/, name);
      detected.push(name);
    }
    // All settings/roles as well as DDL above must have rolled back.
    requireSameMetadata(baseline, inspectDisposableMetadata(connection));
    const changedVersion = { ...baseline, server: '170011' };
    assert.throws(() => requireSameMetadata(baseline, changedVersion), /drift/);
    detected.push('server-version');

    sql('insert into auth.sample(id,note,amount) values(2,\'ordinary write\',3); analyze auth.sample;');
    sql("select nextval('auth.sample_sequence');");
    requireSameMetadata(baseline, inspectDisposableMetadata(connection));

    for (const [name, mutation, undo] of [
      ['missing-managed-metadata', 'alter view backup_export.managed_versions rename to missing', 'alter view backup_export.missing rename to managed_versions'],
      ['unreadable-managed-metadata', `revoke select on backup_export.managed_versions from ${reader}`, `grant select on backup_export.managed_versions to ${reader}`],
      ['unreadable-catalog', 'revoke select on pg_catalog.pg_policy from public', 'grant select on pg_catalog.pg_policy to public'],
      ['unreadable-deparser', 'revoke execute on function pg_catalog.pg_get_functiondef(oid) from public', 'grant execute on function pg_catalog.pg_get_functiondef(oid) to public'],
      ['unknown-catalog', 'set allow_system_table_mods=on; create table pg_catalog.unknown_metadata(id int)', 'set allow_system_table_mods=on; drop table pg_catalog.unknown_metadata'],
      ['empty-managed-metadata', 'delete from supabase_migrations.schema_migrations', "insert into supabase_migrations.schema_migrations values('synthetic-v1')"],
      ['unsupported-object', 'create materialized view auth.unsupported as select 1 as id', 'drop materialized view auth.unsupported'],
      ['unknown-extension', 'create extension pg_trgm', 'drop extension pg_trgm'],
      ['unknown-namespace', 'create schema unknown_dependencies', 'drop schema unknown_dependencies'],
      ['unknown-external-function', "create function auth.external_function(text) returns text language internal strict as 'textin'", 'drop function auth.external_function(text)'],
      ['unsupported-descending-sequence', 'alter sequence auth.sample_sequence increment -1 minvalue -9223372036854775807', 'alter sequence auth.sample_sequence increment 1 minvalue 1'],
      ['unsupported-large-object', 'select lo_create(987654)', 'select lo_unlink(987654)']
    ]) {
      sql(mutation);
      try { assert.throws(() => inspectDisposableMetadata(connection), /failed|Unsupported/, name); }
      finally { sql(undo); }
      detected.push(name);
    }
    // GRANT after REVOKE leaves explicit ACLs in place of PostgreSQL's NULL defaults.
    // Exact metadata equality conservatively requires a fresh reviewed baseline.
    const refreshedBaseline = inspectDisposableMetadata(connection);
    assert.throws(() => inspectDisposableMetadata({ ...connection, host: 'hosted.example' }), /loopback/);

    await withDatabaseSnapshot(connection, `lock table backup_export.sample,backup_export.managed_versions in access share mode;
      select json_build_object('snapshot',pg_export_snapshot());`, async rows => {
      const inSnapshot = () => JSON.parse(sql(`begin isolation level repeatable read read only;
        set transaction snapshot '${rows[0].snapshot}'; ${metadataGuardSql()} rollback;`, reader));
      const before = inSnapshot();
      const locked = run("\\set VERBOSITY sqlstate\nset lock_timeout='200ms'; truncate auth.sample;");
      assert.notEqual(locked.status, 0); assert.match(locked.stderr, /55P03/);
      sql("insert into auth.sample(id,note,amount) values(3,'concurrent write',4);");
      sql("create or replace function auth.fixture_function() returns integer language sql as 'select 99';");
      const stale = inSnapshot();
      assert.deepEqual(before.fingerprints, stale.fingerprints);
      assert.notEqual(before.deparsers, stale.deparsers);
      assert.throws(() => requireSameMetadata(refreshedBaseline, inspectDisposableMetadata(connection)), /drift/);
      // An A-to-B-to-A body change is invisible to endpoint observations.
      sql("create or replace function auth.fixture_function() returns integer language sql as 'select 1';");
      requireSameMetadata(refreshedBaseline, inspectDisposableMetadata(connection));
    });
    t.diagnostic(JSON.stringify({ detected, ordinaryWritesAnalyzeAndNextvalAccepted: true,
      freshObserverDetectsPersistentFunctionDrift: true, sameSnapshotCatalogRowsMissIt: true,
      sameSnapshotDeparserSeesNewDefinition: true,
      viewLocksBlockTruncateAllowInsert: true, endpointChecksMissTransientFunctionDrift: true }));
  } finally {
    if (created) dropDisposable(local, target);
    if (roleCreated) sql(`drop role ${reader};`, 'postgres', 'postgres');
  }
});
