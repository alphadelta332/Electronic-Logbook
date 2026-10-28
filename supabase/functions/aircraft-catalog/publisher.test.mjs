import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import vm from 'node:vm';
import { gzipSync } from 'node:zlib';
import { test } from 'node:test';
import { gunzipSync } from 'node:zlib';
import { digest, boundedBytes, validateManifest, validateSnapshot, maxJsonBytes } from './catalog.mjs';

const seed = JSON.parse(gunzipSync(readFileSync(new URL('../../../data/aircraft-catalog/mictronics.json.gz', import.meta.url))));
const source = stripTypeScriptTypes(readFileSync(new URL('./index.ts', import.meta.url), 'utf8').replace(/^import .*;\r?\n/gm, ''));
const cycle = '2026-10-01', token = 'a'.repeat(64), origin = 'https://abcdefghijklmnopqrst.supabase.co';
const labelsHash = await digest(new TextEncoder().encode(JSON.stringify(seed.labelOverrides ?? {})));

async function fixture({ current = false, sourceFailure = false, failWrite = '', corruptPrevious = false } = {}) {
    const objects = new Map(), calls = [], logs = [];
    const compressed = gzipSync(JSON.stringify(seed)), sha256 = await digest(compressed);
    const manifest = { schemaVersion: 1, cycle: current ? cycle : '2026-09-03', publishedAt: '2026-10-01T00:00:00Z',
        sha256, path: `snapshots/${sha256}.json.gz`, count: seed.count, sourceRevision: seed.source.revision,
        sourceCommittedAt: seed.source.committedAt, labelsSha256: labelsHash };
    objects.set('manifest.json', JSON.stringify(manifest));
    objects.set(manifest.path, corruptPrevious ? new Uint8Array([1]) : compressed);
    let handler, sourceCalls = 0;
    vm.runInNewContext(source, {
        Deno: { env: { get: name => ({ AIRCRAFT_CATALOG_PUBLISH_TOKEN: token, SUPABASE_URL: origin, SUPABASE_SERVICE_ROLE_KEY: 'server-key' })[name] }, serve: fn => handler = fn },
        Response, TextDecoder, TextEncoder, Uint8Array, Blob, CompressionStream, DecompressionStream, AbortSignal,
        console: { error: message => logs.push(message) }, labelOverrides: seed.labelOverrides ?? {}, digest, boundedBytes, validateManifest, validateSnapshot, maxJsonBytes,
        airacCycle: () => cycle,
        fetchSnapshot: async revision => { sourceCalls++; assert.equal(revision, 'latest'); if (sourceFailure) throw new Error('private upstream failure'); return seed; },
        fetch: async (url, options) => {
            assert.ok(url.startsWith(origin + '/storage/v1/object/aircraft-catalog/'), 'must never touch airport or personal buckets');
            assert.equal(options.headers.apikey, 'server-key'); assert.equal(options.redirect, 'error');
            const path = url.split('/aircraft-catalog/')[1]; calls.push({ path, method: options.method ?? 'GET' });
            if (options.method === 'POST') {
                if (path === failWrite || failWrite === 'snapshot' && path.startsWith('snapshots/')) return new Response('', { status: 503 });
                objects.set(path, options.body); return Response.json({ Key: path });
            }
            return objects.has(path) ? new Response(objects.get(path)) : new Response('', { status: 404 });
        },
    });
    const request = (credential = token, method = 'POST') => handler(new Request(origin + '/functions/v1/aircraft-catalog', {
        method, headers: credential ? { Authorization: 'Bearer ' + credential } : {},
    }));
    return { objects, calls, logs, request, sourceCalls: () => sourceCalls };
}

test('publisher rejects anonymous, client/service and airport credentials before storage or upstream access', async () => {
    const f = await fixture();
    for (const credential of ['', 'server-key', 'sb_publishable_client', 'b'.repeat(64)]) assert.equal((await f.request(credential)).status, 401);
    assert.equal((await f.request(token, 'GET')).status, 401); assert.equal(f.calls.length, 0); assert.equal(f.sourceCalls(), 0);
});

test('completed cycle suppresses upstream download and writes', async () => {
    const f = await fixture({ current: true });
    const response = await f.request(); assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { status: 'current', cycle, count: 2774 });
    assert.equal(f.sourceCalls(), 0); assert.deepEqual(f.calls, [{ path: 'manifest.json', method: 'GET' }]);
});

test('due publication validates prior copy and publishes compressed hash snapshot before manifest', async () => {
    const f = await fixture(); const response = await f.request(); assert.equal(response.status, 200);
    const result = await response.json(); assert.equal(result.status, 'published'); assert.equal(f.sourceCalls(), 1);
    const puts = f.calls.filter(c => c.method === 'POST');
    assert.equal(puts.length, 2); assert.equal(puts[0].path, `snapshots/${result.sha256}.json.gz`); assert.equal(puts[1].path, 'manifest.json');
    const manifest = validateManifest(JSON.parse(f.objects.get('manifest.json')));
    assert.equal(await digest(f.objects.get(manifest.path)), manifest.sha256);
    const snapshot = validateSnapshot(JSON.parse(gunzipSync(f.objects.get(manifest.path))));
    assert.equal(snapshot.aircraft.find(row => row.i === 'GANT').e, null); assert.equal(snapshot.source.licenseText, seed.source.licenseText);
});

test('source, checksum, snapshot upload and manifest upload failures retain the previous manifest', async () => {
    for (const options of [{ sourceFailure: true }, { corruptPrevious: true }, { failWrite: 'snapshot' }, { failWrite: 'manifest.json' }]) {
        const f = await fixture(options), previous = f.objects.get('manifest.json');
        assert.equal((await f.request()).status, 503); assert.equal(f.objects.get('manifest.json'), previous);
        assert.ok(f.logs.every(line => !line.includes(token) && !line.includes('private upstream')));
    }
});

test('bad/future manifest fails closed and approved label changes can publish within a cycle', async () => {
    for (const update of [{ path: '../other-bucket' }, { cycle: '2027-01-01' }, { sourceRevision: 'main' }]) {
        const f = await fixture(); f.objects.set('manifest.json', JSON.stringify({ ...JSON.parse(f.objects.get('manifest.json')), ...update }));
        assert.equal((await f.request()).status, 503); assert.equal(f.sourceCalls(), 0);
    }
    const f = await fixture({ current: true });
    f.objects.set('manifest.json', JSON.stringify({ ...JSON.parse(f.objects.get('manifest.json')), labelsSha256: 'c'.repeat(64) }));
    assert.equal((await (await f.request()).json()).status, 'published'); assert.equal(f.sourceCalls(), 1);
});

test('first publication does not depend on an existing manifest', async () => {
    const f = await fixture(); f.objects.clear();
    assert.equal((await (await f.request()).json()).status, 'published'); assert.equal(f.sourceCalls(), 1);
});
