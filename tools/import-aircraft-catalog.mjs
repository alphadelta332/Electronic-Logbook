import { mkdir, open, rename, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';
import { parseArgs } from 'node:util';
import { digest, fetchSnapshot, initialRevision, validateSnapshot } from '../supabase/functions/aircraft-catalog/catalog.mjs';

export async function replaceSnapshot(snapshot, destination) {
    validateSnapshot(snapshot);
    const compressed = gzipSync(JSON.stringify(snapshot), { mtime: 0 });
    await mkdir(dirname(destination), { recursive: true });
    const temporary = `${destination}.${crypto.randomUUID()}.tmp`;
    try {
        const file = await open(temporary, 'wx');
        try { await file.writeFile(compressed); await file.sync(); } finally { await file.close(); }
        await rename(temporary, destination);
    } finally { await rm(temporary, { force: true }); }
    return digest(compressed);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const { values } = parseArgs({ options: { revision: { type: 'string', default: initialRevision }, output: { type: 'string' } } });
    const destination = values.output ? resolve(values.output) : fileURLToPath(new URL('../data/aircraft-catalog/mictronics.json.gz', import.meta.url));
    const snapshot = await fetchSnapshot(values.revision);
    const hash = await replaceSnapshot(snapshot, destination);
    console.log(`Imported ${snapshot.count} aircraft (${snapshot.omittedCodes.length} unnamed omitted) from ${snapshot.source.revision}; SHA-256 ${hash}`);
}
