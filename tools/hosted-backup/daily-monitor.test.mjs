import test from 'node:test';
import assert from 'node:assert/strict';
import { monitorConditions, checkBackupMonitor } from './daily-monitor.mjs';

const hour = 60 * 60 * 1000, start = Date.parse('2026-10-20T00:00:00Z');
const iso = time => new Date(time).toISOString();
function review(now = start) {
  const at = iso(now), end = iso(now + 31 * 24 * hour);
  return { version: 2, approvedAt: at, expiresAt: end, weeklyCheckedAt: at, sourceExpiresAt: end,
    jobSha256: 'a'.repeat(64), sourceOrigin: `https://${'b'.repeat(20)}.supabase.co`,
    githubPaidActionsBlocked: true, supabasePlan: 'free', egress: { used: 0, included: 5e9 },
    cachedEgress: { used: 0, included: 5e9 },
    github: { observedAt: at, periodStart: at, periodEnd: end, remainingLinuxMinutes: 1997 },
    r2: { observedAt: at, periodStart: at, periodEnd: end, storageGbMonths: 0, requests: 0,
      storageBillableGbMonths: 0, classABillable: 0, classBBillable: 0, usageCostUsd: 0 } };
}
function observation(now = start) {
  return { version: 1, observedAt: iso(now), usage: 'allowed',
    lastRun: { startedAt: iso(now), conclusion: 'success' },
    lastVerified: { version: 2, status: 'verified', verifiedAt: iso(now),
      key: 'daily/2026-10-20.elbbackup', sha256: 'c'.repeat(64), bytes: 1500000 } };
}
function memoryStorage(values = new Map()) {
  let tail = Promise.resolve();
  return { values, transaction(action) {
    const pending = tail.then(async () => {
      const next = structuredClone(values);
      const result = await action({ get: async key => structuredClone(next.get(key)),
        put: async (key, value) => next.set(key, structuredClone(value)) });
      values.clear(); for (const [key, value] of next) values.set(key, value);
      return result;
    });
    tail = pending.catch(() => {}); return pending;
  } };
}
const unhealthy = now => ({ ...observation(now), lastRun: null, lastVerified: null });

test('GitHub monitoring attempts are charged before I/O, bounded to five/check and thirty/day across restart', async () => {
  const storage = memoryStorage(); let now = start, calls = 0;
  for (let check = 0; check < 6; check++, now += 4 * hour) {
    const restarted = memoryStorage(storage.values);
    await checkBackupMonitor(restarted, review(), async (r2, github) => {
      for (let index = 0; index < 5; index++) await github(async () => {
        calls++; assert.equal(storage.values.get('component-requests').counts.monitorGithub, calls);
      });
      await assert.rejects(github(async () => { calls++; }), /ceiling/);
      return observation(now);
    }, async () => ({ accepted: true }), () => now);
  }
  assert.equal(calls, 30); assert.equal(storage.values.get('component-requests').counts.monitorGithub, 30);
});

test('freshness uses original supplied verification time with strict >30h and >26h missing-run boundaries', () => {
  const data = observation();
  assert.deepEqual(monitorConditions(data, review(), start), []);
  data.lastVerified.verifiedAt = iso(start - 30 * hour);
  data.lastRun.startedAt = iso(start - 26 * hour);
  assert.deepEqual(monitorConditions(data, review(), start), []);
  data.lastVerified.verifiedAt = iso(start - 30 * hour - 1);
  data.lastRun.startedAt = iso(start - 26 * hour - 1);
  assert.deepEqual(monitorConditions(data, review(), start), ['missing-run', 'stale-backup']);
  assert.deepEqual(monitorConditions(unhealthy(start), review(), start), ['missing-run', 'stale-backup']);
});

for (const conclusion of ['failure', 'cancelled', 'timed_out', 'in_progress'])
  test(`detects ${conclusion} independently of last good backup`, () => {
    const data = observation(); data.lastRun = { startedAt: iso(start - hour - 1), conclusion };
    assert.deepEqual(monitorConditions(data, review(), start), ['failed-run']);
  });

test('detects cost warnings/stops and expiring, overdue or invalid reviews without authorizing backups', () => {
  for (const usage of ['warning', 'stopped'])
    assert.deepEqual(monitorConditions({ ...observation(), usage }, review(), start), [`usage-${usage}`]);
  const expiring = review(); expiring.expiresAt = iso(start + 48 * hour);
  assert.deepEqual(monitorConditions(observation(), expiring, start), ['review-expiring']);
  const expired = { ...expiring, expiresAt: iso(start) };
  assert.deepEqual(monitorConditions(observation(), expired, start), ['review-expiring', 'review-required']);
  assert.deepEqual(monitorConditions(observation(start + 6 * 24 * hour), review(), start + 6 * 24 * hour), ['weekly-review-due']);
  assert.deepEqual(monitorConditions(observation(start + 7 * 24 * hour), review(), start + 7 * 24 * hour), ['review-required', 'weekly-review-due']);
  assert.deepEqual(monitorConditions(observation(), null, start), ['review-required']);
});

test('rejects pending receipts, malformed/missing/future/stale/unknown observations', () => {
  const invalid = [null, {}, { ...observation(), version: 2 }, { ...observation(), usage: 'ok' },
    { ...observation(), observedAt: iso(start + 1) }, { ...observation(), observedAt: iso(start - 10 * 60 * 1000) },
    { ...observation(), lastRun: undefined }, { ...observation(), lastRun: { startedAt: iso(start), conclusion: 'unknown' } }];
  for (const change of [{ status: 'pending' }, { verifiedAt: iso(start + 1) }, { sha256: 'bad' }, { bytes: 0 }, { version: 1 }])
    invalid.push({ ...observation(), lastVerified: { ...observation().lastVerified, ...change } });
  for (const data of invalid) assert.throws(() => monitorConditions(data, review(), start));
});

test('parallel checks grant one lease, charge two R2 attempts before I/O and deny excess', async () => {
  const storage = memoryStorage(); let calls = 0, messages = 0;
  const observe = async r2 => {
    for (let index = 0; index < 2; index++) await r2(async () => {
      calls++; assert.equal(storage.values.get('component-requests').counts.monitor, calls);
    });
    await assert.rejects(r2(async () => { calls++; }), /ceiling/);
    return unhealthy(start);
  };
  const results = await Promise.allSettled(Array.from({ length: 16 }, () =>
    checkBackupMonitor(storage, review(), observe, async () => { messages++; return { accepted: true }; }, () => start)));
  assert.equal(results.filter(r => r.status === 'fulfilled').length, 1);
  assert.equal(calls, 2); assert.equal(messages, 1);
});

test('restart and review/token rotation preserve suppression and daily budgets; recovery starts a new incident', async () => {
  const storage = memoryStorage(); const messages = [];
  const deliver = async message => { messages.push(message); return { accepted: true }; };
  await checkBackupMonitor(storage, review(), async () => unhealthy(start), deliver, () => start);
  const restarted = memoryStorage(storage.values), next = start + 4 * hour;
  assert.equal((await checkBackupMonitor(restarted, review(next), async () => unhealthy(next), deliver, () => next)).delivery, 'suppressed');
  await assert.rejects(checkBackupMonitor(restarted, review(), async () => observation(), deliver, () => start), /interval/);
  await checkBackupMonitor(restarted, review(), async () => observation(next + 4 * hour), deliver, () => next + 4 * hour);
  await checkBackupMonitor(restarted, review(), async () => unhealthy(next + 8 * hour), deliver, () => next + 8 * hour);
  assert.equal(messages.length, 1, 'A second incident cannot spend the observer daily share.');
  await checkBackupMonitor(restarted, review(), async () => unhealthy(start + 24 * hour), deliver, () => start + 24 * hour);
  assert.equal(messages.length, 2); assert.notEqual(messages[0].idempotencyKey, messages[1].idempotencyKey);
  assert.equal(restarted.values.get('monitor-state').dailyAttempts, 1);
});

test('lost/failed delivery spends allowance and retries same immutable body/key only once', async () => {
  const storage = memoryStorage(), messages = [];
  const deliver = async message => {
    messages.push(message);
    assert.equal(storage.values.get('monitor-state').monthlyAttempts, messages.length);
    throw new Error('lost reply');
  };
  for (let index = 0; index < 3; index++) {
    const now = start + (20 + index * 4) * hour;
    const pending = checkBackupMonitor(storage, review(), async () => unhealthy(now), deliver, () => now);
    if (index < 2) await assert.rejects(pending, /lost reply/);
    else assert.equal((await pending).delivery, 'suppressed');
  }
  assert.deepEqual(messages[0], messages[1]); assert.equal(messages.length, 2);
});

test('monitor can signal missing runs without a backup grant; invalid evidence alerts without leaking diagnostics', async () => {
  const storage = memoryStorage(); let message;
  const result = await checkBackupMonitor(storage, null, async r2 => {
    await r2(async () => { throw new Error('private token and response'); });
  }, async value => { message = value; return { accepted: true }; }, () => start);
  assert.deepEqual(result.conditions, ['monitor-evidence-unavailable']);
  assert.equal(storage.values.get('component-requests').counts.monitor, 1);
  assert.ok(!message.text.includes('private')); assert.equal(storage.values.has('source-budget'), false);
});

test('an uncertain delivery is not retried after the provider idempotency window expires', async () => {
  const storage = memoryStorage(); let sends = 0;
  await assert.rejects(checkBackupMonitor(storage, review(), async () => unhealthy(start),
    async () => { sends++; throw new Error('uncertain'); }, () => start));
  const later = start + 24 * hour;
  assert.equal((await checkBackupMonitor(storage, review(), async () => unhealthy(later),
    async () => { sends++; return { accepted: true }; }, () => later)).delivery, 'suppressed');
  assert.equal(sends, 1);
});

test('daily/monthly caps survive incident churn and review rotation', async () => {
  const storage = memoryStorage(); let sends = 0;
  const monthStart = Date.parse('2026-10-01T00:00:00Z');
  for (let index = 0; index < 31 * 6; index++) {
    const now = monthStart + index * 4 * hour, data = unhealthy(now);
    data.usage = index % 2 ? 'stopped' : 'warning';
    await checkBackupMonitor(storage, review(now), async () => data,
      async () => { sends++; return { accepted: true }; }, () => now);
    assert.ok(storage.values.get('monitor-state').dailyAttempts <= 1);
  }
  assert.equal(sends, 16); assert.equal(storage.values.get('monitor-state').monthlyAttempts, 16);
  const nextMonth = monthStart + 31 * 24 * hour;
  await checkBackupMonitor(storage, review(nextMonth), async () => unhealthy(nextMonth),
    async () => { sends++; return { accepted: true }; }, () => nextMonth);
  assert.equal(sends, 17); assert.equal(storage.values.get('monitor-state').monthlyAttempts, 1);
});

test('late observations cannot publish or send, and the twelve-request ceiling survives repeated checks', async () => {
  const storage = memoryStorage(); let now = start, calls = 0;
  await assert.rejects(checkBackupMonitor(storage, review(), async () => {
    now += 5 * 60 * 1000; return unhealthy(now);
  }, async () => { assert.fail('Late check must not send'); }, () => now), /lease/);
  now = start + 4 * hour;
  const state = storage.values.get('component-requests') ?? { day: iso(now).slice(0, 10), counts: {} };
  state.counts.monitor = 12; storage.values.set('component-requests', state);
  const result = await checkBackupMonitor(storage, review(), async r2 => {
    await r2(async () => { calls++; }); return observation(now);
  }, async () => ({ accepted: true }), () => now);
  assert.deepEqual(result.conditions, ['monitor-evidence-unavailable']); assert.equal(calls, 0);
});
