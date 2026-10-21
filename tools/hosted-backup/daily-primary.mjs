import { cachedProviderEvidence, refreshProviderEvidence } from './daily-request-controls.mjs';
import { validateCostEvidence } from './daily-cost-policy.mjs';
import { checkBackupMonitor } from './daily-monitor.mjs';
import { observeBackup } from './daily-monitor-observation.mjs';
import { sendBackupAlert } from './daily-alert-delivery.mjs';

const requireValue = condition => { if (!condition) throw new Error('Primary monitoring inputs unavailable.'); };

// Evaluate the existing permit policy internally; this does not issue a source/export permit.
function providerPermit(evidence, review, provider, history) {
  requireValue(evidence?.version === 2 && evidence.status === 'collected-not-permitted' &&
    history.jobSha256 === review?.jobSha256 && history.accountId === provider.accountId &&
    review.sourceOrigin === `https://${provider.projectRef}.supabase.co` &&
    provider.r2Period.periodStart === review.r2.periodStart && provider.r2Period.periodEnd === review.r2.periodEnd &&
    evidence.r2.accountId === provider.accountId && evidence.r2.bucket === provider.bucket &&
    JSON.stringify(evidence.supabase.deploymentRevisions) === JSON.stringify(provider.deploymentRevisions));
  const at = evidence.retrievedAt;
  return { version: 3, jobSha256: history.jobSha256, ownerReview: review, checkedAt: at,
    expiresAt: new Date(Date.parse(at) + 2 * 3600000).toISOString(),
    deploymentObservedAt: evidence.supabase.retrievedAt,
    github: { ...evidence.github, observedAt: evidence.github.retrievedAt,
      queryStart: evidence.github.periodStart, queryEnd: evidence.github.retrievedAt },
    supabase: { ...evidence.supabase, observedAt: evidence.supabase.retrievedAt },
    r2: { ...evidence.r2, observedAt: at } };
}

export function providerUsage(evidence, review, provider, history, now = Date.now()) {
  const permit = providerPermit(evidence, review, provider, history);
  try {
    return validateCostEvidence(permit, history.jobSha256, review.sourceOrigin, now).warnings.length ? 'warning' : 'allowed';
  } catch { return 'stopped'; }
}

// Public reads use only the durable cache; they cannot spend a refresh or monitoring allowance.
export async function cachedBackupPermit(storage, env, now = Date.now()) {
  requireValue(env.BACKUP_ENABLED === 'true');
  const { provider, history } = JSON.parse(env.PRIMARY_MONITOR_JSON), review = JSON.parse(env.OWNER_REVIEW_JSON);
  const evidence = await cachedProviderEvidence(storage, provider, now);
  const permit = providerPermit(evidence, review, provider, history);
  validateCostEvidence(permit, history.jobSha256, review.sourceOrigin, now);
  requireValue(evidence.r2.privateStandardBucket === true &&
    JSON.stringify(evidence.r2.reviewedPeriod) === JSON.stringify(provider.r2Period));
  const policySha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
    new TextEncoder().encode(JSON.stringify(provider.policy))))).map(byte => byte.toString(16).padStart(2, '0')).join('');
  const result = { ...permit, deploymentRevisions: evidence.supabase.deploymentRevisions,
    r2: { ...permit.r2, policySha256 } };
  requireValue(new TextEncoder().encode(JSON.stringify(result)).length <= 16384);
  return result;
}

export async function checkPrimaryMonitor(storage, env, request = fetch, clock = Date.now) {
  if (env.MONITOR_ENABLED !== 'true') return;
  let review;
  try { review = JSON.parse(env.OWNER_REVIEW_JSON); } catch { /* Missing review is monitored as unavailable evidence. */ }
  return checkBackupMonitor(storage, review, async (r2, github) => {
    const { provider, history } = JSON.parse(env.PRIMARY_MONITOR_JSON);
    requireValue(history.jobSha256 === review?.jobSha256 && history.accountId === provider.accountId &&
      review.sourceOrigin === `https://${provider.projectRef}.supabase.co` &&
      provider.r2Period.periodStart === review.r2.periodStart && provider.r2Period.periodEnd === review.r2.periodEnd);
    let evidence;
    try { evidence = await cachedProviderEvidence(storage, provider, clock()); }
    catch {
      await refreshProviderEvidence(storage, provider, JSON.parse(env.PROVIDER_CREDENTIALS_JSON), request, clock);
      evidence = await cachedProviderEvidence(storage, provider, clock());
    }
    const usage = providerUsage(evidence, review, provider, history, clock());
    return observeBackup(history, JSON.parse(env.MONITOR_CREDENTIALS_JSON), usage, r2, github, request, clock());
  }, message => sendBackupAlert(storage, JSON.parse(env.ALERT_DELIVERY_JSON), env.RESEND_SEND_TOKEN, message, request, clock),
  clock, async () => {
    requireValue(typeof env.MONITOR_HEARTBEAT?.completed === 'function');
    await env.MONITOR_HEARTBEAT.completed();
  });
}

export async function runPrimaryMonitor(env) {
  if (env.MONITOR_ENABLED !== 'true') return;
  const stub = env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source'));
  const response = await stub.fetch('https://controls.internal/internal-monitor', { method: 'POST' });
  if (!response.ok) throw new Error('Primary monitoring unavailable.');
}
