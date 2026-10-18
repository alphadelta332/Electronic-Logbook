import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, randomUUID } from 'node:crypto';
import { execFile, spawnSync } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { createServer } from 'node:http';
import { gzipSync } from 'node:zlib';
import { buildHostedArchive, buildSupervisedArchive, tableManifestSql } from './orchestrate.mjs';
import { openArchive, sealArchive } from './archive.mjs';
import { verifySupervisedArchive } from './verify-supervised.mjs';
import { restoreDisposable, readDisposable, dropDisposable } from './restore.mjs';

test('bundles a coherent disposable snapshot and rejects incomplete/changing inputs without a success file', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'elb-backup-orchestration-'));
  const root = join(directory, 'sources'); mkdirSync(root);
  const database = 'elb_backup_fixture_' + randomUUID().replaceAll('-', '');
  const pgBin = process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin';
  const suffix = process.platform === 'win32' ? '.exe' : '';
  const local = { host: '127.0.0.1', port: Number(process.env.ELB_BACKUP_TEST_PORT ?? 54322), user: 'postgres', password: 'postgres', pgBin };
  function sql(target, input) {
    const result = spawnSync(join(pgBin, 'psql' + suffix), ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', target], {
      env: { ...process.env, PGHOST: local.host, PGPORT: String(local.port), PGUSER: local.user,
        PGPASSWORD: local.password, PGSSLMODE: 'disable', PGOPTIONS: '' },
      input, encoding: 'utf8', windowsHide: true, timeout: 10000
    });
    assert.equal(result.status, 0, 'Synthetic SQL failed; diagnostics suppressed.');
    return result.stdout.trim();
  }
  const sourceNames = ['supabase/config.toml', 'supabase/migrations/20261017000000_fixture.sql',
    'supabase/activity-worker/worker.mjs', 'supabase/activity-worker/wrangler.toml', 'tools/Publish-AirportCatalog.ps1',
    'supabase/functions/airport-catalog/index.ts', 'supabase/functions/recovery-envelope/index.ts',
    'tools/hosted-backup/cli.mjs', 'tools/hosted-backup/ReplayCheck/ReplayCheck.csproj',
    'updater/src/ElectronicLogbook.Portable/ElectronicLogbook.Portable.csproj', 'versions.properties'];
  for (const name of sourceNames) {
    mkdirSync(dirname(join(root, name)), { recursive: true }); writeFileSync(join(root, name), 'synthetic source\n');
  }
  const git = args => {
    const r = spawnSync('git', ['-C', root, ...args], { windowsHide: true }); assert.equal(r.status, 0);
  };
  git(['init', '--quiet']); git(['add', '.']);
  git(['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'fixture']);
  const sourcePath = join(root, 'supabase/config.toml');
  writeFileSync(sourcePath, 'synthetic uncommitted source\n');
  const airports = Array.from({ length: 5000 }, (_, i) => ({ i: 'X' + String(i).padStart(4, '0'), n: 'Synthetic ' + i, y: 0, x: 0 }));
  const bytes = gzipSync(Buffer.from(JSON.stringify(airports)));
  const hash = createHash('sha256').update(bytes).digest('hex');
  const path = `snapshots/${hash}.json.gz`;
  const manifest = Buffer.from(JSON.stringify({ schemaVersion: 1, cycle: '2026-10-01', publishedAt: '2026-10-17T00:00:00Z',
    sha256: hash, path, count: 5000 }));
  const objects = { 'manifest.json': manifest, [path]: bytes, 'seed.json.gz': bytes };
  let mode = 'normal';
  let requests = 0;
  let mutated = false;
  const server = createServer((request, response) => {
    requests++;
    const name = new URL(request.url, 'http://localhost').pathname.split('/airport-catalog/')[1];
    if (!mutated && name === 'manifest.json') {
      mutated = true;
      if (mode === 'concurrent-data') sql(database, "insert into public.operations values(2,'later-value');");
      if (mode === 'schema-change') sql(database, 'create table public.late_schema(id integer);');
      if (mode === 'migration-change') sql(database, "insert into supabase_migrations.schema_migrations values('later-migration');");
      if (mode === 'source-change') writeFileSync(sourcePath, readFileSync(sourcePath).toString() + 'changed during export\n');
      if (mode === 'storage-change') sql(database, "update storage.objects set metadata=metadata||'{\"changed\":true}'::jsonb where name='seed.json.gz';");
    }
    if ((mode === 'missing' && name === path) || !objects[name]) { response.writeHead(404).end(); return; }
    if (mode === 'oversized' && name === 'manifest.json') { response.end(Buffer.alloc(4097)); return; }
    if (mode === 'redirect') { response.writeHead(302, { location: 'http://example.invalid/unexpected' }).end(); return; }
    response.end(mode === 'corrupt' && name === path ? Buffer.from('damaged') : objects[name]);
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const connection = { ...local, database, disposable: true, projectRef: 'a'.repeat(20),
    fixtureObjectOrigin: `http://127.0.0.1:${server.address().port}` };
  const keys = generateKeyPairSync('rsa', { modulusLength: 3072,
    publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
  let created = false;
  let restored;
  try {
    sql('postgres', `create database ${database};`); created = true;
    const tables = ['accounts','logbooks','logbook_memberships','devices','operation_acks','pairing_requests',
      'key_envelopes','security_events','workbook_migrations','configuration_revisions'];
    sql(database, `create schema auth; create schema storage; create schema supabase_migrations;
      create extension citext with schema public;
      create table auth.users(id integer); create table auth.identities(id integer);
      ${tables.map(name => `create table public.${name}(id integer);`).join('\n')}
      create table public.operations(id integer, payload_ciphertext public.citext);
      insert into public.operations values(1,'synthetic-snapshot-value');
      create table storage.buckets(id text, public boolean);
      insert into storage.buckets values('airport-catalog',true);
      create table storage.objects(bucket_id text,name text,metadata jsonb);
      create table supabase_migrations.schema_migrations(version text);
      insert into supabase_migrations.schema_migrations values('fixture');
      ${Object.entries(objects).map(([name, content]) => `insert into storage.objects values('airport-catalog','${name}',
        '{"eTag":"${createHash('md5').update(content).digest('hex')}"}');`).join('\n')}`);
    mode = 'concurrent-data';
    const archive = await buildHostedArchive(connection, keys.publicKey, root);
    const files = openArchive(archive, keys.privateKey);
    const verification = JSON.parse(files['verification.json']);
    assert.equal(sql(database, 'select count(*) from public.operations;'), '2');
    assert.equal(verification.tables.find(t => t.table === 'public.operations').rows, 1);
    assert.equal(verification.sources.dirty, true);
    assert.equal(files['sources/supabase/config.toml'].toString(), 'synthetic uncommitted source\n');
    for (const [name, value] of Object.entries(objects)) assert.ok(files['reference/' + name].equals(value));
    assert.equal(archive.includes(Buffer.from('synthetic-snapshot-value')), false);
    restored = restoreDisposable(archive, keys.privateKey, createHash('sha256').update(archive).digest('hex'), local);
    const restoredTables = readDisposable(local, restored, tableManifestSql).split(/\r?\n/).map(line => JSON.parse(line));
    assert.deepEqual(restoredTables, verification.tables);
    assert.equal(readDisposable(local, restored, "select n.nspname from pg_extension e join pg_namespace n on n.oid=e.extnamespace where e.extname='citext';"), 'public');
    dropDisposable(local, restored); restored = undefined;

    const configPath = join(directory, 'connection.json'); const recipient = join(directory, 'public.pem');
    writeFileSync(configPath, JSON.stringify(connection)); writeFileSync(recipient, keys.publicKey);
    const output = join(directory, 'bundle.elbbackup');
    const args = [resolve('tools/hosted-backup/cli.mjs'), 'bundle', '--connection', configPath, '--recipient', recipient,
      '--source', root, '--output', output];
    const run = () => promisify(execFile)(process.execPath, args, { windowsHide: true, timeout: 60000 });
    mode = 'normal'; mutated = false;
    const result = await run();
    assert.equal(JSON.parse(result.stdout).status, 'exported');
    const original = readFileSync(output);
    await assert.rejects(run()); assert.ok(readFileSync(output).equals(original));
    rmSync(output);

    const failures = { missing: /download failed/, corrupt: /do not match/, oversized: /size limit/,
      redirect: /fetch failed/, 'schema-change': /Schema changed/, 'migration-change': /Migration history changed/,
      'source-change': /source changed/, 'storage-change': /Storage metadata changed/ };
    function resetMutation(failure) {
      if (failure === 'schema-change') sql(database, 'drop table public.late_schema;');
      if (failure === 'migration-change') sql(database, "delete from supabase_migrations.schema_migrations where version='later-migration';");
      if (failure === 'storage-change') sql(database, "update storage.objects set metadata=metadata-'changed';");
    }
    for (const [failure, expected] of Object.entries(failures)) {
      mode = failure; mutated = false;
      await assert.rejects(buildHostedArchive(connection, keys.publicKey, root), expected);
      resetMutation(failure);
      mode = failure; mutated = false;
      await assert.rejects(run(), error => {
        assert.equal(error.stdout, '');
        assert.ok(!error.stderr.includes('synthetic-snapshot-value') && !error.stderr.includes(local.password));
        return true;
      });
      assert.equal(existsSync(output), false, failure + ' must not leave a success file');
      resetMutation(failure);
    }
    mode = 'normal'; mutated = false;
    const supervised = await buildSupervisedArchive(connection, keys.publicKey, root);
    const supervisedHash = createHash('sha256').update(supervised).digest('hex');
    const supervisedResult = verifySupervisedArchive(supervised, keys.privateKey, supervisedHash, local);
    assert.equal(supervisedResult.status, 'database-restored-and-matched');
    assert.equal(supervisedResult.allRowsAndRevisionsMatched, true);
    assert.throws(() => verifySupervisedArchive(supervised, keys.privateKey, '0'.repeat(64), local), /trusted receipt/);
    const mismatched = openArchive(supervised, keys.privateKey);
    const wrongManifest = JSON.parse(mismatched['verification.json']); wrongManifest.tables[0].rows++;
    const changed = sealArchive({ ...mismatched, 'verification.json': Buffer.from(JSON.stringify(wrongManifest)) }, keys.publicKey);
    assert.throws(() => verifySupervisedArchive(changed, keys.privateKey, createHash('sha256').update(changed).digest('hex'), local), /Restored table/);
    Object.values(mismatched).forEach(value => value.fill(0));
    const passphrase = 'synthetic local-only recovery test password';
    const protectedKey = createPrivateKey(keys.privateKey).export({ type: 'pkcs8', format: 'pem', cipher: 'aes-256-cbc', passphrase });
    const recoveryRoot = join(directory, 'manual-recovery'); mkdirSync(recoveryRoot);
    const trustedRoot = join(directory, 'local/ElectronicLogbook/HostedBackup'); mkdirSync(trustedRoot, { recursive: true });
    const capsule = sealArchive({ 'preview-recovery.env': Buffer.from('synthetic offline recovery secret') }, keys.publicKey);
    const recipientHash = createHash('sha256').update(createPublicKey(keys.publicKey).export({ type: 'spki', format: 'der' })).digest('hex');
    const digest = bytes => createHash('sha256').update(bytes).digest('hex');
    writeFileSync(join(recoveryRoot, 'backup-private.encrypted.pem'), protectedKey);
    writeFileSync(join(recoveryRoot, 'recovery.elbbackup'), capsule);
    writeFileSync(join(trustedRoot, 'recovery-receipt.json'), JSON.stringify({ protectedIdentitySha256: digest(protectedKey),
      recoveryArchiveSha256: digest(capsule), recipientSha256: recipientHash }));
    const manualPath = join(directory, 'manual.elbbackup'); writeFileSync(manualPath, supervised);
    writeFileSync(manualPath + '.receipt.json', JSON.stringify({ accessMode: 'supervised-owner', sha256: supervisedHash, recipientSha256: recipientHash }));
    const verifyRequest = { archive: manualPath, identity: join(recoveryRoot, 'backup-private.encrypted.pem'),
      recoveryArchive: join(recoveryRoot, 'recovery.elbbackup'), passphrase, localPort: local.port };
    const verifyCli = request => spawnSync(process.execPath, [resolve('tools/hosted-backup/verify-supervised.mjs')], {
      input: JSON.stringify(request), encoding: 'utf8', windowsHide: true, timeout: 30000,
      env: { ...process.env, LOCALAPPDATA: join(directory, 'local') } });
    const rejected = verifyCli({ ...verifyRequest, passphrase: 'wrong password' });
    assert.equal(rejected.status, 1); assert.equal(rejected.stdout, '');
    assert.ok(!rejected.stderr.includes(passphrase) && !rejected.stderr.includes('synthetic offline recovery secret'));
    assert.equal(existsSync(manualPath + '.verification.json'), false);
    const verified = verifyCli(verifyRequest);
    assert.equal(verified.status, 0, 'Protected owner-run verification CLI failed: ' + verified.stderr);
    assert.equal(JSON.parse(verified.stdout).recoveryCapsuleAuthenticated, true);
    const verificationBytes = readFileSync(manualPath + '.verification.json');
    assert.equal(verifyCli(verifyRequest).status, 1);
    assert.ok(readFileSync(manualPath + '.verification.json').equals(verificationBytes));
    assert.equal(sql('postgres', "select count(*) from pg_database where shobj_description(oid,'pg_database') like 'flightlogx-backup-restore:%';"), '0');
    const actualFiles = openArchive(await buildHostedArchive(connection, keys.publicKey, resolve('.')), keys.privateKey);
    for (const name of ['tools/hosted-backup/orchestrate.mjs', 'supabase/config.toml',
      'updater/src/ElectronicLogbook.Portable/ElectronicLogbook.Portable.csproj'])
      assert.ok(actualFiles['sources/' + name].equals(readFileSync(name)));
    Object.values(actualFiles).forEach(value => value.fill(0));
    sql(database, "insert into storage.buckets values('unexpected-private',false);");
    const beforeRequests = requests;
    await assert.rejects(buildHostedArchive(connection, keys.publicKey, root), /Unexpected Storage bucket/);
    assert.equal(requests, beforeRequests);
    sql(database, "delete from storage.buckets where id='unexpected-private';");
    sql(database, "insert into storage.objects values('airport-catalog','unexpected-private-file',null);");
    await assert.rejects(buildHostedArchive(connection, keys.publicKey, root), /Unexpected Storage object/);
    sql(database, "delete from storage.objects where name='unexpected-private-file';");
    sql(database, "update storage.objects set metadata='{}' where name='manifest.json';");
    await assert.rejects(buildHostedArchive(connection, keys.publicKey, root), /do not match/);
    rmSync(join(root, 'supabase/functions/recovery-envelope/index.ts'));
    await assert.rejects(buildHostedArchive(connection, keys.publicKey, root));
    assert.equal(sql(database, "select count(*) from pg_stat_activity where application_name='flightlogx-backup-export' and pid<>pg_backend_pid();"), '0');
    Object.values(files).forEach(value => value.fill(0));
  } finally {
    server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
    if (restored) dropDisposable(local, restored);
    if (created) { assert.match(database, /^elb_backup_fixture_[a-f0-9]{32}$/); sql('postgres', `drop database ${database};`); }
    const target = resolve(directory);
    assert.ok(target.startsWith(resolve(tmpdir()) + (process.platform === 'win32' ? '\\' : '/')));
    rmSync(target, { recursive: true, force: true });
  }
});
