import { airacCycle } from '../airport-catalog/catalog.mjs';
import { boundedBytes, digest, fetchSnapshot, validateManifest, validateSnapshot, maxJsonBytes } from './catalog.mjs';
import labelOverrides from '../../../data/aircraft-catalog/label-overrides.json' with { type: 'json' };

const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true });
const maxCompressedBytes = 1024 * 1024;

Deno.serve(async request => {
    const token = Deno.env.get('AIRCRAFT_CATALOG_PUBLISH_TOKEN');
    if (request.method !== 'POST' || !token || !/^[a-f0-9]{64}$/.test(token) ||
        request.headers.get('authorization') !== `Bearer ${token}`)
        return new Response('Unauthorized', { status: 401 });
    const url = Deno.env.get('SUPABASE_URL')!;
    const key = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!;
    const headers = { authorization: `Bearer ${key}`, apikey: key };
    const objectUrl = `${url}/storage/v1/object/aircraft-catalog/`;
    const get = (path: string) => fetch(objectUrl + path, { headers, signal: AbortSignal.timeout(30000), redirect: 'error' });
    const put = async (path: string, body: Uint8Array | string, cache: number, type: string) => {
        const response = await fetch(objectUrl + path, {
            method: 'POST', headers: { ...headers, 'content-type': type, 'cache-control': `max-age=${cache}`, 'x-upsert': 'true' },
            body, signal: AbortSignal.timeout(30000), redirect: 'error',
        });
        if (!response.ok) throw new Error('Aircraft publication failed', { cause: response.status });
    };
    let stage = 'manifest';
    try {
        const cycle = airacCycle();
        const labelsSha256 = await digest(encoder.encode(JSON.stringify(labelOverrides)));
        const response = await get('manifest.json');
        let previous = null;
        if (response.ok) previous = validateManifest(JSON.parse(decoder.decode(await boundedBytes(response, 4096))));
        else if (response.status !== 400 && response.status !== 404) throw new Error('Aircraft manifest unavailable');
        if (previous && previous.cycle > cycle) throw new Error('Aircraft manifest is from a future cycle');
        if (previous?.cycle === cycle && previous.labelsSha256 === labelsSha256)
            return Response.json({ status: 'current', cycle, count: previous.count });

        if (previous) {
            stage = 'previous-snapshot';
            const compressed = await boundedBytes(await get(previous.path), maxCompressedBytes);
            if (await digest(compressed) !== previous.sha256) throw new Error('Previous aircraft checksum mismatch');
            const json = await boundedBytes(new Response(new Blob([compressed]).stream().pipeThrough(new DecompressionStream('gzip'))), maxJsonBytes);
            const snapshot = validateSnapshot(JSON.parse(decoder.decode(json)));
            if (snapshot.count !== previous.count || snapshot.source.revision !== previous.sourceRevision ||
                await digest(encoder.encode(snapshot.source.licenseText)) !== snapshot.source.licenseSha256)
                throw new Error('Previous aircraft snapshot does not match manifest');
        }
        stage = 'upstream';
        const snapshot = await fetchSnapshot('latest');
        stage = 'compression';
        const json = encoder.encode(JSON.stringify(snapshot));
        if (json.byteLength > maxJsonBytes) throw new Error('Aircraft snapshot too large');
        const compressed = await boundedBytes(new Response(new Blob([json]).stream().pipeThrough(new CompressionStream('gzip'))), maxCompressedBytes);
        const sha256 = await digest(compressed);
        const path = `snapshots/${sha256}.json.gz`;
        const manifest = validateManifest({ schemaVersion: 1, cycle, publishedAt: new Date().toISOString(), sha256, path,
            count: snapshot.count, sourceRevision: snapshot.source.revision, sourceCommittedAt: snapshot.source.committedAt, labelsSha256 });
        stage = 'snapshot-upload';
        await put(path, compressed, 31536000, 'application/gzip');
        stage = 'manifest-upload';
        await put('manifest.json', JSON.stringify(manifest), 300, 'application/json');
        return Response.json({ status: 'published', cycle, count: snapshot.count, sha256 });
    } catch (error) {
        console.error(JSON.stringify({ aircraft_status: 'failed', stage, failureType: error?.name ?? 'unknown',
            httpStatus: typeof error?.cause === 'number' ? error.cause : null,
            upstreamQuotaRemaining: typeof error?.rateLimitRemaining === 'number' ? error.rateLimitRemaining : null }));
        return Response.json({ status: 'failed' }, { status: 503 });
    }
});
