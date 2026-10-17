import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, generateKeyPairSync, createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { exportDatabase, buildDatabaseArchive, openArchive } from './archive.mjs';
import { restoreDisposable, readDisposable, dropDisposable } from './restore.mjs';

test('exports and restores a disposable database, then replays its recovered application history and settings', () => {
  const database = 'elb_backup_fixture_' + randomUUID().replaceAll('-', '');
  const directory = mkdtempSync(join(tmpdir(), 'elb-backup-fixture-'));
  const pgBin = process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin';
  const suffix = process.platform === 'win32' ? '.exe' : '';
  const env = { ...process.env, PGHOST: '127.0.0.1', PGPORT: '54322', PGUSER: 'postgres', PGPASSWORD: 'postgres', PGSSLMODE: 'disable' };
  function psql(target, sql) {
    const result = spawnSync(join(pgBin, 'psql' + suffix), ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', target, '-A', '-t'], {
      env, input: sql, encoding: 'utf8', windowsHide: true, timeout: 20000
    });
    assert.ok(result.status === 0, 'Disposable fixture SQL failed; raw diagnostics suppressed.');
    return result.stdout.trim();
  }
  let created = false;
  let restored;
  const localConnection = { host: '127.0.0.1', port: 54322, user: 'postgres', password: 'postgres', pgBin };
  try {
    const replayProject = resolve('tools/hosted-backup/ReplayCheck/ReplayCheck.csproj');
    const build = spawnSync('dotnet', ['build', replayProject, '--verbosity', 'quiet'], { encoding: 'utf8', windowsHide: true, timeout: 120000 });
    assert.ok(build.status === 0, 'Application replay harness build failed.');
    const replayDll = resolve('tools/hosted-backup/ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll');
    const replay = (mode, input) => spawnSync('dotnet', [replayDll, mode], {
      input: input ? JSON.stringify(input) : undefined, encoding: 'utf8', windowsHide: true, timeout: 30000
    });
    const seeded = replay('seed'); assert.equal(seeded.status, 0);
    const fixture = JSON.parse(seeded.stdout);
    const sqlJson = value => "'" + JSON.stringify(value).replaceAll("'", "''") + "'::jsonb";
    psql('postgres', `create database ${database};`); created = true;
    psql(database, `create schema auth; create schema storage; create schema supabase_migrations;
      create table auth.users(id integer primary key, identity_marker text);
      create table auth.identities(id integer primary key, user_id integer references auth.users);
      insert into auth.users values (1,'synthetic-account'); insert into auth.identities values(2,1);
      create table public.accounts(id integer primary key references auth.users);
      create table public.logbooks(id integer primary key, account_id integer references public.accounts);
      create table public.logbook_memberships(id integer primary key);
      create table public.devices(id integer primary key);
      create table public.operations(id integer primary key, payload_ciphertext text, envelope jsonb);
      create table public.operation_acks(id integer primary key);
      create table public.pairing_requests(id integer primary key);
      create table public.key_envelopes(id integer primary key, ciphertext text, envelope jsonb);
      create table public.security_events(id integer primary key);
      create table public.workbook_migrations(id integer primary key);
      create table public.configuration_revisions(id integer primary key, payload_ciphertext text, envelope jsonb);
      create table storage.buckets(id text primary key);
      create table supabase_migrations.schema_migrations(version text primary key);
      insert into public.accounts values(1); insert into public.logbooks values(3,1);
      ${fixture.operations.map((e, i) => `insert into public.operations values(${i + 1},'${e.payloadCiphertext}',${sqlJson(e)});`).join('\n')}
      insert into public.configuration_revisions values(9,'${fixture.configuration.payloadCiphertext}',${sqlJson(fixture.configuration)});
      insert into public.key_envelopes values(10,'${fixture.keyEnvelope.ciphertext}',${sqlJson(fixture.keyEnvelope)});
      alter table public.operations enable row level security;
      create policy fixture_policy on public.operations for select using (true);
      insert into supabase_migrations.schema_migrations values('synthetic-schema-v1');`);
    const connection = { host: '127.0.0.1', port: 54322, database, user: 'postgres', password: 'postgres', pgBin, disposable: true };
    const preservedTables = ['auth.users', 'auth.identities', 'public.accounts', 'public.logbooks', 'public.logbook_memberships',
      'public.devices', 'public.operations', 'public.operation_acks', 'public.pairing_requests', 'public.key_envelopes',
      'public.security_events', 'public.workbook_migrations', 'public.configuration_revisions',
      'storage.buckets', 'supabase_migrations.schema_migrations'];
    const tableRead = table => `select coalesce(jsonb_agg(to_jsonb(t) order by to_jsonb(t)::text),'[]'::jsonb)::text from ${table} t;`;
    const originalRows = Object.fromEntries(preservedTables.map(table => [table, psql(database, tableRead(table))]));
    const identity = generateKeyPairSync('rsa', { modulusLength: 3072,
      publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
    const archive = buildDatabaseArchive(connection, identity.publicKey, { 'reference/test.txt': Buffer.from('synthetic-reference') });
    const files = openArchive(archive, identity.privateKey);
    assert.equal(files['database.dump'].subarray(0, 5).toString(), 'PGDMP');
    assert.ok(files['reference/test.txt'].equals(Buffer.from('synthetic-reference')));
    assert.equal(psql(database, 'select count(*) from public.operations;'), '6');
    assert.equal(psql(database, 'select count(*) from auth.identities;'), '1');
    assert.throws(() => buildDatabaseArchive(connection, identity.publicKey, { 'database.dump': Buffer.from('replacement') }), /replaces required/);
    const configPath = join(directory, 'connection.json');
    const recipientPath = join(directory, 'recipient.pem');
    const privatePath = join(directory, 'synthetic-private.pem');
    const outputPath = join(directory, 'synthetic.elbbackup');
    writeFileSync(configPath, JSON.stringify(connection)); writeFileSync(recipientPath, identity.publicKey); writeFileSync(privatePath, identity.privateKey);
    const cli = resolve('tools/hosted-backup/cli.mjs');
    const run = args => spawnSync(process.execPath, [cli, ...args], { encoding: 'utf8', windowsHide: true, timeout: 30000 });
    const exportArgs = ['export', '--connection', configPath, '--recipient', recipientPath, '--output', outputPath];
    assert.equal(run(exportArgs).status, 0);
    const before = createHash('sha256').update(readFileSync(outputPath)).digest('hex');
    const repeated = run(exportArgs); assert.equal(repeated.status, 1);
    assert.equal(createHash('sha256').update(readFileSync(outputPath)).digest('hex'), before);
    assert.equal(run(['verify', '--archive', outputPath, '--identity', privatePath]).status, 0);
    assert.equal(repeated.stderr.includes('synthetic-account'), false);
    const expectedSha = createHash('sha256').update(archive).digest('hex');
    const beforeDatabases = psql('postgres', 'select count(*) from pg_database;');
    assert.throws(() => restoreDisposable(archive, identity.privateKey, '0'.repeat(64), localConnection), /trusted job digest/);
    const wrong = generateKeyPairSync('rsa', { modulusLength: 3072 }).privateKey.export({ type: 'pkcs8', format: 'pem' });
    assert.throws(() => restoreDisposable(archive, wrong, expectedSha, localConnection), /could not be authenticated/);
    assert.equal(psql('postgres', 'select count(*) from pg_database;'), beforeDatabases);
    assert.throws(() => restoreDisposable(archive, identity.privateKey, expectedSha,
      { ...localConnection, host: 'remote.example.com' }), /disposable local Supabase/);
    restored = restoreDisposable(archive, identity.privateKey, expectedSha, localConnection);
    for (const table of preservedTables)
      assert.ok(readDisposable(localConnection, restored, tableRead(table)) === originalRows[table], `Restored fixture differs in ${table}`);
    const data = JSON.parse(readDisposable(localConnection, restored, `select json_build_object(
      'identityCount', (select count(*) from auth.identities),
      'operations', (select json_agg(envelope order by id) from public.operations),
      'configuration', (select envelope from public.configuration_revisions where id=9),
      'keyEnvelope', (select envelope from public.key_envelopes where id=10));`));
    const restoredInput = { ...data, kek: fixture.kek, expectedSha: fixture.expectedSha, logbookGuid: fixture.logbookGuid };
    const verified = replay('verify', restoredInput);
    assert.ok(verified.status === 0, 'Application could not replay restored archive.');
    assert.deepEqual(JSON.parse(verified.stdout), { status: 'verified', operations: 6, activeEntries: 1,
      deletedEntries: 1, commandHours: 2.75, settingsMatch: true });
    for (const changed of [{ identityCount: 0 }, { keyEnvelope: null }, { kek: Buffer.alloc(32).toString('base64') },
      { logbookGuid: randomUUID() }, { operations: data.operations.slice(1) }, { configuration: null }])
      assert.equal(replay('verify', { ...restoredInput, ...changed }).status, 1);
    assert.equal(readDisposable(localConnection, restored, "select relrowsecurity from pg_class where oid='public.operations'::regclass;"), 't');
    assert.equal(readDisposable(localConnection, restored, "select count(*) from pg_policy where polname='fixture_policy';"), '1');
    assert.equal(readDisposable(localConnection, restored, 'select count(*) from public.logbooks l join public.accounts a on a.id=l.account_id join auth.users u on u.id=a.id;'), '1');
    assert.throws(() => readDisposable(localConnection, { ...restored, token: '0'.repeat(32) }, 'select 1;'), /identity does not match/);
    assert.throws(() => dropDisposable(localConnection, { ...restored, database: 'postgres' }), /Invalid disposable/);
    psql(restored.database, 'delete from auth.identities;');
    assert.equal(replay('verify', { ...restoredInput, identityCount: Number(readDisposable(localConnection, restored,
      'select count(*) from auth.identities;')) }).status, 1);
    psql(database, 'drop table auth.identities;');
    assert.throws(() => exportDatabase(connection), /missing required Auth table identities/);
    psql(database, 'drop table public.operations;');
    assert.throws(() => exportDatabase(connection), /missing required table operations/);
  } finally {
    if (restored) dropDisposable(localConnection, restored);
    assert.match(database, /^elb_backup_fixture_[a-f0-9]{32}$/);
    if (created) psql('postgres', `drop database ${database};`);
    const resolved = resolve(directory);
    assert.ok(resolved.startsWith(resolve(tmpdir()) + (process.platform === 'win32' ? '\\' : '/')));
    rmSync(resolved, { recursive: true, force: true });
  }
});
