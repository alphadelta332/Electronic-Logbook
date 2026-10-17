import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync, randomBytes } from 'node:crypto';
import { sealArchive, openArchive, exportDatabase, maxArchiveBytes } from './archive.mjs';

const identity = generateKeyPairSync('rsa', { modulusLength: 3072,
  publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });

test('restores every byte including binary data, and hides file names and record text', () => {
  const files = { 'database.dump': randomBytes(8192), 'roles.sql': Buffer.from('synthetic-secret-account-name'),
    'reference/catalog.gz': Buffer.from([0, 255, 128]) };
  const archive = sealArchive(files, identity.publicKey);
  assert.equal(archive.includes(Buffer.from('synthetic-secret-account-name')), false);
  assert.equal(archive.includes(Buffer.from('database.dump')), false);
  const opened = openArchive(archive, identity.privateKey);
  assert.deepEqual(Object.keys(opened).sort(), Object.keys(files).sort());
  for (const name of Object.keys(files)) assert.ok(opened[name].equals(files[name]));
  assert.equal(archive.equals(sealArchive(files, identity.publicKey)), false);
});

test('rejects a wrong recipient key, tampering in every envelope section and truncation', () => {
  const other = generateKeyPairSync('rsa', { modulusLength: 3072 });
  const archive = sealArchive({ 'database.dump': Buffer.from('synthetic') }, identity.publicKey);
  assert.throws(() => openArchive(archive, other.privateKey.export({ type: 'pkcs8', format: 'pem' })), /could not be authenticated/);
  for (const offset of [0, 8, 12, 395, 410, archive.length - 1]) {
    const damaged = Buffer.from(archive); damaged[offset] ^= 1;
    assert.throws(() => openArchive(damaged, identity.privateKey), /could not be authenticated/);
  }
  for (const size of [0, 9, 100, archive.length - 1])
    assert.throws(() => openArchive(archive.subarray(0, size), identity.privateKey), /could not be authenticated/);
});

test('rejects weak identities, unsafe file names, non-byte input and oversized archives', () => {
  const weak = generateKeyPairSync('rsa', { modulusLength: 2048 }).publicKey.export({ type: 'spki', format: 'pem' });
  assert.throws(() => sealArchive({ 'database.dump': Buffer.from('synthetic') }, weak), /3072/);
  for (const name of ['../escape', '/absolute', 'C:/absolute', 'a\\b', 'a//b', 'a/./b'])
    assert.throws(() => sealArchive({ [name]: Buffer.from('synthetic') }, identity.publicKey), /file names/);
  assert.throws(() => sealArchive({ 'database.dump': 'text' }, identity.publicKey), /bytes/);
  assert.throws(() => openArchive(Buffer.alloc(maxArchiveBytes + 1), identity.privateKey), /could not be authenticated/);
  assert.throws(() => sealArchive({ 'database.dump': Buffer.alloc(64 * 1024 * 1024 + 1) }, identity.publicKey), /permitted size/);
});

test('rejects mismatched hosted source identity and remote insecure connections before connecting', () => {
  const connection = { host: 'aws-0-ap-southeast-2.pooler.supabase.com', port: 5432, database: 'postgres',
    user: 'postgres.wrong', password: 'synthetic-private-password', projectRef: 'a'.repeat(20), sslRootCert: 'synthetic.crt' };
  for (const changed of [{}, { user: 'postgres.' + 'a'.repeat(20), sslRootCert: '' },
    { disposable: true, host: 'remote.example.com' }, { host: '127.0.0.1', disposable: false }])
    assert.throws(() => exportDatabase({ ...connection, ...changed }), /Unexpected database export target/);
});
