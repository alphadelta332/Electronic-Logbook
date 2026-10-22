import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { cachedBackupPermit, checkPrimaryMonitor, providerUsage, runPrimaryMonitor } from './daily-primary.mjs';
import { validateCostEvidence } from './daily-cost-policy.mjs';
import router, { DailyAttemptGuard } from './daily-controls.mjs';

const now = Date.parse('2026-10-21T12:00:00Z'), day = 86400000;
const iso = value => new Date(value).toISOString();
function fixture() {
  const start = '2026-10-01T00:00:00.000Z', end = '2026-11-01T00:00:00.000Z';
  const provider = { accountId: 'a'.repeat(32), projectRef: 'b'.repeat(20), bucket: 'flightlogx-preview-backups',
    r2Period: { periodStart: start, periodEnd: end }, deploymentRevisions: [],
    policy: JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url))) };
  const history = { accountId: provider.accountId, jobSha256: 'c'.repeat(64), commit: 'd'.repeat(40),
    workflowId: 1, workflowPath: '.github/workflows/daily-backup.yml', branch: 'main' };
  const review = { version: 2, jobSha256: history.jobSha256, sourceOrigin: `https://${provider.projectRef}.supabase.co`,
    approvedAt: iso(now-day), weeklyCheckedAt: iso(now-day), expiresAt: end, sourceExpiresAt: end,
    githubPaidActionsBlocked: true, supabasePlan: 'free', egress: { used: 1, included: 5e9 }, cachedEgress: { used: 1, included: 5e9 },
    github: { observedAt: iso(now-day), periodStart: start, periodEnd: end, remainingLinuxMinutes: 1997 },
    r2: { observedAt: iso(now-day), periodStart: start, periodEnd: end, storageGbMonths: 0.01, requests: 1,
      storageBillableGbMonths: 0, classABillable: 0, classBBillable: 0, usageCostUsd: 0 } };
  const evidence = { version: 2, status: 'collected-not-permitted', retrievedAt: iso(now),
    github: { retrievedAt: iso(now), periodStart: start, plan: 'free', actionsMinutes: [] },
    supabase: { retrievedAt: iso(now), plan: 'free', deploymentRevisions: [] },
    r2: { accountId: provider.accountId, bucket: provider.bucket, newestStorageMeasurementAt: iso(now-60000),
      privateStandardBucket: true, reviewedPeriod: structuredClone(provider.r2Period),
      currentBytes: 1000, sampledPeakBytes: 1000, pendingUploads: 0, infrequentAccessBytes: 0,
      windows: [{ from: start, until: iso(now), requests: 1 }, { from: iso(now-31*day), until: iso(now), requests: 1 }] } };
  return { provider, history, review, evidence };
}
function storage() {
  const values = new Map(); let tail = Promise.resolve();
  return { values, get: async key => structuredClone(values.get(key)), transaction(action) {
    const operation = tail.then(async () => {
      const next = structuredClone(values), result = await action({ get: async key => structuredClone(next.get(key)),
        put: async (key, value) => next.set(key, structuredClone(value)) });
      values.clear(); for (const item of next) values.set(...item); return result;
    }); tail = operation.catch(() => {}); return operation;
  } };
}
function envFor(f) {
  return { MONITOR_ENABLED: 'true', PRIMARY_MONITOR_JSON: JSON.stringify({ provider: f.provider, history: f.history }),
    PROVIDER_CREDENTIALS_JSON: '{}',
    OWNER_REVIEW_JSON: JSON.stringify(f.review), MONITOR_CREDENTIALS_JSON: JSON.stringify({ github: 'g'.repeat(30) }),
    ALERT_DELIVERY_JSON: JSON.stringify({ version: 1, recipient: 'owner@example.test' }), RESEND_SEND_TOKEN: 're_'+'a'.repeat(30) };
}
async function seed(db, f) {
  const fingerprint = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(f.provider)))).toString('hex');
  db.values.set('provider-cache', { fingerprint, startedAt: now, evidence: f.evidence });
}

test('current provider observations use permit3 policy without renewing historical review dates', () => {
  const f = fixture(), original = structuredClone(f);
  assert.equal(providerUsage(f.evidence, f.review, f.provider, f.history, now), 'allowed');
  assert.deepEqual(f, original);
  f.evidence.r2.windows[0].requests = 5000;
  assert.equal(providerUsage(f.evidence, f.review, f.provider, f.history, now), 'warning');
  f.evidence.r2.windows[0].requests = 10000;
  assert.equal(providerUsage(f.evidence, f.review, f.provider, f.history, now), 'stopped');
  for (const change of [f => f.evidence.github.retrievedAt = iso(now-2*3600000),
    f => f.review.expiresAt = iso(now), f => f.review.githubPaidActionsBlocked = false]) {
    const f = fixture(); change(f);
    assert.equal(providerUsage(f.evidence, f.review, f.provider, f.history, now), 'stopped');
  }
  for (const change of [f => f.history.accountId = 'e'.repeat(32), f => f.history.jobSha256 = 'e'.repeat(64),
    f => f.provider.projectRef = 'e'.repeat(20), f => f.provider.r2Period.periodStart = iso(now),
    f => f.evidence.supabase.deploymentRevisions = [{ slug: 'changed' }], f => f.evidence.status = 'untrusted']) {
    const f = fixture(); change(f); assert.throws(() => providerUsage(f.evidence, f.review, f.provider, f.history, now));
  }
});

test('cached permit preserves provider timestamps and history, matches runner policy and cannot collect or alter counters', async () => {
  const f = fixture(), db = storage(), env = { ...envFor(f), BACKUP_ENABLED: 'true' }; await seed(db, f);
  const before = structuredClone(db.values);
  const permit = await cachedBackupPermit(db, env, now+1000);
  assert.deepEqual(validateCostEvidence(permit, f.history.jobSha256, f.review.sourceOrigin, now+1000), { warnings: [] });
  assert.equal(permit.checkedAt, f.evidence.retrievedAt);
  assert.equal(permit.github.observedAt, f.evidence.github.retrievedAt);
  assert.deepEqual(permit.ownerReview, f.review); assert.deepEqual(permit.deploymentRevisions, f.provider.deploymentRevisions);
  const expected = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(f.provider.policy)))).toString('hex');
  assert.equal(permit.r2.policySha256, expected); assert.deepEqual(db.values, before);
  const responses = await Promise.all(Array.from({ length: 16 }, () => cachedBackupPermit(db, env, now+1000)));
  assert.equal(responses.length, 16); assert.deepEqual(db.values, before);
  for (const change of [f => f.evidence.r2.privateStandardBucket = false,
    f => f.evidence.r2.reviewedPeriod.periodStart = iso(now), f => f.evidence.r2.windows[0].requests = 10000,
    f => f.review.expiresAt = iso(now), f => f.history.jobSha256 = 'e'.repeat(64),
    f => f.evidence.supabase.deploymentRevisions = [{ slug: 'changed' }],
    f => f.evidence.github.retrievedAt = iso(now-2*3600000)]) {
    const f = fixture(), db = storage(); change(f); await seed(db, f);
    await assert.rejects(cachedBackupPermit(db, { ...envFor(f), BACKUP_ENABLED: 'true' }, now));
  }
  await assert.rejects(cachedBackupPermit(db, env, now+2*3600000));
  await assert.rejects(cachedBackupPermit(db, { ...env, BACKUP_ENABLED: 'false' }, now));
  await assert.rejects(cachedBackupPermit(storage(), env, now));
});

test('permit router denies disabled calls before storage, forwards only GET and returns safe failures', async () => {
  const off = { BACKUP_ENABLED: 'false' };
  const response = await router.fetch(new Request('https://controls/daily-backup-permit'), off);
  assert.equal(response.status, 409); assert.equal(response.headers.get('Cache-Control'), 'no-store');
  const db = storage(), env = { BACKUP_ENABLED: 'true' }, guard = new DailyAttemptGuard({ storage: db }, env);
  env.DAILY_ATTEMPTS = { idFromName(name) { assert.equal(name, 'flightlogx-preview-source'); return name; },
    get() { return { fetch: request => guard.fetch(request) }; } };
  const denied = await router.fetch(new Request('https://controls/daily-backup-permit'), env);
  assert.equal(denied.status, 409); assert.deepEqual(db.values, new Map());
  for (const method of ['POST', 'PUT', 'DELETE'])
    assert.equal((await router.fetch(new Request('https://controls/daily-backup-permit', { method }), env)).status, 404);
});

test('disabled timer and internal dispatch do no I/O; public callers cannot reach either internal action', async () => {
  const env = new Proxy({ MONITOR_ENABLED: 'false' }, { get(target, key) { assert.equal(key, 'MONITOR_ENABLED'); return target[key]; } });
  await runPrimaryMonitor(env); await checkPrimaryMonitor(null, env);
  await router.scheduled({}, env);
  const db = storage(), guard = new DailyAttemptGuard({ storage: db }, env);
  assert.equal((await guard.fetch(new Request('https://internal/internal-monitor', { method: 'POST' }))).status, 409);
  assert.equal(db.values.size, 0);
  for (const path of ['/internal-monitor', '/refresh', '/check']) {
    assert.equal((await router.fetch(new Request('https://public'+path, { method: 'POST' }), env)).status, 404);
  }
  const config = JSON.parse(readFileSync(new URL('./daily-controls.wrangler.json', import.meta.url)));
  assert.equal(config.vars.MONITOR_ENABLED, 'false'); assert.equal(config.triggers?.crons?.length ?? 0, 0);
});

test('timer uses the original stable object and propagates failed internal checks', async () => {
  let calls = 0;
  const env = { MONITOR_ENABLED: 'true', DAILY_ATTEMPTS: {
    idFromName(name) { assert.equal(name, 'flightlogx-preview-source'); return 'id'; },
    get(id) { assert.equal(id, 'id'); return { async fetch(url, options) {
      calls++; assert.equal(url, 'https://controls.internal/internal-monitor');
      assert.deepEqual(options, { method: 'POST' }); return new Response('', { status: 409 });
    } }; } } };
  await assert.rejects(runPrimaryMonitor(env)); assert.equal(calls, 1);
});

test('internal primary checkpoints show persisted attempts on success and failure without public diagnostics', async t => {
  const f = fixture(), db = storage(), env = envFor(f), lines = [];
  t.mock.method(Date, 'now', () => now);
  t.mock.method(console, 'log', line => lines.push(JSON.parse(line)));
  t.mock.method(globalThis, 'fetch', async url => {
    if (url.startsWith('https://api.github.com/')) return Response.json({ total_count: 0, workflow_runs: [] });
    return Response.json({ id: '01234567-89ab-cdef-0123-456789abcdef' });
  });
  await seed(db, f);
  env.MONITOR_HEARTBEAT = { async completed() {} };
  const guard = new DailyAttemptGuard({ storage: db }, env);
  const request = () => new Request('https://internal/internal-monitor', { method: 'POST' });
  assert.equal((await guard.fetch(request())).status, 200);
  assert.deepEqual(lines.map(line => line.phase), ['before', 'after']);
  assert.equal(lines[0].monitor, null); assert.equal(lines[1].monitor.checks, 1);
  assert.equal(lines[1].monitor.delivered, true); assert.equal(lines[1].monitor.wireAttempt, 1);
  assert.equal(lines[1].requests.counts.monitorGithub, 2); assert.equal(lines[1].source, null);
  const before = structuredClone(db.values);
  assert.equal((await guard.fetch(request())).status, 409);
  assert.deepEqual(lines.slice(2).map(line => line.phase), ['before', 'after']);
  assert.deepEqual(db.values, before); assert.deepEqual(lines[3], lines[1]);
  assert.equal((await router.fetch(request(), env)).status, 404); assert.equal(lines.length, 4);
});

test('internal primary accepts a zero-byte streamed POST but rejects nonempty streams before storage', async t => {
  const f = fixture(), db = storage(), env = envFor(f); await seed(db, f);
  t.mock.method(Date, 'now', () => now); t.mock.method(console, 'log', () => {});
  t.mock.method(globalThis, 'fetch', async url => url.startsWith('https://api.github.com/')
    ? Response.json({ total_count: 0, workflow_runs: [] }) : Response.json({ id: '01234567-89ab-cdef-0123-456789abcdef' }));
  env.MONITOR_HEARTBEAT = { async completed() {} };
  const guard = new DailyAttemptGuard({ storage: db }, env);
  const streamed = value => new Request('https://internal/internal-monitor', { method: 'POST', duplex: 'half',
    body: new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array());
      if (value.length) controller.enqueue(new TextEncoder().encode(value)); controller.close(); } }) });
  const before = structuredClone(db.values);
  assert.equal((await guard.fetch(streamed('x'))).status, 409); assert.deepEqual(db.values, before);
  assert.equal((await guard.fetch(streamed(''))).status, 200);
  assert.equal(db.values.get('monitor-state').checks, 1); assert.equal(db.values.get('monitor-state').delivered, true);
});

test('cached composition reserves before delivery, deduplicates concurrent checks and completes private heartbeat', async () => {
  const f = fixture(), db = storage(), env = envFor(f); await seed(db, f);
  let requests = 0, heartbeats = 0;
  env.MONITOR_HEARTBEAT = { async completed() { heartbeats++; assert.equal(db.values.get('monitor-state').delivered, true); } };
  const request = async (url, options) => {
    requests++;
    if (url.startsWith('https://api.github.com/')) {
      assert.equal(db.values.get('component-requests').counts.monitorGithub, requests);
      return Response.json({ total_count: 0, workflow_runs: [] });
    }
    assert.equal(url, 'https://api.resend.com/emails');
    assert.equal(db.values.get('monitor-state').resend.attempt, 1);
    assert.match(JSON.parse(options.body).text, /missing-run,stale-backup/);
    return Response.json({ id: '01234567-89ab-cdef-0123-456789abcdef' });
  };
  const checks = await Promise.allSettled(Array.from({ length: 16 }, () => checkPrimaryMonitor(db, env, request, () => now)));
  assert.equal(checks.filter(r => r.status === 'fulfilled').length, 1);
  assert.equal(requests, 3); assert.equal(heartbeats, 1);
  assert.equal(db.values.get('monitor-state').checks, 1);
  await assert.rejects(checkPrimaryMonitor(db, { ...env, RESEND_SEND_TOKEN: 're_'+'b'.repeat(30) }, request, () => now+1000));
  assert.equal(requests, 3);
});

test('missing/changed inputs and failed collection alert once without a successful provider/history claim', async () => {
  for (const kind of ['missing-review', 'changed-input', 'failed-refresh', 'missing-heartbeat', 'failed-delivery']) {
    const f = fixture(), db = storage(), env = envFor(f);
    if (kind !== 'failed-refresh') await seed(db, f);
    if (kind === 'missing-review') delete env.OWNER_REVIEW_JSON;
    if (kind === 'changed-input') env.PRIMARY_MONITOR_JSON = JSON.stringify({ provider: f.provider, history: { ...f.history, jobSha256: 'e'.repeat(64) } });
    if (kind !== 'missing-heartbeat') env.MONITOR_HEARTBEAT = { async completed() {} };
    let sends = 0;
    const request = async url => {
      if (url === 'https://api.resend.com/emails') {
        sends++; if (kind === 'failed-delivery') throw new Error('lost reply');
        return Response.json({ id: '01234567-89ab-cdef-0123-456789abcdef' });
      }
      if (kind === 'failed-refresh') throw new Error('provider unavailable');
      assert.ok(['missing-heartbeat', 'failed-delivery'].includes(kind));
      return Response.json({ total_count: 0, workflow_runs: [] });
    };
    const check = checkPrimaryMonitor(db, env, request, () => now);
    if (['missing-heartbeat', 'failed-delivery'].includes(kind)) await assert.rejects(check);
    else assert.deepEqual((await check).conditions, ['monitor-evidence-unavailable']);
    assert.equal(sends, 1); assert.equal(db.values.get('monitor-state').dailyAttempts, 1);
    if (kind === 'failed-refresh') assert.equal(db.values.get('provider-cache').batches, 1);
  }
});
