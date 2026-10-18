// This collects provider evidence; it cannot issue a backup permit before live validation.
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const integer = value => Number.isSafeInteger(value) && value >= 0;
const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);

async function jsonRequest(url, token, request, body) {
  requireValue(typeof token === 'string' && token.length >= 20 && !/\s/.test(token), 'Missing dedicated monitoring credential.');
  const response = await request(url, {
    method: body ? 'POST' : 'GET', redirect: 'error', cache: 'no-store', signal: AbortSignal.timeout(15000),
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/json',
      'User-Agent': 'FlightLogX-backup-evidence', ...(url.startsWith('https://api.github.com/')
        ? { 'X-GitHub-Api-Version': '2026-03-10' } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {})
  });
  requireValue(response.status === 200, 'Provider evidence request failed; response suppressed.');
  const chunks = []; let size = 0;
  for await (const part of response.body) {
    size += part.length; requireValue(size <= 1024 * 1024, 'Provider evidence response exceeded limit.'); chunks.push(part);
  }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const part of chunks) { bytes.set(part, offset); offset += part.length; }
  let result;
  try { result = JSON.parse(new TextDecoder().decode(bytes)); }
  catch { throw new Error('Provider evidence response is not JSON; response suppressed.'); }
  requireValue(!response.headers.has('link'), 'Paginated provider evidence is unsupported.');
  return result;
}

function cloudflareResult(value) {
  requireValue(value?.success === true && Array.isArray(value.errors) && value.errors.length === 0 && value.result,
    'Cloudflare evidence failed.');
  return value.result;
}

export async function collectGithubEvidence(user, token, request = fetch, now = Date.now()) {
  requireValue(user === 'alphadelta332' && integer(now), 'Unexpected GitHub account or time.');
  const date = new Date(now), year = date.getUTCFullYear(), month = date.getUTCMonth() + 1;
  const get = path => jsonRequest('https://api.github.com' + path, token, request);
  const [account, usage] = await Promise.all([get('/user'),
    get(`/users/${user}/settings/billing/usage/summary?year=${year}&month=${month}`)]);
  requireValue(account?.login === user && account.plan?.name === 'free', 'GitHub account/plan missing or changed.');
  requireValue(usage?.user === user && usage.timePeriod?.year === year && usage.timePeriod?.month === month &&
    Array.isArray(usage.usageItems), 'GitHub billing period/account missing or changed.');
  const actionsMinutes = [];
  for (const item of usage.usageItems.filter(item => item.product?.toLowerCase() === 'actions')) {
    requireValue(typeof item.sku === 'string' && typeof item.grossQuantity === 'number' && Number.isFinite(item.grossQuantity) &&
      item.grossQuantity >= 0 && item.unitType === 'minutes' && item.netAmount === 0, 'Unsupported or paid Actions usage.');
    requireValue(['actions_linux', 'actions_windows', 'actions_macos'].includes(item.sku.toLowerCase()), 'Unreviewed Actions SKU.');
    actionsMinutes.push({ sku: item.sku, grossQuantity: item.grossQuantity });
  }
  return { retrievedAt: date.toISOString(), plan: account.plan.name,
    periodStart: new Date(Date.UTC(year, month - 1, 1)).toISOString(), actionsMinutes, remainingAllowanceProved: false };
}

export async function collectSupabaseEvidence(config, projectToken, organizationToken, request = fetch, now = Date.now()) {
  requireValue(/^[a-z0-9-]+$/.test(config.supabaseOrganization) && /^[a-z]{20}$/.test(config.projectRef) && integer(now),
    'Invalid Supabase identifiers or time.');
  requireValue(projectToken !== organizationToken, 'Project and organization metadata credentials must be separate.');
  requireValue(Array.isArray(config.deploymentRevisions) &&
    same(config.deploymentRevisions.map(item => item.slug).sort(), ['airport-catalog', 'recovery-envelope']),
    'Missing reviewed deployment revisions.');
  const [organization, functions] = await Promise.all([
    jsonRequest('https://api.supabase.com/v1/organizations/' + config.supabaseOrganization, organizationToken, request),
    jsonRequest('https://api.supabase.com/v1/projects/' + config.projectRef + '/functions', projectToken, request)
  ]);
  requireValue(organization?.id === config.supabaseOrganization && organization.plan === 'free', 'Supabase organization/plan changed.');
  requireValue(Array.isArray(functions) && functions.length === config.deploymentRevisions.length, 'Deployment inventory changed.');
  const deploymentRevisions = config.deploymentRevisions.map(expected => {
    const actual = functions.filter(item => item.slug === expected.slug);
    requireValue(actual.length === 1 && actual[0].status === 'ACTIVE' &&
      integer(expected.version) && expected.version > 0 && integer(expected.updated_at) && expected.updated_at > 0 &&
      actual[0].version === expected.version && actual[0].updated_at === expected.updated_at, 'Deployed revision changed.');
    return { slug: expected.slug, version: actual[0].version, updated_at: actual[0].updated_at };
  });
  return { retrievedAt: new Date(now).toISOString(), plan: organization.plan, deploymentRevisions };
}

export async function collectProviderEvidence(config, credentials, request = fetch, now = Date.now()) {
  requireValue(config?.version === 1 && config.githubUser === 'alphadelta332', 'Unexpected evidence account.');
  requireValue(/^[a-f0-9]{32}$/.test(config.accountId) && /^[a-z0-9-]+$/.test(config.supabaseOrganization) &&
    /^[a-z]{20}$/.test(config.projectRef), 'Invalid provider identifiers.');
  requireValue(config.bucket === 'flightlogx-preview-backups', 'Unexpected backup bucket.');
  requireValue(Array.isArray(config.deploymentRevisions) &&
    same(config.deploymentRevisions.map(item => item.slug).sort(), ['airport-catalog', 'recovery-envelope']),
    'Missing reviewed deployment revisions.');
  requireValue(integer(now), 'Invalid collection time.');
  const retrievedAt = new Date(now).toISOString();
  const date = new Date(now), year = date.getUTCFullYear(), month = date.getUTCMonth() + 1;
  const start = new Date(Date.UTC(year, month - 1, 1)).toISOString();
  const cf = path => jsonRequest('https://api.cloudflare.com/client/v4/accounts/' + config.accountId + path,
    credentials.cloudflare, request).then(cloudflareResult);

  // Every account is queried without a repository/bucket usage filter.
  const [github, supabase, metrics, bucket, lock, lifecycle, managed, domains, analytics] = await Promise.all([
    collectGithubEvidence(config.githubUser, credentials.github, request, now),
    collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, request, now),
    cf('/r2/metrics'), cf(`/r2/buckets/${config.bucket}`), cf(`/r2/buckets/${config.bucket}/lock`),
    cf(`/r2/buckets/${config.bucket}/lifecycle`), cf(`/r2/buckets/${config.bucket}/domains/managed`),
    cf(`/r2/buckets/${config.bucket}/domains/custom`),
    jsonRequest('https://api.cloudflare.com/client/v4/graphql', credentials.cloudflare, request, {
      query: `query($account: string!, $from: Time!, $until: Time!) {
        viewer { accounts(filter: {accountTag: $account}) {
          operations: r2OperationsAdaptiveGroups(limit: 10000, filter: {datetime_geq: $from, datetime_leq: $until}) {
            sum { requests } dimensions { actionType }
          }
          storage: r2StorageAdaptiveGroups(limit: 10000, filter: {datetime_geq: $from, datetime_leq: $until}) {
            max { payloadSize metadataSize uploadCount } dimensions { bucketName datetime }
          }
        } }
      }`, variables: { account: config.accountId, from: start, until: retrievedAt }
    })
  ]);

  requireValue(bucket?.name === config.bucket && bucket.storage_class === 'Standard' && managed?.enabled === false &&
    Array.isArray(domains?.domains) && domains.domains.length === 0, 'Bucket is public or storage configuration changed.');
  requireValue(same(lock, config.policy?.lock) && same(lifecycle, config.policy?.lifecycle), 'Retention policy changed.');
  let currentBytes = 0;
  for (const storageClass of ['standard', 'infrequentAccess']) {
    for (const state of ['published', 'uploaded']) {
      const value = metrics?.[storageClass]?.[state];
      requireValue(value && ['payloadSize', 'metadataSize', 'objects'].every(key => integer(value[key])), 'Incomplete account storage evidence.');
      if (storageClass === 'infrequentAccess') requireValue(Object.values(value).every(number => number === 0), 'Unreviewed Infrequent Access usage.');
      currentBytes += value.payloadSize + value.metadataSize;
    }
  }
  requireValue(integer(currentBytes), 'Invalid account storage total.');
  requireValue((analytics?.errors == null || (Array.isArray(analytics.errors) && analytics.errors.length === 0)) &&
    analytics?.data?.viewer?.accounts?.length === 1, 'Cloudflare analytics failed.');
  const account = analytics.data.viewer.accounts[0];
  requireValue(Array.isArray(account.operations) && account.operations.length < 10000 &&
    Array.isArray(account.storage) && account.storage.length > 0 && account.storage.length < 10000,
    'Missing or truncated analytics.');
  let operations = 0, newestStorage = 0;
  const peaks = new Map();
  for (const item of account.operations) {
    requireValue(integer(item.sum?.requests) && typeof item.dimensions?.actionType === 'string', 'Invalid operation metric.');
    operations += item.sum.requests;
  }
  for (const item of account.storage) {
    const measured = Date.parse(item.dimensions?.datetime), name = item.dimensions?.bucketName;
    requireValue(Number.isFinite(measured) && measured >= Date.parse(start) && measured <= now && typeof name === 'string' && name &&
      ['payloadSize', 'metadataSize', 'uploadCount'].every(key => integer(item.max?.[key])), 'Invalid storage measurement.');
    requireValue(item.max.uploadCount === 0, 'Pending uploads require review.');
    newestStorage = Math.max(newestStorage, measured);
    peaks.set(name, Math.max(peaks.get(name) ?? 0, item.max.payloadSize + item.max.metadataSize));
  }
  const sampledPeakBytes = [...peaks.values()].reduce((sum, bytes) => sum + bytes, 0);
  requireValue(integer(operations) && integer(sampledPeakBytes) && peaks.has(config.bucket), 'Incomplete account analytics.');
  requireValue(now - newestStorage < 2 * 60 * 60 * 1000, 'Storage analytics are stale.');
  return {
    version: 1, status: 'collected-not-permitted', retrievedAt,
    github,
    supabase,
    r2: { accountId: config.accountId, bucket: config.bucket, privateStandardBucket: true,
      currentBytes, currentMeasurementAt: null, operations, periodStart: start,
      newestStorageMeasurementAt: new Date(newestStorage).toISOString(), sampledPeakBytes,
      monthlyBillingCoverageProved: false }
  };
}
