import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, createHmac } from 'node:crypto';
import { observeBackup, readBackupHistory, inspectBackupObject } from './daily-monitor-observation.mjs';
import { monitorConditions } from './daily-monitor.mjs';

const now = Date.parse('2026-10-20T12:00:00Z'), iso = value => new Date(value).toISOString();
const config = { workflowId: 12, workflowPath: '.github/workflows/daily-backup.yml', branch: 'main',
  commit: 'a'.repeat(40), jobSha256: 'b'.repeat(64), accountId: 'c'.repeat(32) };
const repository = 'alphadelta332/flightlogx-preview-backups';
const credentials = { github: 'g'.repeat(30), r2: { accessKeyId: 'd'.repeat(32), secretAccessKey: 'e'.repeat(64) } };
const bytes = Buffer.alloc(500, 42), hash = value => createHash('sha256').update(value).digest('hex');
const invoke = action => action();
function fixture() {
  const run = { id: 34, run_attempt: 1, workflow_id: config.workflowId, path: config.workflowPath,
    head_sha: config.commit, head_branch: config.branch, event: 'workflow_dispatch',
    repository: { full_name: repository, private: true }, head_repository: { full_name: repository, private: true },
    run_started_at: iso(now - 600000), updated_at: iso(now - 100000), status: 'completed', conclusion: 'success' };
  const receipt = { version: 2, status: 'verified', key: 'daily/2026-10-20.elbbackup', bytes: bytes.length,
    sha256: hash(bytes), jobSha256: config.jobSha256, repository, commit: config.commit, runId: '34', runAttempt: '1',
    uploadedAt: iso(now - 400000), verifiedAt: iso(now - 200000), reservation: {} };
  const jobs = { total_count: 2, jobs: ['upload', 'readback'].map((name, index) => ({
    id: 50 + index, run_id: run.id, run_attempt: 1, head_sha: config.commit, status: 'completed', conclusion: 'success',
    name, started_at: iso(now - 300000), completed_at: iso(now - 150000) })) };
  return { run, receipt, jobs, last: run, successful: run, object: bytes,
    location: 'https://fixture.blob.core.windows.net/logs/signed?sig=private', calls: [] };
}
function transport(data, override = () => undefined) {
  return async (url, options) => {
    data.calls.push({ url, options });
    assert.equal(options.method, 'GET'); assert.ok(options.signal);
    const changed = override(url, options); if (changed) return changed;
    if (url.startsWith('https://api.github.com/')) {
      assert.equal(options.headers.Authorization, `Bearer ${credentials.github}`);
      assert.equal(options.redirect, 'manual');
      if (url.includes('/runs?')) {
        const run = url.includes('status=success') ? data.successful : data.last;
        return Response.json({ total_count: run ? 1 : 0, workflow_runs: run ? [run] : [] });
      }
      if (url.includes('/attempts/')) return Response.json(data.jobs);
      if (url.endsWith('/51/logs')) return new Response(null, { status: 302, headers: { location: data.location } });
    }
    if (url === data.location) {
      assert.deepEqual(options.headers, {});
      return new Response(data.log ?? `${iso(now - 200000)} ${JSON.stringify(data.receipt)}\n`);
    }
    if (url.includes('.r2.cloudflarestorage.com/')) {
      assert.equal(url, `https://${config.accountId}.r2.cloudflarestorage.com/flightlogx-preview-backups/${data.receipt.key}`);
      assert.equal(options.redirect, 'error');
      assert.ok(!options.headers.Authorization.includes(credentials.github));
      return new Response(data.object);
    }
    assert.fail('Unexpected outbound target.');
  };
}
const observe = data => observeBackup(config, credentials, 'allowed', invoke, invoke, transport(data), now);

test('authenticated readback job plus matching stored bytes produce original verification time in six bounded reads', async () => {
  const data = fixture(), result = await observe(data);
  assert.equal(result.lastVerified.verifiedAt, data.receipt.verifiedAt);
  assert.equal(result.lastRun.conclusion, 'success');
  assert.equal(data.calls.length, 6);
  assert.equal(data.calls.filter(call => call.url.includes('r2.cloudflarestorage.com')).length, 1);
  assert.equal(result.lastVerified.reservation, undefined);
});

test('missing history is missing evidence without an R2 call; lookup failure never becomes missing history', async () => {
  const data = fixture(); data.last = null; data.successful = null;
  const result = await observe(data);
  assert.equal(result.lastRun, null); assert.equal(result.lastVerified, null); assert.equal(data.calls.length, 2);
  assert.ok(monitorConditions(result, null, now).includes('missing-run'));
  await assert.rejects(readBackupHistory(config, credentials.github, invoke,
    async () => new Response('private diagnostics', { status: 403 }), now), /unavailable/);
});

test('live disabled-workflow shape reports skipped as failed with no successful readback or R2 request', async () => {
  const data = fixture(); data.run.conclusion = 'skipped'; data.successful = null;
  const result = await observe(data);
  assert.equal(result.lastRun.conclusion, 'failure'); assert.equal(result.lastVerified, null);
  assert.equal(data.calls.length, 2);
  const conditions = monitorConditions(result, null, now);
  assert.ok(conditions.includes('failed-run')); assert.ok(conditions.includes('stale-backup'));
});

test('new failed run does not conceal an older independently verified backup; stale time is not refreshed', async () => {
  const data = fixture(); data.last = { ...data.run, id: 35, conclusion: 'failure' };
  for (const field of ['run_started_at', 'updated_at']) data.run[field] = iso(Date.parse(data.run[field]) - 31 * 3600000);
  // last is a separate newer run; only the old successful run and its receipt/jobs age.
  for (const job of data.jobs.jobs) for (const field of ['started_at', 'completed_at'])
    job[field] = iso(Date.parse(job[field]) - 31 * 3600000);
  for (const field of ['uploadedAt', 'verifiedAt']) data.receipt[field] = iso(Date.parse(data.receipt[field]) - 31 * 3600000);
  data.receipt.key = `daily/${data.receipt.uploadedAt.slice(0, 10)}.elbbackup`;
  const result = await observe(data), conditions = monitorConditions(result, null, now);
  assert.ok(conditions.includes('failed-run')); assert.ok(conditions.includes('stale-backup'));
});

for (const [name, change] of Object.entries({
  'foreign repository': data => { data.run.repository.full_name = 'other/repo'; },
  'public repository': data => { data.run.repository.private = false; },
  'fork head': data => { data.run.head_repository.full_name = 'other/repo'; },
  'changed workflow': data => { data.run.workflow_id++; },
  'changed path': data => { data.run.path = '.github/workflows/other.yml'; },
  'changed commit': data => { data.run.head_sha = 'f'.repeat(40); },
  'wrong branch': data => { data.run.head_branch = 'other'; },
  'untrusted event': data => { data.run.event = 'pull_request'; },
  'future run': data => { data.run.updated_at = iso(now + 1); },
  'missing jobs': data => { data.jobs.total_count = 3; },
  'uploader only': data => { data.jobs.jobs[1].name = 'upload'; },
  'skipped readback': data => { data.jobs.jobs[1].conclusion = 'skipped'; },
  'wrong job run': data => { data.jobs.jobs[1].run_id++; },
  'wrong job attempt': data => { data.jobs.jobs[1].run_attempt++; },
  'wrong job commit': data => { data.jobs.jobs[1].head_sha = 'f'.repeat(40); },
  'pending receipt': data => { data.receipt.status = 'pending'; },
  'receipt wrong run': data => { data.receipt.runId = '35'; },
  'receipt wrong attempt': data => { data.receipt.runAttempt = '2'; },
  'receipt wrong source': data => { data.receipt.jobSha256 = 'f'.repeat(64); },
  'receipt wrong commit': data => { data.receipt.commit = 'f'.repeat(40); },
  'receipt key mismatch': data => { data.receipt.key = 'daily/2026-10-19.elbbackup'; },
  'leaked capability': data => { data.receipt.reservation.readbackToken = 'secret'; },
  'receipt after job': data => { data.receipt.verifiedAt = iso(now); },
  'receipt oversized': data => { data.receipt.bytes = 50 * 1024 * 1024 + 1; },
  'receipt malformed digest': data => { data.receipt.sha256 = 'bad'; },
  'duplicate receipts': data => { const line = `${iso(now)} ${JSON.stringify(data.receipt)}\n`; data.log = line + line; },
  'unmarked JSON': data => { data.log = JSON.stringify(data.receipt); },
  'oversized logs': data => { data.log = 'x'.repeat(256 * 1024 + 1); },
  'unsafe redirect': data => { data.location = 'https://example.com/logs'; },
  'redirect userinfo': data => { data.location = 'https://private@fixture.blob.core.windows.net/logs'; },
  'redirect port': data => { data.location = 'https://fixture.blob.core.windows.net:8443/logs'; },
  'redirect HTTP': data => { data.location = 'http://fixture.blob.core.windows.net/logs'; },
  'object digest mismatch': data => { data.object = Buffer.alloc(500, 43); },
  'object truncated': data => { data.object = Buffer.alloc(499); },
  'object oversized': data => { data.object = Buffer.alloc(501); }
})) test(`rejects ${name}`, async () => {
  const data = fixture(); change(data); await assert.rejects(observe(data));
});

test('R2 GET signature matches independent Node HMAC construction, signed read-only key and date', async () => {
  const data = fixture(); let called = false;
  await inspectBackupObject(config.accountId, credentials.r2, data.receipt, invoke, async (url, options) => {
    called = true;
    const day = '20261020', date = '20261020T120000Z', empty = hash('');
    const host = `${config.accountId}.r2.cloudflarestorage.com`;
    const canonical = `GET\n/flightlogx-preview-backups/${data.receipt.key}\n\nhost:${host}\nx-amz-content-sha256:${empty}\nx-amz-date:${date}\n\nhost;x-amz-content-sha256;x-amz-date\n${empty}`;
    let key = 'AWS4' + credentials.r2.secretAccessKey;
    for (const part of [day, 'auto', 's3', 'aws4_request']) key = createHmac('sha256', key).update(part).digest();
    const signature = createHmac('sha256', key).update(`AWS4-HMAC-SHA256\n${date}\n${day}/auto/s3/aws4_request\n${hash(canonical)}`).digest('hex');
    assert.ok(options.headers.Authorization.endsWith('Signature=' + signature));
    assert.equal(options.headers['x-amz-date'], date);
    return new Response(bytes);
  }, now);
  assert.equal(called, true);
});

test('failed reads have no hidden retries, second redirects deny and every I/O uses its allowance', async () => {
  const data = fixture(); let github = 0, r2 = 0;
  await assert.rejects(observeBackup(config, credentials, 'allowed', async action => { r2++; return action(); },
    async action => { github++; return action(); }, transport(data, url =>
      url.includes('r2.cloudflarestorage.com') ? new Response(null, { status: 404 }) : undefined), now));
  assert.equal(github, 5); assert.equal(r2, 1); assert.equal(data.calls.length, 6);
  const redirected = fixture();
  await assert.rejects(readBackupHistory(config, credentials.github, invoke, transport(redirected, url =>
    url === redirected.location ? new Response(null, { status: 302, headers: { location: 'https://example.com' } }) : undefined), now));
  assert.equal(redirected.calls.length, 5);
});
