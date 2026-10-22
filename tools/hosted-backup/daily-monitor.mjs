import { chargeComponentRequest } from './daily-request-controls.mjs';
import { validateOwnerReview } from './daily-cost-policy.mjs';

const hour = 60 * 60 * 1000;
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const age = (value, now) => typeof value === 'string' ? now - Date.parse(value) : NaN;
export const monitorDeliveryLimits = Object.freeze({ primary: Object.freeze({ day: 1, month: 16 }),
  observer: Object.freeze({ day: 1, month: 15 }) });

// An empty POST can have a stream; reject actual bytes without buffering a payload.
export async function emptyMonitorBody(request) {
  const reader = request.body?.getReader();
  if (!reader) return true;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) return true;
      if (value.byteLength) return false;
    }
  } finally { await reader.cancel(); reader.releaseLock(); }
}

// Fixed operational fields only: never log whole ledgers, provider responses or alert bodies.
export async function logMonitorCheckpoint(storage, kind, phase) {
  requireValue(['primary', 'observer'].includes(kind) && ['before', 'after', 'heartbeat'].includes(phase), 'Invalid monitor checkpoint.');
  const count = value => Number.isSafeInteger(value) && value >= 0 ? value : null;
  const date = value => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null;
  const checkpoint = await storage.transaction(async txn => {
    const [monitor, requests, cache, heartbeat, source] = await Promise.all(
      ['monitor-state', 'component-requests', 'provider-cache', 'monitor-heartbeat', 'source-budget'].map(key => txn.get(key)));
    return { version: 1, event: 'backup-monitor-checkpoint', kind, phase,
      monitor: monitor ? { day: date(monitor.day), startedAt: count(monitor.startedAt), checks: count(monitor.checks),
        dailyAttempts: count(monitor.dailyAttempts), monthlyAttempts: count(monitor.monthlyAttempts),
        incidentAttempts: count(monitor.incidentAttempts), delivered: monitor.delivered === true,
        active: Boolean(monitor.active), wireAttempt: count(monitor.resend?.attempt) } : null,
      requests: requests ? { day: date(requests.day), counts: Object.fromEntries(
        ['collector', 'github', 'supabase', 'workflow', 'monitor', 'monitorGithub'].map(key => [key, count(requests.counts?.[key])])) } : null,
      cache: cache ? { startedAt: count(cache.startedAt), batches: count(cache.batches), active: Boolean(cache.active),
        hasEvidence: Boolean(cache.evidence), retrievedAt: count(Date.parse(cache.evidence?.retrievedAt)) } : null,
      heartbeatAt: count(heartbeat?.completedAt),
      source: source ? { attempts: count(source.attempts), readbackUsed: source.readbackUsed === true } : null };
  });
  console.log(JSON.stringify(checkpoint));
}

export function monitorAlertMessage(state) {
  return { idempotencyKey: `${state.kind === 'observer' ? 'backup-watchdog' : 'backup-monitor'}/${state.incident}`,
    subject: state.deliveryTest === true ? 'FlightLogX backup alert test' : 'FlightLogX backup needs attention',
    text: (state.deliveryTest === true ? 'Supervised delivery test only. Backup automation remains disabled.\n\n' : '') +
      `Backup monitoring detected: ${state.signature}.\nFirst observed: ${state.incidentAt}.\nCheck the backup controls before resuming backups.` };
}

// Only an internal adapter may supply these observations, after authenticating history and stored bytes.
export function monitorConditions(observation, review, now = Date.now()) {
  requireValue(Number.isSafeInteger(now) && now >= 0 && observation?.version === 1 &&
    age(observation.observedAt, now) >= 0 && age(observation.observedAt, now) < 10 * 60 * 1000 &&
    ['allowed', 'warning', 'stopped'].includes(observation.usage), 'Independent monitoring evidence unavailable.');
  const conditions = [];
  if (observation.lastRun === null) conditions.push('missing-run');
  else {
    const run = observation.lastRun;
    requireValue(age(run?.startedAt, now) >= 0 &&
      ['success', 'failure', 'cancelled', 'timed_out', 'in_progress'].includes(run.conclusion), 'Invalid run evidence.');
    if (age(run.startedAt, now) > 26 * hour) conditions.push('missing-run');
    if (['failure', 'cancelled', 'timed_out'].includes(run.conclusion) ||
      (run.conclusion === 'in_progress' && age(run.startedAt, now) > hour)) conditions.push('failed-run');
  }
  if (observation.lastVerified === null) conditions.push('stale-backup');
  else {
    const verified = observation.lastVerified;
    requireValue(verified?.version === 2 && verified.status === 'verified' &&
      /^[a-f0-9]{64}$/.test(verified.sha256 ?? '') &&
      /^daily\/\d{4}-\d{2}-\d{2}\.elbbackup$/.test(verified.key ?? '') &&
      Number.isSafeInteger(verified.bytes) && verified.bytes > 422 && verified.bytes <= 50 * 1024 * 1024 &&
      age(verified.verifiedAt, now) >= 0, 'Invalid verified readback evidence.');
    if (age(verified.verifiedAt, now) > 30 * hour) conditions.push('stale-backup');
  }
  if (observation.usage !== 'allowed') conditions.push(`usage-${observation.usage}`);
  try { validateOwnerReview(review, review?.jobSha256, review?.sourceOrigin, now); }
  catch { conditions.push('review-required'); }
  if (Number.isFinite(Date.parse(review?.expiresAt)) && Date.parse(review.expiresAt) - now <= 48 * hour)
    conditions.push('review-expiring');
  if (Number.isFinite(Date.parse(review?.weeklyCheckedAt)) && now - Date.parse(review.weeklyCheckedAt) >= 6 * 24 * hour)
    conditions.push('weekly-review-due');
  return conditions.sort();
}

// Internal only: not exported from the Worker, enabled or scheduled by this module.
export async function checkBackupMonitor(storage, review, observe, deliver, clock = Date.now, heartbeat) {
  const result = await checkMonitor(storage, 'primary', async (r2, github) =>
    monitorConditions(await observe(r2, github), review, clock()), deliver, clock);
  // Report completed policy checks, never merely a timer firing or an unaccepted alert.
  if (heartbeat && (result.delivery !== 'suppressed' || (await storage.get('monitor-state'))?.delivered === true))
    await heartbeat();
  return result;
}

// Called only through the primary Worker's private service binding; use receiver time.
export async function recordMonitorHeartbeat(storage, now = Date.now()) {
  requireValue(Number.isSafeInteger(now) && now >= 0, 'Invalid heartbeat clock.');
  return storage.transaction(async txn => {
    const previous = await txn.get('monitor-heartbeat');
    requireValue(!previous || now - previous.completedAt >= 4 * hour, 'Heartbeat already recorded this interval.');
    await txn.put('monitor-heartbeat', { completedAt: now });
  });
}

export async function checkMissingMonitor(storage, deliver, clock = Date.now) {
  return checkMonitor(storage, 'observer', async () => {
    const heartbeat = await storage.get('monitor-heartbeat'), now = clock();
    requireValue(!heartbeat || (Number.isSafeInteger(heartbeat.completedAt) && heartbeat.completedAt >= 0 &&
      heartbeat.completedAt <= now), 'Invalid heartbeat evidence.');
    return !heartbeat || now - heartbeat.completedAt > 6 * hour ? ['missing-monitor'] : [];
  }, deliver, clock);
}

async function checkMonitor(storage, kind, observe, deliver, clock) {
  const limits = monitorDeliveryLimits[kind];
  const startedAt = clock(), id = crypto.randomUUID();
  requireValue(Number.isSafeInteger(startedAt) && startedAt >= 0, 'Invalid monitoring clock.');
  const day = new Date(startedAt).toISOString().slice(0, 10), month = day.slice(0, 7);
  await storage.transaction(async txn => {
    const previous = await txn.get('monitor-state');
    requireValue(!previous || (previous.kind ?? 'primary') === kind, 'Monitor ledger belongs to another sender.');
    requireValue(!previous || (startedAt - previous.startedAt >= 4 * hour &&
      startedAt >= previous.startedAt + 5 * 60 * 1000), 'Monitor already attempted this interval.');
    const checks = previous?.day === day ? previous.checks : 0;
    requireValue(Number.isSafeInteger(checks) && checks < 6, 'Daily monitoring ceiling reached.');
    await txn.put('monitor-state', { ...previous, kind, day, month, checks: checks + 1, startedAt, active: id,
      dailyAttempts: previous?.day === day ? previous.dailyAttempts : 0,
      monthlyAttempts: previous?.month === month ? previous.monthlyAttempts : 0 });
  });
  const live = state => requireValue(state?.active === id && clock() >= startedAt &&
    clock() - startedAt < 5 * 60 * 1000 && new Date(clock()).toISOString().slice(0, 10) === day,
    'Monitoring lease expired.');
  let requests = 0, githubRequests = 0;
  // Adapter wraps each individual R2 GET, including any retry; a failed call is still spent.
  const r2 = async request => {
    requireValue(typeof request === 'function' && ++requests <= 2, 'Per-check R2 ceiling reached.');
    await storage.transaction(async txn => {
      live(await txn.get('monitor-state'));
      await chargeComponentRequest(txn, 'monitor', clock());
    });
    return request();
  };
  const github = async request => {
    requireValue(typeof request === 'function' && ++githubRequests <= 5, 'Per-check GitHub ceiling reached.');
    await storage.transaction(async txn => {
      live(await txn.get('monitor-state'));
      await chargeComponentRequest(txn, 'monitorGithub', clock());
    });
    return request();
  };
  try {
    let conditions;
    try { conditions = await observe(r2, github); }
    catch { conditions = ['monitor-evidence-unavailable']; }
    const signature = conditions.join(','), attemptedAt = new Date(startedAt).toISOString();
    const message = await storage.transaction(async txn => {
      const state = await txn.get('monitor-state'); live(state);
      if (state.signature !== signature) {
        state.signature = signature; state.incident = id; state.incidentAt = attemptedAt;
        state.incidentAttempts = 0; state.delivered = false;
      }
      state.conditions = conditions;
      let message = null;
      if (signature && !state.delivered && state.incidentAttempts < 2 &&
        clock() - Date.parse(state.incidentAt) < 24 * hour &&
        state.dailyAttempts < limits.day && state.monthlyAttempts < limits.month) {
        state.incidentAttempts++; state.dailyAttempts++; state.monthlyAttempts++;
        message = monitorAlertMessage(state);
      }
      await txn.put('monitor-state', state);
      return message;
    });
    if (!message) return { conditions, delivery: signature ? 'suppressed' : 'unnecessary' };
    // Persist attempts before I/O; failure/lost replies cannot refund or reset the allowance.
    const result = await deliver(message);
    requireValue(result?.accepted === true, 'Alert delivery not accepted.');
    await storage.transaction(async txn => {
      const state = await txn.get('monitor-state'); live(state);
      await txn.put('monitor-state', { ...state, delivered: true });
    });
    return { conditions, delivery: 'accepted' };
  } finally {
    await storage.transaction(async txn => {
      const state = await txn.get('monitor-state');
      if (state?.active === id) await txn.put('monitor-state', { ...state, active: null });
    });
  }
}
