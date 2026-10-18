import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, statSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { openArchive, maxArchiveBytes } from './archive.mjs';
import { tableManifestSql } from './orchestrate.mjs';
import { restoreDisposable, readDisposable, dropDisposable } from './restore.mjs';
import { buildReplayCheck, replaySupervised } from './replay-supervised.mjs';
import { restoreLogicalArchive } from './logical-restore.mjs';

export function verifySupervisedArchive(archive, identity, expectedSha256, local, recoveryEnvironment) {
  assert.equal(createHash('sha256').update(archive).digest('hex'), expectedSha256, 'Archive differs from the trusted receipt.');
  const files = openArchive(archive, identity);
  let target;
  try {
    const expected = JSON.parse(files['verification.json']);
    if(expected.accessMode==='logical-capability')return restoreLogicalArchive(archive,identity,expectedSha256,local,recoveryEnvironment);
    assert.equal(expected.accessMode, 'supervised-owner');
    try { target = restoreDisposable(archive, identity, expectedSha256, local); }
    catch (error) { target = error.restoreReceipt; throw error; }
    const actual = readDisposable(local, target, tableManifestSql).split(/\r?\n/).map(line => JSON.parse(line));
    assert.deepEqual(actual, expected.tables, 'Restored table counts, hashes or revisions differ.');
    const applicationReplay = recoveryEnvironment ? replaySupervised(local, target, recoveryEnvironment) : undefined;
    return { status: 'database-restored-and-matched', tablesMatched: actual.length, applicationReplay,
      allRowsAndRevisionsMatched: true, managedServiceRecoveryVerified: false };
  } finally {
    if (target) dropDisposable(local, target);
    Object.values(files).forEach(bytes => bytes.fill(0));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    let input = ''; for await (const chunk of process.stdin) { input += chunk; assert.ok(input.length < 65536); }
    const request = JSON.parse(input); input = '';
    const port = request.localPort ?? 54322;
    assert.ok([54322, 55432].includes(port), 'Use a reviewed loopback fixture port.');
    const reportPath = request.archive + (request.applicationReplay ? '.application-verification.json' : '.verification.json');
    assert.ok(!existsSync(reportPath), 'A completed verification already exists.');
    if (request.applicationReplay) buildReplayCheck();
    assert.ok(statSync(request.archive).size <= maxArchiveBytes);
    const receipt = JSON.parse(readFileSync(request.archive + '.receipt.json'));
    assert.ok(['supervised-owner','logical-capability'].includes(receipt.accessMode));
    const identity = { key: readFileSync(request.identity), passphrase: request.passphrase };
    assert.match(identity.key.toString('utf8'), /^-----BEGIN ENCRYPTED PRIVATE KEY-----/);
    const recoveryReceipt = JSON.parse(readFileSync(join(process.env.LOCALAPPDATA, 'ElectronicLogbook/HostedBackup/recovery-receipt.json')));
    const recoveryArchive = readFileSync(request.recoveryArchive);
    assert.equal(createHash('sha256').update(identity.key).digest('hex'), recoveryReceipt.protectedIdentitySha256);
    assert.equal(createHash('sha256').update(recoveryArchive).digest('hex'), recoveryReceipt.recoveryArchiveSha256);
    assert.equal(receipt.recipientSha256, recoveryReceipt.recipientSha256);
    const capsule = openArchive(recoveryArchive, identity);
    let result;
    try {
      assert.ok(capsule['preview-recovery.env']?.length);
      result = verifySupervisedArchive(readFileSync(request.archive), identity, receipt.sha256, {
        host: '127.0.0.1', port, user: 'postgres', password: 'postgres',
        pgBin: process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : '/usr/bin'
      }, request.applicationReplay ? capsule['preview-recovery.env'] : undefined);
    } finally { Object.values(capsule).forEach(bytes => bytes.fill(0)); }
    request.passphrase = null; identity.passphrase = null;
    const report = { ...result, checkedAt: new Date().toISOString(), sha256: receipt.sha256,
      recoveryCapsuleAuthenticated: true, disposableTargetRemoved: true };
    writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
    console.log(JSON.stringify(report));
  } catch {
    console.error('Supervised verification failed; no recovery acceptance claimed. No password, key, provider diagnostic or decrypted records printed.');
    process.exitCode = 1;
  }
}
