import test from 'node:test';
import assert from 'node:assert/strict';
import { checkBackupMonitor, monitorAlertMessage } from './daily-monitor.mjs';
import { sendBackupAlert } from './daily-alert-delivery.mjs';

const start = Date.parse('2026-10-20T00:00:00Z'), hour = 3600000;
const config = { version: 1, recipient: 'owner@example.test' }, token = 're_' + 'a'.repeat(30);
const id = '01234567-89ab-cdef-0123-456789abcdef';
function storage(values = new Map()) {
  let tail = Promise.resolve();
  return { values, transaction(action) {
    const operation = tail.then(async () => {
      const next = structuredClone(values), result = await action({ get: async key => structuredClone(next.get(key)),
        put: async (key, value) => next.set(key, structuredClone(value)) });
      values.clear(); for (const [key, value] of next) values.set(key, value); return result;
    });
    tail = operation.catch(() => {}); return operation;
  } };
}
const observe = now => async () => ({ version: 1, observedAt: new Date(now).toISOString(), usage: 'stopped',
  lastRun: null, lastVerified: null });
const run = (db, request, now = start, settings = config, key = token) => checkBackupMonitor(db, null, observe(now),
  message => sendBackupAlert(db, settings, key, message, request, () => now), () => now);

test('real monitor reservation sends one exact fixed-origin request and persists wire binding without recipient/token', async () => {
  const db = storage(); let calls = 0;
  const result = await run(db, async (url, options) => {
    calls++; assert.equal(url, 'https://api.resend.com/emails'); assert.equal(options.method, 'POST');
    assert.equal(options.redirect, 'manual'); assert.ok(options.signal);
    const state = db.values.get('monitor-state');
    assert.equal(state.incidentAttempts, 1); assert.equal(state.resend.attempt, 1);
    assert.match(state.resend.bodySha256, /^[a-f0-9]{64}$/);
    assert.equal(options.headers.Authorization, 'Bearer ' + token);
    assert.equal(options.headers['Idempotency-Key'], `backup-monitor/${state.incident}`);
    assert.deepEqual(JSON.parse(options.body), { from: 'FlightLogX backups <backups@auth.flightlogx.app>',
      to: [config.recipient], subject: 'FlightLogX backup needs attention', text: monitorAlertMessage(state).text });
    assert.ok(!JSON.stringify(state).includes(config.recipient)); assert.ok(!JSON.stringify(state).includes(token));
    return Response.json({ id });
  });
  assert.equal(result.delivery, 'accepted'); assert.equal(calls, 1);
  assert.equal(db.values.get('monitor-state').delivered, true);
  await run(storage(db.values), async () => { calls++; throw new Error('Unexpected retry'); }, start + 4 * hour);
  assert.equal(calls, 1);
});

test('isolated acceptance ledger labels test email without marking ordinary monitoring as a test', async () => {
  const db = storage(new Map([['monitor-state', { deliveryTest: true, startedAt: start - 4 * hour, day: '2026-10-19' }]]));
  await run(db, async (_, options) => {
    const body = JSON.parse(options.body);
    assert.equal(body.subject, 'FlightLogX backup alert test');
    assert.match(body.text, /^Supervised delivery test only\. Backup automation remains disabled\./);
    return Response.json({ id });
  });
});

test('failed reply retries identical wire body/key once after restart and sending-key rotation', async () => {
  const db = storage(); const messages = [];
  const request = async (_, options) => { messages.push(options); throw new Error('private diagnostics'); };
  await assert.rejects(run(db, request, start + 20 * hour), /delivery unavailable/);
  await assert.rejects(run(storage(db.values), request, start + 24 * hour, config, 're_' + 'b'.repeat(30)), /delivery unavailable/);
  assert.equal(messages.length, 2); assert.equal(messages[0].body, messages[1].body);
  assert.equal(messages[0].headers['Idempotency-Key'], messages[1].headers['Idempotency-Key']);
  assert.notEqual(messages[0].headers.Authorization, messages[1].headers.Authorization);
  assert.equal(db.values.get('monitor-state').resend.attempt, 2);
  const result = await run(storage(db.values), request, start + 28 * hour);
  assert.equal(result.delivery, 'suppressed'); assert.equal(messages.length, 2);
});

test('recipient/config rotation cannot mutate an incident wire body or refund a reserved attempt', async () => {
  const db = storage(); let calls = 0;
  const request = async () => { calls++; throw new Error('uncertain reply'); };
  await assert.rejects(run(db, request, start + 20 * hour));
  await assert.rejects(run(storage(db.values), request, start + 24 * hour,
    { version: 1, recipient: 'different@example.test' }), /delivery unavailable/);
  assert.equal(calls, 1); assert.equal(db.values.get('monitor-state').incidentAttempts, 2);
});

test('same reservation cannot spend twice, and unreserved/mutated/late messages deny before I/O', async () => {
  const db = storage(); let message, calls = 0;
  await assert.rejects(checkBackupMonitor(db, null, observe(start), async current => {
    message = current;
    const request = async () => { calls++; throw new Error('lost response'); };
    await assert.rejects(sendBackupAlert(db, config, token, message, request, () => start));
    await assert.rejects(sendBackupAlert(db, config, token, message, request, () => start));
    throw new Error('end check');
  }, () => start));
  assert.equal(calls, 1);
  for (const changed of [message, { ...message, text: 'private data' }, { ...message, subject: 'other' }])
    await assert.rejects(sendBackupAlert(db, config, token, changed, async () => { calls++; }, () => start));
  await assert.rejects(sendBackupAlert(storage(), config, token, message, async () => { calls++; }, () => start));
  assert.equal(calls, 1);
});

test('sixteen concurrent adapter callbacks spend exactly one reserved attempt', async () => {
  const db = storage(); let calls = 0;
  const result = await checkBackupMonitor(db, null, observe(start), async message => {
    const replies = await Promise.allSettled(Array.from({ length: 16 }, () => sendBackupAlert(db, config, token, message,
      async () => { calls++; return Response.json({ id }); }, () => start)));
    assert.equal(replies.filter(reply => reply.status === 'fulfilled').length, 1);
    return replies.find(reply => reply.status === 'fulfilled').value;
  }, () => start);
  assert.equal(calls, 1); assert.equal(result.delivery, 'accepted');
});

for (const recipient of ['x@example.test\r\nBcc:secret@example.test', 'a@example.test,b@example.test',
  ['a@example.test'], 'owner', 'a@example.test\n', 'x'.repeat(260) + '@example.test'])
  test('invalid or multiple recipients cannot transmit', async () => {
    let calls = 0;
    await assert.rejects(run(storage(), async () => { calls++; }, start, { version: 1, recipient }));
    assert.equal(calls, 0);
  });

for (const status of [301, 302, 400, 401, 403, 409, 429, 500])
  test(`HTTP ${status} is spent once without redirects/retries/provider diagnostics`, async () => {
    const db = storage(); let calls = 0;
    await assert.rejects(run(db, async () => { calls++; return new Response('private provider data', { status }); }),
      error => error.message === 'Backup alert delivery unavailable.');
    assert.equal(calls, 1); assert.equal(db.values.get('monitor-state').resend.attempt, 1);
    assert.equal(db.values.get('monitor-state').delivered, false);
  });

for (const response of [() => Response.json({}), () => Response.json({ id: 'bad' }),
  () => new Response('bad JSON'), () => new Response('x'.repeat(4097)),
  () => new Response('{}', { headers: { 'content-length': '9999' } })])
  test('malformed/oversized success is uncertain, never accepted', async () => {
    const db = storage(); await assert.rejects(run(db, async () => response()), /delivery unavailable/);
    assert.equal(db.values.get('monitor-state').delivered, false);
  });
