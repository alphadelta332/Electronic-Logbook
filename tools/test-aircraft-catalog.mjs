import assert from 'node:assert/strict';
import { gunzipSync } from 'node:zlib';
import { airacCycle } from '../supabase/functions/airport-catalog/catalog.mjs';
import { boundedBytes, digest, validateManifest, validateSnapshot, maxJsonBytes } from '../supabase/functions/aircraft-catalog/catalog.mjs';

const origin = process.env.AIRCRAFT_CATALOG_TEST_ORIGIN, anon = process.env.AIRCRAFT_CATALOG_TEST_ANON_KEY;
assert.match(origin ?? '', /^https:\/\/[a-z]{20}\.supabase\.co$/);
assert.ok(anon);
const options = { redirect: 'error', signal: AbortSignal.timeout(45000) };
const root = origin + '/storage/v1/object/public/aircraft-catalog/';
const manifest = validateManifest(JSON.parse(new TextDecoder().decode(await boundedBytes(await fetch(root + 'manifest.json?validation=' + Date.now(), options), 4096))));
assert.equal(manifest.cycle, airacCycle());
const compressed = await boundedBytes(await fetch(root + manifest.path, options), 1024 * 1024);
assert.equal(await digest(compressed), manifest.sha256);
const snapshot = validateSnapshot(JSON.parse(gunzipSync(compressed, { maxOutputLength: maxJsonBytes })));
assert.equal(snapshot.count, manifest.count); assert.equal(snapshot.source.revision, manifest.sourceRevision);
assert.equal(snapshot.source.committedAt, manifest.sourceCommittedAt);
assert.equal(await digest(new TextEncoder().encode(snapshot.source.licenseText)), snapshot.source.licenseSha256);
assert.equal(await digest(new TextEncoder().encode(JSON.stringify(snapshot.labelOverrides ?? {}))), manifest.labelsSha256);
const unauthorized = await fetch(origin + '/functions/v1/aircraft-catalog', { ...options, method: 'POST', body: '{}' });
assert.equal(unauthorized.status, 401);
const clientPublisher = await fetch(origin + '/functions/v1/aircraft-catalog', { ...options, method: 'POST', headers: { Authorization: `Bearer ${anon}`, apikey: anon }, body: '{}' });
assert.equal(clientPublisher.status, 401);
const write = await fetch(origin + '/storage/v1/object/aircraft-catalog/anonymous-write-check-' + crypto.randomUUID() + '.json', {
    ...options, method: 'POST', headers: { Authorization: `Bearer ${anon}`, apikey: anon, 'Content-Type': 'application/json' }, body: '{}',
});
assert.ok([400, 401, 403].includes(write.status), `Client reference-data write must be rejected (${write.status})`);
console.log(JSON.stringify({ cycle: manifest.cycle, count: snapshot.count, sourceRevision: snapshot.source.revision,
    compressedBytes: compressed.byteLength, sha256: manifest.sha256, unnamedOmitted: snapshot.omittedCodes.length,
    labelOverrides: Object.keys(snapshot.labelOverrides ?? {}).length, anonymousPublisher: unauthorized.status,
    clientPublisher: clientPublisher.status, clientWrite: write.status }));
