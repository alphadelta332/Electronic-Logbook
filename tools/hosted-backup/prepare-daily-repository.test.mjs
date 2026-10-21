import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { dailyRepositoryFiles, prepareDailyRepository } from './prepare-daily-repository.mjs';
import { reconstructionSources } from './orchestrate.mjs';

test('allowlisted private stage runs independently and every staged byte is in reconstruction/provenance', () => {
  const parent = mkdtempSync(join(tmpdir(), 'elb-daily-repository-')), stage = join(parent, 'stage');
  const provenance = prepareDailyRepository(stage);
  const reconstruction = reconstructionSources(fileURLToPath(new URL('../../', import.meta.url)));
  assert.equal(new Set(dailyRepositoryFiles).size, dailyRepositoryFiles.length);
  for (const file of provenance.files) {
    const bytes = readFileSync(join(stage, file.name));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), file.sha256);
    if (file.source) assert.deepEqual(bytes, reconstruction.files['sources/' + file.source]);
    assert.ok(!/\.local\.|\.pem$|\.elbbackup$|(^|\/)\.wrangler(\/|$)|daily-job\.public/.test(file.name));
  }
  assert.equal(existsSync(join(stage, '.git')), false);
  assert.deepEqual(JSON.parse(readFileSync(join(stage, '.github/workflows/daily-backup.yml'))).on, { workflow_dispatch: {} });
  for (const name of ['daily-controls.wrangler.json', 'daily-watchdog.wrangler.json']) {
    const config = JSON.parse(readFileSync(join(stage, 'tools/hosted-backup', name)));
    assert.equal(config.triggers?.crons?.length ?? 0, 0);
  }
  assert.equal(JSON.parse(readFileSync(join(stage, 'tools/hosted-backup/daily-watchdog.wrangler.json'))).vars.WATCHDOG_ENABLED, 'false');
  const tests = dailyRepositoryFiles.filter(name => name.endsWith('.test.mjs'));
  const result = spawnSync(process.execPath, ['--test', ...tests], { cwd: stage, windowsHide: true,
    timeout: 60000, maxBuffer: 1024 * 1024,
    env: { SystemRoot: process.env.SystemRoot, PATH: process.env.PATH, TEMP: process.env.TEMP, TMP: process.env.TMP,
      APPDATA: process.env.APPDATA, LOCALAPPDATA: process.env.LOCALAPPDATA, USERPROFILE: process.env.USERPROFILE,
      WRANGLER_SEND_METRICS: 'false', CI: 'true', BROWSER: 'none' } });
  assert.equal(result.status, 0, result.stdout?.toString() + result.stderr?.toString());
  const hashes = readFileSync(join(stage, 'source-provenance.json'));
  assert.throws(() => prepareDailyRepository(stage), /EEXIST/);
  assert.deepEqual(readFileSync(join(stage, 'source-provenance.json')), hashes);
  assert.throws(() => prepareDailyRepository(''));
  assert.throws(() => prepareDailyRepository(undefined));
  console.log('Retained isolated private source stage: ' + stage);
  console.log(result.stdout.toString().split('\n').filter(line => /tests |pass |fail |skipped /.test(line)).join('\n'));
});
