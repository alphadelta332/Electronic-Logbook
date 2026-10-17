import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { createRecoverySet } from './initialize-recovery.mjs';
import { openArchive } from './archive.mjs';

test('writes only protected private material, verifies the independent copy, and refuses replacement or wrong passwords', () => {
  const root = mkdtempSync(join(tmpdir(), 'elb-recovery-test-'));
  try {
    const passphrase = 'synthetic unrelated words for this test';
    const keys = generateKeyPairSync('rsa', { modulusLength: 3072,
      publicKeyEncoding: { type: 'spki', format: 'pem' },
      privateKeyEncoding: { type: 'pkcs8', format: 'pem', cipher: 'aes-256-cbc', passphrase } });
    const options = { directory: join(root, 'offline'), localDirectory: join(root, 'local'), recipient: keys.publicKey.trimEnd(),
      encryptedIdentity: keys.privateKey, passphrase, secretFiles: { 'synthetic-recovery.env': Buffer.from('synthetic-secret') } };
    assert.equal(createRecoverySet(options).status, 'created-and-verified');
    const archive = readFileSync(join(options.directory, 'recovery.elbbackup'));
    assert.equal(archive.includes(Buffer.from('synthetic-secret')), false);
    const storedKey = readFileSync(join(options.directory, 'backup-private.encrypted.pem'));
    assert.ok(storedKey.toString().startsWith('-----BEGIN ENCRYPTED PRIVATE KEY-----'));
    assert.throws(() => openArchive(archive, { key: storedKey, passphrase: 'wrong' }), /could not be authenticated/);
    assert.throws(() => createRecoverySet(options), /already exist/);
    const alternate = { ...options, directory: join(root, 'bad-offline'), localDirectory: join(root, 'bad-local') };
    assert.throws(() => createRecoverySet({ ...alternate, passphrase: 'wrong password of sufficient length' }));
    assert.equal(existsSync(alternate.directory), false);
    assert.throws(() => createRecoverySet({ ...alternate, encryptedIdentity: '-----BEGIN PRIVATE KEY-----' }), /Invalid recovery/);
  } finally {
    const target = resolve(root);
    assert.ok(target.startsWith(resolve(tmpdir()) + (process.platform === 'win32' ? '\\' : '/')));
    rmSync(target, { recursive: true, force: true });
  }
});
