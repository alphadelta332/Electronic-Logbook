import { unzipSync } from 'fflate';
import labelOverrides from '../../../data/aircraft-catalog/label-overrides.json' with { type: 'json' };

export const initialRevision = 'd31104a82bdc429be7fe94726247fc45aa0c49a4';
export const initialArchiveHash = 'fdff9ab076f279658d21aa4bc61f7b2b8310410000a8e51b7cd3c29989a094f7';
export const licenseHash = '11a6d83734845ad09d809667aa219857a5b985b28c258a2eebf5677668a6bd3b';
export const repository = 'https://github.com/Mictronics/aircraft-database';
export const maxArchiveBytes = 16 * 1024 * 1024;
export const maxJsonBytes = 4 * 1024 * 1024;
const unnamedCodes = new Set('BALL DRON EMER FFLO GND GYRO PARA SERV SHIP TWR UHEL ULAC VFHC ZZZZ'.split(' '));
const decoder = new TextDecoder('utf-8', { fatal: true });

export async function digest(data) {
    return [...new Uint8Array(await crypto.subtle.digest('SHA-256', data))].map(value => value.toString(16).padStart(2, '0')).join('');
}

export function engineCount(classification) {
    // Mictronics also uses zero for coupled engines (C08T/GANT); keep these unknown.
    return /^[ALHGSRT][123468][PJTER]$/.test(classification) ? Number(classification[1]) : null;
}

export function validateAircraft(row) {
    if (!row || typeof row.i !== 'string' || !/^[A-Z0-9]{2,4}$/.test(row.i) ||
        typeof row.n !== 'string' || !row.n.trim() || row.n.length > 500 || /[\u0000-\u001f]/.test(row.n) ||
        typeof row.c !== 'string' || row.c !== '' && !/^[A-Z-][0-9C-][A-Z-]$/.test(row.c) ||
        !['', '-', 'L', 'M', 'H', 'J'].includes(row.w) || row.e !== engineCount(row.c))
        throw new Error('Invalid aircraft record');
    return row;
}

export function validateLabels(labels, aircraft) {
    if (!labels || Array.isArray(labels) || typeof labels !== 'object') throw new Error('Invalid aircraft labels');
    const codes = new Set(aircraft.map(row => row.i));
    for (const [code, name] of Object.entries(labels)) {
        if (!codes.has(code) || typeof name !== 'string' || !name.trim() || name.length > 500 || /[\u0000-\u001f]/.test(name))
            throw new Error('Invalid aircraft label override');
    }
    return labels;
}

export function parseTypes(data) {
    if (data.byteLength > maxJsonBytes) throw new Error('Aircraft types exceed size bound');
    const text = decoder.decode(data);
    const types = JSON.parse(text);
    if (!types || Array.isArray(types) || typeof types !== 'object' || !Object.keys(types).length || Object.keys(types).length > 20000)
        throw new Error('Invalid aircraft type object/count');
    // JSON.parse otherwise silently overwrites duplicate source codes, including escaped keys.
    const keys = new Set();
    for (const token of text.matchAll(/"(?:[^"\\]|\\.)*"/g)) {
        if (!/^\s*:/.test(text.slice(token.index + token[0].length))) continue;
        const key = JSON.parse(token[0]);
        if (keys.has(key)) throw new Error('Duplicate aircraft code');
        keys.add(key);
    }
    const aircraft = [], omittedCodes = [];
    for (const code of Object.keys(types).sort()) {
        const values = types[code];
        if (!/^[A-Z0-9]{2,4}$/.test(code) || !Array.isArray(values) || values.length !== 3 || values.some(value => typeof value !== 'string'))
            throw new Error('Invalid source aircraft');
        const [name, classification, wake] = values;
        const row = { i: code, n: name, c: classification, w: wake, e: engineCount(classification) };
        if (name === '' && unnamedCodes.has(code)) {
            validateAircraft({ ...row, n: code });
            omittedCodes.push(code);
        } else aircraft.push(validateAircraft(row));
    }
    return { aircraft, count: aircraft.length, sourceCount: Object.keys(types).length, omittedCodes };
}

export async function buildSnapshot(archive, revision, committedAt, labels = {}) {
    if (!/^[a-f0-9]{40}$/.test(revision) || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(committedAt) ||
        !Number.isFinite(Date.parse(committedAt)) || new Date(committedAt).toISOString() !== committedAt.replace('Z', '.000Z'))
        throw new Error('Invalid upstream revision/date');
    if (!archive.byteLength || archive.byteLength > maxArchiveBytes) throw new Error('Aircraft archive exceeds size bound');
    const archiveSha256 = await digest(archive);
    if (revision === initialRevision && archiveSha256 !== initialArchiveHash) throw new Error('Aircraft archive checksum mismatch');
    const seen = new Set();
    const files = unzipSync(archive, { filter: entry => {
        if (!['types.json', 'LICENSE'].includes(entry.name)) return false;
        const limit = entry.name === 'types.json' ? maxJsonBytes : 128 * 1024;
        if (seen.has(entry.name) || !entry.originalSize || entry.originalSize > limit) throw new Error('Duplicate/oversized ZIP member');
        seen.add(entry.name);
        return true;
    } });
    if (!files['types.json'] || !files.LICENSE) throw new Error('Missing types/licence ZIP member');
    if (await digest(files.LICENSE) !== licenseHash) throw new Error('Upstream licence changed; review required');
    const parsed = parseTypes(files['types.json']);
    return {
        schemaVersion: 1,
        source: {
            name: 'Mictronics aircraft-database', url: repository, revision, committedAt,
            archiveUrl: `https://raw.githubusercontent.com/Mictronics/aircraft-database/${revision}/indexedDB.zip`,
            archiveSha256, typesSha256: await digest(files['types.json']), license: 'ODC-By-1.0',
            licenseUrl: `${repository}/blob/${revision}/LICENSE`, licenseSha256: licenseHash,
            licenseText: decoder.decode(files.LICENSE),
            attribution: 'Contains information from Mictronics aircraft-database, made available under the ODC Attribution License.',
        },
        ...parsed,
        labelOverrides: validateLabels(labels, parsed.aircraft),
    };
}

export function validateSnapshot(snapshot) {
    if (snapshot?.schemaVersion !== 1 || !Array.isArray(snapshot.aircraft) || snapshot.count !== snapshot.aircraft.length ||
        snapshot.count < 2000 || snapshot.count > 20000 || !Array.isArray(snapshot.omittedCodes) ||
        snapshot.sourceCount !== snapshot.count + snapshot.omittedCodes.length ||
        snapshot.omittedCodes.some(code => !unnamedCodes.has(code)) || new Set(snapshot.omittedCodes).size !== snapshot.omittedCodes.length ||
        snapshot.source?.licenseSha256 !== licenseHash || snapshot.source.license !== 'ODC-By-1.0' ||
        !/^[a-f0-9]{40}$/.test(snapshot.source.revision) || !Number.isFinite(Date.parse(snapshot.source.committedAt)) ||
        !/^[a-f0-9]{64}$/.test(snapshot.source.archiveSha256) || !/^[a-f0-9]{64}$/.test(snapshot.source.typesSha256) ||
        typeof snapshot.source.licenseText !== 'string' || !snapshot.source.licenseText.length)
        throw new Error('Invalid aircraft snapshot');
    const codes = new Set(snapshot.omittedCodes);
    for (const row of snapshot.aircraft) {
        validateAircraft(row);
        if (codes.has(row.i)) throw new Error('Duplicate aircraft snapshot code');
        codes.add(row.i);
    }
    validateLabels(snapshot.labelOverrides ?? {}, snapshot.aircraft);
    return snapshot;
}

export function validateManifest(manifest) {
    if (manifest?.schemaVersion !== 1 || !/^\d{4}-\d{2}-\d{2}$/.test(manifest.cycle) ||
        !Number.isFinite(Date.parse(manifest.publishedAt)) || !Number.isInteger(manifest.count) || manifest.count < 2000 || manifest.count > 20000 ||
        !/^[a-f0-9]{64}$/.test(manifest.sha256) || manifest.path !== `snapshots/${manifest.sha256}.json.gz` ||
        !/^[a-f0-9]{40}$/.test(manifest.sourceRevision) || !Number.isFinite(Date.parse(manifest.sourceCommittedAt)) ||
        !/^[a-f0-9]{64}$/.test(manifest.labelsSha256))
        throw new Error('Invalid aircraft manifest');
    return manifest;
}

export async function boundedBytes(response, limit) {
    if (!response.ok || !response.body) throw new Error('Aircraft request failed', { cause: response.status });
    let total = 0;
    const stream = response.body.pipeThrough(new TransformStream({ transform(chunk, controller) {
        total += chunk.byteLength;
        if (total > limit) throw new Error('Aircraft response exceeds size bound');
        controller.enqueue(chunk);
    } }));
    return new Uint8Array(await new Response(stream).arrayBuffer());
}

export async function fetchSnapshot(revision = 'latest', fetcher = fetch, labels = labelOverrides) {
    if (revision !== 'latest' && !/^[a-f0-9]{40}$/.test(revision)) throw new Error('Invalid source revision');
    const options = () => ({ redirect: 'error', signal: AbortSignal.timeout(45000), headers: { 'User-Agent': 'FlightLogX-aircraft-catalog' } });
    const commitResponse = await fetcher(`https://api.github.com/repos/Mictronics/aircraft-database/commits/${revision === 'latest' ? 'main' : revision}`, options());
    if (!commitResponse.ok) {
        const error = new Error('Aircraft revision lookup failed', { cause: commitResponse.status });
        error.name = 'AircraftRevisionRequestError';
        const remaining = commitResponse.headers.get('x-ratelimit-remaining');
        error.rateLimitRemaining = remaining !== null && /^\d+$/.test(remaining) ? Number(remaining) : null;
        throw error;
    }
    const commit = JSON.parse(decoder.decode(await boundedBytes(commitResponse, 1024 * 1024)));
    if (!/^[a-f0-9]{40}$/.test(commit.sha) || revision !== 'latest' && commit.sha !== revision) throw new Error('Source revision mismatch');
    const archive = await boundedBytes(await fetcher(`https://raw.githubusercontent.com/Mictronics/aircraft-database/${commit.sha}/indexedDB.zip`, options()), maxArchiveBytes);
    return validateSnapshot(await buildSnapshot(archive, commit.sha, commit.commit.committer.date, labels));
}
