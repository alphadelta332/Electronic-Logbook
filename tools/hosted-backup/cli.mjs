import { parseArgs } from 'node:util';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { buildDatabaseArchive, openArchive, maxArchiveBytes } from './archive.mjs';
import { buildHostedArchive } from './orchestrate.mjs';

try {
  const { positionals, values } = parseArgs({ allowPositionals: true, options: {
    connection: { type: 'string' }, recipient: { type: 'string' }, output: { type: 'string' },
    archive: { type: 'string' }, identity: { type: 'string' }, source: { type: 'string' }
  } });
  if (positionals.length === 1 && ['export', 'bundle'].includes(positionals[0]) && values.connection && values.recipient && values.output) {
    if (existsSync(values.output)) throw new Error('Output already exists.');
    const connection = JSON.parse(readFileSync(values.connection, 'utf8'));
    if (positionals[0] === 'bundle' && !values.source) throw new Error('Source checkout required.');
    const archive = positionals[0] === 'bundle'
      ? await buildHostedArchive(connection, readFileSync(values.recipient), values.source)
      : buildDatabaseArchive(connection, readFileSync(values.recipient));
    writeFileSync(values.output, archive, { flag: 'wx' });
    console.log(JSON.stringify({ status: 'exported', bytes: archive.length,
      sha256: createHash('sha256').update(archive).digest('hex') }));
  } else if (positionals.length === 1 && positionals[0] === 'verify' && values.archive && values.identity) {
    if (statSync(values.archive).size > maxArchiveBytes) throw new Error('Archive too large.');
    const archive = readFileSync(values.archive);
    const files = openArchive(archive, readFileSync(values.identity));
    Object.values(files).forEach(bytes => bytes.fill(0));
    console.log(JSON.stringify({ status: 'authenticated', bytes: archive.length, files: Object.keys(files).length }));
  } else {
    throw new Error('Invalid arguments.');
  }
} catch {
  console.error('Backup command failed. No credentials, decrypted records or provider diagnostics were printed.');
  process.exitCode = 1;
}
