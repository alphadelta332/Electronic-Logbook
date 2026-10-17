import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import vm from 'node:vm';
import worker from './worker.mjs';
import { airacCycle } from '../functions/airport-catalog/catalog.mjs';

const token = 'a'.repeat(64);
const env = { SUPABASE_URL: 'https://abcdefghijklmnopqrst.supabase.co', AIRPORT_CATALOG_PUBLISH_TOKEN: token };
const endpoint = env.SUPABASE_URL + '/functions/v1/airport-catalog';
const originalFetch = globalThis.fetch, originalLog = console.log;
let calls = 0, logs = [];
const current = { status: 'current', cycle: airacCycle(), count: 12000 };
const reply = (body = current, status = 200, url = endpoint) => ({ status, url, json: async () => body });
try {
  console.log = value => logs.push(value);
  globalThis.fetch = async (url, options) => {
    calls++;
    assert.equal(url, endpoint);
    assert.equal(options.headers.Authorization, 'Bearer ' + token);
    assert.equal(options.method, 'POST'); assert.equal(options.redirect, 'manual');
    assert.equal(options.body, '{}'); assert.ok(options.signal instanceof AbortSignal);
    return reply();
  };
  await worker.scheduled({ cron: '7 * * * *' }, env);
  assert.equal(calls, 1);
  for (const invalid of [{}, { ...env, AIRPORT_CATALOG_PUBLISH_TOKEN: undefined },
    { ...env, AIRPORT_CATALOG_PUBLISH_TOKEN: 'sb_secret_privileged' },
    { ...env, SUPABASE_URL: env.SUPABASE_URL + '@evil.invalid' }])
    await assert.rejects(worker.scheduled({ cron: '7 * * * *' }, invalid), /configuration/);
  assert.equal(calls, 1);
  for (const response of [reply(current, 401), reply(current, 302), reply(current, 503),
    reply(current, 200, 'https://wrong-project.invalid'), reply(null), reply([]),
    reply({ ...current, extra: 'private' }), reply({ ...current, count: 1 }),
    reply({ ...current, status: 'failed' }), reply({ ...current, cycle: 'invalid' }),
    reply({ ...current, status: 'published', sha256: 'invalid' }),
    { status: 200, url: endpoint, json: async () => { throw new Error('private'); } }]) {
    globalThis.fetch = async () => { calls++; return response; };
    const before = calls;
    await assert.rejects(worker.scheduled({ cron: '7 * * * *' }, env), /request failed/);
    assert.equal(calls, before + 1, 'failure must not retry');
  }
  globalThis.fetch = async () => reply({ ...current, status: 'published', sha256: 'b'.repeat(64) });
  await worker.scheduled({ cron: '* * * * *' }, env);
  globalThis.fetch = async () => { throw new Error('private credential'); };
  await assert.rejects(worker.scheduled({ cron: '7 * * * *' }, env), /request failed/);
  assert.equal(logs.length, 2); assert.ok(!logs.join('').includes(token));
} finally { globalThis.fetch = originalFetch; console.log = originalLog; }

// Execute the real Edge entry point with its runtime mocked; unauthorized calls make no storage requests.
let handler, storageCalls = 0;
let configuredToken = token;
const source = readFileSync(new URL('../functions/airport-catalog/index.ts', import.meta.url), 'utf8').replace(/^import .*;\r?\n/gm, '');
vm.runInNewContext(stripTypeScriptTypes(source), {
  Deno: { env: { get: name => name === 'AIRPORT_CATALOG_PUBLISH_TOKEN' ? configuredToken : name === 'SUPABASE_URL' ? env.SUPABASE_URL : 'server-only-key' }, serve: fn => handler = fn },
  Response, Uint8Array, TextDecoder, TextEncoder, TransformStream, Blob, AbortSignal, console, crypto,
  airacCycle: () => current.cycle,
  fetch: async (_url, options) => { storageCalls++; assert.equal(options.headers.apikey, 'server-only-key'); return Response.json(current); },
});
for (const credential of [undefined, 'server-only-key', 'sb_publishable_test', 'b'.repeat(64)]) {
  const headers = credential ? { Authorization: 'Bearer ' + credential } : {};
  assert.equal((await handler(new Request(endpoint, { method: 'POST', headers }))).status, 401);
}
assert.equal(storageCalls, 0);
const authorized = () => new Request(endpoint, { method: 'POST', headers: { Authorization: 'Bearer ' + token } });
assert.equal((await handler(authorized())).status, 200);
assert.equal(storageCalls, 1);
configuredToken = undefined;
assert.equal((await handler(authorized())).status, 401);
assert.equal(storageCalls, 1);
console.log('Airport scheduler and real publisher authentication/fail-closed checks passed.');
