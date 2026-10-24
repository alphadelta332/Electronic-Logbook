import { tagProviderFailure, logProviderFailure } from './daily-provider-collector.mjs';

const repository = 'alphadelta332/flightlogx-preview-backups';
const root = `https://api.github.com/repos/${repository}`;
const bucket = 'flightlogx-preview-backups';
const maximumBytes = 50 * 1024 * 1024;
const requireValue = condition => { if (!condition) throw new Error('Independent backup observation unavailable.'); };
const text = value => new TextEncoder().encode(value);
const hex = value => Array.from(new Uint8Array(value), byte => byte.toString(16).padStart(2, '0')).join('');
const digest = async value => hex(await crypto.subtle.digest('SHA-256', value));
const timestamp = (value, now) => typeof value === 'string' && Number.isFinite(Date.parse(value)) && Date.parse(value) <= now;
const positive = value => Number.isSafeInteger(value) && value > 0;

async function boundedBody(response, maximum, exact = false) {
  requireValue(!response.headers.has('content-length') ||
    (/^\d+$/.test(response.headers.get('content-length')) && Number(response.headers.get('content-length')) <= maximum));
  const output = new Uint8Array(maximum); let size = 0;
  const reader = response.body?.getReader(); requireValue(reader);
  try {
    for (;;) {
      const { done, value } = await reader.read(); if (done) break;
      requireValue(size + value.length <= maximum); output.set(value, size); size += value.length;
    }
    requireValue(!exact || size === maximum);
    return output.subarray(0, size);
  } catch (error) { await reader.cancel(); throw error; }
  finally { reader.releaseLock(); }
}

// Only this fixed repository's reviewed workflow/commit and independent readback job establish history.
export async function readBackupHistory(config, token, github, request = fetch, now = Date.now()) {
  requireValue(positive(config.workflowId) && /^[a-f0-9]{40}$/.test(config.commit ?? '') &&
    /^[a-f0-9]{64}$/.test(config.jobSha256 ?? '') && /^[a-zA-Z0-9/._-]+$/.test(config.branch ?? '') &&
    /^\.github\/workflows\/[a-zA-Z0-9_-]+\.ya?ml$/.test(config.workflowPath ?? '') &&
    typeof token === 'string' && token.length >= 20 && !/[\s]/.test(token) && Number.isSafeInteger(now));
  const get = async (url, authenticated = true) => github(() => request(url, {
    method: 'GET', redirect: 'manual', cache: 'no-store', signal: AbortSignal.timeout(10000),
    headers: authenticated ? { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json',
      'User-Agent': 'FlightLogX-backup-evidence', 'X-GitHub-Api-Version': '2022-11-28' } : {}
  }));
  const json = async path => {
    let stage = 'request', status = null;
    try {
      const response = await get(root + path); stage = 'http'; status = response.status;
      requireValue(response.status === 200); stage = 'body';
      const bytes = await boundedBody(response, 256 * 1024); stage = 'json';
      return JSON.parse(new TextDecoder().decode(bytes));
    } catch (error) { throw tagProviderFailure(error, 'github', stage, status); }
  };
  const latest = async success => {
    const result = await json(`/actions/workflows/${config.workflowId}/runs?per_page=1&head_sha=${config.commit}&branch=${encodeURIComponent(config.branch)}${success ? '&status=success' : ''}`);
    requireValue(Number.isSafeInteger(result.total_count) && result.total_count >= 0 &&
      Array.isArray(result.workflow_runs) && result.workflow_runs.length === Math.min(result.total_count, 1));
    if (!result.total_count) return null;
    const run = result.workflow_runs[0];
    requireValue(positive(run.id) && positive(run.run_attempt) && run.workflow_id === config.workflowId &&
      run.path === config.workflowPath && run.head_sha === config.commit && run.head_branch === config.branch &&
      ['workflow_dispatch', 'schedule'].includes(run.event) &&
      run.repository?.full_name === repository && run.repository.private === true &&
      run.head_repository?.full_name === repository && run.head_repository.private === true &&
      timestamp(run.run_started_at, now) && timestamp(run.updated_at, now) &&
      Date.parse(run.updated_at) >= Date.parse(run.run_started_at));
    requireValue(run.status === 'completed' ?
      ['success', 'failure', 'cancelled', 'timed_out', 'action_required', 'neutral', 'skipped', 'stale'].includes(run.conclusion) :
      ['queued', 'requested', 'waiting', 'pending', 'in_progress'].includes(run.status) && run.conclusion === null);
    if (success) requireValue(run.status === 'completed' && run.conclusion === 'success');
    return run;
  };
  const last = await latest(false), successful = await latest(true);
  requireValue(!successful || (last && Date.parse(successful.run_started_at) <= Date.parse(last.run_started_at)));
  const lastRun = last ? { startedAt: last.run_started_at, conclusion: last.status !== 'completed' ? 'in_progress' :
    ['success', 'cancelled', 'timed_out'].includes(last.conclusion) ? last.conclusion : 'failure' } : null;
  if (!successful) return { lastRun, lastVerified: null };
  const jobs = await json(`/actions/runs/${successful.id}/attempts/${successful.run_attempt}/jobs?per_page=100`);
  requireValue(jobs.total_count === 2 && Array.isArray(jobs.jobs) && jobs.jobs.length === 2);
  for (const job of jobs.jobs) requireValue(positive(job.id) && job.run_id === successful.id &&
    job.run_attempt === successful.run_attempt && job.head_sha === config.commit &&
    job.status === 'completed' && job.conclusion === 'success');
  requireValue(jobs.jobs.filter(job => job.name === 'upload').length === 1 &&
    jobs.jobs.filter(job => job.name === 'readback').length === 1);
  const job = jobs.jobs.find(job => job.name === 'readback');
  requireValue(timestamp(job.started_at, now) && timestamp(job.completed_at, now) &&
    Date.parse(job.started_at) >= Date.parse(successful.run_started_at) &&
    Date.parse(job.completed_at) >= Date.parse(job.started_at) &&
    Date.parse(job.completed_at) <= Date.parse(successful.updated_at));
  const response = await get(`${root}/actions/jobs/${job.id}/logs`); requireValue(response.status === 302);
  const location = new URL(response.headers.get('location'));
  // The signed download never receives the API token, and cannot redirect again.
  requireValue(location.protocol === 'https:' && !location.username && !location.password && !location.port &&
    (/^[a-z0-9-]+\.blob\.core\.windows\.net$/.test(location.hostname) ||
      location.hostname === 'results-receiver.actions.githubusercontent.com'));
  const logs = await get(location.href, false); requireValue(logs.status === 200);
  const lines = new TextDecoder().decode(await boundedBody(logs, 256 * 1024)).split(/\r?\n/);
  const receipts = lines.flatMap(line => {
    const match = line.match(/^\d{4}-\d\d-\d\dT[0-9:.]+Z (\{.*\})$/);
    if (!match) return [];
    const value = JSON.parse(match[1]); return value?.status === 'verified' ? [value] : [];
  });
  requireValue(receipts.length === 1);
  const receipt = receipts[0];
  requireValue(Object.keys(receipt).sort().join(',') ===
    'bytes,commit,jobSha256,key,repository,reservation,runAttempt,runId,sha256,status,uploadedAt,verifiedAt,version' &&
    receipt.version === 2 && receipt.repository === repository && receipt.commit === config.commit &&
    receipt.jobSha256 === config.jobSha256 && receipt.runId === String(successful.id) &&
    receipt.runAttempt === String(successful.run_attempt) && /^[a-f0-9]{64}$/.test(receipt.sha256 ?? '') &&
    positive(receipt.bytes) && receipt.bytes > 422 && receipt.bytes <= maximumBytes &&
    timestamp(receipt.uploadedAt, now) && timestamp(receipt.verifiedAt, now) &&
    Date.parse(receipt.uploadedAt) >= Date.parse(successful.run_started_at) &&
    Date.parse(receipt.verifiedAt) >= Date.parse(receipt.uploadedAt) &&
    Date.parse(receipt.verifiedAt) >= Date.parse(job.started_at) &&
    Date.parse(receipt.verifiedAt) <= Date.parse(job.completed_at) &&
    receipt.key === `daily/${receipt.uploadedAt.slice(0, 10)}.elbbackup` &&
    receipt.reservation && !Object.hasOwn(receipt.reservation, 'readbackToken'));
  return { lastRun, lastVerified: { version: 2, status: 'verified', key: receipt.key,
    sha256: receipt.sha256, bytes: receipt.bytes, verifiedAt: receipt.verifiedAt } };
}

// GET-only SigV4, fixed bucket/daily key; no list, write, retry or provider-token substitution.
export async function inspectBackupObject(accountId, access, receipt, r2, request = fetch, now = Date.now()) {
  requireValue(/^[a-f0-9]{32}$/.test(accountId) && /^[a-f0-9]{32}$/.test(access?.accessKeyId ?? '') &&
    /^[a-f0-9]{64}$/.test(access?.secretAccessKey ?? '') && receipt?.version === 2 && receipt.status === 'verified' &&
    /^daily\/\d{4}-\d{2}-\d{2}\.elbbackup$/.test(receipt.key ?? '') &&
    /^[a-f0-9]{64}$/.test(receipt.sha256 ?? '') && positive(receipt.bytes) &&
    receipt.bytes > 422 && receipt.bytes <= maximumBytes && timestamp(receipt.verifiedAt, now));
  const at = new Date(now).toISOString().replace(/[:-]|\.\d{3}/g, ''), day = at.slice(0, 8);
  const host = `${accountId}.r2.cloudflarestorage.com`, path = `/${bucket}/${receipt.key}`;
  const empty = await digest(text('')), scope = `${day}/auto/s3/aws4_request`;
  const headers = `host:${host}\nx-amz-content-sha256:${empty}\nx-amz-date:${at}\n`;
  const names = 'host;x-amz-content-sha256;x-amz-date';
  const hmac = async (key, value) => crypto.subtle.sign('HMAC', await crypto.subtle.importKey('raw', key,
    { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']), text(value));
  let key = text('AWS4' + access.secretAccessKey);
  for (const part of [day, 'auto', 's3', 'aws4_request']) key = await hmac(key, part);
  const signature = hex(await hmac(key, `AWS4-HMAC-SHA256\n${at}\n${scope}\n${await digest(text(`GET\n${path}\n\n${headers}\n${names}\n${empty}`))}`));
  const response = await r2(() => request(`https://${host}${path}`, { method: 'GET', redirect: 'manual',
    cache: 'no-store', signal: AbortSignal.timeout(60000), headers: {
      'x-amz-date': at, 'x-amz-content-sha256': empty,
      Authorization: `AWS4-HMAC-SHA256 Credential=${access.accessKeyId}/${scope}, SignedHeaders=${names}, Signature=${signature}`
    } }));
  requireValue(response.status === 200);
  const bytes = await boundedBody(response, receipt.bytes, true);
  try { requireValue(await digest(bytes) === receipt.sha256); }
  finally { bytes.fill(0); }
  return receipt;
}

// Internal composition for checkBackupMonitor; usage comes from the existing reviewed permit/cache policy.
export async function observeBackup(config, credentials, usage, r2, github, request = fetch, now = Date.now()) {
  requireValue(['allowed', 'warning', 'stopped'].includes(usage));
  let history;
  try { history = await readBackupHistory(config, credentials.github, github, request, now); }
  catch (error) { logProviderFailure(tagProviderFailure(error, 'github', 'validation')); throw error; }
  if (history.lastVerified) await inspectBackupObject(config.accountId, credentials.r2, history.lastVerified, r2, request, now);
  return { version: 1, observedAt: new Date(now).toISOString(), usage, ...history };
}
