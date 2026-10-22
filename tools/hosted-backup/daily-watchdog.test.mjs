import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { checkBackupMonitor, checkMissingMonitor, recordMonitorHeartbeat, monitorDeliveryLimits } from './daily-monitor.mjs';
import { sendBackupAlert } from './daily-alert-delivery.mjs';
import { MissingMonitorGuard, runMissingMonitor, watchdogObjectName } from './daily-watchdog.mjs';

const hour = 3600000, start = Date.parse('2026-10-01T20:00:00Z');
const config = { version: 1, recipient: 'owner@example.test' }, token = 're_' + 'a'.repeat(30);
const id = '01234567-89ab-cdef-0123-456789abcdef';
function storage(values = new Map()) {
  let tail = Promise.resolve();
  return { values, get: async key => structuredClone(values.get(key)), transaction(action) {
    const pending = tail.then(async () => {
      const next = structuredClone(values), result = await action({ get: async key => structuredClone(next.get(key)),
        put: async (key, value) => next.set(key, structuredClone(value)) });
      values.clear(); for (const [key, value] of next) values.set(key, value); return result;
    });
    tail = pending.catch(() => {}); return pending;
  } };
}
const check = (db, now, request) => checkMissingMonitor(db,
  message => sendBackupAlert(db, config, token, message, request, () => now), () => now);

test('missing primary alerts from a separate ledger without any primary/source/R2/GitHub access', async () => {
  const db = storage(); let calls = 0;
  const result = await check(db, start, async (url, options) => {
    calls++; assert.equal(url, 'https://api.resend.com/emails');
    assert.match(JSON.parse(options.body).text, /missing-monitor/);
    assert.match(options.headers['Idempotency-Key'], /^backup-watchdog\//);
    assert.equal(db.values.get('monitor-state').dailyAttempts, 1);
    assert.equal(db.values.get('monitor-state').resend.attempt, 1);
    return Response.json({ id });
  });
  assert.deepEqual(result, { conditions: ['missing-monitor'], delivery: 'accepted' });
  assert.equal(calls, 1); assert.equal(db.values.has('source-budget'), false);
  assert.equal(db.values.has('component-requests'), false);
  await check(storage(db.values), start + 4 * hour, async () => assert.fail('Duplicate alert'));
});

test('receiver-timed heartbeat has a strict >6h stale boundary and is never refreshed by observer checks', async () => {
  for (const offset of [6 * hour, 6 * hour + 1]) {
    const db = storage(); await recordMonitorHeartbeat(db, start);
    const result = await check(db, start + offset, async () => Response.json({ id }));
    assert.deepEqual(result.conditions, offset === 6 * hour ? [] : ['missing-monitor']);
    assert.equal(db.values.get('monitor-heartbeat').completedAt, start);
  }
  const db = storage(); await recordMonitorHeartbeat(db, start);
  await assert.rejects(recordMonitorHeartbeat(db, start + 4 * hour - 1), /interval/);
  await assert.rejects(recordMonitorHeartbeat(db, start - 1), /interval/);
  await recordMonitorHeartbeat(db, start + 4 * hour);
  db.values.set('monitor-heartbeat', { completedAt: start + 20 * hour });
  assert.deepEqual((await check(db, start + 8 * hour, async () => Response.json({ id }))).conditions,
    ['monitor-evidence-unavailable']);
});

test('parallel observer triggers admit one check and preserve uncertain-delivery attempts across restart/rotation', async () => {
  const db = storage(); let sends = 0; const messages = [];
  const request = async (_, options) => { sends++; messages.push(options); throw new Error('private failure'); };
  const replies = await Promise.allSettled(Array.from({ length: 16 }, () => check(db, start, request)));
  assert.equal(replies.filter(result => result.status === 'rejected').length, 16); assert.equal(sends, 1);
  const next = start + 4 * hour;
  await assert.rejects(checkMissingMonitor(storage(db.values), message => sendBackupAlert(storage(db.values), config,
    're_' + 'b'.repeat(30), message, request, () => next), () => next), /delivery unavailable/);
  assert.equal(sends, 2); assert.equal(messages[0].body, messages[1].body);
  assert.equal(messages[0].headers['Idempotency-Key'], messages[1].headers['Idempotency-Key']);
  assert.equal(db.values.get('monitor-state').monthlyAttempts, 2);
  assert.equal((await check(db, next + 4 * hour, request)).delivery, 'suppressed');
  assert.equal(sends, 2);
});

test('independent sender caps cannot exceed two/day and thirty-one/month under incident churn or rotation', async () => {
  assert.equal(monitorDeliveryLimits.primary.day + monitorDeliveryLimits.observer.day, 2);
  assert.equal(monitorDeliveryLimits.primary.month + monitorDeliveryLimits.observer.month, 31);
  const primary = storage(), observer = storage(); let primarySends = 0, observerSends = 0;
  for (let day = 0; day < 31; day++) {
    const now = Date.parse('2026-10-01T00:00:00Z') + day * 24 * hour;
    // Recovery followed by silence creates new observer incidents without resetting its delivery counters.
    await recordMonitorHeartbeat(observer, now);
    await check(observer, now, async () => assert.fail('Fresh heartbeat'));
    await check(observer, now + 8 * hour, async () => { observerSends++; return Response.json({ id }); });
    for (let slot = 0; slot < 2; slot++) await checkBackupMonitor(primary, null, async () => ({
      version: 1, observedAt: new Date(now + slot * 4 * hour).toISOString(), usage: slot ? 'warning' : 'stopped',
      lastRun: null, lastVerified: null }), async () => { primarySends++; return { accepted: true }; }, () => now + slot * 4 * hour);
    assert.ok(primary.values.get('monitor-state').dailyAttempts + observer.values.get('monitor-state').dailyAttempts <= 2);
  }
  assert.equal(primarySends, 16); assert.equal(observerSends, 15);
  const retained = primary.values.get('monitor-state'); retained.monthlyAttempts = 30;
  assert.equal((await checkBackupMonitor(storage(primary.values), null, async () => { throw new Error(); },
    async () => assert.fail('Legacy attempt refund'), () => Date.parse('2026-10-31T20:00:00Z'))).delivery, 'suppressed');
});

test('completed primary checks publish heartbeats, failed or undelivered checks do not', async () => {
  const db = storage(); let heartbeats = 0;
  const observe = now => async () => ({ version: 1, observedAt: new Date(now).toISOString(), usage: 'stopped',
    lastRun: null, lastVerified: null });
  const pulse = async () => { heartbeats++; };
  const first = start - 20 * hour;
  await assert.rejects(checkBackupMonitor(db, null, observe(first), async () => { throw new Error('send failed'); }, () => first, pulse));
  await checkBackupMonitor(db, null, observe(first + hour * 4), async () => assert.fail('Daily share spent'), () => first + hour * 4, pulse);
  assert.equal(heartbeats, 0);
  await checkBackupMonitor(db, null, observe(first + hour * 8), async () => assert.fail('Daily share spent'), () => first + hour * 8, pulse);
  assert.equal(heartbeats, 0, 'Expired uncertain incident cannot falsely revive the primary heartbeat');
  const accepted = storage();
  await checkBackupMonitor(accepted, null, observe(start), async () => ({ accepted: true }), () => start, pulse);
  await checkBackupMonitor(accepted, null, observe(start + 4 * hour), async () => assert.fail('Already accepted'), () => start + 4 * hour, pulse);
  assert.equal(heartbeats, 2);
});

test('disabled observer never touches storage or network; scheduled dispatch pins the separate stable object', async () => {
  await runMissingMonitor({ MISSING_MONITOR: { get() { assert.fail('Disabled observer touched storage'); } } });
  let calls = 0;
  const env = { WATCHDOG_ENABLED: 'true', MISSING_MONITOR: {
    idFromName(name) { assert.equal(name, watchdogObjectName); return 'separate-object'; },
    get(name) { assert.equal(name, 'separate-object'); return { fetch: async (url, options) => {
      calls++; assert.equal(url, 'https://watchdog.internal/check'); assert.equal(options.method, 'POST');
      return new Response(null, { status: 409 });
    } }; }
  } };
  await assert.rejects(runMissingMonitor(env), /unavailable/); assert.equal(calls, 1);
  const guard = new MissingMonitorGuard({ storage: storage() }, {});
  assert.equal((await guard.fetch(new Request('https://internal/check', { method: 'POST' }))).status, 409);
  const enabled = new MissingMonitorGuard({ storage: storage() }, { WATCHDOG_ENABLED: 'true' });
  for (const request of [new Request('https://internal/heartbeat'),
    new Request('https://internal/heartbeat', { method: 'POST', body: '{}' }),
    new Request('https://internal/unknown', { method: 'POST' })])
    assert.equal((await enabled.fetch(request)).status, 404);
});

test('observer accepts empty streamed heartbeats/checks and rejects actual payloads before touching state', async t => {
  t.mock.method(Date, 'now', () => start); const lines = [];
  t.mock.method(console, 'log', line => lines.push(JSON.parse(line)));
  const db = storage(), guard = new MissingMonitorGuard({ storage: db }, { WATCHDOG_ENABLED: 'true',
    ALERT_DELIVERY_JSON: JSON.stringify(config), RESEND_SEND_TOKEN: token });
  const request = (route, value) => new Request('https://internal/' + route, { method: 'POST', duplex: 'half',
    body: new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array());
      if (value.length) controller.enqueue(new TextEncoder().encode(value)); controller.close(); } }) });
  assert.equal((await guard.fetch(request('heartbeat', 'x'))).status, 404); assert.equal(db.values.size, 0);
  assert.equal((await guard.fetch(request('heartbeat', ''))).status, 204);
  assert.equal(db.values.get('monitor-heartbeat').completedAt, start);
  const before = structuredClone(db.values);
  assert.equal((await guard.fetch(request('check', 'x'))).status, 404); assert.deepEqual(db.values, before);
  assert.equal((await guard.fetch(request('check', ''))).status, 200);
  assert.equal(db.values.get('monitor-state').checks, 1);
  assert.deepEqual(lines.map(line => line.phase), ['heartbeat', 'before', 'after']);
});

test('a lease cannot charge yesterday or the previous month for a send after midnight', async () => {
  for (const at of ['2026-10-20T23:59:59Z', '2026-10-31T23:59:59Z']) {
    const db = storage(), now = Date.parse(at); let calls = 0;
    await assert.rejects(checkMissingMonitor(db, message => sendBackupAlert(db, config, token, message,
      async () => { calls++; return Response.json({ id }); }, () => now + 2000), () => now), /delivery unavailable/);
    assert.equal(calls, 0); assert.equal(db.values.get('monitor-state').monthlyAttempts, 1);
    assert.equal(db.values.get('monitor-state').resend, undefined);
  }
});

test('primary heartbeat transport failure cannot refund an accepted alert or refresh the receiver ledger', async () => {
  const db = storage(); let sends = 0, pulses = 0;
  const observe = now => async () => ({ version: 1, observedAt: new Date(now).toISOString(), usage: 'stopped',
    lastRun: null, lastVerified: null });
  const pulse = async () => { pulses++; throw new Error('Observer binding unavailable'); };
  for (const now of [start, start + 4 * hour]) await assert.rejects(checkBackupMonitor(db, null, observe(now),
    async () => { sends++; return { accepted: true }; }, () => now, pulse), /binding unavailable/);
  assert.equal(sends, 1); assert.equal(pulses, 2); assert.equal(db.values.get('monitor-state').monthlyAttempts, 1);
});

test('watchdog has its own namespace and named-only heartbeat; both deployments retain no schedule', () => {
  const observer = JSON.parse(readFileSync(new URL('./daily-watchdog.wrangler.json', import.meta.url)));
  const primary = JSON.parse(readFileSync(new URL('./daily-controls.wrangler.json', import.meta.url)));
  assert.equal(observer.vars.WATCHDOG_ENABLED, 'false');
  assert.equal(observer.workers_dev, false); assert.equal(observer.preview_urls, false);
  assert.equal(observer.triggers, undefined); assert.equal(primary.triggers, undefined);
  assert.notEqual(observer.name, primary.name);
  assert.deepEqual(observer.durable_objects.bindings, [{ name: 'MISSING_MONITOR', class_name: 'MissingMonitorGuard' }]);
  assert.deepEqual(primary.services, [{ binding: 'MONITOR_HEARTBEAT', service: observer.name, entrypoint: 'MonitorHeartbeat' }]);
});
