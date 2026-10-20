// This collects provider evidence; it cannot issue a backup permit before live validation.
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const integer = value => Number.isSafeInteger(value) && value >= 0;
const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);

async function jsonRequest(url, token, request, body, headers = {}) {
  requireValue(typeof token === 'string' && token.length >= 20 && !/\s/.test(token), 'Missing dedicated monitoring credential.');
  const response = await request(url, {
    method: body ? 'POST' : 'GET', redirect: 'error', cache: 'no-store', signal: AbortSignal.timeout(15000),
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/json',
      'User-Agent': 'FlightLogX-backup-evidence', ...headers, ...(url.startsWith('https://api.github.com/')
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
    actionsMinutes.push({ sku: item.sku, grossQuantity: item.grossQuantity, netAmount: item.netAmount });
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
  requireValue(config?.version === 2 && config.githubUser === 'alphadelta332', 'Unexpected evidence account.');
  requireValue(/^[a-f0-9]{32}$/.test(config.accountId) && /^[a-z0-9-]+$/.test(config.supabaseOrganization) &&
    /^[a-z]{20}$/.test(config.projectRef), 'Invalid provider identifiers.');
  requireValue(config.bucket === 'flightlogx-preview-backups', 'Unexpected backup bucket.');
  requireValue(Array.isArray(config.deploymentRevisions) &&
    same(config.deploymentRevisions.map(item => item.slug).sort(), ['airport-catalog', 'recovery-envelope']),
    'Missing reviewed deployment revisions.');
  requireValue(integer(now), 'Invalid collection time.');
  const retrievedAt = new Date(now).toISOString();
  const day = 24 * 60 * 60 * 1000;
  const periodStart = Date.parse(config.r2Period?.periodStart), periodEnd = Date.parse(config.r2Period?.periodEnd);
  requireValue(Number.isFinite(periodStart) && Number.isFinite(periodEnd) && periodStart < now && periodEnd > now &&
    periodEnd - periodStart <= 62 * day, 'Missing, expired or unsupported reviewed R2 period.');
  const windows = [periodStart, now - 31 * day].map(from => ({ from: new Date(from).toISOString(), until: retrievedAt }));
  const ranges = windows.map(window => {
    const parts = [];
    for (let from = Date.parse(window.from); from < now; from += 31 * day)
      parts.push({ from: new Date(from).toISOString(), until: new Date(Math.min(from + 31 * day, now)).toISOString() });
    return parts;
  });
  const parts = ranges.flat();
  const variables = { account: config.accountId };
  const declarations = parts.map((part, index) => {
    variables['from' + index] = part.from; variables['until' + index] = part.until;
    return `$from${index}: Time!, $until${index}: Time!`;
  });
  const queries = parts.map((_, index) => `
    operations${index}: r2OperationsAdaptiveGroups(limit: 10000, filter: {datetime_geq: $from${index}, datetime_lt: $until${index}}) {
      sum { requests } dimensions { actionType bucketName }
    }
    storage${index}: r2StorageAdaptiveGroups(limit: 10000, filter: {datetime_geq: $from${index}, datetime_lt: $until${index}}) {
      max { payloadSize metadataSize uploadCount } dimensions { bucketName datetime }
    }`);
  const cf = (path, headers = {}) => jsonRequest('https://api.cloudflare.com/client/v4/accounts/' + config.accountId + path,
    credentials.cloudflare, request, undefined, headers).then(cloudflareResult);
  const entitlements = await cf('/entitlements');
  requireValue(Array.isArray(entitlements), 'Missing account entitlements.');
  const r2Features = entitlements.filter(item => item?.feature?.key?.startsWith('r2.') || item?.id?.startsWith('r2.'));
  requireValue(r2Features.every(item => item.id === item.feature?.key && typeof item.deleted_date === 'string' &&
    (item.deleted_date === '' || Date.parse(item.deleted_date) <= now)), 'Invalid R2 entitlement evidence.');
  const activeFeatures = r2Features.filter(item => item.deleted_date === '');
  requireValue(same(activeFeatures.map(item => item.feature.key).sort(), ['r2.enabled', 'r2.fedramp', 'r2.infrequent_access']) &&
    activeFeatures.every(item => item.allocation?.type === 'bool' && typeof item.allocation.value === 'boolean') &&
    activeFeatures.find(item => item.feature.key === 'r2.enabled').allocation.value === true &&
    activeFeatures.find(item => item.feature.key === 'r2.fedramp').allocation.value === false,
    'R2 jurisdiction entitlements missing, enabled or changed.');
  // Exclude government namespaces using positive account evidence, never a denied list request.
  const jurisdictions = ['default', 'eu', 'us'];
  const inventories = jurisdictions.map(async jurisdiction => {
    const response = await jsonRequest('https://api.cloudflare.com/client/v4/accounts/' + config.accountId + '/r2/buckets?per_page=1000',
      credentials.cloudflare, request, undefined, { 'cf-r2-jurisdiction': jurisdiction });
    const inventory = cloudflareResult(response), info = response.result_info;
    requireValue(Array.isArray(inventory.buckets) && integer(info?.total_count) && info.total_count === inventory.buckets.length &&
      !info.cursor, 'Missing or paginated bucket inventory.');
    requireValue(inventory.buckets.length === (jurisdiction === 'default' ? 1 : 0) && inventory.buckets.every(item =>
      item.name === config.bucket && (item.jurisdiction === undefined || item.jurisdiction === jurisdiction) &&
      (item.storage_class === undefined || item.storage_class === 'Standard')),
      'Unreviewed bucket or storage class.');
  });

  // Every account is queried without a repository/bucket usage filter.
  const [github, supabase, metrics, lock, lifecycle, managed, domains, analytics, bucket] = await Promise.all([
    collectGithubEvidence(config.githubUser, credentials.github, request, now),
    collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, request, now),
    cf('/r2/metrics'), cf(`/r2/buckets/${config.bucket}/lock`),
    cf(`/r2/buckets/${config.bucket}/lifecycle`), cf(`/r2/buckets/${config.bucket}/domains/managed`),
    cf(`/r2/buckets/${config.bucket}/domains/custom`),
    jsonRequest('https://api.cloudflare.com/client/v4/graphql', credentials.cloudflare, request, {
      query: `query($account: string!, ${declarations.join(', ')}) {
        viewer { accounts(filter: {accountTag: $account}) { ${queries.join('\n')} } }
      }`, variables
    }), cf(`/r2/buckets/${config.bucket}`, { 'cf-r2-jurisdiction': 'default' }), ...inventories
  ]);

  // List responses may omit these fields; authenticated details must prove them.
  requireValue(bucket?.name === config.bucket && bucket.jurisdiction === 'default' && bucket.storage_class === 'Standard',
    'Unreviewed bucket details or storage class.');
  requireValue(managed?.enabled === false &&
    Array.isArray(domains?.domains) && domains.domains.length === 0, 'Bucket is public or storage configuration changed.');
  requireValue(same(lock, config.policy?.lock) && same(lifecycle, config.policy?.lifecycle), 'Retention policy changed.');
  requireValue(same(Object.keys(metrics ?? {}).sort(), ['infrequentAccess', 'standard']), 'Unknown account storage class.');
  let currentBytes = 0, uploadedObjects = 0, uploadedBytes = 0, infrequentAccessBytes = 0, infrequentAccessObjects = 0;
  for (const storageClass of ['standard', 'infrequentAccess']) {
    requireValue(same(Object.keys(metrics[storageClass] ?? {}).sort(), ['published', 'uploaded']), 'Unknown account storage state.');
    for (const state of ['published', 'uploaded']) {
      const value = metrics?.[storageClass]?.[state];
      requireValue(value && ['payloadSize', 'metadataSize', 'objects'].every(key => integer(value[key])), 'Incomplete account storage evidence.');
      currentBytes += value.payloadSize + value.metadataSize;
      if (state === 'uploaded') { uploadedObjects += value.objects; uploadedBytes += value.payloadSize + value.metadataSize; }
      if (storageClass === 'infrequentAccess') {
        infrequentAccessBytes += value.payloadSize + value.metadataSize; infrequentAccessObjects += value.objects;
      }
    }
  }
  requireValue(integer(currentBytes), 'Invalid account storage total.');
  requireValue((analytics?.errors == null || (Array.isArray(analytics.errors) && analytics.errors.length === 0)) &&
    analytics?.data?.viewer?.accounts?.length === 1, 'Cloudflare analytics failed.');
  const account = analytics.data.viewer.accounts[0];
  const actions = new Set(['ListBuckets', 'PutBucket', 'ListObjects', 'PutObject', 'CopyObject', 'CompleteMultipartUpload',
    'CreateMultipartUpload', 'ListMultipartUploads', 'UploadPart', 'UploadPartCopy', 'ListParts', 'PutBucketEncryption',
    'PutBucketCors', 'PutBucketLifecycleConfiguration', 'HeadBucket', 'HeadObject', 'GetObject', 'UsageSummary',
    'GetBucketEncryption', 'GetBucketLocation', 'GetBucketCors', 'GetBucketLifecycleConfiguration',
    'DeleteObject', 'DeleteBucket', 'AbortMultipartUpload']);
  let newestStorage = 0, pendingUploads = 0, partIndex = 0;
  const peaks = new Map();
  for (let windowIndex = 0; windowIndex < windows.length; windowIndex++) {
    let requests = 0, windowNewest = 0;
    for (const part of ranges[windowIndex]) {
      const operations = account['operations' + partIndex], storage = account['storage' + partIndex++];
      requireValue(Array.isArray(operations) && operations.length < 10000 &&
        Array.isArray(storage) && storage.length < 10000, 'Missing or truncated analytics.');
      const operationGroups = new Set(), storageGroups = new Set();
      for (const item of operations) {
        const name = item.dimensions?.bucketName, action = item.dimensions?.actionType;
        requireValue(integer(item.sum?.requests) && actions.has(action), 'Invalid or unreviewed operation metric.');
        requireValue(name === config.bucket || (name === '' && ['ListBuckets', 'UsageSummary'].includes(action)), 'Unreviewed operation bucket.');
        const group = JSON.stringify([name, action]);
        requireValue(!operationGroups.has(group), 'Duplicate operation metric.'); operationGroups.add(group);
        requests += item.sum.requests;
      }
      for (const item of storage) {
        const measured = Date.parse(item.dimensions?.datetime), name = item.dimensions?.bucketName;
        requireValue(Number.isFinite(measured) && measured >= Date.parse(part.from) && measured < Date.parse(part.until) &&
          name === config.bucket && ['payloadSize', 'metadataSize', 'uploadCount'].every(key => integer(item.max?.[key])),
          'Invalid or unreviewed storage measurement.');
        requireValue(!storageGroups.has(measured), 'Duplicate storage metric.'); storageGroups.add(measured);
        pendingUploads = Math.max(pendingUploads, item.max.uploadCount);
        newestStorage = Math.max(newestStorage, measured);
        windowNewest = Math.max(windowNewest, measured);
        peaks.set(name, Math.max(peaks.get(name) ?? 0, item.max.payloadSize + item.max.metadataSize));
      }
    }
    requireValue(now - windowNewest < 2 * 60 * 60 * 1000, 'Storage analytics are stale.');
    requireValue(integer(requests), 'Invalid account request total.'); windows[windowIndex].requests = requests;
  }
  const sampledPeakBytes = [...peaks.values()].reduce((sum, bytes) => sum + bytes, 0);
  requireValue(integer(sampledPeakBytes) && peaks.has(config.bucket), 'Incomplete account analytics.');
  requireValue(now - newestStorage < 2 * 60 * 60 * 1000, 'Storage analytics are stale.');
  requireValue(pendingUploads === 0 && uploadedObjects === 0 && uploadedBytes === 0, 'Pending uploads require review.');
  requireValue(infrequentAccessBytes === 0 && infrequentAccessObjects === 0, 'Unreviewed Infrequent Access usage.');
  return {
    version: 2, status: 'collected-not-permitted', retrievedAt,
    github,
    supabase,
    r2: { accountId: config.accountId, bucket: config.bucket, privateStandardBucket: true,
      currentBytes, currentMeasurementAt: null, windows,
      jurisdictionCoverage: { inspected: jurisdictions, fedrampEnabled: false },
      reviewedPeriod: { periodStart: config.r2Period.periodStart, periodEnd: config.r2Period.periodEnd },
      pendingUploads, uploadedObjects, uploadedBytes, infrequentAccessBytes, infrequentAccessObjects,
      billingPeriodProved: false,
      newestStorageMeasurementAt: new Date(newestStorage).toISOString(), sampledPeakBytes,
      monthlyBillingCoverageProved: false }
  };
}
