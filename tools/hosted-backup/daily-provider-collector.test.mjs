import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { collectGithubEvidence, collectSupabaseEvidence, collectProviderEvidence } from './daily-provider-collector.mjs';
import { validateCostEvidence } from './daily-cost-policy.mjs';
import { refreshProviderEvidence, cachedProviderEvidence } from './daily-request-controls.mjs';

const now = Date.parse('2026-10-18T12:00:00Z');
const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));
const config = { version: 2, githubUser: 'alphadelta332', accountId: 'a'.repeat(32), bucket: policy.bucket,
  r2Period: { periodStart: '2026-10-06T00:00:00.000Z', periodEnd: '2026-11-06T00:00:00.000Z' },
  supabaseOrganization: 'fixture-org', projectRef: 'b'.repeat(20), policy,
  deploymentRevisions: ['airport-catalog', 'recovery-envelope'].map(slug => ({ slug, version: 1, updated_at: 123 })) };
const credentials = { github: 'github-' + 'g'.repeat(30), supabaseProject: 'sbp-' + 's'.repeat(30),
  supabaseOrganization: 'sbp-' + 'o'.repeat(30), cloudflare: 'cf-' + 'c'.repeat(30) };
function fixture(mutate = () => {}) {
  const zero = { objects: 0, payloadSize: 0, metadataSize: 0 };
  const values = {
    entitlements: ['r2.enabled', 'r2.fedramp', 'r2.infrequent_access'].map(key =>
      ({ id: key, feature: { key }, allocation: { type: 'bool', value: key !== 'r2.fedramp' }, deleted_date: '' })),
    account: { login: config.githubUser, plan: { name: 'free' } },
    usage: { user: config.githubUser, timePeriod: { year: 2026, month: 10 }, usageItems: [
      { product: 'Actions', sku: 'actions_linux', grossQuantity: 3, unitType: 'minutes', netAmount: 0 },
      { product: 'Actions', sku: 'actions_windows', grossQuantity: 2, unitType: 'minutes', netAmount: 0 }] },
    organization: { id: config.supabaseOrganization, plan: 'free' },
    functions: config.deploymentRevisions.map(item => ({ ...item, status: 'ACTIVE' })),
    metrics: { standard: { published: { ...zero, objects: 3, payloadSize: 1500, metadataSize: 129 }, uploaded: zero },
      infrequentAccess: { published: zero, uploaded: zero } },
    bucket: { name: config.bucket, storage_class: 'Standard', jurisdiction: 'default' }, lock: policy.lock, lifecycle: policy.lifecycle,
    inventories: { default: { result: { buckets: [{ name: config.bucket, creation_date: '2026-10-17T03:58:25Z' }] },
      result_info: { total_count: 1 } }, ...Object.fromEntries(['eu', 'us'].map(name =>
      [name, { result: { buckets: [] }, result_info: { total_count: 0 } }])) },
    managed: { enabled: false }, domains: { domains: [] },
    analytics: { data: { viewer: { accounts: [{ operations: [{ sum: { requests: 62 }, dimensions: { actionType: 'GetObject', bucketName: config.bucket } }],
      storage: [{ max: { payloadSize: 1800, metadataSize: 129, uploadCount: 0 },
        dimensions: { bucketName: config.bucket, datetime: '2026-10-18T11:45:00Z' } }] }] } } }
  };
  mutate(values);
  const calls = [];
  const request = async (url, options) => {
    calls.push({ url, options });
    assert.equal(options.redirect, 'manual'); assert.equal(options.cache, 'no-store');
    assert.equal(options.headers.Authorization, 'Bearer ' + credentials[url.includes('github.com') ? 'github'
      : url.includes('/organizations/') ? 'supabaseOrganization' : url.includes('supabase.com') ? 'supabaseProject' : 'cloudflare']);
    assert.ok(Object.values(credentials).every(token => !url.includes(token)));
    let key;
    if (url.includes('/r2/buckets?')) return Response.json({ success: true, errors: [], ...values.inventories[options.headers['cf-r2-jurisdiction']] });
    if (url.includes('github.com')) key = url.endsWith('/user') ? 'account' : 'usage';
    else if (url.endsWith('/functions')) key = 'functions';
    else if (url.includes('/organizations/')) key = 'organization';
    else if (url.endsWith('/graphql')) {
      key = 'analytics'; const body = JSON.parse(options.body);
      assert.ok(!body.query.includes('bucketName:') && !body.query.includes('repository:'));
      assert.ok(body.query.includes('datetime_lt:') && !body.query.includes('datetime_leq:'));
      const result = structuredClone(values.analytics), account = result.data?.viewer?.accounts?.[0];
      if (account) for (let index = 0; Object.hasOwn(body.variables, 'from' + index); index++) {
        account['operations' + index] = account.operations;
        account['storage' + index] = account.storage?.filter(item => {
          const at = Date.parse(item.dimensions?.datetime);
          return !Number.isFinite(at) || (at >= Date.parse(body.variables['from' + index]) && at < Date.parse(body.variables['until' + index]));
        });
        if (account.storage?.length === 10000) account['storage' + index] = account.storage;
      }
      return Response.json(result);
    } else key = url.endsWith('/entitlements') ? 'entitlements' : url.endsWith('/domains/custom') ? 'domains' : url.endsWith('/domains/managed') ? 'managed'
      : url.endsWith('/metrics') ? 'metrics' : url.endsWith('/lock') ? 'lock' : url.endsWith('/lifecycle') ? 'lifecycle' : 'bucket';
    const value = values[key];
    return Response.json(['account', 'usage', 'organization', 'functions', 'analytics'].includes(key) ? value : { success: true, errors: [], result: value });
  };
  return { values, calls, request };
}

function durableFixture() {
  const values=new Map();let tail=Promise.resolve();
  return {values,transaction(action) {
    const next=tail.then(()=>action({get:async key=>structuredClone(values.get(key)),
      put:async(key,value)=>values.set(key,structuredClone(value))}));
    tail=next.catch(()=>{});return next;
  }};
}

test('real minimal bucket lists use authenticated bucket details for class and jurisdiction', async () => {
  const f = fixture(v => { v.inventories.default.result.buckets = [{ name: config.bucket, creation_date: '2026-10-17T03:58:25Z' }]; });
  const result = await collectProviderEvidence(config, credentials, f.request, now);
  assert.equal(result.status, 'collected-not-permitted');
  const details = f.calls.filter(call => call.url.endsWith('/r2/buckets/' + config.bucket));
  assert.equal(details.length, 1);
  assert.equal(details[0].options.headers['cf-r2-jurisdiction'], 'default');
});

test('regional HTTP denials cannot become empty inventories when bucket details succeed', async () => {
  for (const [jurisdiction, status] of [['eu', 403], ['us', 400]]) {
    const f = fixture(), storage = durableFixture();
    await assert.rejects(refreshProviderEvidence(storage, config, credentials, (url, options) =>
      url.includes('/r2/buckets?') && options.headers['cf-r2-jurisdiction'] === jurisdiction
        ? Promise.resolve(new Response(status === 400 ? 'BadRequest: invalid jurisdiction' : 'Access Denied', { status }))
        : f.request(url, options), () => now), /Provider evidence request failed/);
    await assert.rejects(cachedProviderEvidence(storage, config, now), /unavailable/);
  }
});

test('collection-start ceiling preserves inherited counters without refunding attempts', async () => {
  const storage = durableFixture();
  storage.values.set('provider-cache', { day: new Date(now).toISOString().slice(0, 10), batches: 21,
    startedAt: now - 60 * 60 * 1000, active: null });
  await assert.rejects(refreshProviderEvidence(storage, config, credentials, () => assert.fail('No I/O after ceiling'), () => now), /ceiling/);
  assert.equal(storage.values.get('provider-cache').batches, 21);
});

test('concurrent cache refresh makes one bounded collection; reads do not fan out or re-date evidence',async()=>{
  const storage=durableFixture(),f=fixture();
  await assert.rejects(cachedProviderEvidence(storage,config,now),/unavailable/);
  const results=await Promise.allSettled(Array.from({length:16},()=>refreshProviderEvidence(storage,config,credentials,f.request,()=>now)));
  assert.equal(results.filter(r=>r.status==='fulfilled').length,1);
  assert.equal(f.calls.length,15);
  assert.deepEqual(storage.values.get('component-requests').counts,{collector:11,github:2,supabase:2});
  for(let i=0;i<40;i++)assert.equal((await cachedProviderEvidence(storage,config,now+1000)).retrievedAt,new Date(now).toISOString());
  assert.equal(f.calls.length,15);
  await assert.rejects(refreshProviderEvidence(storage,config,{...credentials,cloudflare:'rotated-token-value-is-long'},f.request,()=>now+1000),/hour/);
  await assert.rejects(cachedProviderEvidence(storage,{...config,accountId:'f'.repeat(32)},now+1000),/unavailable/);
  await assert.rejects(cachedProviderEvidence(storage,config,now+2*60*60*1000),/stale/);
  await assert.rejects(cachedProviderEvidence(storage,config,now-1),/stale/);
});

test('failed refresh invalidates previous cache, charges failures and prevents hourly retry after restart/config rotation',async()=>{
  const storage=durableFixture(),f=fixture();
  await refreshProviderEvidence(storage,config,credentials,f.request,()=>now);
  const later=now+60*60*1000;
  const failed=fixture(v=>v.managed.enabled=true);
  await assert.rejects(refreshProviderEvidence(storage,config,credentials,failed.request,()=>later),/public/);
  assert.equal(failed.calls.length,15);
  await assert.rejects(cachedProviderEvidence(storage,config,later),/unavailable/);
  await assert.rejects(refreshProviderEvidence(storage,{...config,accountId:'f'.repeat(32)},credentials,f.request,()=>later+1000),/hour/);
  assert.equal(storage.values.get('provider-cache').batches,2);
  assert.equal(storage.values.get('component-requests').counts.collector,22);
});

test('cached provider measurement age can expire before retrieval age',async()=>{
  const storage=durableFixture(),f=fixture();
  await refreshProviderEvidence(storage,config,credentials,f.request,()=>now);
  await assert.rejects(cachedProviderEvidence(storage,config,now+105*60*1000),/Underlying/);
});

test('daily batch ceiling survives failed collections, UTC rollover, abandoned leases and late completion',async()=>{
  const storage=durableFixture();
  const midnight=Date.parse('2026-10-18T00:00:00Z');
  for(let i=0;i<21;i++)await assert.rejects(refreshProviderEvidence(storage,{},credentials,()=>assert.fail('Invalid config did I/O'),
    ()=>midnight+i*60*60*1000),/account/);
  await assert.rejects(refreshProviderEvidence(storage,config,credentials,()=>assert.fail('Batch ceiling did I/O'),
    ()=>midnight+21*60*60*1000),/ceiling/);
  assert.equal(storage.values.get('provider-cache').batches,21);
  await assert.rejects(refreshProviderEvidence(storage,{},credentials,fetch,()=>midnight+24*60*60*1000),/account/);
  assert.equal(storage.values.get('provider-cache').batches,1);
  const hung=durableFixture(),f=fixture();let release,entered;
  const ready=new Promise(resolve=>entered=resolve),gate=new Promise(resolve=>release=resolve);
  let time=now;
  const late=refreshProviderEvidence(hung,config,credentials,async(...args)=>{entered();await gate;return f.request(...args);},()=>time);
  const rejected=assert.rejects(late,/expired/);
  await ready;time+=60*60*1000;
  await assert.rejects(refreshProviderEvidence(hung,{},credentials,fetch,()=>time),/account/);
  release();await rejected;
  await assert.rejects(cachedProviderEvidence(hung,config,time),/unavailable/);
  assert.equal(hung.values.get('provider-cache').batches,2);
});

test('provider HTTP failure is counted before I/O and cannot spend more than the batch envelope',async()=>{
  const storage=durableFixture();let attempts=0;
  await assert.rejects(refreshProviderEvidence(storage,config,credentials,async url=>{
    const component=url.includes('github.com')?'github':url.includes('supabase.com')?'supabase':'collector';
    assert.ok(storage.values.get('component-requests').counts[component]>0);
    attempts++;return new Response('',{status:503});
  },()=>now));
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(attempts,1);
  assert.equal(storage.values.get('component-requests').counts.collector,1);
  await assert.rejects(cachedProviderEvidence(storage,config,now),/unavailable/);
});

test('collector isolates provider credentials and records actual observations without inventing a permit or billing freshness', async () => {
  const f = fixture(); const result = await collectProviderEvidence(config, credentials, f.request, now);
  assert.equal(f.calls.length, 15); assert.deepEqual(result.github.actionsMinutes,
    [{ sku: 'actions_linux', grossQuantity: 3, netAmount: 0 }, { sku: 'actions_windows', grossQuantity: 2, netAmount: 0 }]);
  assert.equal(result.github.remainingAllowanceProved, false);
  assert.equal(result.r2.currentBytes, 1629); assert.equal(result.r2.sampledPeakBytes, 1929);
  assert.equal(result.r2.pendingUploads, 0); assert.equal(result.r2.infrequentAccessBytes, 0);
  assert.equal(result.r2.uploadedObjects, 0); assert.equal(result.r2.uploadedBytes, 0); assert.equal(result.r2.infrequentAccessObjects, 0);
  assert.equal(result.r2.currentMeasurementAt, null); assert.equal(result.r2.monthlyBillingCoverageProved, false);
  const analyticsQuery = JSON.parse(f.calls.find(call => call.url.endsWith('/graphql')).options.body);
  assert.deepEqual(result.r2.windows, [config.r2Period.periodStart, '2026-09-17T12:00:00.000Z'].map(from =>
    ({ from, until: new Date(now).toISOString(), requests: 62 })));
  assert.equal(analyticsQuery.variables.from0, config.r2Period.periodStart);
  assert.equal(analyticsQuery.variables.from1, result.r2.windows[1].from);
  assert.equal(result.r2.billingPeriodProved, false);
  assert.ok(!Object.hasOwn(result.r2, 'periodStart'));
  assert.equal(result.status, 'collected-not-permitted');
  assert.ok(!JSON.stringify(result).includes(credentials.github));
  assert.ok(!Object.hasOwn(result, 'expiresAt')); assert.ok(!Object.hasOwn(result.supabase, 'egress'));
});

test('collector rejects account, period, paid/SKU, deployment, privacy, policy and incomplete/stale analytics drift', async t => {
  const cases = {
    'wrong account': v => v.account.login = 'other', 'missing plan': v => delete v.account.plan,
    'changed GitHub plan': v => v.account.plan.name = 'pro',
    'wrong user': v => v.usage.user = 'other', 'wrong month': v => v.usage.timePeriod.month = 9,
    'paid usage': v => v.usage.usageItems[0].netAmount = 0.01, 'unknown runner': v => v.usage.usageItems[0].sku = 'gpu',
    'missing units': v => delete v.usage.usageItems[0].unitType, 'missing organization': v => delete v.organization.id,
    'paid plan': v => v.organization.plan = 'pro', 'revision drift': v => v.functions[0].version++,
    'missing function': v => v.functions.pop(), 'inactive function': v => v.functions[0].status = 'REMOVED',
    'public bucket': v => v.managed.enabled = true, 'custom domain': v => v.domains.domains.push({ enabled: true }),
    'wrong listed storage class': v => v.inventories.default.result.buckets[0].storage_class = 'InfrequentAccess', 'lock drift': v => v.lock = { rules: [] },
    'wrong detail storage class': v => v.bucket.storage_class = 'InfrequentAccess',
    'missing detail storage class': v => { delete v.bucket.storage_class; },
    'wrong detail name': v => v.bucket.name = 'another-bucket',
    'wrong detail jurisdiction': v => v.bucket.jurisdiction = 'eu',
    'missing detail jurisdiction': v => { delete v.bucket.jurisdiction; },
    'missing storage field': v => delete v.metrics.standard.published.metadataSize,
    'infrequent access usage': v => v.metrics.infrequentAccess = { published: { objects: 1, payloadSize: 10, metadataSize: 0 }, uploaded: { objects: 0, payloadSize: 0, metadataSize: 0 } },
    'GraphQL error': v => v.analytics.errors = [{ message: 'denied' }],
    'empty account': v => v.analytics.data.viewer.accounts = [],
    'truncated operations': v => v.analytics.data.viewer.accounts[0].operations = Array(10000).fill({}),
    'missing history': v => v.analytics.data.viewer.accounts[0].storage = [],
    'future sample': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.datetime = '2026-10-19T00:00:00Z',
    'stale sample': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.datetime = '2026-10-18T09:00:00Z',
    'pending upload': v => v.analytics.data.viewer.accounts[0].storage[0].max.uploadCount = 1
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    await assert.rejects(collectProviderEvidence(config, credentials, fixture(mutate).request, now));
  });
});

test('GitHub collection can run independently without other provider credentials and never returns account profile data', async () => {
  const f = fixture(); const result = await collectGithubEvidence(config.githubUser, credentials.github, f.request, now);
  assert.equal(f.calls.length, 2); assert.ok(f.calls.every(call => call.url.startsWith('https://api.github.com/')));
  assert.equal(result.plan, 'free'); assert.equal(result.remainingAllowanceProved, false);
  assert.deepEqual(Object.keys(result).sort(), ['retrievedAt', 'plan', 'periodStart', 'actionsMinutes', 'remainingAllowanceProved'].sort());
});

test('Supabase separates organization and Preview credentials and accepts the documented organization id without a slug field', async () => {
  const f = fixture();
  const result = await collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, f.request, now);
  assert.equal(f.calls.length, 2); assert.equal(result.plan, 'free'); assert.deepEqual(result.deploymentRevisions, config.deploymentRevisions);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseProject, f.request, now), /separate/);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, undefined, f.request, now));
  const denied = async (url, options) => url.includes('/organizations/') ? new Response('', { status: 403 }) : f.request(url, options);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, denied, now));
});

test('HTTP denials, redirects, pagination, malformed and oversized bodies do not yield evidence', async t => {
  for (const [name, response] of [
    ['denied', () => new Response('private error', { status: 403 })],
    ['redirect', () => new Response('', { status: 302 })],
    ['pagination', () => Response.json({}, { headers: { link: '<next>' } })],
    ['malformed', () => new Response('private invalid JSON')],
    ['oversized', () => new Response('x'.repeat(1024 * 1024 + 1))]
  ]) await t.test(name, async () => {
    await assert.rejects(collectProviderEvidence(config, credentials, async () => response(), now), error => !error.message.includes('private'));
  });
  await assert.rejects(collectProviderEvidence(config, { ...credentials, github: '' }, fixture().request, now));
});

test('every jurisdiction, historical bucket, operation and actual multipart/storage count is checked', async t => {
  const cases = {
    'default extra bucket': v => { v.inventories.default.result.buckets.push({ name: 'new-bucket' }); v.inventories.default.result_info.total_count++; },
    'missing inventory total': v => delete v.inventories.default.result_info,
    'unseen inventory page': v => v.inventories.default.result_info.total_count++,
    'continuation cursor': v => v.inventories.default.result_info.cursor = 'next',
    'wrong inventory jurisdiction': v => v.inventories.default.result.buckets[0].jurisdiction = 'eu',
    'unknown storage class': v => v.metrics.futureClass = {},
    'unknown storage state': v => v.metrics.standard.futureState = {},
    'uploaded object without payload': v => v.metrics.standard.uploaded = { objects: 1, payloadSize: 0, metadataSize: 0 },
    'uploaded bytes without objects': v => v.metrics.standard.uploaded = { objects: 0, payloadSize: 0, metadataSize: 1 },
    'IA object without payload': v => v.metrics.infrequentAccess.published = { objects: 1, payloadSize: 0, metadataSize: 0 },
    'uploaded IA metadata': v => v.metrics.infrequentAccess.uploaded = { objects: 0, payloadSize: 0, metadataSize: 1 },
    'unknown operation': v => v.analytics.data.viewer.accounts[0].operations[0].dimensions.actionType = 'NewAction',
    'tier transition': v => v.analytics.data.viewer.accounts[0].operations[0].dimensions.actionType = 'LifecycleStorageTierTransition',
    'other operation bucket': v => v.analytics.data.viewer.accounts[0].operations[0].dimensions.bucketName = 'deleted-bucket',
    'missing operation bucket': v => delete v.analytics.data.viewer.accounts[0].operations[0].dimensions.bucketName,
    'empty object bucket': v => v.analytics.data.viewer.accounts[0].operations[0].dimensions.bucketName = '',
    'historical storage bucket': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.bucketName = 'deleted-bucket',
    'missing upload count': v => delete v.analytics.data.viewer.accounts[0].storage[0].max.uploadCount,
    'negative requests': v => v.analytics.data.viewer.accounts[0].operations[0].sum.requests = -1,
    'request overflow': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: Number.MAX_SAFE_INTEGER },
      dimensions: { actionType: 'HeadObject', bucketName: config.bucket } }),
    'duplicate operation group': v => v.analytics.data.viewer.accounts[0].operations.push(v.analytics.data.viewer.accounts[0].operations[0]),
    'duplicate storage group': v => v.analytics.data.viewer.accounts[0].storage.push(v.analytics.data.viewer.accounts[0].storage[0]),
    'truncated storage': v => v.analytics.data.viewer.accounts[0].storage = Array(10000).fill(v.analytics.data.viewer.accounts[0].storage[0]),
    'exact two-hour stale boundary': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.datetime = '2026-10-18T10:00:00Z'
  };
  for (const jurisdiction of ['eu', 'us']) cases[jurisdiction + ' new bucket'] = v => {
    v.inventories[jurisdiction].result.buckets.push({ name: 'new-bucket', jurisdiction, storage_class: 'Standard' });
    v.inventories[jurisdiction].result_info.total_count = 1;
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    await assert.rejects(collectProviderEvidence(config, credentials, fixture(mutate).request, now));
  });
  const f = fixture(v => v.analytics.data.viewer.accounts[0].operations.push(
    { sum: { requests: 5 }, dimensions: { actionType: 'ListBuckets', bucketName: '' } },
    { sum: { requests: 3 }, dimensions: { actionType: 'DeleteObject', bucketName: config.bucket } }));
  const evidence = await collectProviderEvidence(config, credentials, f.request, now);
  assert.deepEqual(evidence.r2.windows.map(window => window.requests), [70, 70]);
  assert.deepEqual(f.calls.filter(call => call.url.includes('/r2/buckets?')).map(call => call.options.headers['cf-r2-jurisdiction']),
    ['default', 'eu', 'us']);
});

test('exact reviewed retired bucket counts stay in both windows and bind the cache', async () => {
  const retiredBucketReview = { bucket: 'retired-fixture-bucket', observedAt: new Date(now - 1000).toISOString() };
  const reviewed = { ...config, retiredBucketReview }, storage = durableFixture();
  const f = fixture(v => v.analytics.data.viewer.accounts[0].operations.push(...['PutBucket', 'DeleteBucket'].map(actionType =>
    ({ sum: { requests: 1 }, dimensions: { actionType, bucketName: retiredBucketReview.bucket } }))));
  const evidence = await refreshProviderEvidence(storage, reviewed, credentials, f.request, () => now);
  assert.deepEqual(evidence.r2.windows.map(window => window.requests), [64, 64]);
  assert.deepEqual(evidence.r2.retiredBucketReview, { ...retiredBucketReview,
    windows: [{ PutBucket: 1, DeleteBucket: 1 }, { PutBucket: 1, DeleteBucket: 1 }] });
  assert.equal(evidence.status, 'collected-not-permitted');
  assert.equal(f.calls.length, 15);
  assert.deepEqual(storage.values.get('component-requests').counts, { collector: 11, github: 2, supabase: 2 });
  assert.deepEqual(await cachedProviderEvidence(storage, reviewed, now), evidence);
  await assert.rejects(cachedProviderEvidence(storage, config, now), /unavailable/);
  await assert.rejects(cachedProviderEvidence(storage, { ...reviewed,
    retiredBucketReview: { ...retiredBucketReview, bucket: 'different-retired-bucket' } }, now), /unavailable/);
  const expiring = { ...reviewed, retiredBucketReview: { ...retiredBucketReview,
    observedAt: new Date(now - 31 * 86400000 + 1).toISOString() } };
  const expiringStorage = durableFixture();
  await refreshProviderEvidence(expiringStorage, expiring, credentials, fixture().request, () => now);
  await assert.rejects(cachedProviderEvidence(expiringStorage, expiring, now + 1), /review expired/);
});

test('retired bucket review never allows storage, other actions, extra counts or a live bucket', async t => {
  const retiredBucketReview = { bucket: 'retired-fixture-bucket', observedAt: new Date(now - 1000).toISOString() };
  const reviewed = { ...config, retiredBucketReview };
  const cases = {
    'object operation': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 1 },
      dimensions: { actionType: 'GetObject', bucketName: retiredBucketReview.bucket } }),
    'create count exceeds one': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 2 },
      dimensions: { actionType: 'PutBucket', bucketName: retiredBucketReview.bucket } }),
    'delete count exceeds one': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 2 },
      dimensions: { actionType: 'DeleteBucket', bucketName: retiredBucketReview.bucket } }),
    'same prefix different bucket': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 1 },
      dimensions: { actionType: 'PutBucket', bucketName: retiredBucketReview.bucket + '-other' } }),
    'zero-byte storage sample': v => v.analytics.data.viewer.accounts[0].storage.push({
      max: { payloadSize: 0, metadataSize: 0, uploadCount: 0 },
      dimensions: { bucketName: retiredBucketReview.bucket, datetime: '2026-10-18T11:45:00Z' } })
  };
  for (const jurisdiction of ['default', 'eu', 'us']) cases['live ' + jurisdiction] = v => {
    v.inventories[jurisdiction].result.buckets.push({ name: retiredBucketReview.bucket });
    v.inventories[jurisdiction].result_info.total_count++;
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    const storage = durableFixture(), f = fixture(mutate);
    await assert.rejects(refreshProviderEvidence(storage, reviewed, credentials, f.request, () => now));
    await assert.rejects(cachedProviderEvidence(storage, reviewed, now), /unavailable/);
  });
  const f = fixture();
  const evidence = await collectProviderEvidence(reviewed, credentials, f.request, now);
  assert.deepEqual(evidence.r2.retiredBucketReview.windows, [{ PutBucket: 0, DeleteBucket: 0 }, { PutBucket: 0, DeleteBucket: 0 }]);
});

test('invalid or expired retired reviews reject before I/O', async () => {
  for (const retiredBucketReview of [null, [], {}, { bucket: config.bucket, observedAt: new Date(now).toISOString() },
    { bucket: 'retired-*', observedAt: new Date(now).toISOString() },
    { bucket: 'retired-bucket', observedAt: null }, { bucket: 'retired-bucket', observedAt: 'invalid' },
    { bucket: 'retired-bucket', observedAt: new Date(now + 1).toISOString() },
    { bucket: 'retired-bucket', observedAt: new Date(now - 31 * 86400000).toISOString() },
    { bucket: 'retired-bucket', observedAt: new Date(now).toISOString(), maxRequests: 500 }]) {
    const f = fixture();
    await assert.rejects(collectProviderEvidence({ ...config, retiredBucketReview }, credentials, f.request, now), /retired bucket review/);
    assert.equal(f.calls.length, 0);
  }
});

test('retired operation ceiling applies across every part of a split window', async () => {
  const reviewed = { ...config, r2Period: { periodStart: '2026-09-01T00:00:00Z', periodEnd: '2026-11-01T00:00:00Z' },
    retiredBucketReview: { bucket: 'retired-fixture-bucket', observedAt: new Date(now - 1000).toISOString() } };
  const f = fixture(v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 1 },
    dimensions: { actionType: 'PutBucket', bucketName: reviewed.retiredBucketReview.bucket } }));
  await assert.rejects(collectProviderEvidence(reviewed, credentials, f.request, now), /Retired bucket operation ceiling/);
});

const zeroReview = { bucket: 'retired-fixture-bucket', observedAt: new Date(now - 1000).toISOString(),
  zeroStorageAt: '2026-10-18T11:45:00Z' };
const zeroSample = () => ({ dimensions: { bucketName: zeroReview.bucket, datetime: zeroReview.zeroStorageAt },
  max: { payloadSize: 0, metadataSize: 0, uploadCount: 0 } });

test('one explicit retired zero sample is retained per window without dropping requests or changing live storage/freshness', async () => {
  const reviewed = { ...config, retiredBucketReview: zeroReview }, storage = durableFixture();
  const f = fixture(v => {
    const account = v.analytics.data.viewer.accounts[0]; account.storage.push(zeroSample());
    account.operations.push(...['PutBucket', 'DeleteBucket'].map(actionType =>
      ({ sum: { requests: 1 }, dimensions: { bucketName: zeroReview.bucket, actionType } })));
  });
  const evidence = await refreshProviderEvidence(storage, reviewed, credentials, f.request, () => now);
  assert.deepEqual(evidence.r2.retiredBucketReview, { ...zeroReview,
    windows: [0, 1].map(() => ({ PutBucket: 1, DeleteBucket: 1, zeroStorageSamples: 1 })) });
  assert.deepEqual(evidence.r2.windows.map(window => window.requests), [64, 64]);
  assert.equal(evidence.r2.currentBytes, 1629); assert.equal(evidence.r2.sampledPeakBytes, 1929);
  assert.equal(evidence.r2.newestStorageMeasurementAt, new Date(Date.parse(zeroReview.zeroStorageAt)).toISOString());
  assert.equal(f.calls.length, 15);
  assert.deepEqual(await cachedProviderEvidence(storage, reviewed, now+1000), evidence);
  for (const retiredBucketReview of [{ ...zeroReview, zeroStorageAt: '2026-10-18T11:44:00Z' },
    { bucket: zeroReview.bucket, observedAt: zeroReview.observedAt }])
    await assert.rejects(cachedProviderEvidence(storage, { ...reviewed, retiredBucketReview }, now), /unavailable/);
  assert.equal(f.calls.length, 15);
});

test('explicit zero review rejects other names/times, duplicate/nonzero/unknown samples, object operations and live reappearance', async t => {
  const cases = {
    'different name': v => { v.analytics.data.viewer.accounts[0].storage.at(-1).dimensions.bucketName += '-other'; },
    'different time': v => { v.analytics.data.viewer.accounts[0].storage.at(-1).dimensions.datetime = '2026-10-18T11:44:00Z'; },
    'duplicate exact sample': v => v.analytics.data.viewer.accounts[0].storage.push(zeroSample()),
    'additional zero sample': v => v.analytics.data.viewer.accounts[0].storage.push({ ...zeroSample(),
      dimensions: { bucketName: zeroReview.bucket, datetime: '2026-10-18T11:44:00Z' } }),
    'unknown measurement': v => { v.analytics.data.viewer.accounts[0].storage.at(-1).max.unknown = 0; },
    'unknown dimension': v => { v.analytics.data.viewer.accounts[0].storage.at(-1).dimensions.unknown = ''; },
    'unknown group field': v => { v.analytics.data.viewer.accounts[0].storage.at(-1).unknown = {}; },
    'object operation even zero': v => v.analytics.data.viewer.accounts[0].operations.push({ sum: { requests: 0 },
      dimensions: { bucketName: zeroReview.bucket, actionType: 'GetObject' } })
  };
  for (const key of ['payloadSize', 'metadataSize', 'uploadCount']) {
    for (const value of [1, -1, 0.1, '0', null]) cases[key+'='+value] = v => { v.analytics.data.viewer.accounts[0].storage.at(-1).max[key] = value; };
    cases['missing '+key] = v => { delete v.analytics.data.viewer.accounts[0].storage.at(-1).max[key]; };
  }
  for (const jurisdiction of ['default', 'eu', 'us']) cases['live '+jurisdiction] = v => {
    v.inventories[jurisdiction].result.buckets.push({ name: zeroReview.bucket }); v.inventories[jurisdiction].result_info.total_count++;
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    const f = fixture(v => { v.analytics.data.viewer.accounts[0].storage.push(zeroSample()); mutate(v); });
    const storage = durableFixture(), reviewed = { ...config, retiredBucketReview: zeroReview };
    await assert.rejects(refreshProviderEvidence(storage, reviewed, credentials, f.request, () => now));
    await assert.rejects(cachedProviderEvidence(storage, reviewed, now), /unavailable/);
  });
});

test('invalid/expired zero sample reviews reject before I/O and cached evidence expires on the earlier sample boundary', async () => {
  for (const zeroStorageAt of [null, undefined, 0, 'bad', new Date(now).toISOString(),
    new Date(now - 31*86400000).toISOString()]) {
    const f = fixture();
    await assert.rejects(collectProviderEvidence({ ...config, retiredBucketReview: { ...zeroReview, zeroStorageAt } }, credentials, f.request, now), /retired bucket review/);
    assert.equal(f.calls.length, 0);
  }
  const expiredAt = now - 31*86400000 + 1;
  const reviewed = { ...config, retiredBucketReview: { ...zeroReview, zeroStorageAt: new Date(expiredAt).toISOString() } };
  const storage = durableFixture();
  await refreshProviderEvidence(storage, reviewed, credentials, fixture().request, () => now);
  await cachedProviderEvidence(storage, reviewed, now);
  await assert.rejects(cachedProviderEvidence(storage, reviewed, now+1), /review expired/);
});

test('reviewed zero sample cannot satisfy live storage freshness or replace the live bucket', async () => {
  const reviewed = { ...config, retiredBucketReview: zeroReview };
  for (const storage of [[zeroSample()], [zeroSample(), { dimensions: { bucketName: config.bucket, datetime: '2026-10-18T09:00:00Z' },
    max: { payloadSize: 1000, metadataSize: 129, uploadCount: 0 } }]]) {
    const f = fixture(v => { v.analytics.data.viewer.accounts[0].storage = storage; });
    await assert.rejects(collectProviderEvidence(reviewed, credentials, f.request, now), /Storage analytics are stale/);
  }
});

test('zero sample at a split boundary is counted once and duplicates across pieces reject', async () => {
  const boundary = '2026-10-02T00:00:00.000Z';
  const reviewed = { ...config, r2Period: { periodStart: '2026-09-01T00:00:00.000Z', periodEnd: '2026-11-01T00:00:00.000Z' },
    retiredBucketReview: { ...zeroReview, zeroStorageAt: boundary } };
  const sample = { ...zeroSample(), dimensions: { bucketName: zeroReview.bucket, datetime: boundary } };
  const f = fixture(v => v.analytics.data.viewer.accounts[0].storage.push(sample));
  const result = await collectProviderEvidence(reviewed, credentials, f.request, now);
  assert.deepEqual(result.r2.retiredBucketReview.windows.map(window => window.zeroStorageSamples), [1, 1]);
  const duplicate = async (url, options) => {
    const response = await f.request(url, options);
    if (!url.endsWith('/graphql')) return response;
    const body = await response.json(); body.data.viewer.accounts[0].storage0.push(sample); return Response.json(body);
  };
  await assert.rejects(collectProviderEvidence(reviewed, credentials, duplicate, now), /storage measurement/);
});

test('positive disabled FedRAMP evidence is required before any regional or usage read', async t => {
  const cases = {
    'enabled government region': v => { v.entitlements[1].allocation.value = true; },
    'unknown active R2 region': v => v.entitlements.push({ id: 'r2.fedramp-high', feature: { key: 'r2.fedramp-high' },
      allocation: { type: 'bool', value: false }, deleted_date: '' }),
    'missing government flag': v => v.entitlements.splice(1, 1),
    'wrong entitlement id': v => { v.entitlements[1].id = 'r2.another'; },
    'nonboolean flag': v => { v.entitlements[1].allocation.value = 'false'; },
    'wrong allocation type': v => { v.entitlements[1].allocation.type = 'string'; },
    'inactive government flag': v => { v.entitlements[1].deleted_date = '2026-10-17T00:00:00Z'; },
    'malformed deletion time': v => { v.entitlements[1].deleted_date = 'bad'; },
    'future deletion time': v => { v.entitlements[1].deleted_date = '2026-10-19T00:00:00Z'; },
    'duplicate flag': v => v.entitlements.push(structuredClone(v.entitlements[1])),
    'R2 disabled': v => { v.entitlements[0].allocation.value = false; },
    'missing entitlements': v => { v.entitlements = {}; }
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    const f = fixture(mutate);
    await assert.rejects(collectProviderEvidence(config, credentials, f.request, now));
    assert.equal(f.calls.length, 1);
    assert.ok(f.calls[0].url.endsWith('/entitlements'));
  });
  const f = fixture();
  const result = await collectProviderEvidence(config, credentials, f.request, now);
  assert.deepEqual(result.r2.jurisdictionCoverage, { inspected: ['default', 'eu', 'us'], fedrampEnabled: false });
});

test('reviewed period is mandatory, current and bounded before any provider I/O', async () => {
  for (const r2Period of [undefined, {}, { periodStart: 'invalid', periodEnd: config.r2Period.periodEnd },
    { periodStart: new Date(now).toISOString(), periodEnd: config.r2Period.periodEnd },
    { periodStart: config.r2Period.periodStart, periodEnd: new Date(now).toISOString() },
    { periodStart: '2026-08-01T00:00:00Z', periodEnd: config.r2Period.periodEnd }]) {
    const f = fixture();
    await assert.rejects(collectProviderEvidence({ ...config, r2Period }, credentials, f.request, now), /reviewed R2 period/);
    assert.equal(f.calls.length, 0);
  }
  const f = fixture();
  await assert.rejects(collectProviderEvidence({ ...config, version: 1 }, credentials, f.request, now));
  assert.equal(f.calls.length, 0);
});

test('split request windows use adjacent half-open 31-day pieces without losing or double-counting boundary events', async () => {
  const longConfig = { ...config, r2Period: { periodStart: '2026-09-01T00:00:00.000Z', periodEnd: '2026-11-01T00:00:00.000Z' } };
  const boundary = Date.parse('2026-10-02T00:00:00Z');
  const events = [Date.parse(longConfig.r2Period.periodStart), boundary - 1, boundary, now - 1, now];
  const f = fixture();
  const request = async (url, options) => {
    const response = await f.request(url, options);
    if (!url.endsWith('/graphql')) return response;
    const body = JSON.parse(options.body), result = await response.json(), account = result.data.viewer.accounts[0];
    for (let index = 0; Object.hasOwn(body.variables, 'from' + index); index++) {
      const from = Date.parse(body.variables['from' + index]), until = Date.parse(body.variables['until' + index]);
      assert.ok(until - from <= 31 * 86400000);
      account['operations' + index] = [{ sum: { requests: events.filter(at => at >= from && at < until).length },
        dimensions: { actionType: 'GetObject', bucketName: config.bucket } }];
    }
    assert.equal(body.variables.until0, body.variables.from1);
    assert.equal(body.variables.from1, new Date(boundary).toISOString());
    assert.equal(body.variables.until1, new Date(now).toISOString());
    assert.equal(body.variables.from2, new Date(now - 31 * 86400000).toISOString());
    assert.ok(!Object.hasOwn(body.variables, 'from3'));
    return Response.json(result);
  };
  const result = await collectProviderEvidence(longConfig, credentials, request, now);
  assert.deepEqual(result.r2.windows.map(window => window.requests), [4, 3]);
  assert.equal(f.calls.filter(call => call.url.endsWith('/graphql')).length, 1);
});

test('missing split responses and malformed/out-of-range timestamps reject instead of producing partial evidence', async t => {
  const cases = {
    'missing operations window': a => delete a.operations1,
    'missing storage window': a => delete a.storage1,
    'truncated second operations window': a => a.operations1 = Array(10000).fill({}),
    'stale second window': a => a.storage1 = [],
    'missing timestamp': a => delete a.storage0[0].dimensions.datetime,
    'future timestamp': a => a.storage0[0].dimensions.datetime = new Date(now + 1).toISOString(),
    'exact until timestamp': a => a.storage0[0].dimensions.datetime = new Date(now).toISOString(),
    'outside from timestamp': a => a.storage0[0].dimensions.datetime = '2026-10-05T23:59:59.999Z'
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    const f = fixture();
    const request = async (url, options) => {
      const response = await f.request(url, options);
      if (!url.endsWith('/graphql')) return response;
      const result = await response.json(); mutate(result.data.viewer.accounts[0]); return Response.json(result);
    };
    await assert.rejects(collectProviderEvidence(config, credentials, request, now));
  });
});

test('collected R2 windows/counts pass permit3 without fabricating balances or changing the historical review', async () => {
  const evidence = await collectProviderEvidence(config, credentials, fixture().request, now);
  const at = new Date(now).toISOString(), start = '2026-10-01T00:00:00.000Z', end = '2026-11-01T00:00:00.000Z';
  const jobSha256 = 'c'.repeat(64), sourceOrigin = 'https://' + config.projectRef + '.supabase.co';
  const ownerReview = { version: 2, approvedAt: at, expiresAt: end, sourceExpiresAt: end, weeklyCheckedAt: at,
    jobSha256, sourceOrigin, githubPaidActionsBlocked: true, supabasePlan: 'free',
    egress: { used: 1, included: 5e9 }, cachedEgress: { used: 1, included: 5e9 },
    github: { observedAt: at, periodStart: start, periodEnd: end, remainingLinuxMinutes: 640 },
    r2: { observedAt: at, ...config.r2Period, storageGbMonths: 0.1, requests: 62,
      storageBillableGbMonths: 0, classABillable: 0, classBBillable: 0, usageCostUsd: 0 } };
  const permit = { version: 3, checkedAt: at, expiresAt: new Date(now + 60000).toISOString(), jobSha256,
    ownerReview, deploymentObservedAt: at, supabase: { ...evidence.supabase, observedAt: at },
    github: { ...evidence.github, observedAt: at, queryStart: evidence.github.periodStart, queryEnd: at },
    r2: { ...evidence.r2, observedAt: at } };
  assert.deepEqual(validateCostEvidence(permit, jobSha256, sourceOrigin, now), { warnings: [] });
  assert.equal(permit.r2.billingPeriodProved, false); assert.equal(permit.r2.monthlyBillingCoverageProved, false);
  assert.equal(ownerReview.github.remainingLinuxMinutes, 640);
  assert.ok(!Object.hasOwn(evidence, 'ownerReview'));
});
