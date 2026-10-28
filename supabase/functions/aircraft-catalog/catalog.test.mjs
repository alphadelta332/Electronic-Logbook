import assert from 'node:assert/strict';
import { readFile, mkdtemp, readdir, mkdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { test } from 'node:test';
import { zipSync, strToU8 } from 'fflate';
import { buildSnapshot, parseTypes, validateSnapshot, validateManifest, validateLabels, fetchSnapshot, initialRevision, maxJsonBytes, boundedBytes } from './catalog.mjs';
import { replaceSnapshot } from '../../../tools/import-aircraft-catalog.mjs';

const seed = JSON.parse(gunzipSync(await readFile(new URL('../../../data/aircraft-catalog/mictronics.json.gz', import.meta.url))));
const revision = 'a'.repeat(40), date = '2026-10-25T09:05:04Z';
const fixtureTypes = { C172: ['CESSNA 172 Skyhawk', 'L1P', 'L'] };
const zip = (types = fixtureTypes, extra = {}) => zipSync({
    'types.json': strToU8(typeof types === 'string' ? types : JSON.stringify(types)),
    LICENSE: strToU8(seed.source.licenseText), ...extra,
});

test('embedded source snapshot has licensed provenance, 2774 named types and approved omissions', () => {
    validateSnapshot(seed);
    assert.equal(seed.count, 2774); assert.equal(seed.sourceCount, 2788);
    assert.equal(seed.omittedCodes.length, 14);
    assert.equal(seed.source.revision, initialRevision);
    assert.equal(seed.aircraft.find(row => row.i === 'C172').e, 1);
    assert.equal(seed.aircraft.find(row => row.i === 'GANT').e, null);
});

test('classification is retained and engine counts are conservative', () => {
    const { aircraft } = parseTypes(strToU8(JSON.stringify({
        C172: ['Cessna', 'L1P', 'L'], B738: ['Boeing', 'L2J', 'M'], R44: ['Robinson', 'H1P', 'L'],
        GLID: ['Glider', 'L0-', '-'], C08T: ['Soloy', 'L0T', 'L'], GLIM: ['Motorglider', 'L1M', 'L'],
        TEST: ['Coupled', 'LCT', 'L'], UNKN: ['Unknown', '', ''],
    })));
    assert.deepEqual(Object.fromEntries(aircraft.map(row => [row.i, row.e])),
        { B738: 2, C08T: null, C172: 1, GLID: null, GLIM: null, R44: 1, TEST: null, UNKN: null });
});

test('only the approved empty-name codes are omitted; named versions remain eligible', () => {
    const parsed = parseTypes(strToU8('{"BALL":["","B0-","-"],"C172":["Cessna","L1P","L"]}'));
    assert.deepEqual(parsed.omittedCodes, ['BALL']); assert.equal(parsed.count, 1); assert.equal(parsed.sourceCount, 2);
    assert.equal(parseTypes(strToU8('{"BALL":["Named balloon","B0-","-"]}')).count, 1);
});

test('rejects malformed/truncated input, invalid codes/names/metadata and escaped duplicate keys', () => {
    for (const input of ['', '{', '[]', '{}', 'null', '1',
        '{"C172":["A","L1P","L"],"C172":["B","L1P","L"]}',
        '{"C172":["A","L1P","L"],"\\u0043172":["B","L1P","L"]}',
        '{"c172":["Name","L1P","L"]}', '{"TOOLONG":["Name","L1P","L"]}',
        '{"C172":["","L1P","L"]}', '{"C172":[" ","L1P","L"]}',
        '{"BALL":["","bad","L"]}', '{"C172":["Name","bad","L"]}',
        '{"C172":["Name","L1P","bad"]}', '{"C172":["Name",1,"L"]}', '{"C172":["Name","L1P"]}'])
        assert.throws(() => parseTypes(strToU8(input)), undefined, input);
    assert.throws(() => parseTypes(new Uint8Array([255])));
    assert.throws(() => parseTypes(new Uint8Array(maxJsonBytes + 1)), /size/);
    assert.equal(parseTypes(strToU8('{"C172":["Name with \\\"quoted\\\": text","L1P","L"]}')).count, 1);
});

test('opens only types and licence, preserving source hashes and attribution', async () => {
    const snapshot = await buildSnapshot(zip(fixtureTypes, { 'registrations.json': new Uint8Array(8 * 1024 * 1024) }), revision, date);
    assert.equal(snapshot.count, 1); assert.equal(snapshot.source.licenseText, seed.source.licenseText);
    assert.ok(!JSON.stringify(snapshot).includes('registrations.json'));
    assert.match(snapshot.source.archiveSha256, /^[a-f0-9]{64}$/);
    assert.match(snapshot.source.attribution, /Mictronics/);
});

test('rejects damaged/truncated ZIP, missing/changed licence, initial pin mismatch and invalid upstream date', async () => {
    const archive = zip();
    for (const input of [new Uint8Array(), archive.subarray(0, archive.length - 20), zipSync({ 'types.json': strToU8('{}') }),
        zip(fixtureTypes, { LICENSE: strToU8('changed') })])
        await assert.rejects(buildSnapshot(input, revision, date));
    await assert.rejects(buildSnapshot(archive, initialRevision, date), /checksum/);
    await assert.rejects(buildSnapshot(archive, revision, '2026-02-31T00:00:00Z'), /date/);
});

test('rejects duplicate and oversized selected ZIP members before accepting data', async () => {
    const archive = Buffer.from(zip(fixtureTypes, { 'other.json': strToU8('{}') }));
    let offset = 0;
    while ((offset = archive.indexOf('other.json', offset)) !== -1) { archive.write('types.json', offset); offset += 10; }
    await assert.rejects(buildSnapshot(archive, revision, date), /Duplicate/);
    await assert.rejects(buildSnapshot(zip('x'.repeat(maxJsonBytes + 1)), revision, date), /oversized/);
});

test('snapshot and manifest reject duplicate codes, bad engine counts, count/hash/schema/path changes', () => {
    for (const mutation of [s => s.schemaVersion++, s => s.count--, s => s.aircraft[0].n = '',
        s => s.aircraft[0].e = 99, s => s.aircraft[1].i = s.aircraft[0].i, s => s.source.licenseSha256 = 'x']) {
        const changed = structuredClone(seed); mutation(changed); assert.throws(() => validateSnapshot(changed));
    }
    const manifest = { schemaVersion: 1, cycle: '2026-10-01', publishedAt: date, count: 2774,
        sha256: 'b'.repeat(64), path: `snapshots/${'b'.repeat(64)}.json.gz`, sourceRevision: revision, sourceCommittedAt: date, labelsSha256: 'c'.repeat(64) };
    validateManifest(manifest);
    for (const mutation of [m => m.schemaVersion++, m => m.count = 1, m => m.sha256 = 'bad', m => m.path = '../seed', m => m.sourceRevision = 'main']) {
        const changed = { ...manifest }; mutation(changed); assert.throws(() => validateManifest(changed));
    }
});

test('owner labels are optional, explicit and never rewrite upstream names or infer models', () => {
    assert.deepEqual(validateLabels({}, seed.aircraft), {});
    const original = seed.aircraft.find(row => row.i === 'C172').n;
    assert.deepEqual(validateLabels({ C172: 'CESSNA 172' }, seed.aircraft), { C172: 'CESSNA 172' });
    assert.equal(seed.aircraft.find(row => row.i === 'C172').n, original);
    for (const invalid of [null, [], { XXXX: 'Name' }, { C172: '' }, { C172: 1 }, { C172: 'Name\n' }])
        assert.throws(() => validateLabels(invalid, seed.aircraft));
});

test('latest refresh resolves immutable revision and records new source without using initial archive pin', async () => {
    const types = Object.fromEntries(Array.from({ length: 2000 }, (_, i) => [i.toString().padStart(4, '0'), [`Aircraft ${i}`, 'L1P', 'L']]));
    const urls = [];
    const snapshot = await fetchSnapshot('latest', async (url, options) => {
        urls.push(url); assert.equal(options.redirect, 'error');
        return urls.length === 1 ? Response.json({ sha: revision, commit: { committer: { date } } }) : new Response(zip(types));
    }, {});
    assert.ok(urls[0].endsWith('/commits/main')); assert.ok(urls[1].includes(`/${revision}/indexedDB.zip`));
    assert.equal(snapshot.source.revision, revision); assert.equal(snapshot.count, 2000);
});

test('bounded downloads and incomplete refreshes fail closed', async () => {
    await assert.rejects(boundedBytes(new Response('12345'), 4), /size/);
    await assert.rejects(boundedBytes(new Response('', { status: 503 }), 4), /failed/);
    let calls = 0;
    await assert.rejects(fetchSnapshot('latest', async () => ++calls === 1 ? Response.json({ sha: revision, commit: { committer: { date } } }) : new Response(zip()), {}), /snapshot/);
    await assert.rejects(fetchSnapshot(initialRevision, async () => Response.json({ sha: revision })), /mismatch/);
});

test('upstream revision quota exhaustion is reported without response bodies or credentials', async () => {
    await assert.rejects(fetchSnapshot('latest', async () => new Response('private upstream details', {
        status: 403, headers: { 'x-ratelimit-remaining': '0' },
    }), {}), error => error.name === 'AircraftRevisionRequestError' && error.cause === 403 &&
        error.rateLimitRemaining === 0 && !error.message.includes('private upstream'));
});

test('reproducible replacement and failed source/write retain the previous snapshot without temporary files', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'AircraftCatalog-'));
    try {
        const destination = join(directory, 'catalog.json.gz');
        const first = await replaceSnapshot(seed, destination), before = await readFile(destination);
        assert.equal(await replaceSnapshot(seed, destination), first);
        const invalid = { ...seed, count: 1 };
        await assert.rejects(replaceSnapshot(invalid, destination));
        assert.deepEqual(await readFile(destination), before);
        const blocked = join(directory, 'directory-instead-of-file'); await mkdir(blocked);
        await assert.rejects(replaceSnapshot(seed, blocked));
        assert.deepEqual((await readdir(directory)).sort(), ['catalog.json.gz', 'directory-instead-of-file']);
    } finally { await rm(directory, { recursive: true, force: true }); }
});
