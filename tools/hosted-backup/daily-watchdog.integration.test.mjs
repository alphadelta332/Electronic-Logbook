import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:net';

test('isolated observer SQLite schedules/deduplicates alerts with no primary Worker, survives restart and secret rotation', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'elb-daily-watchdog-'));
  const socket = createServer(); await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
  const port = socket.address().port; await new Promise(resolve => socket.close(resolve));
  const config = JSON.parse(readFileSync(new URL('./daily-watchdog.wrangler.json', import.meta.url)));
  const harness = join(directory, 'worker.mjs');
  writeFileSync(harness, `
    import watchdog, { MissingMonitorGuard, MonitorHeartbeat } from ${JSON.stringify(fileURLToPath(new URL('./daily-watchdog-worker.mjs', import.meta.url)))};
    import { watchdogStub } from ${JSON.stringify(fileURLToPath(new URL('./daily-watchdog.mjs', import.meta.url)))};
    export { MonitorHeartbeat };
    let now = Date.now(), wireCalls = 0, loseReply = true;
    Date.now = () => now;
    // Synthetic-only outbound transport. Any unexpected provider request fails immediately.
    globalThis.fetch = async (url, options) => {
      if (url !== 'https://api.resend.com/emails' || options.method !== 'POST') throw new Error('Unexpected outbound I/O');
      wireCalls++;
      if (loseReply) throw new Error('Synthetic lost reply');
      return Response.json({ id: '01234567-89ab-cdef-0123-456789abcdef' });
    };
    export class FixtureGuard extends MissingMonitorGuard {
      async fetch(request) {
        if (new URL(request.url).pathname === '/fixture-state') return Response.json({
          monitor: await this.storage.get('monitor-state'), heartbeat: await this.storage.get('monitor-heartbeat'), wireCalls });
        return super.fetch(request);
      }
    }
    export default {
      async fetch(request, env) {
        const route = new URL(request.url).pathname;
        if (!route.startsWith('/fixture-')) return watchdog.fetch(request, env);
        const body = await request.json(); now = body.now; loseReply = body.fail !== false;
        if (route === '/fixture-state') return watchdogStub(env).fetch('https://internal/fixture-state');
        try {
          if (route === '/fixture-heartbeat') await env.TEST_HEARTBEAT.completed();
          else if (route === '/fixture-scheduled') await watchdog.scheduled({}, env);
          else return new Response('', { status: 404 });
          return new Response('', { status: 204 });
        } catch { return new Response('', { status: 409 }); }
      }
    };
  `);
  config.main = harness;
  config.durable_objects.bindings[0].class_name = 'FixtureGuard';
  config.migrations[0].new_sqlite_classes = ['FixtureGuard'];
  config.services = [{ binding: 'TEST_HEARTBEAT', service: config.name, entrypoint: 'MonitorHeartbeat' }];
  config.vars = { WATCHDOG_ENABLED: 'true', ALERT_DELIVERY_JSON: JSON.stringify({ version: 1, recipient: 'owner@example.test' }),
    RESEND_SEND_TOKEN: 're_' + 'a'.repeat(30) };
  const configFile = join(directory, 'wrangler.json'); writeFileSync(configFile, JSON.stringify(config));
  const base = `http://127.0.0.1:${port}`;
  let child, closed, log = '';
  const start = async () => {
    log = '';
    child = spawn(process.execPath, [join(process.env.APPDATA, 'npm/node_modules/wrangler/bin/wrangler.js'),
      'dev', '--local', '--config', configFile, '--ip', '127.0.0.1', '--port', String(port), '--persist-to', join(directory, 'state')],
    { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: { SystemRoot: process.env.SystemRoot, PATH: process.env.PATH,
      TEMP: process.env.TEMP, TMP: process.env.TMP, USERPROFILE: process.env.USERPROFILE, APPDATA: process.env.APPDATA,
      LOCALAPPDATA: process.env.LOCALAPPDATA, WRANGLER_SEND_METRICS: 'false', CI: 'true', BROWSER: 'none' } });
    closed = new Promise(resolve => child.once('close', resolve));
    for (const stream of [child.stdout, child.stderr]) stream.on('data', part => { log = (log + part.toString()).slice(-8000); });
    for (let attempt = 0; attempt < 100; attempt++) {
      if (child.exitCode !== null) break;
      try { if ((await fetch(base, { signal: AbortSignal.timeout(500) })).status === 404) return; } catch {}
      await new Promise(resolve => setTimeout(resolve, 150));
    }
    assert.fail('Local observer did not start: ' + log);
  };
  const stop = async () => {
    if (!child) return;
    child.stdin.end();
    if (child.exitCode === null) {
      if (process.platform === 'win32') spawnSync('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, timeout: 10000 });
      else child.kill();
    }
    let timer;
    try { assert.equal(await Promise.race([closed.then(() => true), new Promise(resolve => { timer = setTimeout(() => resolve(false), 10000); })]), true); }
    finally { clearTimeout(timer); child = undefined; }
  };
  const hour = 3600000, first = Date.parse('2026-10-01T20:00:00Z');
  const send = (route, now, fail = true) => fetch(base + '/fixture-' + route, { method: 'POST',
    body: JSON.stringify({ now, fail }), signal: AbortSignal.timeout(10000) });
  const state = async now => (await send('state', now)).json();
  try {
    await start();
    for (const method of ['GET', 'POST']) assert.equal((await fetch(base + '/heartbeat', { method })).status, 404);
    // No primary deployment or ledger exists in this runtime. Only the observer and synthetic Resend are present.
    const checks = await Promise.all(Array.from({ length: 16 }, () => send('scheduled', first)));
    assert.equal(checks.filter(response => response.status === 409).length, 16);
    let before = await state(first);
    assert.equal(before.wireCalls, 1); assert.equal(before.monitor.monthlyAttempts, 1);
    assert.equal(before.monitor.resend.attempt, 1); assert.equal(before.heartbeat, undefined);
    const incident = before.monitor.incident, bodySha256 = before.monitor.resend.bodySha256;
    await stop(); config.vars.RESEND_SEND_TOKEN = 're_' + 'b'.repeat(30);
    writeFileSync(configFile, JSON.stringify(config)); await start();
    assert.equal((await send('scheduled', first + 4 * hour)).status, 409);
    let after = await state(first + 4 * hour);
    assert.equal(after.wireCalls, 1); assert.equal(after.monitor.monthlyAttempts, 2);
    assert.equal(after.monitor.incident, incident); assert.equal(after.monitor.resend.bodySha256, bodySha256);
    assert.equal(after.monitor.resend.attempt, 2); assert.equal(after.monitor.dailyAttempts, 1);
    await stop(); await start();
    assert.equal((await send('scheduled', first + 8 * hour, false)).status, 204);
    after = await state(first + 8 * hour);
    assert.equal(after.wireCalls, 0); assert.equal(after.monitor.monthlyAttempts, 2);
    assert.equal(after.monitor.incident, incident); assert.equal(after.monitor.resend.bodySha256, bodySha256);
    // Exercise the real named service-binding RPC, not an HTTP heartbeat endpoint.
    assert.equal((await send('heartbeat', first + 12 * hour)).status, 204);
    assert.equal((await send('heartbeat', first + 12 * hour)).status, 409);
    assert.equal((await send('scheduled', first + 12 * hour)).status, 204);
    after = await state(first + 12 * hour);
    assert.equal(after.heartbeat.completedAt, first + 12 * hour); assert.deepEqual(after.monitor.conditions, []);
    await stop(); await start();
    assert.equal((await send('scheduled', first + 20 * hour, false)).status, 204);
    after = await state(first + 20 * hour);
    assert.deepEqual(after.monitor.conditions, ['missing-monitor']);
    assert.equal(after.monitor.monthlyAttempts, 2, 'Recovery does not refund today\'s spent delivery share');
    assert.equal(after.wireCalls, 0);
    assert.equal((await send('scheduled', first + 28 * hour, false)).status, 204);
    after = await state(first + 28 * hour);
    assert.equal(after.monitor.monthlyAttempts, 3); assert.equal(after.monitor.delivered, true);
    assert.equal(after.wireCalls, 1);
  } finally {
    await stop();
    await assert.rejects(fetch(base, { signal: AbortSignal.timeout(500) }));
    // Retained SQLite is regenerated local evidence, never operational state or transferred authentication.
  }
});
