import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { saveR2Credentials } from './r2-credentials.mjs';

test('R2 credentials validate both identities, exclude extra secrets and refuse replacement', () => {
  const directory = mkdtempSync(join(tmpdir(), 'elb-r2-access-'));
  try {
    const file = join(directory, 'credentials.json');
    const credentials = { accountId: 'a'.repeat(32), bucket: 'flightlogx-preview-backups',
      upload: { accessKeyId: 'b'.repeat(32), secretAccessKey: 'c'.repeat(64), token: 'must-not-save' },
      restore: { accessKeyId: 'd'.repeat(32), secretAccessKey: 'e'.repeat(64) }, privateKey: 'must-not-save' };
    assert.throws(() => saveR2Credentials(file, { ...credentials, restore: credentials.upload }));
    assert.throws(() => saveR2Credentials(file, { ...credentials, bucket: 'other' }));
    assert.throws(() => saveR2Credentials(file, { ...credentials, upload: { ...credentials.upload, secretAccessKey: 'short' } }));
    assert.equal(existsSync(file), false);
    saveR2Credentials(file, credentials);
    const before = readFileSync(file, 'utf8');
    assert.ok(!before.includes('must-not-save'));
    assert.equal(JSON.parse(before).restore.secretAccessKey, credentials.restore.secretAccessKey);
    assert.throws(() => saveR2Credentials(file, credentials));
    assert.equal(readFileSync(file, 'utf8'), before);
  } finally {
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()));
    rmSync(directory, { recursive: true, force: true });
  }
});

test('R2 policy uses Standard storage with compatible 30-day protection and expiry', () => {
  const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));
  assert.equal(policy.storageClass, 'Standard');
  assert.equal(policy.lock.rules.length, 1);
  assert.equal(policy.lifecycle.rules.length, 1);
  const lock = policy.lock.rules[0];
  const lifecycle = policy.lifecycle.rules[0];
  assert.equal(lock.enabled, true);
  assert.equal(lifecycle.enabled, true);
  assert.equal(lock.prefix, '');
  assert.equal(lifecycle.conditions.prefix, '');
  assert.deepEqual(lock.condition, { type: 'Age', maxAgeSeconds: 30 * 86400 });
  assert.deepEqual(lifecycle.deleteObjectsTransition.condition, { type: 'Age', maxAge: 30 * 86400 });
  assert.equal(lifecycle.storageClassTransitions, undefined);
  assert.equal(lifecycle.abortMultipartUploadsTransition.condition.maxAge, 86400);
});
