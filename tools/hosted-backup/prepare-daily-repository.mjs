import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { reconstructionSources } from './orchestrate.mjs';

// Explicit source files only: never mirror a working directory or generated credentials/state.
export const dailyRepositoryFiles = [
  'archive.mjs', 'archive.test.mjs', 'postgres.mjs', 'source-access.mjs', 'orchestrate.mjs', 'logical-client.mjs',
  'preview-logical-contract.json', 'r2-policy.json', 'daily-workflow.json',
  'daily-backup.mjs', 'daily-backup.test.mjs',
  'daily-controls.mjs', 'daily-controls-worker.mjs', 'daily-controls.wrangler.json',
  'daily-controls.test.mjs', 'daily-controls.integration.test.mjs',
  'daily-primary.mjs', 'daily-primary.test.mjs',
  'daily-cost-policy.mjs', 'daily-cost-policy.test.mjs', 'daily-request-controls.mjs',
  'daily-provider-collector.mjs', 'daily-provider-collector.test.mjs',
  'daily-monitor.mjs', 'daily-monitor.test.mjs', 'daily-monitor-observation.mjs', 'daily-monitor-observation.test.mjs',
  'daily-alert-delivery.mjs', 'daily-alert-delivery.test.mjs',
  'daily-watchdog.mjs', 'daily-watchdog-worker.mjs', 'daily-watchdog.wrangler.json',
  'daily-watchdog.test.mjs', 'daily-watchdog.integration.test.mjs'
].map(name => 'tools/hosted-backup/' + name).concat('supabase/functions/airport-catalog/catalog.mjs');

export function prepareDailyRepository(destination) {
  assert.equal(typeof destination, 'string');
  assert.ok(destination.length > 0, 'A new staging directory is required.');
  const root = fileURLToPath(new URL('../../', import.meta.url));
  const source = reconstructionSources(root), files = new Map();
  for (const name of dailyRepositoryFiles) {
    const bytes = source.files['sources/' + name];
    assert.ok(bytes, 'Required private repository source is absent: ' + name);
    files.set(name, { bytes, source: name });
  }
  files.set('.github/workflows/daily-backup.yml', files.get('tools/hosted-backup/daily-workflow.json'));
  files.set('.gitignore', { bytes: Buffer.from('.wrangler/\n.dev.vars*\n.env*\n*.local.*\n*.pem\n*.elbbackup\n/daily-job.public.json\n'), source: null });
  const provenance = { version: 1, sourceRepository: 'alphadelta332/Electronic-Logbook',
    revision: source.revision, dirty: source.dirty, files: [...files].map(([name, file]) =>
      ({ name, source: file.source, sha256: createHash('sha256').update(file.bytes).digest('hex') })) };
  destination = resolve(destination);
  // Refuse an existing checkout/output: preparation cannot overwrite files or reset local state.
  mkdirSync(destination);
  for (const [name, file] of files) {
    const target = resolve(destination, name);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, file.bytes, { flag: 'wx' });
  }
  writeFileSync(resolve(destination, 'source-provenance.json'), JSON.stringify(provenance, null, 2) + '\n', { flag: 'wx' });
  return provenance;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const provenance = prepareDailyRepository(process.argv[2]);
  console.log(JSON.stringify({ files: provenance.files.length, revision: provenance.revision, dirty: provenance.dirty }));
}
