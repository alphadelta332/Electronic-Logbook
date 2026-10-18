// Synthetic PG17 proof only; deliberately not an unattended or hosted exporter.
import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, randomBytes, generateKeyPairSync, createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { writeFileSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { sealArchive, openArchive } from './archive.mjs';
import { inspectDisposableMetadata, requireSameMetadata } from './metadata-guard.mjs';
import { postgres, withDatabaseSnapshot } from './postgres.mjs';
import { dropDisposable, readDisposable } from './restore.mjs';
import { buildReplayCheck, replaySupervised } from './replay-supervised.mjs';

test('PG17 native baseline plus restricted typed COPY restores rows, behavior, permissions, sequences and encrypted application history', async t => {
  const pgBin = process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin';
  const local = { host: '127.0.0.1', port: 55432, user: 'postgres', password: 'postgres', pgBin };
  const receipt = () => ({ database: 'elb_backup_fixture_' + randomUUID().replaceAll('-', ''), token: randomUUID().replaceAll('-', '') });
  const source = receipt();
  const reader = 'elb_hybrid_' + randomUUID().replaceAll('-', '');
  const owner = 'elb_owner_' + randomUUID().replaceAll('-', '');
  const password = randomBytes(32).toString('hex');
  const connection = { ...local, database: source.database, user: reader, password, disposable: true };
  const literal = value => "'" + String(value).replaceAll("'", "''") + "'";
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  const psqlArgs = ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1'];
  function run(database, name, args, input) {
    assert.match(database, /^(postgres|elb_backup_fixture_[a-f0-9]{32})$/);
    const result = spawnSync(pgBin + '/' + name + (process.platform === 'win32' ? '.exe' : ''), args, {
      input, windowsHide: true, timeout: 120000, maxBuffer: 64 * 1024 * 1024,
      env: { ...process.env, PGHOST: local.host, PGPORT: String(local.port), PGDATABASE: database,
        PGUSER: local.user, PGPASSWORD: local.password, PGSSLMODE: 'disable', PGOPTIONS: '', PGCLIENTENCODING: 'UTF8' }
    });
    assert.equal(result.status, 0, 'Synthetic hybrid command failed; private diagnostics suppressed.');
    return result.stdout;
  }
  const sql = (database, text) => run(database, 'psql', psqlArgs, text).toString('utf8').trim();
  function create(target) {
    sql('postgres', `create database ${target.database} template template0;`);
    sql('postgres', `comment on database ${target.database} is 'flightlogx-backup-restore:${target.token}';
      revoke all on database ${target.database} from public;`);
  }
  const tables = [
    ['auth.identities', 'identities', 'user_id'],
    ['public.logbooks', 'logbooks', 'logbook_id,owner_account_id,deleted_at'],
    ['public.key_envelopes', 'key_envelopes', 'logbook_id,recovery_method,revoked_at,expires_at,wrapping_algorithm,key_version_id,ciphertext,nonce'],
    ['public.operations', 'operations', 'logbook_id,revision,portable_revision_id,author_device_id,client_created_at,operation_format_version,payload_ciphertext,payload_nonce,payload_tag,payload_hash,entry_id,parent_revision_ids'],
    ['public.configuration_revisions', 'configuration_revisions', 'logbook_id,revision,portable_revision_id,author_device_id,client_created_at,configuration_format_version,payload_ciphertext,payload_nonce,payload_tag,payload_hash'],
    ['auth.typed', 'typed', 'id,note,amount,precise,flag,raw,tags,document,happened,span,kind'],
    ['storage.empty_fixture', 'empty_fixture', 'id'],
    ['supabase_migrations.schema_migrations', 'managed_versions', 'version']
  ];
  const sequences = ['auth.typed_id_seq', 'public.unused_sequence'];
  // Text COPY uses each PostgreSQL type's own I/O, without JS number conversion.
  const settings = "set client_encoding='UTF8'; set timezone='UTC'; set datestyle='ISO,YMD'; set intervalstyle='postgres'; set bytea_output='hex'; set extra_float_digits=3;";
  const manifest = table => `select json_build_object('rows',count(*),'digest',
    encode(sha256(convert_to(coalesce(string_agg(to_jsonb(t)::text,E'\\n' order by to_jsonb(t)::text collate "C"),''),'UTF8')),'hex')) from ${table} t;`;
  const rowManifests = database => tables.map(([table]) => JSON.parse(sql(database, settings + manifest(table))));
  const schemaArgs = ['--schema-only', '--format=custom', ...['public','auth','storage','supabase_migrations','backup_export'].map(s => '--schema=' + s)];
  const objectList = join(tmpdir(), 'elb-hybrid-' + randomUUID() + '.toc');
  let listCreated = false;
  const schemaSql = (bytes, section) => run(source.database, 'pg_restore', ['--exit-on-error', '--use-list=' + objectList, '--section=' + section, '--file=-'], bytes);
  const normalizedSchema = database => sql(database, `select json_build_object(
    'schemas',(select json_agg(x order by nspname) from (select nspname,pg_get_userbyid(nspowner) owner,nspacl from pg_namespace
      where nspname in ('public','auth','storage','supabase_migrations','backup_export')) x),
    'relations',(select json_agg(x order by schema,name) from (select n.nspname schema,c.relname name,c.relkind,
      pg_get_userbyid(c.relowner) owner,c.relacl,c.relrowsecurity,c.relforcerowsecurity,c.reloptions
      from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname in ('public','auth','storage','supabase_migrations','backup_export')) x),
    'columns',(select json_agg(x order by schema,name,num) from (select n.nspname schema,c.relname name,a.attnum num,a.attname,
      format_type(a.atttypid,a.atttypmod) type,a.attnotnull,a.attidentity,a.attgenerated,a.attacl,pg_get_expr(d.adbin,d.adrelid) expression
      from pg_attribute a join pg_class c on c.oid=a.attrelid join pg_namespace n on n.oid=c.relnamespace
      left join pg_attrdef d on d.adrelid=c.oid and d.adnum=a.attnum where a.attnum>0 and not a.attisdropped
      and n.nspname in ('public','auth','storage','supabase_migrations','backup_export')) x),
    'constraints',(select json_agg(x order by name) from (select conname name,conrelid::regclass::text relation,convalidated,
      pg_get_constraintdef(oid) definition from pg_constraint where connamespace in ('auth'::regnamespace,'public'::regnamespace)) x),
    'indexes',(select json_agg(x order by schemaname,indexname) from (select schemaname,indexname,indexdef from pg_indexes
      where schemaname in ('auth','public')) x),
    'policies',(select json_agg(x order by schemaname,tablename,policyname) from pg_policies x),
    'triggers',(select json_agg(x order by definition) from (select pg_get_triggerdef(oid) definition,tgenabled from pg_trigger where not tgisinternal) x),
    'views',(select json_agg(x order by table_name) from (select table_name,view_definition from information_schema.views where table_schema='backup_export') x),
    'functions',(select json_agg(x order by definition) from (select pg_get_functiondef(p.oid) definition from pg_proc p
      join pg_namespace n on n.oid=p.pronamespace where n.nspname='auth') x),
    'sequences',(select json_agg(x order by sequencename) from (select schemaname,sequencename,sequenceowner,data_type,start_value,min_value,max_value,increment_by,cycle,cache_size
      from pg_sequences where schemaname in ('auth','public')) x));`);
  let sourceCreated = false, rolesCreated = false, environment;
  const buffers = [];
  try {
    create(source); sourceCreated = true;
    sql(source.database, `create role ${reader} login password '${password}' noinherit nobypassrls;
      create role ${owner} nologin noinherit nobypassrls;
      grant connect on database ${source.database} to ${reader};`); rolesCreated = true;
    buildReplayCheck();
    const seed = spawnSync('dotnet', [fileURLToPath(new URL('./ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll', import.meta.url)), 'seed'],
      { encoding: 'utf8', windowsHide: true, timeout: 30000 });
    assert.equal(seed.status, 0, 'Synthetic application seed failed.');
    const fixture = JSON.parse(seed.stdout);
    environment = Buffer.from(`RECOVERY_KEK_BASE64=${fixture.kek}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1\n`);
    sql(source.database, `create schema auth; create schema storage; create schema supabase_migrations; create schema backup_export;
      create table auth.identities(user_id uuid primary key);
      create table public.logbooks(logbook_id uuid primary key,owner_account_id uuid references auth.identities(user_id),deleted_at timestamptz);
      create table public.key_envelopes(logbook_id uuid references public.logbooks,recovery_method text,revoked_at timestamptz,expires_at timestamptz,
        wrapping_algorithm text,key_version_id text,ciphertext text,nonce text);
      create table public.operations(logbook_id uuid references public.logbooks,revision bigint primary key,portable_revision_id text,author_device_id uuid,
        client_created_at timestamptz,operation_format_version integer,payload_ciphertext text,payload_nonce text,payload_tag text,payload_hash text,
        entry_id text,parent_revision_ids jsonb);
      create table public.configuration_revisions(logbook_id uuid references public.logbooks,revision bigint primary key,portable_revision_id text,
        author_device_id uuid,client_created_at timestamptz,configuration_format_version integer,payload_ciphertext text,payload_nonce text,payload_tag text,payload_hash text);
      create type auth.fixture_kind as enum ('first','second');
      create table auth.typed(id bigint generated always as identity (start 7 increment 3 cache 5) primary key,note text default 'default note',
        amount integer not null check(amount>0),precise numeric(35,12),flag boolean,raw bytea,tags text[],document jsonb,happened timestamptz,
        span interval,kind auth.fixture_kind,doubled integer generated always as(amount*2) stored);
      create table storage.empty_fixture(id bigint);
      create sequence public.unused_sequence start 9007199254740993 increment 7;
      create table supabase_migrations.schema_migrations(version text primary key);
      insert into supabase_migrations.schema_migrations values('synthetic-v1');
      insert into auth.identities values('${fixture.logbookGuid}');
      insert into public.logbooks values('${fixture.logbookGuid}','${fixture.logbookGuid}',null);
      insert into public.key_envelopes values('${fixture.logbookGuid}','managed-service-v1',null,null,'AES-256-GCM','backup-fixture-v1',
        ${literal(fixture.keyEnvelope.ciphertext)},${literal(fixture.keyEnvelope.nonce)});
      insert into auth.typed(note,amount,precise,flag,raw,tags,document,happened,span,kind) values
        (${literal('tab\tline\n\\N\\. café 🛩')},2,900719925474099312345.123456789012,true,decode('00ff0a5c','hex'),
          ARRAY['comma,','quote"',null,''], '{"large":900719925474099312345,"nested":[null,true]}', '2026-10-18 12:34:56.123456+11', '1 mon 2 days 03:04:05.123456','first'),
        (null,3,null,false,null,ARRAY[]::text[],'null','infinity','-2 hours','second'),
        ('reject-trigger',4,-0.000000000001,null,''::bytea,null,null,'-infinity',null,null);
      create index typed_partial on auth.typed(lower(note)) where amount>1;
      alter table auth.typed enable row level security; alter table auth.typed force row level security;
      create policy typed_read on auth.typed for select to ${reader} using(amount>2);
      create function auth.check_note() returns trigger language plpgsql as $body$ begin
        if NEW.note='reject-trigger' then raise exception 'fixture trigger fired'; end if; return NEW; end $body$;
      create trigger typed_trigger before insert on auth.typed for each row execute function auth.check_note();
      alter table auth.typed owner to ${owner};
      grant select(note) on auth.typed to ${reader};
      ${tables.map(([table, view]) => `create view backup_export.${view} as select * from ${table};`).join('\n')}
      create view backup_export.sequence_state as ${sequences.map(name => `select '${name}'::text name,last_value::text,is_called from ${name}`).join(' union all ')};
      grant usage on schema backup_export to ${reader}; grant select on all tables in schema backup_export to ${reader};`);
    const common = e => [literal(fixture.logbookGuid), e.hostedRevision, literal(e.revisionId.value), literal(e.deviceId.value.slice(4)),
      literal(e.createdAt), e.schemaVersion - 1, literal(e.payloadCiphertext), literal(e.payloadNonce), literal(e.payloadTag), literal(e.payloadHash)].join(',');
    for (const e of fixture.operations) sql(source.database, `insert into public.operations values(${common(e)},
      ${literal(e.entryId.value)},${literal(JSON.stringify(e.parentRevisionIds.map(id => id.value)))}::jsonb);`);
    sql(source.database, `insert into public.configuration_revisions values(${common(fixture.configuration)});`);
    assert.throws(() => postgres(connection, 'psql', psqlArgs, 'select * from auth.typed;'), /failed/);
    assert.throws(() => postgres(connection, 'psql', psqlArgs, 'select rolpassword from pg_authid;'), /failed/);
    assert.throws(() => postgres(connection, 'psql', psqlArgs, "select nextval('public.unused_sequence');"), /failed/);
    assert.throws(() => postgres(connection, 'psql', psqlArgs, 'delete from backup_export.operations;'), /failed/);
    assert.equal(sql(source.database, `select rolbypassrls or rolsuper or rolcreaterole or rolcreatedb or rolreplication from pg_roles where rolname='${reader}';`), 'f');
    assert.equal(sql(source.database, `select count(*) from pg_auth_members where member='${reader}'::regrole;`), '0');

    const baseline = inspectDisposableMetadata(connection);
    const native = run(source.database, 'pg_dump', schemaArgs);
    buffers.push(native);
    // pg_dump assumes initdb's public-schema privileges; retain template0's schema.
    const contents = run(source.database, 'pg_restore', ['--list'], native).toString('utf8');
    writeFileSync(objectList, contents.split(/\r?\n/).filter(line => !/^\d+;\s+\d+\s+\d+\s+SCHEMA - public\s/.test(line)).join('\n'), { flag: 'wx', mode: 0o600 });
    listCreated = true;
    requireSameMetadata(baseline, inspectDisposableMetadata(connection));
    const keys = generateKeyPairSync('rsa', { modulusLength: 3072,
      publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
    const baselineArchive = sealArchive({ 'schema.dump': native, 'guard.json': Buffer.from(JSON.stringify(baseline)) }, keys.publicKey);
    const baselineDigest = hash(baselineArchive);
    const schemaBefore = normalizedSchema(source.database);
    async function capture(afterRows = () => {}) {
      requireSameMetadata(baseline, inspectDisposableMetadata(connection));
      const files = {};
      const expected = [];
      await withDatabaseSnapshot(connection, `lock table ${tables.map(([, view]) => 'backup_export.' + view).join(',')},backup_export.sequence_state in access share mode;
        select json_build_object('snapshot',pg_export_snapshot());`, async rows => {
        const snapshot = rows[0].snapshot;
        assert.match(snapshot, /^[A-F0-9-]+$/);
        const inSnapshot = statement => postgres(connection, 'psql', psqlArgs,
          `begin isolation level repeatable read read only; set transaction snapshot '${snapshot}'; ${settings} ${statement} rollback;`);
        for (const [, view, columns] of tables) {
          files[view + '.copy'] = inSnapshot(`copy (select ${columns} from backup_export.${view}) to stdout with(format text);`);
          buffers.push(files[view + '.copy']);
          expected.push(JSON.parse(inSnapshot(manifest('backup_export.' + view))));
        }
        afterRows();
        // Sequence state is deliberately a fresh non-MVCC observation after row export.
        files['sequences.json'] = postgres(connection, 'psql', psqlArgs,
          'select json_agg(s order by name) from backup_export.sequence_state s;');
        files['rows.json'] = Buffer.from(JSON.stringify(expected));
        files['guard.json'] = Buffer.from(JSON.stringify(inspectDisposableMetadata(connection)));
        requireSameMetadata(baseline, JSON.parse(files['guard.json']));
      });
      return files;
    }
    const files = await capture(() => {
      for (const [table] of tables)
        assert.throws(() => sql(source.database, `set lock_timeout='100ms'; truncate ${table} cascade;`), /command failed/);
      sql(source.database, "insert into storage.empty_fixture values(42); select nextval('auth.typed_id_seq');");
    });
    assert.equal(JSON.parse(files['rows.json'])[6].rows, 0, 'Rows committed after the held snapshot must be absent.');
    assert.equal(sql(source.database, 'select count(*) from storage.empty_fixture;'), '1');
    const capturedState = JSON.parse(files['sequences.json']);
    assert.equal(capturedState.find(s => s.name === 'public.unused_sequence').is_called, false);
    assert.ok(BigInt(capturedState.find(s => s.name === 'auth.typed_id_seq').last_value) > 13n, 'Cache history must exceed maximum copied identity.');
    const dataArchive = sealArchive(files, keys.publicKey);
    const dataDigest = hash(dataArchive);
    let restores = 0, rejected = 0;
    function restore(data = dataArchive, trustedDigest = hash(data), mutate = () => {}, verify = target => {
      assert.deepEqual(rowManifests(target.database), JSON.parse(files['rows.json']));
      assert.equal(normalizedSchema(target.database), schemaBefore);
      assert.equal(sql(target.database, `begin; set local role ${reader}; select count(*) from backup_export.typed; rollback;`), '3');
      assert.equal(sql(target.database, `begin; grant usage on schema auth to ${reader}; set local role ${reader};
        select count(note) from auth.typed; rollback;`), '1', 'Restored RLS and column privileges must actually apply.');
      const report = replaySupervised(local, target, environment);
      assert.deepEqual(report, { status: 'application-replayed', logbooks: [{ operations: 6, configurations: 1,
        activeEntries: 1, deletedEntries: 1, conflicts: 0, customFields: 1 }] });
      const restored = JSON.parse(readDisposable(local, target, `select json_build_object(
        'identityCount',(select count(*) from auth.identities),
        'operations',(select json_agg(o order by revision) from public.operations o),
        'configuration',(select row_to_json(c) from public.configuration_revisions c),
        'keyEnvelope',(select row_to_json(k) from public.key_envelopes k));`));
      const envelope = row => ({ hostedRevision: row.revision,revisionId: { value: row.portable_revision_id },
        deviceId: { value: 'dev_' + row.author_device_id.replaceAll('-', '') },createdAt: row.client_created_at,
        schemaVersion: (row.operation_format_version ?? row.configuration_format_version)+1,
        payloadCiphertext: row.payload_ciphertext,payloadNonce: row.payload_nonce,payloadTag: row.payload_tag,payloadHash: row.payload_hash });
      const exact = spawnSync('dotnet', [fileURLToPath(new URL('./ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll', import.meta.url)), 'verify'], {
        input: JSON.stringify({ ...restored, logbookGuid: fixture.logbookGuid,kek: fixture.kek,expectedSha: fixture.expectedSha,
          operations: restored.operations.map(o => ({ ...envelope(o),entryId: { value: o.entry_id },parentRevisionIds: o.parent_revision_ids.map(value => ({ value })) })),
          configuration: envelope(restored.configuration) }), encoding: 'utf8',windowsHide: true,timeout: 30000
      });
      assert.equal(exact.status, 0, 'Restored application document differs from original; private diagnostics suppressed.');
      assert.deepEqual(JSON.parse(exact.stdout), { status: 'verified',operations: 6,activeEntries: 1,deletedEntries: 1,commandHours: 2.75,settingsMatch: true });
      for (const state of capturedState) {
        const next = BigInt(sql(target.database, `select nextval('${state.name}');`));
        const increment = state.name === 'auth.typed_id_seq' ? 3n : 7n;
        assert.equal(next, BigInt(state.last_value) + (state.is_called ? increment : 0n));
        if (state.name === 'auth.typed_id_seq') assert.ok(next > 13n);
      }
      assert.throws(() => sql(target.database, "insert into auth.typed(note,amount) values('reject-trigger',1);"), /command failed/);
      assert.throws(() => sql(target.database, 'insert into auth.typed(amount) values(-1);'), /command failed/);
      assert.throws(() => sql(target.database, `insert into public.logbooks values('${randomUUID()}','${randomUUID()}',null);`), /command failed/);
      sql(target.database, 'insert into auth.typed(amount) values(5);');
      assert.equal(sql(target.database, "select note||':'||doubled from auth.typed order by id desc limit 1;"), 'default note:10');
      return report;
    }) {
      assert.equal(hash(baselineArchive), baselineDigest, 'Baseline digest mismatch.');
      assert.equal(hash(data), trustedDigest, 'Data digest mismatch.');
      const base = openArchive(baselineArchive, keys.privateKey);
      const contents = openArchive(data, keys.privateKey);
      const target = receipt();
      let created = false;
      try {
        requireSameMetadata(JSON.parse(base['guard.json']), JSON.parse(contents['guard.json']));
        assert.deepEqual(Object.keys(contents).sort(), [...tables.map(([, view]) => view + '.copy'), 'sequences.json','rows.json','guard.json'].sort());
        const state = JSON.parse(contents['sequences.json']);
        assert.deepEqual(state.map(s => s.name).sort(), [...sequences].sort());
        assert.ok(state.every(s => /^\d+$/.test(s.last_value) && BigInt(s.last_value)>0n && typeof s.is_called==='boolean'));
        create(target); created = true;
        readDisposable(local, target, 'select 1;');
        run(target.database, 'psql', [...psqlArgs, '--single-transaction'], schemaSql(base['schema.dump'], 'pre-data'));
        for (const [table, view, columns] of tables)
          run(target.database, 'psql', [...psqlArgs, '-c', settings, '-c', `copy ${table}(${columns}) from stdin with(format text);`], contents[view + '.copy']);
        for (const s of state) sql(target.database, `select setval('${s.name}',${s.last_value},${s.is_called});`);
        run(target.database, 'psql', [...psqlArgs, '--single-transaction'], schemaSql(base['schema.dump'], 'post-data'));
        assert.deepEqual(rowManifests(target.database), JSON.parse(contents['rows.json']));
        assert.equal(normalizedSchema(target.database), schemaBefore);
        mutate(target);
        const result = verify(target);
        restores++;
        return result;
      } finally {
        if (created) dropDisposable(local, target);
        Object.values(base).forEach(b => b.fill(0));
        Object.values(contents).forEach(b => b.fill(0));
      }
    }
    const databaseCount = () => sql('postgres', "select count(*) from pg_database where datname like 'elb_backup_fixture_%';");
    const before = databaseCount();
    restore(dataArchive, dataDigest);
    assert.equal(databaseCount(), before);
    const reject = action => { assert.throws(action); assert.equal(databaseCount(), before, 'Failed restore leaked a marked database.'); rejected++; };
    for (const missing of ['operations.copy', 'sequences.json', 'rows.json']) {
      const incomplete = { ...files }; delete incomplete[missing];
      reject(() => restore(sealArchive(incomplete, keys.publicKey)));
    }
    reject(() => restore(dataArchive, '0'.repeat(64)));
    const damaged = Buffer.from(dataArchive); damaged[damaged.length - 1] ^= 1;
    reject(() => restore(damaged));
    reject(() => restore(sealArchive({ ...files, 'operations.copy': Buffer.alloc(0) }, keys.publicKey)));
    reject(() => restore(sealArchive({ ...files, 'typed.copy': Buffer.from('invalid typed row\n') }, keys.publicKey)));
    reject(() => restore(sealArchive({ ...files, 'sequences.json': Buffer.from('[]') }, keys.publicKey)));
    reject(() => restore(sealArchive({ ...files, 'sequences.json': Buffer.from(JSON.stringify(capturedState.map(s => ({ ...s, last_value: '-1' })))) }, keys.publicKey)));
    reject(() => restore(sealArchive({ ...files, 'sequences.json': Buffer.from(JSON.stringify(capturedState.map(s => ({ ...s, last_value: '1' })))) }, keys.publicKey)));
    // Correct row/DDL verification alone cannot substitute for application replay.
    for (const table of ['public.configuration_revisions','public.key_envelopes'])
      reject(() => restore(dataArchive, dataDigest, target => sql(target.database, `delete from ${table};`),
        target => replaySupervised(local, target, environment)));
    reject(() => restore(dataArchive, dataDigest, target => sql(target.database, "update public.operations set payload_tag='AAAAAAAAAAAAAAAAAAAAAA==' where revision=1;"),
      target => replaySupervised(local, target, environment)));
    reject(() => restore(dataArchive, dataDigest, () => {}, target => replaySupervised(local, target,
      Buffer.from(`RECOVERY_KEK_BASE64=${Buffer.alloc(32).toString('base64')}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1`))));
    sql(source.database, 'alter table auth.typed no force row level security;');
    await assert.rejects(capture(), /drift/); rejected++;
    sql(source.database, 'alter table auth.typed force row level security;');
    requireSameMetadata(baseline, inspectDisposableMetadata(connection));
    await assert.rejects(capture(() => sql(source.database, "create or replace function auth.check_note() returns trigger language plpgsql as 'begin return NEW; end';")), /drift/);
    rejected++;
    assert.equal(databaseCount(), before);
    t.diagnostic(JSON.stringify({ status: 'synthetic-hybrid-restored', tables: tables.length, sequences: sequences.length,
      restores, rejected, applicationReplay: '6 revisions; 1 active, 1 deleted, 0 conflicts; custom fields/settings verified',
      ownershipAclRlsIndexesConstraintsGeneratedValuesMatched: true, targetsCleanedAfterSuccessAndFailure: true,
      scope: 'same PG17 runtime and cluster roles; no hosted, scheduler, service or phone recovery claim' }));
  } finally {
    environment?.fill(0);
    buffers.forEach(b => b.fill(0));
    if (listCreated) unlinkSync(objectList);
    if (sourceCreated) dropDisposable(local, source);
    if (rolesCreated) sql('postgres', `drop role ${reader}; drop role ${owner};`);
  }
});
