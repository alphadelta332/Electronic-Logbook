import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync, randomUUID } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { sealArchive, openArchive } from './archive.mjs';

// This owner-run check creates one small encrypted object retained by the 30-day lock.
let stage = 'input';
let temporaryDirectory;
let probeBucket;
let ownerApi;
try {
  const root = join(process.env.LOCALAPPDATA, 'ElectronicLogbook');
  const credentials = JSON.parse(readFileSync(join(root, 'HostedBackup/r2-credentials.local.json')));
  assert.match(credentials.accountId, /^[a-f0-9]{32}$/);
  assert.equal(credentials.bucket, 'flightlogx-preview-backups');
  for (const access of [credentials.upload, credentials.restore]) {
    assert.match(access.accessKeyId, /^[a-f0-9]{32}$/);
    assert.match(access.secretAccessKey, /^[a-f0-9]{64}$/);
  }
  assert.notEqual(credentials.upload.accessKeyId, credentials.restore.accessKeyId);
  const accountConfig = JSON.parse(readFileSync(new URL('../../supabase/activity-worker/wrangler.local.json', import.meta.url)));
  assert.equal(accountConfig.account_id, credentials.accountId);
  const auth = spawnSync(process.execPath, [join(process.env.APPDATA, 'npm/node_modules/wrangler/bin/wrangler.js'),
    'auth', 'token', '--json'], { encoding: 'utf8', windowsHide: true });
  assert.equal(auth.status, 0);
  const { token } = JSON.parse(auth.stdout);
  ownerApi = async (suffix, method = 'GET', body) => {
    const response = await fetch(`https://api.cloudflare.com/client/v4/accounts/${credentials.accountId}/r2/buckets${suffix}`, {
      method, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30000)
    });
    const result = await response.json();
    assert.equal(response.ok && result.success, true);
    return result.result;
  };
  const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));
  const bucketPath = '/' + credentials.bucket;
  stage = 'owner configuration readback';
  assert.deepEqual(await ownerApi(bucketPath + '/lock'), policy.lock);
  assert.deepEqual(await ownerApi(bucketPath + '/lifecycle'), policy.lifecycle);
  assert.equal((await ownerApi(bucketPath)).storage_class, 'Standard');
  assert.equal((await ownerApi(bucketPath + '/domains/managed')).enabled, false);
  assert.deepEqual((await ownerApi(bucketPath + '/domains/custom')).domains, []);
  const initialBuckets = await ownerApi('');
  assert.deepEqual(initialBuckets.buckets.map(b => b.name), [credentials.bucket]);
  assert.ok(!initialBuckets.truncated && !initialBuckets.cursor);

  temporaryDirectory = mkdtempSync(join(tmpdir(), 'elb-r2-check-'));
  const keyPair = generateKeyPairSync('rsa', { modulusLength: 3072,
    publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
  const original = Buffer.from('Synthetic backup access check. No participant data or recovery secrets.');
  const archive = sealArchive({ 'synthetic.txt': original }, keyPair.publicKey);
  const archiveFile = join(temporaryDirectory, 'synthetic.elbbackup');
  writeFileSync(archiveFile, archive, { flag: 'wx' });
  assert.ok(archive.length < 4096);
  const sha256 = value => createHash('sha256').update(value).digest('hex');
  const request = (kind, method, bucket, key = '', query = '', bodyFile) => {
    assert.match(bucket, /^[a-z0-9-]{3,63}$/);
    assert.match(key, /^[a-zA-Z0-9/._-]*$/);
    assert.ok(['', '?list-type=2&max-keys=1000', '?lifecycle'].includes(query));
    const access = credentials[kind];
    const config = [
      `url = "https://${credentials.accountId}.r2.cloudflarestorage.com/${bucket}${key ? '/' + key : ''}${query}"`,
      `request = "${method}"`, 'aws-sigv4 = "aws:amz:auto:s3"',
      `user = "${access.accessKeyId}:${access.secretAccessKey}"`,
      'silent', 'show-error', 'connect-timeout = 10', 'max-time = 30',
      'max-filesize = 1048576', 'proto = "=https"', 'write-out = "\\n%{http_code}"'
    ];
    if (bodyFile) {
      assert.equal(bodyFile, archiveFile);
      config.push('header = "Content-Type: application/octet-stream"',
        `header = "x-amz-content-sha256: ${sha256(archive)}"`,
        `data-binary = "@${bodyFile.replaceAll('\\', '/')}"`);
    }
    const result = spawnSync('curl.exe', ['--disable', '--config', '-'], {
      input: config.join('\n') + '\n', windowsHide: true, maxBuffer: 2 * 1024 * 1024, timeout: 35000
    });
    assert.equal(result.status, 0);
    const status = Number(result.stdout.subarray(-3).toString());
    assert.ok(Number.isInteger(status) && status >= 100 && status <= 599);
    return { status, body: result.stdout.subarray(0, -4) };
  };
  const denied = response => {
    assert.equal(response.status, 403);
    assert.match(response.body.toString(), /<Code>AccessDenied<\/Code>/);
  };
  const locked = response => {
    assert.equal(response.status, 409);
    assert.match(response.body.toString(), /<Code>ObjectLockedByBucketPolicy<\/Code>/);
  };
  stage = 'initial object inventory';
  const before = request('upload', 'GET', credentials.bucket, '', '?list-type=2&max-keys=1000');
  assert.equal(before.status, 200);
  const beforeXml = before.body.toString();
  assert.match(beforeXml, /<IsTruncated>false<\/IsTruncated>/);
  const priorKeys = [...beforeXml.matchAll(/<Key>([^<]+)<\/Key>/g)].map(match => match[1]);
  // shortcut: at most three retained synthetic probes; wait for expiry before further runs.
  assert.ok(priorKeys.length <= 2);
  for (const key of priorKeys) assert.match(key, /^checks\/access-[a-zA-Z0-9._-]+\.elbbackup$/);
  const priorBytes = [...beforeXml.matchAll(/<Size>(\d+)<\/Size>/g)].reduce((sum, match) => sum + Number(match[1]), 0);
  assert.ok(priorBytes <= 8192);

  const objectKey = `checks/access-${new Date().toISOString().replaceAll(':', '-')}-${randomUUID()}.elbbackup`;
  stage = 'encrypted upload';
  assert.equal(request('upload', 'PUT', credentials.bucket, objectKey, '', archiveFile).status, 200);
  stage = 'upload identity readback';
  const uploaded = request('upload', 'GET', credentials.bucket, objectKey);
  assert.equal(uploaded.status, 200);
  assert.ok(uploaded.body.equals(archive));
  stage = 'restore identity readback and decryption';
  const downloaded = request('restore', 'GET', credentials.bucket, objectKey);
  assert.equal(downloaded.status, 200);
  assert.equal(sha256(downloaded.body), sha256(archive));
  const opened = openArchive(downloaded.body, keyPair.privateKey);
  assert.ok(opened['synthetic.txt'].equals(original));
  Object.values(opened).forEach(value => value.fill(0));
  stage = 'restore identity write denial';
  denied(request('restore', 'PUT', credentials.bucket, `checks/denied-${randomUUID()}.elbbackup`, '', archiveFile));
  stage = 'lock replacement denial';
  locked(request('upload', 'PUT', credentials.bucket, objectKey, '', archiveFile));
  stage = 'lock deletion denial';
  locked(request('upload', 'DELETE', credentials.bucket, objectKey));
  stage = 'bucket configuration read denial';
  for (const kind of ['upload', 'restore']) denied(request(kind, 'GET', credentials.bucket, '', '?lifecycle'));

  stage = 'other-bucket scope denial';
  const candidate = 'elb-backup-scope-' + randomUUID().replaceAll('-', '');
  await ownerApi('', 'POST', { name: candidate, storageClass: 'Standard', locationHint: 'oc' });
  probeBucket = candidate;
  for (const kind of ['upload', 'restore']) denied(request(kind, 'GET', probeBucket, '', '?list-type=2&max-keys=1000'));
  await ownerApi('/' + probeBucket, 'DELETE');
  probeBucket = undefined;
  stage = 'final state verification';
  const final = request('restore', 'GET', credentials.bucket, objectKey);
  assert.equal(final.status, 200);
  assert.ok(final.body.equals(archive));
  const after = request('upload', 'GET', credentials.bucket, '', '?list-type=2&max-keys=1000');
  assert.equal(after.status, 200);
  assert.match(after.body.toString(), /<IsTruncated>false<\/IsTruncated>/);
  assert.equal([...after.body.toString().matchAll(/<Key>/g)].length, priorKeys.length + 1);
  assert.equal([...after.body.toString().matchAll(/<Size>(\d+)<\/Size>/g)].reduce((sum, match) => sum + Number(match[1]), 0), priorBytes + archive.length);
  assert.deepEqual((await ownerApi('')).buckets.map(b => b.name), [credentials.bucket]);
  const evidence = { checked_at: new Date().toISOString(), synthetic_only: true, ciphertext_bytes: archive.length,
    ciphertext_sha256: sha256(archive), retained_synthetic_object: objectKey, upload_readback_passed: true,
    restore_readback_and_decryption_passed: true, restore_write_denied: true,
    locked_replacement_denied: true, locked_deletion_denied: true,
    both_bucket_configuration_reads_denied: true, both_other_bucket_lists_denied: true,
    temporary_empty_probe_bucket_removed: true, final_bucket_count: 1, final_object_count: priorKeys.length + 1,
    final_payload_bytes: priorBytes + archive.length, production_backup_uploaded: false, scheduled_job_enabled: false,
    lock_expiry_30_days_observed: false };
  console.log(JSON.stringify(evidence));
} catch {
  console.error(`R2 access check failed at: ${stage}. No credentials or provider response bodies printed. Inspect state before retrying; do not replace existing objects.`);
  process.exitCode = 1;
} finally {
  if (probeBucket && ownerApi) {
    try { await ownerApi('/' + probeBucket, 'DELETE'); }
    catch { console.error('Temporary empty scope-check bucket cleanup failed; inspect account bucket list.'); process.exitCode = 1; }
  }
  if (temporaryDirectory) {
    assert.equal(dirname(resolve(temporaryDirectory)), resolve(tmpdir()));
    rmSync(temporaryDirectory, { recursive: true, force: true });
  }
}
