import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, generateKeyPairSync, createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { buildReplayCheck, replaySupervised } from './replay-supervised.mjs';
import { dropDisposable } from './restore.mjs';
import { buildDatabaseArchive } from './archive.mjs';
import { tableManifestSql } from './orchestrate.mjs';
import { verifySupervisedArchive } from './verify-supervised.mjs';

test('USB-capsule keys replay production-shaped rows and reject missing identity, envelope, settings, history and wrong keys', () => {
  buildReplayCheck();
  const seeded = spawnSync('dotnet', [fileURLToPath(new URL('./ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll', import.meta.url)), 'seed'],
    { encoding: 'utf8', windowsHide: true, timeout: 30000 });
  assert.equal(seeded.status, 0);
  const fixture = JSON.parse(seeded.stdout);
  const local = { host: '127.0.0.1', port: Number(process.env.ELB_BACKUP_TEST_PORT ?? 54322), user: 'postgres', password: 'postgres',
    pgBin: process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin' };
  const target = { database: 'elb_backup_fixture_' + randomUUID().replaceAll('-', ''), token: randomUUID().replaceAll('-', '') };
  const sql = (database, input) => {
    const result = spawnSync(local.pgBin + '/psql' + (process.platform === 'win32' ? '.exe' : ''),
      ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', database], { input, encoding: 'utf8', windowsHide: true, timeout: 10000,
        env: { ...process.env, PGHOST: local.host, PGPORT: String(local.port), PGUSER: local.user, PGPASSWORD: local.password, PGSSLMODE: 'disable', PGOPTIONS: '' } });
    assert.equal(result.status, 0, 'Synthetic SQL failed; diagnostics suppressed.');
    return result.stdout.trim();
  };
  const literal = value => "'" + String(value).replaceAll("'", "''") + "'";
  const environment = Buffer.from(`RECOVERY_KEK_BASE64=${fixture.kek}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1\n`);
  let created = false;
  try {
    sql('postgres', `create database ${target.database};`); created = true;
    sql('postgres', `comment on database ${target.database} is 'flightlogx-backup-restore:${target.token}';`);
    sql(target.database, `create schema auth; create table auth.identities(user_id uuid);
      insert into auth.identities values('${fixture.logbookGuid}');
      create table public.logbooks(logbook_id uuid, owner_account_id uuid, deleted_at timestamptz);
      insert into public.logbooks values('${fixture.logbookGuid}','${fixture.logbookGuid}',null);
      create table public.key_envelopes(logbook_id uuid, recovery_method text, revoked_at timestamptz, expires_at timestamptz,
        wrapping_algorithm text, key_version_id text, ciphertext text, nonce text);
      insert into public.key_envelopes values('${fixture.logbookGuid}','managed-service-v1',null,null,'AES-256-GCM',
        'backup-fixture-v1',${literal(fixture.keyEnvelope.ciphertext)},${literal(fixture.keyEnvelope.nonce)});
      create table public.operations(logbook_id uuid, revision bigint, portable_revision_id text, author_device_id uuid,
        client_created_at timestamptz, operation_format_version integer, payload_ciphertext text, payload_nonce text,
        payload_tag text, payload_hash text, entry_id text, parent_revision_ids jsonb);
      create table public.configuration_revisions(logbook_id uuid, revision bigint, portable_revision_id text, author_device_id uuid,
        client_created_at timestamptz, configuration_format_version integer, payload_ciphertext text, payload_nonce text,
        payload_tag text, payload_hash text);`);
    const common = e => [literal(fixture.logbookGuid), e.hostedRevision, literal(e.revisionId.value),
      literal(e.deviceId.value.slice(4)), literal(e.createdAt), e.schemaVersion - 1, literal(e.payloadCiphertext),
      literal(e.payloadNonce), literal(e.payloadTag), literal(e.payloadHash)].join(',');
    for (const e of fixture.operations) sql(target.database, `insert into public.operations values(${common(e)},
      ${literal(e.entryId.value)},${literal(JSON.stringify(e.parentRevisionIds.map(id => id.value)))}::jsonb);`);
    sql(target.database, `insert into public.configuration_revisions values(${common(fixture.configuration)});`);
    assert.deepEqual(replaySupervised(local, target, environment), { status: 'application-replayed',
      logbooks: [{ operations: 6, configurations: 1, activeEntries: 1, deletedEntries: 1, conflicts: 0, customFields: 1 }] });
    sql(target.database, `create table auth.users(id uuid); create schema storage; create table storage.buckets(id text);
      create schema supabase_migrations; create table supabase_migrations.schema_migrations(version text);
      ${['accounts','logbook_memberships','devices','operation_acks','pairing_requests','security_events','workbook_migrations']
        .map(name => `create table public.${name}(id integer);`).join('\n')}`);
    const keys = generateKeyPairSync('rsa', { modulusLength: 3072,
      publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
    const verification = { accessMode: 'supervised-owner', extensions: [],
      tables: sql(target.database, tableManifestSql).split(/\r?\n/).map(line => JSON.parse(line)) };
    const archive = buildDatabaseArchive({ ...local, database: target.database, disposable: true }, keys.publicKey,
      { 'verification.json': Buffer.from(JSON.stringify(verification)) });
    const digest = createHash('sha256').update(archive).digest('hex');
    const databasesBefore = sql('postgres', "select count(*) from pg_database where datname like 'elb_backup_fixture_%';");
    assert.equal(verifySupervisedArchive(archive, keys.privateKey, digest, local, environment).applicationReplay.status, 'application-replayed');
    assert.throws(() => verifySupervisedArchive(archive, keys.privateKey, digest, local,
      Buffer.from(`RECOVERY_KEK_BASE64=${Buffer.alloc(32).toString('base64')}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1`)));
    assert.equal(sql('postgres', "select count(*) from pg_database where datname like 'elb_backup_fixture_%';"), databasesBefore,
      'Successful and failed application replay must both remove their restore target.');
    assert.throws(() => replaySupervised(local, target, Buffer.from(`RECOVERY_KEK_BASE64=${Buffer.alloc(32).toString('base64')}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1`)));
    assert.throws(() => replaySupervised(local, target, Buffer.from(`RECOVERY_KEK_BASE64=${fixture.kek}\nRECOVERY_KEY_VERSION_ID=wrong-version`)));
    for (const mutation of ['delete from auth.identities', 'delete from public.key_envelopes',
      'update public.key_envelopes set revoked_at=now()', 'delete from public.configuration_revisions',
      'delete from public.operations where revision=1', "update public.operations set payload_tag='AAAAAAAAAAAAAAAAAAAAAA==' where revision=1"]) {
      // Roll back each negative case so the next one starts from the same valid fixture.
      const snapshot = sql(target.database, `select json_build_object('identities',(select json_agg(i) from auth.identities i),
        'keys',(select json_agg(k) from public.key_envelopes k),'operations',(select json_agg(o) from public.operations o),
        'configurations',(select json_agg(c) from public.configuration_revisions c));`);
      sql(target.database, mutation + ';');
      assert.throws(() => replaySupervised(local, target, environment), undefined, mutation);
      for (const [table, name] of [['auth.identities','identities'], ['public.key_envelopes','keys'],
        ['public.operations','operations'], ['public.configuration_revisions','configurations']])
        sql(target.database, `delete from ${table}; insert into ${table} select * from json_populate_recordset(null::${table},${literal(snapshot)}::json->'${name}');`);
    }
  } finally {
    environment.fill(0);
    if (created) dropDisposable(local, target);
  }
});
