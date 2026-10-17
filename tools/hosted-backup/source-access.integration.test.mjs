import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { postgres } from './postgres.mjs';
import { sourceRoleSql, inspectSourceAccess, initializeSourceAccess } from './source-access.mjs';
import { exportDatabase } from './archive.mjs';

test('restricted source exports complete RLS data and roles; denied writes and boundary escapes remain denied with read-only disabled', () => {
  const database = 'elb_backup_fixture_' + randomUUID().replaceAll('-', '');
  const pgBin = process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin';
  const password = randomBytes(32).toString('hex');
  const owner = { host: '127.0.0.1', port: 54322, database, user: 'postgres', password: 'postgres', pgBin, disposable: true };
  function admin(target, sql) {
    const r = spawnSync(pgBin + '/psql' + (process.platform === 'win32' ? '.exe' : ''), ['-X','-w','-At','-v','ON_ERROR_STOP=1','-d',target], {
      env: { ...process.env, PGHOST: '127.0.0.1', PGPORT: '54322', PGUSER: 'postgres', PGPASSWORD: 'postgres', PGSSLMODE: 'disable' },
      input: sql, encoding: 'utf8', windowsHide: true, timeout: 20000 });
    assert.equal(r.status, 0, 'Synthetic source fixture SQL failed: ' + r.stderr.replaceAll(password, '[redacted]')); return r.stdout.trim();
  }
  let created = false;
  let roleCreated = false;
  try {
    assert.equal(admin('postgres', "select count(*) from pg_roles where rolname='flightlogx_backup';"), '0', 'Do not reuse any existing source role.');
    admin('postgres', `create database ${database};`); created = true;
    const tables = ['accounts','logbooks','logbook_memberships','devices','operations','operation_acks','pairing_requests',
      'key_envelopes','security_events','workbook_migrations','configuration_revisions'];
    admin(database, `create schema auth; create schema storage; create schema supabase_migrations; create schema vault;
      create table vault.secret(value text); insert into vault.secret values('outside-boundary');
      create table auth.users(id int); create table auth.identities(id int);
      create table storage.buckets(id int); create table supabase_migrations.schema_migrations(id int);
      ${tables.map(t => `create table public.${t}(id int); insert into public.${t} values(1),(2);
        alter table public.${t} enable row level security;`).join('\n')}
      create sequence public.fixture_sequence;
      create schema net; create table net.http_request_queue(id int);
      grant usage on schema net to public; grant all on net.http_request_queue to public;`);
    try { assert.throws(() => initializeSourceAccess(owner, password), /write_relations/); }
    finally { roleCreated = admin(database,"select count(*) from pg_roles where rolname='flightlogx_backup';") === '1'; }
    assert.equal(admin(database,"select rolcanlogin from pg_roles where rolname='flightlogx_backup';"),'f');
    admin(database,'revoke all on net.http_request_queue from public;');
    inspectSourceAccess(owner, true);
    admin(database,'alter role flightlogx_backup login;');
    const reader = { ...owner, user: 'flightlogx_backup', password };
    inspectSourceAccess(reader);
    assert.equal(postgres(reader,'psql',['-X','-w','-At'], 'select count(*) from public.operations;').toString().trim(),'2');
    const files = exportDatabase(reader);
    assert.match(files['roles.sql'].toString(), /CREATE ROLE flightlogx_backup/);
    assert.equal(files['roles.sql'].includes(Buffer.from(password)), false);
    const toc = postgres(reader,'pg_restore',['--list'],files['database.dump']).toString();
    for (const t of tables) assert.ok(toc.includes(`TABLE DATA public ${t} `));
    const restoredSql = postgres(reader,'pg_restore',['--file=-','--data-only','--no-owner','--no-privileges'],files['database.dump']).toString();
    assert.match(restoredSql, /COPY public.operations \(id\) FROM stdin;\r?\n1\r?\n2\r?\n/);
    for (const sql of ['update public.operations set id=3','delete from public.operations','truncate public.operations',
      'insert into public.operations values(3)','alter table public.operations add column x int',
      'create table public.escape(id int)', 'create schema escape','create role escape', 'set role postgres',
      'set role service_role', 'set session authorization postgres',
      'select * from vault.secret', "select nextval('public.fixture_sequence')", 'select rolpassword from pg_authid'])
      assert.throws(() => postgres(reader,'psql',['-X','-w','-At','-v','ON_ERROR_STOP=1'],
        `begin; set local transaction_read_only=off; ${sql}; rollback;`), /failed/, sql);
    assert.equal(admin(database,'select count(*) from public.operations where id in (1,2);'),'2');
    admin(database,'grant usage on schema vault to flightlogx_backup; grant select on vault.secret to flightlogx_backup;');
    assert.throws(() => exportDatabase(reader), /outside_reads/);
    admin(database,`revoke usage on schema vault from flightlogx_backup;
      create function public.unsafe() returns void language sql security definer as 'delete from public.operations';`);
    assert.throws(() => exportDatabase(reader), /executable_definers/);
    admin(database,'revoke execute on function public.unsafe() from public; revoke select on public.operations from flightlogx_backup;');
    assert.throws(() => exportDatabase(reader), /unreadable_relations/);
    assert.throws(() => sourceRoleSql("bad'; grant postgres to flightlogx_backup;--"));
    Object.values(files).forEach(b => b.fill(0));
  } finally {
    if (created) admin('postgres',`drop database ${database};`);
    if (roleCreated) admin('postgres','revoke connect on database postgres from flightlogx_backup; drop role flightlogx_backup;');
  }
});
