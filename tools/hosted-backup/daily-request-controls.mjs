import { collectProviderEvidence, tagProviderFailure, logProviderFailure } from './daily-provider-collector.mjs';

const hour = 60 * 60 * 1000;
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const sha = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
  new TextEncoder().encode(JSON.stringify(value))))).map(byte => byte.toString(16).padStart(2, '0')).join('');
export const componentRequestLimits = Object.freeze({ collector: 231, github: 42, supabase: 42, workflow: 8, monitor: 12, monitorGithub: 30 });

// These keys share the existing stable Durable Object; review/token/config rotation never resets them.
export async function chargeComponentRequest(txn, component, now) {
  requireValue(Object.hasOwn(componentRequestLimits, component) && Number.isSafeInteger(now) && now >= 0,
    'Unknown request component or time.');
  const day = new Date(now).toISOString().slice(0, 10);
  const previous = await txn.get('component-requests');
  requireValue(!previous || previous.day <= day, 'Request clock moved backwards.');
  const ledger = previous?.day === day ? previous : { day, counts: {} };
  const count = ledger.counts[component] ?? 0;
  requireValue(Number.isSafeInteger(count) && count >= 0 && count < componentRequestLimits[component],
    'Daily component request ceiling reached.');
  ledger.counts[component] = count + 1;
  await txn.put('component-requests', ledger);
}

export async function reserveComponentRequest(storage, component, now = Date.now()) {
  await storage.transaction(txn => chargeComponentRequest(txn, component, now));
}

export async function cachedProviderEvidence(storage, config, now = Date.now()) {
  const fingerprint = await sha(config);
  return storage.transaction(async txn => {
    const cache = await txn.get('provider-cache');
    requireValue(cache?.fingerprint === fingerprint && cache.evidence &&
      now >= cache.startedAt && now - cache.startedAt < 2 * hour, 'Provider cache unavailable or stale.');
    if (config.retiredBucketReview) for (const at of [config.retiredBucketReview.observedAt,
      ...(Object.hasOwn(config.retiredBucketReview, 'zeroStorageAt') ? [config.retiredBucketReview.zeroStorageAt] : [])]) {
      const age = now - Date.parse(at);
      requireValue(age >= 0 && age < 31 * 24 * hour, 'Retired bucket review expired.');
    }
    for (const at of [cache.evidence.retrievedAt, cache.evidence.github.retrievedAt,
      cache.evidence.supabase.retrievedAt, cache.evidence.r2.newestStorageMeasurementAt]) {
      const age = now - Date.parse(at);
      requireValue(age >= 0 && age < 2 * hour, 'Underlying cached observation is stale.');
    }
    return cache.evidence;
  });
}

// Internal refresh only: public permit/cache reads must never invoke this function.
export async function refreshProviderEvidence(storage, config, credentials, request = fetch, clock = Date.now) {
  const startedAt = clock(), fingerprint = await sha(config);
  const id = crypto.randomUUID();
  await storage.transaction(async txn => {
    const previous = await txn.get('provider-cache');
    requireValue(!previous || (startedAt - previous.startedAt >= hour &&
      (!previous.active || startedAt >= previous.startedAt + 5 * 60 * 1000)), 'Provider refresh already attempted this hour.');
    const day = new Date(startedAt).toISOString().slice(0, 10);
    requireValue(!previous || previous.day <= day, 'Collection clock moved backwards.');
    const batches = previous?.day === day ? previous.batches : 0;
    requireValue(Number.isSafeInteger(batches) && batches >= 0 && batches < 21, 'Daily collection ceiling reached.');
    // Invalidate old evidence before I/O: failed refreshes cannot silently keep a previous success alive.
    await txn.put('provider-cache', { day, batches: batches + 1, startedAt, fingerprint, active: id });
  });
  const calls = { collector: 0, github: 0, supabase: 0 };
  const limits = { collector: 11, github: 2, supabase: 2 };
  const boundedRequest = async (url, options) => {
    const origin = new URL(url).origin;
    const component = { 'https://api.cloudflare.com': 'collector', 'https://api.github.com': 'github',
      'https://api.supabase.com': 'supabase' }[origin];
    requireValue(component && ++calls[component] <= limits[component], 'Unreviewed provider request.');
    let stage = 'lease';
    try { await storage.transaction(async txn => {
      const current = await txn.get('provider-cache'), now = clock();
      requireValue(current?.active === id && now >= startedAt && now - startedAt < 5 * 60 * 1000,
        'Provider refresh lease expired.');
      stage = 'allowance';
      await chargeComponentRequest(txn, component, now);
    }); } catch (error) { throw tagProviderFailure(error, 'cache', stage); }
    return request(url, options);
  };
  let stage = 'validation';
  try {
    const evidence = await collectProviderEvidence(config, credentials, boundedRequest, startedAt);
    requireValue(new TextEncoder().encode(JSON.stringify(evidence)).length <= 16384, 'Provider cache exceeds size limit.');
    stage = 'lease';
    await storage.transaction(async txn => {
      const current = await txn.get('provider-cache'), now = clock();
      requireValue(current?.active === id && now >= startedAt && now - startedAt < 5 * 60 * 1000,
        'Provider refresh lease expired.');
      await txn.put('provider-cache', { ...current, active: null, evidence });
    });
    return evidence;
  } catch (error) {
    logProviderFailure(tagProviderFailure(error, 'cache', stage));
    throw error;
  } finally {
    // A late completion cannot replace a newer refresh or refund any attempted request.
    await storage.transaction(async txn => {
      const current = await txn.get('provider-cache');
      if (current?.active === id) await txn.put('provider-cache', { ...current, active: null });
    });
  }
}
