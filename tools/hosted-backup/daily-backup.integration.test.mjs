import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, sep, basename } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { gzipSync } from 'node:zlib';
import { sealArchive, openArchive } from './archive.mjs';

test('separate upload/readback CLIs use the public Worker and retained SQLite, then decrypt and deny replay', { timeout: 60000 }, async t => {
  const directory = mkdtempSync(join(tmpdir(), 'elb-runner-integration-'));
  let runtime;
  t.after(async () => {
    await runtime?.dispose();
    assert.equal(basename(directory).startsWith('elb-runner-integration-'), true);
    assert.ok(resolve(directory).startsWith(resolve(tmpdir()) + sep));
    rmSync(directory, { recursive: true, force: true });
  });
  const require = createRequire(join(process.env.APPDATA, 'npm/node_modules/wrangler/package.json'));
  const { build } = require('esbuild');
  const { Miniflare, convertV4MiniflareOptions, Log, LogLevel } = require('miniflare');
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  const identity = generateKeyPairSync('rsa', { modulusLength: 3072,
    publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
  const contract = JSON.parse(readFileSync(new URL('./preview-logical-contract.json', import.meta.url)));
  const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));
  const catalogue = gzipSync(Buffer.from(JSON.stringify(Array.from({ length: 5000 }, (_, i) =>
    ({ i: 'X' + String(i).padStart(4, '0'), n: 'Synthetic ' + i, y: 0, x: 0 })))));
  const cataloguePath = 'snapshots/' + hash(catalogue) + '.json.gz';
  const references = { 'manifest.json': Buffer.from(JSON.stringify({ schemaVersion: 1, cycle: '2026-10-01',
    publishedAt: '2026-10-18T00:00:00Z', sha256: hash(catalogue), path: cataloguePath, count: 5000 })),
  [cataloguePath]: catalogue, 'seed.json.gz': catalogue };
  const sources = { 'sources/fixture.mjs': Buffer.from('export const synthetic = true;'),
    'deployment-sources.elbbackup': sealArchive({ 'deployments/fixture.bundle': Buffer.from('synthetic') }, identity.publicKey) };
  const context = { repository: 'alphadelta332/flightlogx-preview-backups', runId: '123', runAttempt: '1', commit: 'a'.repeat(40) };
  const token = 'b'.repeat(64), reservationToken = 'd'.repeat(64);
  const job = { version: 1, repository: context.repository, accountId: 'c'.repeat(32), bucket: policy.bucket,
    permitUrl: 'https://synthetic.example.workers.dev/daily-backup-permit',
    deploymentRevisions: [{ slug: 'airport-catalog', version: 1, updated_at: 1 }, { slug: 'recovery-envelope', version: 1, updated_at: 1 }],
    source: { origin: 'https://' + 'a'.repeat(20) + '.supabase.co', publishableKey: 'sb_publishable_synthetic', recipient: identity.publicKey, contract,
      sources: Object.fromEntries(Object.entries(sources).map(([name, bytes]) => [name, bytes.toString('base64')])),
      sourceManifest: { revision: context.commit, dirty: true, files: Object.entries(sources).map(([name, bytes]) => ({ name, sha256: hash(bytes) })) } } };
  const at = new Date().toISOString(), start = new Date(Date.now() - 1000).toISOString();
  const end = new Date(Date.parse(start) + 31 * 24 * 3600000).toISOString(), jobSha256 = hash(JSON.stringify(job));
  const review = { version: 2, approvedAt: start, expiresAt: end, weeklyCheckedAt: start, sourceExpiresAt: end,
    jobSha256, sourceOrigin: job.source.origin, githubPaidActionsBlocked: true, supabasePlan: 'free',
    egress: { used: 1, included: 5e9 }, cachedEgress: { used: 1, included: 5e9 },
    github: { observedAt: start, periodStart: start, periodEnd: end, remainingLinuxMinutes: 1997 },
    r2: { observedAt: start, periodStart: start, periodEnd: end, storageGbMonths: 0.01, requests: 67,
      storageBillableGbMonths: 0, classABillable: 0, classBBillable: 0, usageCostUsd: 0 } };
  const provider = { projectRef: 'a'.repeat(20), accountId: job.accountId, bucket: job.bucket, policy,
    r2Period: { periodStart: start, periodEnd: end }, deploymentRevisions: job.deploymentRevisions };
  const evidence = { version: 2, status: 'collected-not-permitted', retrievedAt: at,
    github: { retrievedAt: at, periodStart: start, plan: 'free', actionsMinutes: [{ sku: 'actions_linux', grossQuantity: 7, netAmount: 0 }] },
    supabase: { retrievedAt: at, plan: 'free', deploymentRevisions: job.deploymentRevisions },
    r2: { accountId: job.accountId, bucket: job.bucket, privateStandardBucket: true, reviewedPeriod: provider.r2Period,
      currentBytes: 1500000, sampledPeakBytes: 1500000, newestStorageMeasurementAt: at, pendingUploads: 0, infrequentAccessBytes: 0,
      windows: [{ from: start, until: at, requests: 67 }, { from: new Date(Date.parse(at) - 31 * 24 * 3600000).toISOString(), until: at, requests: 67 }] } };
  const cache = { startedAt: Date.parse(at), fingerprint: hash(JSON.stringify(provider)), active: false, evidence };
  const rows = { 'storage.buckets': [{ id: 'airport-catalog', public: true }],
    'storage.objects': Object.entries(references).map(([name, bytes]) => ({ bucket_id: 'airport-catalog', name,
      metadata: { size: bytes.length, eTag: createHash('md5').update(bytes).digest('hex') } })),
    'public.operations': [{ note: 'synthetic-private-flight' }] };
  const snapshot = { shape: contract.shape, generated: contract.generatedExpressions,
    context: { method: 'GET', readOnly: 'on', isolation: 'read committed', capturedAt: at },
    ledgers: { auth: contract.ledgers.auth, storage: contract.ledgers.storage, application: contract.ledgers.application },
    datasets: contract.shape.map(table => ({ table: table.table, rows: (rows[table.table] ?? []).map(row => JSON.stringify(row)) })), sequences: [] };
  writeFileSync(join(directory, 'job.json'), JSON.stringify(job));
  writeFileSync(join(directory, 'fixture.json'), JSON.stringify({ job, snapshot, token,
    references: Object.fromEntries(Object.entries(references).map(([name, bytes]) => [name, bytes.toString('base64')])) }));
  mkdirSync(join(directory, 'objects'));
  const previous = Buffer.from('preserve previous independently retained object');
  writeFileSync(join(directory, 'objects', encodeURIComponent('manual/previous.elbbackup')), previous);
  const entry = `
    import router, { DailyAttemptGuard as Base } from './tools/hosted-backup/daily-controls.mjs';
    export class FixtureGuard extends Base {
      constructor(state, env) { super(state, env); state.blockConcurrencyWhile(async () => {
        if (!await this.storage.get('provider-cache')) await this.storage.put('provider-cache', JSON.parse(env.FIXTURE_CACHE));
      }); }
      async fetch(request) {
        if (new URL(request.url).pathname === '/fixture-state') return Response.json({
          source: await this.storage.get('source-budget'), requests: await this.storage.get('component-requests') });
        return super.fetch(request);
      }
    }
    export default { fetch(request, env) {
      if (new URL(request.url).pathname === '/fixture-state')
        return env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source')).fetch(request);
      return router.fetch(request, env);
    } };`;
  const bundle = await build({ stdin: { contents: entry, resolveDir: fileURLToPath(new URL('../../', import.meta.url)) },
    bundle: true, write: false, format: 'esm', platform: 'browser', logLevel: 'silent' });
  const startWorker = async () => {
    const options = convertV4MiniflareOptions({ name: 'synthetic-backup', modules: true, script: bundle.outputFiles[0].text,
      compatibilityDate: '2026-10-05', host: '127.0.0.1', port: 0, cf: false, log: new Log(LogLevel.NONE), handleStructuredLogs: () => {},
      durableObjects: { DAILY_ATTEMPTS: { className: 'FixtureGuard', useSQLite: true } },
      bindings: { BACKUP_ENABLED: 'true', MONITOR_ENABLED: 'false', BACKUP_RESERVATION_SHA256: hash(reservationToken),
        OWNER_REVIEW_JSON: JSON.stringify(review), PRIMARY_MONITOR_JSON: JSON.stringify({ provider, history: { accountId: job.accountId, jobSha256 } }),
        FIXTURE_CACHE: JSON.stringify(cache) } });
    runtime = new Miniflare({ ...options, resourcePersistencePath: join(directory, 'sqlite') });
    return (await runtime.ready).href;
  };
  const preload = `
    import assert from 'node:assert/strict';
    import { readFileSync, writeFileSync, readdirSync, appendFileSync, existsSync } from 'node:fs';
    import { join } from 'node:path';
    import childProcess from 'node:child_process';
    import { syncBuiltinESMExports } from 'node:module';
    const dir = process.env.ELB_FIXTURE_DIR, f = JSON.parse(readFileSync(join(dir, 'fixture.json')));
    const trace = value => appendFileSync(join(dir, 'trace.jsonl'), JSON.stringify(value) + '\\n');
    const realFetch = globalThis.fetch;
    globalThis.fetch = async (url, options) => {
      if (new URL(url).origin === new URL(f.job.permitUrl).origin)
        return realFetch(new URL(new URL(url).pathname, process.env.ELB_CONTROLS_URL), options);
      if (url === f.job.source.origin + '/rest/v1/rpc/elb_backup_snapshot') {
        assert.equal(options.headers['x-flightlogx-backup-token'], f.token);
        trace({ kind: 'capture' }); return Response.json(f.snapshot);
      }
      const prefix = f.job.source.origin + '/storage/v1/object/public/airport-catalog/';
      assert.ok(url.startsWith(prefix), 'Unexpected external destination.');
      const bytes = f.references[url.slice(prefix.length).split('?')[0]]; assert.ok(bytes);
      return new Response(Buffer.from(bytes, 'base64'));
    };
    childProcess.spawnSync = (program, args, options) => {
      assert.ok(['curl', 'curl.exe'].includes(program)); assert.deepEqual(args, ['--disable', '--config', '-']);
      const config = options.input, url = new URL(config.match(/^url = "([^"]+)"/m)[1]);
      assert.equal(url.origin, 'https://' + f.job.accountId + '.r2.cloudflarestorage.com');
      assert.equal(config.match(/^user = "([^"]+)"/m)[1], process.env.R2_ACCESS_KEY_ID + ':' + process.env.R2_SECRET_ACCESS_KEY);
      const method = config.match(/^request = "([^"]+)"/m)[1];
      const key = url.pathname.slice(('/' + f.job.bucket).length).replace(/^\\//, '');
      const target = join(dir, 'objects', encodeURIComponent(key)); let bytes;
      if (method === 'PUT') {
        assert.equal(process.env.R2_ACCESS_KEY_ID, '1'.repeat(32)); assert.equal(existsSync(target), false);
        assert.ok(config.includes('header = "If-None-Match: *"'));
        bytes = readFileSync(config.match(/^data-binary = "@([^"]+)"/m)[1]); writeFileSync(target, bytes, { flag: 'wx' });
        trace({ kind: 'upload' }); bytes = Buffer.alloc(0);
      } else if (!key) {
        assert.equal(method, 'GET'); assert.equal(process.env.R2_ACCESS_KEY_ID, '1'.repeat(32));
        bytes = Buffer.from('<ListBucketResult><IsTruncated>false</IsTruncated>' + readdirSync(join(dir, 'objects')).map(name =>
          '<Contents><Key>' + decodeURIComponent(name) + '</Key><Size>' + readFileSync(join(dir, 'objects', name)).length + '</Size></Contents>').join('') + '</ListBucketResult>');
        trace({ kind: 'list' });
      } else {
        assert.equal(method, 'GET'); assert.equal(process.env.R2_ACCESS_KEY_ID, '2'.repeat(32));
        bytes = readFileSync(target); trace({ kind: 'readback' });
      }
      return { status: 0, stdout: Buffer.concat([bytes, Buffer.from('\\n200')]), stderr: Buffer.alloc(0) };
    };
    syncBuiltinESMExports();`;
  writeFileSync(join(directory, 'preload.mjs'), preload);
  let controlsUrl = await startWorker();
  const run = (mode, pending, reservation = reservationToken) => new Promise((resolveRun, reject) => {
    const env = Object.fromEntries(['SystemRoot', 'PATH', 'TEMP', 'TMP', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA']
      .filter(key => process.env[key] !== undefined).map(key => [key, process.env[key]]));
    Object.assign(env, { ELB_FIXTURE_DIR: directory, ELB_CONTROLS_URL: controlsUrl, GITHUB_REPOSITORY: context.repository,
      GITHUB_RUN_ID: context.runId, GITHUB_RUN_ATTEMPT: context.runAttempt, GITHUB_SHA: context.commit,
      R2_ACCESS_KEY_ID: (mode === 'upload' ? '1' : '2').repeat(32), R2_SECRET_ACCESS_KEY: '3'.repeat(64) });
    if (mode === 'upload') Object.assign(env, { SOURCE_CAPABILITY: token, BACKUP_RESERVATION_TOKEN: reservation, GITHUB_OUTPUT: join(directory, 'output.txt') });
    else Object.assign(env, { BACKUP_READBACK_REQUEST: JSON.stringify(pending), GITHUB_STEP_SUMMARY: join(directory, 'summary.md') });
    const child = spawn(process.execPath, ['--import', pathToFileURL(join(directory, 'preload.mjs')).href, fileURLToPath(new URL('./daily-backup.mjs', import.meta.url)), mode, join(directory, 'job.json')],
      { windowsHide: true, env, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('Synthetic CLI timed out.')); }, 15000);
    for (const stream of [child.stdout, child.stderr]) stream.on('data', part => { output += part; });
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', code => { clearTimeout(timer); resolveRun({ code, output }); });
  });
  assert.equal((await run('upload', undefined, '0'.repeat(64))).code, 1);
  assert.deepEqual(await (await fetch(new URL('/fixture-state', controlsUrl))).json(), {});
  assert.equal(existsSync(join(directory, 'trace.jsonl')), false);
  const upload = await run('upload'); assert.equal(upload.code, 0, upload.output);
  const pending = JSON.parse(readFileSync(join(directory, 'output.txt'), 'utf8').trim().slice('request='.length));
  const beforeRestart = await (await fetch(new URL('/fixture-state', controlsUrl))).json();
  await runtime.dispose(); runtime = undefined; controlsUrl = await startWorker();
  assert.deepEqual(await (await fetch(new URL('/fixture-state', controlsUrl))).json(), beforeRestart, 'SQLite state must survive restart.');
  const readback = await run('readback', pending);
  assert.equal(readback.code, 0, JSON.stringify({ output: readback.output,
    state: await (await fetch(new URL('/fixture-state', controlsUrl))).json(), trace: readFileSync(join(directory, 'trace.jsonl'), 'utf8') }));
  const receipt = JSON.parse(readback.output.trim());
  const stored = readFileSync(join(directory, 'objects', encodeURIComponent(pending.key)));
  assert.equal(receipt.status, 'verified'); assert.equal(receipt.sha256, hash(stored));
  assert.equal(Object.hasOwn(receipt.reservation, 'readbackToken'), false);
  assert.ok(readFileSync(join(directory, 'summary.md'), 'utf8').includes(receipt.sha256));
  assert.equal(stored.includes(Buffer.from('synthetic-private-flight')), false);
  const restored = openArchive(stored, identity.privateKey);
  const logical = JSON.parse(restored['logical.json']);
  assert.equal(logical.datasets.length, 47);
  assert.equal(logical.datasets.find(value => value.table === 'public.operations').rows[0], JSON.stringify(rows['public.operations'][0]));
  assert.ok(restored['reference/' + cataloguePath].equals(catalogue));
  Object.values(restored).forEach(bytes => bytes.fill(0));
  const state = await (await fetch(new URL('/fixture-state', controlsUrl))).json();
  assert.equal(state.source.attempts, 1); assert.equal(state.source.readbackUsed, true);
  assert.equal(state.requests.counts.workflow, 4);
  const trace = readFileSync(join(directory, 'trace.jsonl'), 'utf8');
  assert.deepEqual(trace.trim().split('\n').map(line => JSON.parse(line).kind), ['list', 'capture', 'list', 'upload', 'readback']);
  assert.equal((await run('upload')).code, 1); assert.equal((await run('readback', pending)).code, 1);
  assert.equal(readFileSync(join(directory, 'trace.jsonl'), 'utf8'), trace);
  assert.deepEqual(await (await fetch(new URL('/fixture-state', controlsUrl))).json(), state);
  assert.deepEqual(readFileSync(join(directory, 'objects', encodeURIComponent('manual/previous.elbbackup'))), previous);
});
