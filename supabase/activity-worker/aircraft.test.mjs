import assert from 'node:assert/strict';
import { test } from 'node:test';
import worker from './worker.mjs';
import { airacCycle } from '../functions/airport-catalog/catalog.mjs';

test('aircraft timer is isolated from airport publication and Preview activity probes', async () => {
    const token = 'a'.repeat(64), origin = 'https://abcdefghijklmnopqrst.supabase.co';
    const env = { SUPABASE_URL: origin, AIRCRAFT_CATALOG_PUBLISH_TOKEN: token };
    const endpoint = origin + '/functions/v1/aircraft-catalog';
    const current = { status: 'current', cycle: airacCycle(), count: 2774 };
    const originalFetch = globalThis.fetch, originalLog = console.log;
    const logs = [], calls = [];
    const reply = (body = current, status = 200, url = endpoint) => ({ status, url, json: async () => body });
    try {
        console.log = value => logs.push(value);
        globalThis.fetch = async (url, options) => {
            calls.push(url); assert.equal(url, endpoint); assert.equal(options.headers.Authorization, `Bearer ${token}`);
            assert.equal(options.method, 'POST'); assert.equal(options.redirect, 'manual'); return reply();
        };
        await worker.scheduled({ cron: '11 * * * *' }, env); assert.equal(calls.length, 1);
        for (const invalid of [{}, { ...env, AIRCRAFT_CATALOG_PUBLISH_TOKEN: undefined }, { ...env, AIRCRAFT_CATALOG_PUBLISH_TOKEN: 'server-key' }])
            await assert.rejects(worker.scheduled({ cron: '11 * * * *' }, invalid), /configuration/);
        for (const response of [reply(current, 401), reply(current, 302), reply(current, 503), reply(current, 200, 'https://wrong-project.invalid'),
            reply(null), reply([]), reply({ ...current, count: 1 }), reply({ ...current, count: 20001 }), reply({ ...current, extra: token }),
            reply({ ...current, cycle: 'bad' }), reply({ ...current, status: 'published', sha256: 'bad' })]) {
            globalThis.fetch = async () => response;
            await assert.rejects(worker.scheduled({ cron: '11 * * * *' }, env), /request failed/);
        }
        globalThis.fetch = async () => { throw new Error(token); };
        await assert.rejects(worker.scheduled({ cron: '11 * * * *' }, env), /request failed/);
        assert.equal(logs.length, 1); assert.ok(!logs.join('').includes(token));
    } finally { globalThis.fetch = originalFetch; console.log = originalLog; }
});
